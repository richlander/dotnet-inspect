using System.Collections.Immutable;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class DiffCommand
{
    /// <summary>
    /// One assembly whose method bodies a body view reads: an extracted file,
    /// or a package endpoint's implementation participant image
    /// (docs/design/package-endpoint-scope.md, step 4 part 3).
    /// </summary>
    sealed record DiffBodyAssembly(
        ResolvedAssemblyReference Assembly,
        IAssemblyReferenceResolver Resolver,
        string SourcePath,
        ImmutableArray<byte> Image)
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
                participant.Image);

        /// <summary>
        /// Analyzes the assembly's method bodies. An extracted file binds its
        /// references as the legacy path always has; a participant binds
        /// them through its implementation group.
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
            if (includeImplementationProfiles)
            {
                features |= ILInspector.Analysis.LibraryBodyAnalysisFeatures
                    .ImplementationProfiles;
            }
            return MethodBodyInspectionSession.OpenWithPrefetchedImage(
                SourcePath,
                Image,
                features,
                Resolver,
                Assembly);
        }
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
    static async Task<T> WithBodyPopulationAsync<T>(
        DiffInputs inputs,
        Func<DiffInputs, T> view,
        CancellationToken cancellationToken)
    {
        if (inputs.From.PackageScope is not { } before
            || inputs.To.PackageScope is not { } after)
        {
            return view(inputs);
        }

        return await PackageEndpointBodyQuery.ExecuteAsync(
            before,
            after,
            (from, to, _) => ValueTask.FromResult(
                view(inputs with
                {
                    Bodies = new DiffBodyPopulation(
                        [.. from.Select(DiffBodyAssembly.FromParticipant)],
                        [.. to.Select(DiffBodyAssembly.FromParticipant)]),
                })),
            cancellationToken);
    }
}
