using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspect.Cli.Commands;

internal static class AuthoredSourceDocumentPrinter
{
    internal static async Task<int> PrintAsync(
        ApiType type,
        PrintableRow selected,
        string? originalDocumentPath,
        ApiOptions options,
        ResolvedAssemblyReference? sourceAssembly,
        string? packageName,
        string? packageVersion,
        HttpClient symbolClient)
    {
        string? assemblyPath = sourceAssembly?.Path
            ?? options.DllPath
            ?? type.SourceAssemblyPath;
        if (type.DefinitionName is not { } definitionName
            || originalDocumentPath is not { Length: > 0 } originalPath
            || assemblyPath is null)
        {
            CommandError.Write(
                $"row {selected.Row} has no exact type-document acquisition identity.");
            return 1;
        }

        var (participant, context) = CreateContext(
            assemblyPath,
            options,
            sourceAssembly,
            packageName,
            packageVersion,
            symbolClient);
        InspectionEnvelope<AssemblyTypeSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([participant]);
            inspection = await TypeSourceInspection.ExecuteAsync(
                group, participant,
                AssemblyTypeSourceRequest.AuthoredDocument(definitionName, originalPath),
                context);
        }

        if (inspection.Content is not AssemblyTypeSourceEntry.Available
            { Source: AssemblyTypeSource.Pdb source })
        {
            string detail = inspection.Content switch
            {
                AssemblyTypeSourceEntry.Unavailable
                {
                    PdbAttempt.Lines.Value: FindingInspection<string>.Failed failed,
                } => failed.Error.Reason,
                AssemblyTypeSourceEntry.Unavailable
                {
                    PdbAttempt:
                    {
                        Outcome: PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
                        Lines.Value: FindingInspection<string>.Absent absent,
                    },
                } => $"Could not fetch SourceLink source. {absent.Detail}",
                AssemblyTypeSourceEntry.Unavailable
                {
                    PdbAttempt.Lines.Value: FindingInspection<string>.Absent absent,
                } => absent.Detail ?? "The selected authored source document is unavailable.",
                AssemblyTypeSourceEntry.Unavailable unavailable => unavailable.Failure.Detail,
                AssemblyTypeSourceEntry.Rejected rejected => rejected.Failure.ToString(),
                _ => throw new InvalidOperationException("Unexpected authored type-document result."),
            };
            CommandError.Write($"failed to fetch verified source for row {selected.Row}: {detail}");
            return 1;
        }

        var document = new PrintableDocument(
            selected.Row, selected.Section, selected.Label, selected.Path, selected.Url, source.Text)
        {
            Language = "csharp"
        };
        return PrintProjectionOutput.Write(
            [document],
            new PrintProjectionOptions(
                Row: null,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                new ProjectionDestination(null, options.Rows),
                Markdown: options.UsesMarkdownPayloadFormat));
    }

    internal static (AssemblyContextParticipant Participant, AssemblyContextSourceQueryContext Context)
        CreateContext(
            string assemblyPath,
            ApiOptions options,
            ResolvedAssemblyReference? sourceAssembly,
            string? packageName,
            string? packageVersion,
            HttpClient symbolClient,
            IAssemblyBindingPolicy? selectedBindingPolicy = null)
    {
        ResolvedAssemblyReference assembly =
            sourceAssembly ?? ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local("authored source document"));
        IAssemblyBindingPolicy bindingPolicy =
            selectedBindingPolicy
            ?? new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(assemblyPath)
                {
                    ProjectAssetsPath = options.ProjectAssetsPath,
                    TargetFramework = options.Tfm,
                    IncludeDepsJsonAssets = false,
                    IncludeAspNetCoreSharedFramework = false,
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
        var participant = new AssemblyContextParticipant(assembly, bindingPolicy);
        var logger = new VerboseLogger(options.Verbose);
        var context = new AssemblyContextSourceQueryContext(
            symbolClient,
            FileSystemPdbStore.CreateDefault(),
            new SourcePolicyPackageSourceAuthorization(options.SourceOptions),
            new SourceFetch(DotnetInspector.Networking.HttpClientFactory.SharedUntrustedFetch))
        {
            RepositoryPaths = options.SourceRepositories,
            NuGetSourceOptions = options.SourceOptions,
            PdbFallbackPackage = packageName is not null && packageVersion is not null
                ? new(packageName, packageVersion)
                : null,
            AllowLocalSourceReads = true,
            AllowAdjacentPdbReads = true,
            Log = logger.Log,
        };
        return (participant, context);
    }
}
