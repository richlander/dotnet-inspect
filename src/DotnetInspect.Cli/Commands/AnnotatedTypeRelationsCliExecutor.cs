using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspect.Cli.Commands;

internal sealed record AnnotatedTypeRelationsCliExecution(
    AnnotatedTypeRelationsRequestResult.Accepted Resolution,
    InspectionEnvelope<AnnotatedTypeRelationsDocument> Inspection);

internal static class AnnotatedTypeRelationsCliExecutor
{
    private const int MaxAssemblyImageBytes = 512 * 1024 * 1024;

    internal static bool IsSelected(TypeOptions options) =>
        options.AnnotatedRelationIds is not null;

    internal static async Task<(
        AnnotatedTypeRelationsCliExecution? Execution,
        string? Error)> ExecuteAsync(
            TypeOptions options,
            ApiSourceResult source,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.TypeName))
        {
            return (
                null,
                "--relations requires one exact Type name.");
        }

        if (MetadataTypeDefinitionName.ParseSerialized(source.TypeName)
            is not MetadataTypeDefinitionNameResult.Valid validType)
        {
            return (
                null,
                $"Type '{source.TypeName}' is not one exact metadata Type "
                    + "definition name.");
        }

        (AssemblySet? assemblySet, int? preferredIndex, string? error) =
            await CollectAssemblySetAsync(
                    options,
                    source,
                    cancellationToken)
                .ConfigureAwait(false);
        if (assemblySet is null || preferredIndex is null)
            return (null, error);

        using (assemblySet)
        {
            var participants =
                new List<AssemblyContextParticipant>(
                    assemblySet.Assemblies.Count);
            string? packageExtractPath =
                TypeHierarchyRelationsInspectionExecutor
                    .PackagePopulationRoot(
                        assemblySet.Assemblies[preferredIndex.Value],
                        source);
            for (int index = 0; index < assemblySet.Assemblies.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssemblySetEntry entry = assemblySet.Assemblies[index];
                AssemblySetEntry provenanceEntry =
                    index == preferredIndex.Value
                    && TypeHierarchyRelationsInspectionExecutor
                        .IsPlatformSource(options, source)
                        ? TypeHierarchyRelationsInspectionExecutor
                            .ExactPlatformFocusEntry(entry, source)
                        : entry;
                AssemblyResolutionProvenance provenance =
                    TypeHierarchyRelationsInspectionExecutor.ProvenanceFor(
                        provenanceEntry,
                        options,
                        packageExtractPath);
                ResolvedAssemblyReference? assembly =
                    ResolvedAssemblyReference.CreateFromPathIfManaged(
                        entry.Path,
                        provenance);
                if (assembly is null)
                {
                    return (
                        null,
                        $"The selected file '{entry.Path}' does not contain "
                            + "managed metadata.");
                }

                participants.Add(
                    new(
                        assembly,
                        new AssemblyDependencyResolver(
                            new AssemblyDependencyResolutionOptions(
                                entry.Path))));
            }

            var workspace = new InspectionWorkspace();
            AnnotatedTypeRelationsCliExecution? execution = null;
            string? executionError = null;
            try
            {
                using AssemblyContextGroup group =
                    workspace.CreateAssemblyContextGroup(
                        participants,
                        new AssemblyContextGroupOptions
                        {
                            MaxRetainedImageBytes =
                                MaxAssemblyImageBytes,
                        });
                AssemblyContextParticipant focusParticipant =
                    participants[preferredIndex.Value];
                StructuralSubjectIdentity.WorkspaceSubject workspaceSubject =
                    StructuralSubjectIdentity.ForWorkspace(
                        workspace.Identity);
                SubjectRelationFocusAuthority focus =
                    SubjectRelationFocusAuthority.ForAcquiredType(
                        workspaceSubject,
                        focusParticipant.Assembly.Registration,
                        focusParticipant.Assembly.Identity,
                        focusParticipant.Assembly.Provenance,
                        validType.Name);
                SubjectRelationPopulationAuthority population =
                    SubjectRelationPopulationAuthority.Capture(
                        workspaceSubject,
                        group.BindingPolicyVersion);

                AnnotatedTypeRelationsFamilySelection selection;
                try
                {
                    string[] ids = options.AnnotatedRelationIds!;
                    selection =
                        ids is [var all]
                        && all.Equals(
                            "all",
                            StringComparison.OrdinalIgnoreCase)
                            ? AnnotatedTypeRelationsFamilySelection
                                .AllApplicable
                            : AnnotatedTypeRelationsFamilySelection
                                .Explicit(ids);
                    var request = new AnnotatedTypeRelationsRequest(
                        focus,
                        selection,
                        SubjectRelationDirectionSelection.Outgoing,
                        options.AnnotatedRelationColorIds,
                        options.AnnotatedRelationRequiredColorIds,
                        options.Count
                            ? AnnotatedTypeRelationsTerminal.Count
                            : AnnotatedTypeRelationsTerminal.Rows);
                    AnnotatedTypeRelationsCallInspectionResult result =
                        AnnotatedTypeRelationsCallInspection.Execute(
                            group,
                            focusParticipant.Assembly.Registration,
                            validType.Name,
                            population,
                            request,
                            cancellationToken);
                    switch (result)
                    {
                        case AnnotatedTypeRelationsCallInspectionResult
                            .Available available:
                            execution = new(
                                available.Resolution,
                                available.Inspection);
                            break;
                        case AnnotatedTypeRelationsCallInspectionResult
                            .Rejected rejected:
                            executionError = rejected.Failure.Message;
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown Annotated Type Relations inspection "
                                    + "result.");
                    }
                }
                catch (ArgumentException exception)
                {
                    executionError = exception.Message;
                }
            }
            finally
            {
                InspectionWorkspaceCloseReport closeReport =
                    await workspace.CloseAsync().ConfigureAwait(false);
                if (!closeReport.Succeeded)
                {
                    execution = null;
                    executionError =
                        "The Annotated Type Relations Workspace could not "
                            + "release all acquired resources.";
                }
            }

            return (execution, executionError);
        }
    }

    private static async Task<(
        AssemblySet? AssemblySet,
        int? PreferredIndex,
        string? Error)> CollectAssemblySetAsync(
            TypeOptions options,
            ApiSourceResult source,
            CancellationToken cancellationToken)
    {
        string? exactPlatformFocusPath =
            TypeHierarchyRelationsInspectionExecutor
                .SelectExactPlatformFocusPath(options, source);
        AssemblySet assemblySet =
            await AssemblySetResolver.CollectAsync(
                    source.Context.HttpClient,
                    TypeHierarchyRelationsInspectionExecutor
                        .BuildAssemblySetRequest(
                            options,
                            source,
                            exactPlatformFocusPath: null,
                            cancellationToken),
                    source.Context.Logger.Log)
                .ConfigureAwait(false);
        if (assemblySet.Assemblies.Count == 0)
        {
            string error = assemblySet.Diagnostics.Count > 0
                ? string.Join(
                    Environment.NewLine,
                    assemblySet.Diagnostics.Select(
                        diagnostic => diagnostic.Message))
                : "The selected source contains no managed libraries.";
            assemblySet.Dispose();
            return (null, null, error);
        }

        bool exactPlatformFocusMissing =
            exactPlatformFocusPath is not null
            && !assemblySet.Assemblies.Any(
                entry => PathsEqual(entry.Path, exactPlatformFocusPath));
        (int? preferredIndex, string? preferredError) =
            TypeHierarchyRelationsInspectionExecutor.SelectPreferredEntry(
                assemblySet.Assemblies,
                source,
                exactPlatformFocusPath);
        if ((preferredIndex is null || exactPlatformFocusMissing)
            && TypeHierarchyRelationsInspectionExecutor.IsPlatformSource(
                options,
                source)
            && File.Exists(exactPlatformFocusPath ?? source.SearchPath))
        {
            assemblySet.Dispose();
            assemblySet =
                await AssemblySetResolver.CollectAsync(
                        source.Context.HttpClient,
                        TypeHierarchyRelationsInspectionExecutor
                            .BuildAssemblySetRequest(
                                options,
                                source,
                                exactPlatformFocusPath
                                    ?? source.SearchPath,
                                cancellationToken),
                        source.Context.Logger.Log)
                    .ConfigureAwait(false);
            if (assemblySet.Assemblies.Count == 0)
            {
                string error = assemblySet.Diagnostics.Count > 0
                    ? string.Join(
                        Environment.NewLine,
                        assemblySet.Diagnostics.Select(
                            diagnostic => diagnostic.Message))
                    : "The selected source contains no managed libraries.";
                assemblySet.Dispose();
                return (null, null, error);
            }

            (preferredIndex, preferredError) =
                TypeHierarchyRelationsInspectionExecutor
                    .SelectPreferredEntry(
                        assemblySet.Assemblies,
                        source,
                        exactPlatformFocusPath);
        }

        if (preferredIndex is null)
        {
            assemblySet.Dispose();
            return (null, null, preferredError);
        }

        return (assemblySet, preferredIndex, null);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
