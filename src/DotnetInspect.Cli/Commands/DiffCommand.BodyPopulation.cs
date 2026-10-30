using System.Collections.Immutable;
using System.Runtime.InteropServices;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class DiffCommand
{
    /// <summary>
    /// One assembly whose method bodies a body view reads: an extracted file,
    /// or a package endpoint's implementation participant image
    /// (docs/design/package-endpoint-scope.md, step 4 part 3). A participant
    /// also carries the implementation group that holds it, for SourceHouse
    /// (part 4).
    /// </summary>
    sealed record DiffBodyAssembly(
        ResolvedAssemblyReference Assembly,
        IAssemblyReferenceResolver Resolver,
        string SourcePath,
        ImmutableArray<byte> Image,
        AssemblyContextGroup? Group = null,
        AssemblyContextParticipant? GroupParticipant = null)
    {
        public static DiffBodyAssembly FromPath(string path, string purpose) =>
            new(
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(purpose)),
                MetadataSource.DefaultAssemblyReferenceResolver(path),
                path,
                default);

        public static DiffBodyAssembly FromParticipant(
            PackageEndpointBodyParticipant participant) =>
            new(
                participant.Assembly,
                participant.Resolver,
                participant.Descriptor.Asset.Path,
                participant.Image,
                participant.Group,
                participant.GroupParticipant);

        /// <summary>
        /// Analyzes the assembly's method bodies. An extracted file binds its
        /// references through the path resolver beside it; a participant
        /// binds them through its implementation group.
        /// </summary>
        public MethodBodyInspectionSession OpenSession(
            bool includeImplementationProfiles)
        {
            if (Image.IsDefault)
            {
                return MethodBodyInspectionSession.Open(
                    Assembly,
                    includeImplementationProfiles:
                        includeImplementationProfiles);
            }

            var features =
                ILInspector.Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                | ILInspector.Analysis.LibraryBodyAnalysisFeatures.Allocations
                | ILInspector.Analysis.LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities;
            return MethodBodyInspectionSession.OpenWithPrefetchedImage(
                SourcePath,
                Image,
                features,
                Resolver,
                Assembly,
                includeImplementationProfiles:
                    includeImplementationProfiles);
        }
    }

    /// <summary>
    /// One assembly whose PDB source an endpoint reads. An extracted file
    /// joins a group of its own; a package endpoint's implementation
    /// participant is read in the group that holds it, with its package
    /// provenance (docs/design/package-endpoint-scope.md, step 4 part 4).
    /// </summary>
    sealed record PdbSourceAssembly(
        string SourcePath,
        Func<AssemblyContextParticipant> CreateParticipant,
        DiffBodyAssembly? Participant = null)
    {
        /// <summary>The group that holds a participant; an extracted file has none.</summary>
        public AssemblyContextGroup? Group => Participant?.Group;

        public static IReadOnlyList<PdbSourceAssembly> FromPaths(
            IReadOnlyList<string> paths,
            DiffOptions options,
            bool oldSide,
            IReadOnlyList<AssemblySetEntry>? entries) =>
            [.. paths.Select(path => new PdbSourceAssembly(
                path,
                () => CreateSourceParticipant(
                    path,
                    options,
                    oldSide,
                    FindAssemblySetEntry(path, entries))))];

        public static PdbSourceAssembly FromParticipant(DiffBodyAssembly body) =>
            new(
                body.SourcePath,
                () => body.GroupParticipant
                    ?? throw new InvalidOperationException(
                        $"The PDB-source assembly '{body.SourcePath}' has no implementation group."),
                body);

        /// <summary>The assembly's declared methods, the PDB-source targets.</summary>
        public ILInspector.Analysis.LibraryCallGraphAnalysisResult IndexMethods() =>
            (Participant is { } body
                ? MethodBodyInspectionSession.OpenWithPrefetchedImage(
                    SourcePath,
                    body.Image,
                    ILInspector.Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                    body.Resolver,
                    body.Assembly)
                : MethodBodyInspectionSession.Open(
                    SourcePath,
                    includeAllocations: false,
                    includeOpportunities: false))
            .CallGraphAnalysis;

        /// <summary>The complete API surface that maps targets to SourceHouse requests.</summary>
        public ApiSurface? ExtractSurface(bool includeCompilerGenerated) =>
            Participant is { } body
                ? AssemblyReader.ExtractApiSurface(
                    new MemoryStream(
                        ImmutableCollectionsMarshal.AsArray(body.Image)!,
                        writable: false),
                    includeAll: true,
                    typesOnly: false,
                    includeCompilerGenerated)
                : AssemblyReader.ExtractApiSurface(
                    SourcePath,
                    includeAll: true,
                    typesOnly: false,
                    includeCompilerGenerated);
    }

    /// <summary>The before and after assemblies a body view compares.</summary>
    sealed record DiffBodyPopulation(
        IReadOnlyList<DiffBodyAssembly> From,
        IReadOnlyList<DiffBodyAssembly> To)
    {
        public static DiffBodyPopulation FromPaths(
            IReadOnlyList<string> fromPaths,
            IReadOnlyList<string> toPaths,
            string purpose) =>
            new(
                [.. fromPaths.Select(path => DiffBodyAssembly.FromPath(path, purpose))],
                [.. toPaths.Select(path => DiffBodyAssembly.FromPath(path, purpose))]);
    }

    /// <summary>Whether the plan compares method bodies.</summary>
    static bool ReadsMethodBodies(
        CompiledInspectionPlan<DiffQueryContext> plan) =>
        plan.RequestedQueries.Any(static query =>
            query == BodySignalComparisonQuery.Definition
            || query == ImplementationComparisonQuery.Definition);

    static InspectionQueryResults RunQueryPlan(
        CompiledInspectionPlan<DiffQueryContext> plan,
        DiffInputs inputs,
        DiffOptions options) =>
        plan.Run(
            new DiffQueryContext(
                inputs.FromSurface,
                inputs.ToSurface,
                () => CreateBodySignalComparisonInput(inputs, options),
                () => CreateImplementationComparisonInput(
                    inputs,
                    options)));

    /// <summary>
    /// Runs <paramref name="view"/> over the request's body population. A
    /// package endpoint scope's population is its implementation
    /// participants, held only for the duration of the view, which must
    /// return materialized results; every other endpoint's is its
    /// extracted files.
    /// </summary>
    static Task<T> WithBodyPopulationAsync<T>(
        DiffInputs inputs,
        Func<DiffInputs, T> view,
        CancellationToken cancellationToken) =>
        WithBodyPopulationAsync(
            inputs,
            (bodied, _) => ValueTask.FromResult(view(bodied)),
            cancellationToken);

    static async Task<T> WithBodyPopulationAsync<T>(
        DiffInputs inputs,
        Func<DiffInputs, CancellationToken, ValueTask<T>> view,
        CancellationToken cancellationToken)
    {
        if (inputs.From.PackageScope is not { } before
            || inputs.To.PackageScope is not { } after)
        {
            return await view(inputs, cancellationToken);
        }

        return await PackageEndpointBodyQuery.ExecuteAsync(
            before,
            after,
            (from, to, token) => view(
                inputs with
                {
                    Bodies = new DiffBodyPopulation(
                        [.. from.Select(DiffBodyAssembly.FromParticipant)],
                        [.. to.Select(DiffBodyAssembly.FromParticipant)]),
                },
                token),
            cancellationToken);
    }
}
