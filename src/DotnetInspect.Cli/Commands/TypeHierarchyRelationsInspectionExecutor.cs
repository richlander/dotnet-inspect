using System.Collections.Immutable;
using System.Security.Cryptography;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class TypeHierarchyRelationsInspectionExecutor
{
    private const int MaxAssemblyImageBytes = 512 * 1024 * 1024;
    private const int DefaultMaximumRowsPerSection = 10_000;
    private const long MaxMetadataRows = 10_000_000;

    private static readonly AssemblyContextLibraryMaterializationLimits
        MaterializationLimits =
            new(MaxAssemblyImageBytes, MaxAssemblyImageBytes);

    private static readonly LibraryTypeDeclarationInventoryInspectionBounds
        InventoryBounds =
            new(
                maximumAssemblyBytes: MaxAssemblyImageBytes,
                maximumRetainedDeclarations: 500_000,
                maximumMetadataRows: int.MaxValue,
                maximumRetainedTextCharacters: int.MaxValue);

    internal static readonly MetadataOperationPolicy OperationPolicy =
        new(
            maxMetadataRows: MaxMetadataRows,
            maxMethodImplementationRows: MaxMetadataRows,
            maxDeclarationCandidates: MaxMetadataRows,
            maxRelationshipEdges: MaxMetadataRows,
            maxSignatureBytes: MaxMetadataRows,
            maxGenericSubstitutionNodes: MaxMetadataRows,
            maxStructuredNodes: MaxMetadataRows,
            maxRetainedText: MaxMetadataRows,
            maxInterfaceImplementationRows: MaxMetadataRows,
            maxRetainedHierarchyRelations: MaxMetadataRows);

    internal static bool IsSelected(TypeOptions options) =>
        options.IncludeSections is { } sections
        && (sections.Contains(SectionNames.Implementers)
            || sections.Contains(SectionNames.DerivedTypes));

    internal static async Task<(int? ExitCode, string? Error)> ExecuteAsync(
        TypeOptions options,
        ApiSourceResult source,
        Func<TypeHierarchyExactTypeInspection, Task<int>> consume,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(consume);

        if (options.Rows is { Kind: RowWindowKind.Tail })
        {
            return (
                null,
                "Hierarchy relation sections cannot select tail rows without "
                    + "materializing the complete relation population. Use a "
                    + "head or closed range row window.");
        }

        var workspace = new InspectionWorkspace();
        int? exitCode = null;
        string? error = null;
        try
        {
            (TypeHierarchyExactTypeInspection? inspection, error) =
                await ExecuteCoreAsync(
                        workspace,
                        options,
                        source,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (inspection is not null)
            {
                using (inspection.AssemblySet)
                using (inspection.FocusGroup)
                {
                    exitCode =
                        await consume(inspection).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await workspace.CloseAsync().ConfigureAwait(false);
            throw;
        }

        InspectionWorkspaceCloseReport closeReport =
            await workspace.CloseAsync().ConfigureAwait(false);
        if (!closeReport.Succeeded)
        {
            return (
                null,
                "The hierarchy Workspace could not release all acquired "
                    + "resources.");
        }

        return (exitCode, error);
    }

    internal static SubjectRelationPopulationRequest CreatePopulationRequest(
        TypeOptions options,
        SubjectRelationForm form)
    {
        ArgumentNullException.ThrowIfNull(options);
        var selection =
            new SubjectRelationPopulationSelection(
                form,
                form == SubjectRelationForm.Interface
                    ? MetadataRelationGraphCatalog.Interface.Id
                    : MetadataRelationGraphCatalog.BaseType.Id,
                SubjectRelationDirectionSelection.Incoming,
                SubjectRelationEvidenceKind.Declaration);
        SubjectRelationPopulationCountRequest? count =
            options.Count
                ? new SubjectRelationPopulationCountRequest()
                : null;
        SubjectRelationPopulationRowsRequest? rows =
            !options.Count || options.Rows is not null
                ? new SubjectRelationPopulationRowsRequest(
                    options.Rows?.ProducerPrefixBound
                        ?? DefaultMaximumRowsPerSection)
                : null;
        return new(selection, count, rows);
    }

    internal static TypeHierarchyRelationsInspection Execute(
        TypeOptions options,
        WorkspaceDeclarationPopulation population,
        WorkspaceDeclarationOccurrence focusOccurrence,
        MetadataTypeDefinitionName focusType,
        IReadOnlyDictionary<
            object,
            TypeHierarchyRelationCandidateSource> candidateSources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(focusOccurrence);
        ArgumentNullException.ThrowIfNull(focusType);
        ArgumentNullException.ThrowIfNull(candidateSources);

        TypeHierarchyRelationSectionInspection? implementers =
            options.IncludeSections?.Contains(
                SectionNames.Implementers) == true
                ? ExecuteSection(
                    options,
                    population,
                    focusOccurrence,
                    focusType,
                    SubjectRelationForm.Interface,
                    candidateSources,
                    cancellationToken)
                : null;
        TypeHierarchyRelationSectionInspection? derivedTypes =
            options.IncludeSections?.Contains(
                SectionNames.DerivedTypes) == true
                ? ExecuteSection(
                    options,
                    population,
                    focusOccurrence,
                    focusType,
                    SubjectRelationForm.BaseType,
                    candidateSources,
                    cancellationToken)
                : null;
        return new(implementers, derivedTypes);
    }

    private static async Task<(
        TypeHierarchyExactTypeInspection? Inspection,
        string? Error)> ExecuteCoreAsync(
        InspectionWorkspace workspace,
        TypeOptions options,
        ApiSourceResult source,
        CancellationToken cancellationToken)
    {
        AssemblySet assemblySet =
            await AssemblySetResolver.CollectAsync(
                    source.Context.HttpClient,
                    BuildAssemblySetRequest(
                        options,
                        source,
                        exactPlatformFocusPath: null,
                        cancellationToken),
                    source.Context.Logger.Log)
                .ConfigureAwait(false);
        if (assemblySet.Diagnostics.Count > 0)
        {
            assemblySet.Dispose();
            return (
                null,
                string.Join(
                    Environment.NewLine,
                    assemblySet.Diagnostics.Select(
                        static diagnostic => diagnostic.Message)));
        }
        if (assemblySet.Assemblies.Count == 0)
        {
            assemblySet.Dispose();
            return (null, "The selected source contains no managed libraries.");
        }

        var contexts =
            ImmutableArray.CreateBuilder<WorkspaceDeclarationContext>(
                assemblySet.Assemblies.Count);
        var candidateSources =
            new Dictionary<
                object,
                TypeHierarchyRelationCandidateSource>(
                    ReferenceEqualityComparer.Instance);
        string? exactPlatformFocusPath =
            SelectExactPlatformFocusPath(options, source);
        bool exactPlatformFocusMissing =
            exactPlatformFocusPath is not null
            && !assemblySet.Assemblies.Any(
                entry => PathsEqual(entry.Path, exactPlatformFocusPath));
        (int? preferredIndex, string? preferredError) =
            SelectPreferredEntry(
                assemblySet.Assemblies,
                source,
                exactPlatformFocusPath);
        if ((preferredIndex is null || exactPlatformFocusMissing)
            && IsPlatformSource(options, source)
            && File.Exists(
                exactPlatformFocusPath
                    ?? source.SearchPath))
        {
            assemblySet.Dispose();
            assemblySet =
                await AssemblySetResolver.CollectAsync(
                        source.Context.HttpClient,
                        BuildAssemblySetRequest(
                            options,
                            source,
                            exactPlatformFocusPath
                                ?? source.SearchPath,
                            cancellationToken),
                        source.Context.Logger.Log)
                    .ConfigureAwait(false);
            if (assemblySet.Diagnostics.Count > 0)
            {
                assemblySet.Dispose();
                return (
                    null,
                    string.Join(
                        Environment.NewLine,
                        assemblySet.Diagnostics.Select(
                            static diagnostic => diagnostic.Message)));
            }

            (preferredIndex, preferredError) =
                SelectPreferredEntry(
                    assemblySet.Assemblies,
                    source,
                    exactPlatformFocusPath);
        }
        if (preferredIndex is null)
        {
            assemblySet.Dispose();
            return (null, preferredError);
        }
        string? packageExtractPath =
            PackagePopulationRoot(
                assemblySet.Assemblies[preferredIndex.Value],
                source);
        AssemblyContextGroup? focusGroup = null;
        bool transferredFocusGroup = false;

        try
        {
            for (int index = 0; index < assemblySet.Assemblies.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssemblySetEntry entry = assemblySet.Assemblies[index];
                bool isPreferred = index == preferredIndex;
                AssemblySetEntry declarationEntry =
                    isPreferred
                    && IsPlatformSource(options, source)
                    && entry.SourceKind == AssemblySetSourceKind.Assembly
                        ? ExactPlatformFocusEntry(entry, source)
                        : entry;
                AssemblyResolutionProvenance provenance =
                    ProvenanceFor(
                        declarationEntry,
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

                var participant = new AssemblyContextParticipant(
                    assembly,
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            entry.Path)));
                var identity =
                    new ManagedMetadataIdentity.Assembly(assembly.Identity);
                ExactLibrarySourceCoordinate coordinate =
                    CoordinateFor(declarationEntry, options, identity);
                string digest = await DigestAsync(
                        entry.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                string contentRef =
                    $"cli-hierarchy/{index + 1}/{Path.GetFileName(entry.Path)}";
                WorkspaceMemberCoordinate declared =
                    WorkspaceMemberCoordinate.Embedded(
                        contentRef,
                        digest,
                        assembly.Identity.Name);
                var realized =
                    new RealizedMemberCoordinate.Embedded(
                        contentRef,
                        digest,
                        assembly.Identity.Name);
                var contextInput =
                    new WorkspaceContextInput
                    {
                        Framework = options.Tfm ?? entry.Tfm,
                        Members = [declared],
                    };
                FindPackageSourceRequest? packageRequest =
                    coordinate is ExactLibrarySourceCoordinate.Package
                        ? new(
                            options.Tfm ?? entry.Tfm,
                            requestedRuntimeIdentifier: null)
                        : null;

                AssemblyContextLibraryAdapterResult materialization;
                using (AssemblyContextGroup group =
                    workspace.CreateAssemblyContextGroup([participant]))
                {
                    materialization =
                        await AssemblyContextLibraryAdapter.MaterializeAsync(
                                group,
                                participant,
                                AssemblyContextLibraryRole.ApiOnly,
                                MaterializationLimits,
                                cancellationToken)
                            .ConfigureAwait(false);
                }
                if (materialization
                    is not AssemblyContextLibraryAdapterResult.Completed
                        completed)
                {
                    return (
                        null,
                        $"The hierarchy source '{entry.Path}' could not be "
                            + "materialized "
                            + $"({MaterializationFailure(materialization)}).");
                }

                WorkspaceRegistrationRevision registrations =
                    workspace.GetRegistrationSnapshot() switch
                    {
                        WorkspaceRegistrationReadResult.Available available =>
                            available.Revision,
                        _ => throw new InvalidOperationException(
                            "An active Workspace must expose its registration "
                                + "revision."),
                    };
                WorkspaceLibraryAdmissionOutcome admission =
                    await workspace.AdmitLibraryBatchAsync(
                            registrations,
                            completed.Artifacts,
                            [completed.Owner])
                        .ConfigureAwait(false);
                if (admission
                    is not WorkspaceLibraryAdmissionOutcome.Accepted accepted)
                {
                    return (
                        null,
                        $"The hierarchy source '{entry.Path}' could not be "
                            + $"admitted ({admission.GetType().Name}).");
                }

                ArtifactAcquisitionRegistration artifactRegistration =
                    AssertSingleOccurrence(accepted.Receipt)
                        .Library.ApiAssembly.Registration;
                if (isPreferred)
                {
                    if (focusGroup is not null)
                    {
                        return (
                            null,
                            "The selected hierarchy source matched more than "
                                + "one defining assembly.");
                    }

                    ResolvedAssemblyReference? acquiredAssembly =
                        ResolvedAssemblyReference
                            .CreateFromArtifactPathIfManaged(
                                artifactRegistration,
                                entry.Path,
                                () => File.Open(
                                    entry.Path,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read),
                                provenance,
                                File.GetLastWriteTimeUtc(entry.Path));
                    if (acquiredAssembly is null)
                    {
                        return (
                            null,
                            $"The exact hierarchy artifact '{entry.Path}' "
                                + "does not contain managed assembly metadata.");
                    }

                    var acquiredParticipant =
                        new AssemblyContextParticipant(
                            acquiredAssembly,
                            NoResolverAssemblyBindingPolicy.Instance);
                    focusGroup =
                        workspace.CreateAssemblyContextGroup(
                            [acquiredParticipant],
                            new AssemblyContextGroupOptions
                            {
                                MaxRetainedImageBytes =
                                    MaxAssemblyImageBytes,
                            });
                    WorkspaceAssemblyDeclarationContextAdmissionOutcome
                        focusAdmission =
                            WorkspaceAssemblyDeclarationContextAdmission.Admit(
                                workspace,
                                focusGroup,
                                contextInput,
                                [
                                    new(
                                        acquiredParticipant,
                                        coordinate,
                                        declared,
                                        realized,
                                        packageRequest),
                                ]);
                    if (focusAdmission
                        is not
                            WorkspaceAssemblyDeclarationContextAdmissionOutcome
                                .Admitted admittedFocus)
                    {
                        return (
                            null,
                            $"The exact hierarchy declaration for "
                                + $"'{entry.Path}' could not be admitted "
                                + $"({focusAdmission.GetType().Name}).");
                    }

                    contexts.Add(admittedFocus.Context);
                    candidateSources.Add(
                        artifactRegistration,
                        new(
                            acquiredAssembly.Identity.Name,
                            SourceFor(
                                declarationEntry,
                                options,
                                packageExtractPath)));
                    continue;
                }

                WorkspaceLibraryDeclarationContextAdmissionOutcome declaration =
                    WorkspaceLibraryDeclarationContextAdmission.Admit(
                        workspace,
                        accepted.Receipt,
                        new WorkspaceDeclarationRequest.ContextLoad(
                            contextInput),
                        [
                            new WorkspaceLibraryDeclarationContextMember(
                                coordinate,
                                assembly.Identity,
                                new WorkspaceDeclarationOrigin.ContextLoad(
                                    declared,
                                    realized),
                                provenance,
                                packageRequest),
                        ],
                        InventoryBounds);
                if (declaration
                    is not WorkspaceLibraryDeclarationContextAdmissionOutcome
                        .Admitted admitted)
                {
                    return (
                        null,
                        $"The hierarchy declaration for '{entry.Path}' could "
                            + $"not be admitted ({declaration.GetType().Name}).");
                }

                contexts.Add(admitted.Context);
                candidateSources.Add(
                    artifactRegistration,
                    new(
                        assembly.Identity.Name,
                        SourceFor(
                            declarationEntry,
                            options,
                            packageExtractPath)));
            }

            if (focusGroup is null)
            {
                return (
                    null,
                    "The exact assembly selected for the Type was not present "
                        + "in the hierarchy population.");
            }

            ImmutableArray<WorkspaceDeclarationContext> selectedContexts =
                contexts.ToImmutable();
            WorkspaceDeclarationPopulationCapture capture =
                workspace.CaptureDeclarationPopulation(selectedContexts);
            if (capture
                is not WorkspaceDeclarationPopulationCapture.Captured captured)
            {
                return (
                    null,
                    $"The hierarchy declaration population could not be "
                        + "captured "
                        + $"({((WorkspaceDeclarationPopulationCapture.Rejected)capture).Failure}).");
            }

            TypeDeclarationLocatorInspectionExecution locator =
                await TypeDeclarationLocatorInspection.ExecuteWithResultAsync(
                        workspace,
                        [
                            new TypeDeclarationLocatorRequest.Pattern(
                                source.TypeName!),
                        ],
                        TypeDeclarationLocatorSectionPlan.All,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (locator.LocatorResult
                is not TypeDeclarationLocatorResult.Evaluated evaluated)
            {
                return (
                    null,
                    "The exact Type could not be located in the hierarchy "
                        + "declaration population.");
            }

            ImmutableArray<TypeDeclarationLocatorCandidate> definitions =
            [
                .. evaluated.Answers.Single().Candidates.Where(
                    candidate =>
                        candidate.Kind
                            == AssemblyTypeDeclarationKind.Definition
                        && candidate.Observation.Occurrence.ContextOrder
                            == selectedContexts.Single(context =>
                                ReferenceEquals(
                                    context.Group,
                                    focusGroup)).Receipt.Order),
            ];
            ImmutableArray<TypeDeclarationLocatorCandidate>
                exactSpellingDefinitions =
            [
                .. definitions.Where(candidate =>
                    string.Equals(
                        candidate.Name.ToEscapedFullName(),
                        source.TypeName,
                        StringComparison.Ordinal)),
            ];
            if (!exactSpellingDefinitions.IsEmpty)
                definitions = exactSpellingDefinitions;
            if (definitions.Length != 1)
            {
                return (
                    null,
                    definitions.IsEmpty
                        ? $"Type '{source.TypeName}' was not found in the "
                            + "selected defining assembly."
                        : $"Type '{source.TypeName}' is ambiguous in the "
                            + "selected defining assembly.");
            }

            TypeDeclarationLocatorCandidate focus = definitions[0];
            (TypeDeclarationLocatorCandidate? relationFocus,
                string? relationFocusError) =
                SelectRelationFocus(
                    options,
                    source,
                    assemblySet.Assemblies,
                    selectedContexts,
                    [
                        .. evaluated.Answers.Single().Candidates.Where(
                            candidate =>
                                candidate.Kind
                                    == AssemblyTypeDeclarationKind.Definition),
                    ],
                    focus);
            if (relationFocus is null)
            {
                return (null, relationFocusError);
            }
            WorkspaceDeclarationContext focusContext =
                selectedContexts.Single(context =>
                    context.Receipt.Members.Any(member =>
                        ReferenceEquals(
                            member.Occurrence,
                            focus.Observation.Occurrence)));
            SelectedContextExactTypeLiveTarget? liveTarget = null;
            InspectionEnvelope<SelectedContextExactTypeInspectionResult>
                exactType =
                    SelectedContextExactTypeInspectionOperation
                        .ExecuteWithLiveTarget(
                            workspace,
                            focusContext,
                            new SelectedContextExactTypeInspectionRequest(
                                focus.Name.ToEscapedFullName()),
                            target => liveTarget = target,
                            scope: options.IncludeAll
                                ? ApiSurfaceScope.IncludeAll
                                : ApiSurfaceScope.PublicWithNonPublicTypes);
            if (!exactType.Content.Inspection.IsAvailable)
            {
                transferredFocusGroup = true;
                return (
                    new(
                        exactType,
                        Target: null,
                        Source: null,
                        Relations: null,
                        packageExtractPath,
                        focusGroup,
                        assemblySet),
                    null);
            }

            SelectedContextExactTypeSource exactSource =
                exactType.Content.DefiningSources.Length == 1
                    ? exactType.Content.DefiningSources[0]
                    : throw new InvalidOperationException(
                        "An available hierarchy Type requires one exact "
                            + "defining source.");
            if (!ReferenceEquals(
                    exactSource.Occurrence,
                    focus.Observation.Occurrence))
            {
                return (
                    null,
                    "The ordinary Type inspection did not preserve the "
                        + "owner-issued hierarchy focus occurrence.");
            }

            WorkspaceDeclarationPopulation relationPopulation =
                captured.Population;
            if (IsPlatformSource(options, source)
                && !ReferenceEquals(
                    relationFocus.Observation.Occurrence,
                    focus.Observation.Occurrence))
            {
                WorkspaceDeclarationPopulationCapture relationCapture =
                    workspace.CaptureDeclarationPopulation(
                        selectedContexts.Where(context =>
                            !ReferenceEquals(context, focusContext))
                        .ToImmutableArray());
                if (relationCapture
                    is not WorkspaceDeclarationPopulationCapture.Captured
                        capturedRelations)
                {
                    return (
                        null,
                        "The Platform hierarchy relation population could "
                            + "not exclude the duplicate implementation "
                            + "focus occurrence.");
                }
                relationPopulation = capturedRelations.Population;
            }

            TypeHierarchyRelationsInspection relations =
                Execute(
                    options,
                    relationPopulation,
                    relationFocus.Observation.Occurrence,
                    relationFocus.Name,
                    candidateSources,
                    cancellationToken);
            transferredFocusGroup = true;
            return (
                new(
                    exactType,
                    liveTarget
                        ?? throw new InvalidOperationException(
                            "An available hierarchy Type requires one live "
                                + "inspection target."),
                    exactSource,
                    relations,
                    packageExtractPath,
                    focusGroup,
                    assemblySet,
                    new(
                        exactSource,
                        relationFocus.Observation.Occurrence,
                        relationFocus.Name)),
                null);
        }
        finally
        {
            if (!transferredFocusGroup)
            {
                focusGroup?.Dispose();
                assemblySet.Dispose();
            }
        }
    }

    private static TypeHierarchyRelationSectionInspection ExecuteSection(
        TypeOptions options,
        WorkspaceDeclarationPopulation population,
        WorkspaceDeclarationOccurrence focusOccurrence,
        MetadataTypeDefinitionName focusType,
        SubjectRelationForm form,
        IReadOnlyDictionary<
            object,
            TypeHierarchyRelationCandidateSource> candidateSources,
        CancellationToken cancellationToken)
    {
        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(focusOccurrence, focusType),
                    CreatePopulationRequest(options, form)),
                OperationPolicy,
                includeNonPublic: options.IncludeAll,
                includeHidden: options.IncludeAll,
                cancellationToken: cancellationToken);
        ImmutableArray<WorkspaceTypeHierarchyCandidate> candidates =
            form == SubjectRelationForm.Interface
                ? execution.Inspection.Content.Implementers
                : execution.Inspection.Content.DerivedTypes;
        return new(
            execution.Inspection,
            [
                .. candidates.Select(candidate =>
                    Project(candidate, candidateSources)),
            ]);
    }

    internal static TypeHierarchyRelationCandidate Project(
        WorkspaceTypeHierarchyCandidate candidate,
        IReadOnlyDictionary<
            object,
            TypeHierarchyRelationCandidateSource> sources)
    {
        var identity =
            candidate.Type.Identity
                as InspectionGraphTypeIdentity.AcquiredDefinition
                ?? throw new InvalidOperationException(
                    "Workspace hierarchy candidates require acquired "
                        + "definition identity.");
        object registration =
            identity.Registration.ArtifactRegistration is { } artifact
                ? artifact
                : identity.Registration;
        if (!sources.TryGetValue(
                registration,
                out TypeHierarchyRelationCandidateSource? source))
        {
            throw new InvalidOperationException(
                "Workspace hierarchy candidate source correspondence was "
                    + "lost.");
        }

        return new(
            identity.Type.ToMetadataFullName(),
            source.Library,
            source.Source);
    }

    private static AssemblySetRequest BuildAssemblySetRequest(
        TypeOptions options,
        ApiSourceResult source,
        string? exactPlatformFocusPath,
        CancellationToken cancellationToken)
    {
        if (options.PackageRangeAddress is not null)
        {
            throw new InvalidOperationException(
                "Hierarchy relation sections do not support unresolved "
                    + "package range addresses.");
        }

        bool isPlatform = IsPlatformSource(options, source);
        bool isLocal =
            string.Equals(
                source.ApiSource,
                SourceKind.Library,
                StringComparison.Ordinal);
        return new()
        {
            Packages =
                options.PackagePath is { } package
                    ? [package]
                    : [],
            Assemblies =
                options.PackagePath is null
                    && options.AssemblyPath is { } assembly
                    ? [assembly]
                    : isPlatform
                        && exactPlatformFocusPath is not null
                        && File.Exists(exactPlatformFocusPath)
                        ? [exactPlatformFocusPath]
                    : isLocal && File.Exists(source.SearchPath)
                        ? [source.SearchPath]
                        : [],
            Projects =
                options.ProjectPath is { } project
                    ? [project]
                    : [],
            PlatformFrameworks =
                isPlatform
                    ? PlatformHierarchyFamilies(options)
                    : [],
            Tfm = options.Tfm ?? source.SelectedTfm,
            SourceOptions = options.SourceOptions,
            TempDirPrefix = "inspect-type-hierarchy",
            IncludePackageRuntimeAssemblies =
                PackageRelativePath(source) is { } packageAsset
                && packageAsset.StartsWith(
                    "runtimes/",
                    StringComparison.OrdinalIgnoreCase),
            CancellationToken = cancellationToken,
        };
    }

    private static AssemblySetEntry ExactPlatformFocusEntry(
        AssemblySetEntry entry,
        ApiSourceResult source) =>
        new(
            entry.Path,
            source.PlatformFramework ?? "runtime",
            source.ApiVersion,
            AssemblySetSourceKind.PlatformAssembly,
            source.SelectedTfm);

    private static string? SelectExactPlatformFocusPath(
        TypeOptions options,
        ApiSourceResult source)
    {
        if (!IsPlatformSource(options, source)
            || string.IsNullOrWhiteSpace(source.TypeName))
        {
            return null;
        }

        if (source.RuntimeAssemblyPath is { } runtimePath
            && File.Exists(runtimePath))
        {
            string? runtimeFocus =
                ResolvePlatformFocusPath(
                    options,
                    source,
                    runtimePath,
                    source.TypeName);
            if (runtimeFocus is not null)
                return runtimeFocus;
        }

        if (!File.Exists(source.SearchPath))
            return null;

        return ResolvePlatformFocusPath(
            options,
            source,
            source.SearchPath,
            source.TypeName);
    }

    private static string? ResolvePlatformFocusPath(
        TypeOptions options,
        ApiSourceResult source,
        string assemblyPath,
        string typeName)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        AssemblyTypeDeclarationInventory inventory =
            session.TypeDeclarations() switch
            {
                AssemblyTypeDeclarationInventoryOutcome.Read read =>
                    read.Inventory,
                AssemblyTypeDeclarationInventoryOutcome.Rejected rejected =>
                    throw new InvalidOperationException(
                        "The selected Platform assembly declaration inventory "
                            + $"could not be read ({rejected.Failure})."),
                AssemblyTypeDeclarationInventoryOutcome.Incomplete incomplete =>
                    throw new InvalidOperationException(
                        "The selected Platform assembly declaration inventory "
                            + $"exceeded {incomplete.Bound}."),
                _ => throw new InvalidOperationException(
                    "Unknown Platform assembly declaration inventory outcome."),
            };
        AssemblyTypeDeclaration[] declarations =
        [
            .. inventory.Declarations.Where(declaration =>
                TypeMatcher.MatchesTypeFilter(
                    declaration.Name,
                    typeName)),
        ];
        AssemblyTypeDeclaration[] exactSpellingDeclarations =
        [
            .. declarations.Where(declaration =>
                string.Equals(
                    declaration.Name.ToEscapedFullName(),
                    typeName,
                    StringComparison.Ordinal)),
        ];
        if (exactSpellingDeclarations.Length > 0)
            declarations = exactSpellingDeclarations;
        if (declarations.Length != 1)
            return null;

        AssemblyTypeDeclaration selected = declarations[0];
        if (selected.Kind == AssemblyTypeDeclarationKind.Definition)
            return assemblyPath;
        if (selected.Kind != AssemblyTypeDeclarationKind.Forwarder)
            return null;

        using var resolution =
            new TypeDefinitionResolutionSession(
                assemblyPath,
                isPlatformAssembly: true,
                source.ProjectAssetsPath,
                options.Tfm ?? source.SelectedTfm,
                source.PlatformFramework
                    ?? options.PlatformFramework,
                packageDirectory: null,
                options.SourceOptions);
        TypeResolutionOutcome outcome =
            resolution.Resolve(selected.Name);
        return outcome switch
        {
            TypeResolutionOutcome.Resolved
            {
                Definition.Assembly.Assembly.Path: { } path,
            } => path,
            TypeResolutionOutcome.Resolved =>
                throw new InvalidOperationException(
                    "The selected Platform Type definition has no filesystem "
                        + "path."),
            _ => throw new InvalidOperationException(
                $"The selected Platform Type forwarder could not be resolved "
                    + $"({outcome.GetType().Name})."),
        };
    }

    internal static IReadOnlyList<string> PlatformHierarchyFamilies(
        TypeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.IsNullOrWhiteSpace(options.PlatformFramework)
            ? [.. ScopeConstants.PlatformFrameworks]
            : [options.PlatformFramework];
    }

    private static bool IsPlatformSource(
        TypeOptions options,
        ApiSourceResult source) =>
        string.Equals(
            source.ApiSource,
            SourceKind.Platform,
            StringComparison.Ordinal)
        || options.PlatformAssembly is not null
        || options.PlatformFramework is not null;

    private static (
        TypeDeclarationLocatorCandidate? Focus,
        string? Error) SelectRelationFocus(
        TypeOptions options,
        ApiSourceResult source,
        IReadOnlyList<AssemblySetEntry> entries,
        ImmutableArray<WorkspaceDeclarationContext> contexts,
        ImmutableArray<TypeDeclarationLocatorCandidate> definitions,
        TypeDeclarationLocatorCandidate subject)
    {
        if (!IsPlatformSource(options, source))
            return (subject, null);

        int subjectIndex = -1;
        for (int index = 0; index < contexts.Length; index++)
        {
            if (contexts[index].Receipt.Order
                == subject.Observation.Occurrence.ContextOrder)
            {
                subjectIndex = index;
                break;
            }
        }
        if (subjectIndex < 0)
        {
            return (
                null,
                "The exact Platform Type focus was not retained in the "
                    + "hierarchy declaration population.");
        }
        if (entries[subjectIndex].SourceKind
            == AssemblySetSourceKind.PlatformFramework)
        {
            return (subject, null);
        }

        HashSet<int> referenceContextOrders =
        [
            .. contexts
                .Select((context, index) => (context, index))
                .Where(candidate =>
                    entries[candidate.index].SourceKind
                        == AssemblySetSourceKind.PlatformFramework)
                .Select(candidate => candidate.context.Receipt.Order),
        ];
        ImmutableArray<TypeDeclarationLocatorCandidate> corresponding =
        [
            .. definitions.Where(candidate =>
                referenceContextOrders.Contains(
                    candidate.Observation.Occurrence.ContextOrder)
                && candidate.Name.Equals(subject.Name)),
        ];
        return corresponding.Length switch
        {
            1 => (corresponding[0], null),
            0 => (
                null,
                "The exact Platform implementation Type has no corresponding "
                    + "definition in the selected reference hierarchy "
                    + "population."),
            _ => (
                null,
                "The exact Platform implementation Type has more than one "
                    + "corresponding definition in the selected reference "
                    + "hierarchy population. Use --framework to narrow the "
                    + "population."),
        };
    }

    private static (int? Index, string? Error) SelectPreferredEntry(
        IReadOnlyList<AssemblySetEntry> entries,
        ApiSourceResult source,
        string? exactPlatformFocusPath)
    {
        if (exactPlatformFocusPath is not null)
        {
            int? exactPlatformFocus =
                SelectUniqueEntry(
                    entries,
                    entry => PathsEqual(
                        entry.Path,
                        exactPlatformFocusPath));
            if (exactPlatformFocus is not null)
                return (exactPlatformFocus, null);
        }

        int? exactPath =
            SelectUniqueEntry(
                entries,
                entry => PathsEqual(entry.Path, source.SearchPath));
        if (exactPath is not null)
            return (exactPath, null);

        string? packageRelativePath =
            PackageRelativePath(source);
        if (packageRelativePath is not null)
        {
            int? packageAsset =
                SelectUniqueEntry(
                    entries,
                    entry =>
                        entry.SourceKind == AssemblySetSourceKind.Package
                        && HasRelativeSuffix(
                            entry.Path,
                            packageRelativePath));
            if (packageAsset is not null)
                return (packageAsset, null);
        }

        if (source.RuntimeAssemblyPath is { } runtimePath)
        {
            int? runtime =
                SelectUniqueEntry(
                    entries,
                    entry => PathsEqual(entry.Path, runtimePath));
            if (runtime is not null)
                return (runtime, null);
        }

        AssemblyReferenceIdentity? preferredIdentity =
            PreferredAssemblyIdentity(source);
        if (preferredIdentity is null)
        {
            return (
                null,
                "The exact assembly selected for the Type could not be "
                    + "identified.");
        }

        int? identityMatch =
            SelectUniqueEntry(
                entries,
                entry =>
                    ResolvedAssemblyReference.CreateFromPathIfManaged(
                        entry.Path,
                        AssemblyResolutionProvenance.Local(entry.Path))
                        ?.Identity.IsEquivalentTo(preferredIdentity) == true);
        return identityMatch is not null
            ? (identityMatch, null)
            : (
                null,
                "The exact assembly selected for the Type was not present "
                    + "as one unambiguous hierarchy population entry.");
    }

    private static int? SelectUniqueEntry(
        IReadOnlyList<AssemblySetEntry> entries,
        Func<AssemblySetEntry, bool> predicate)
    {
        int? selected = null;
        for (int index = 0; index < entries.Count; index++)
        {
            if (!predicate(entries[index]))
                continue;
            if (selected is not null)
                return null;
            selected = index;
        }
        return selected;
    }

    private static string? PackageRelativePath(ApiSourceResult source)
    {
        if (source.PackageExtractPath is not { } packageRoot
            || !File.Exists(source.SearchPath))
        {
            return null;
        }

        string relativePath =
            Path.GetRelativePath(packageRoot, source.SearchPath)
                .Replace('\\', '/');
        return PackageEntryPath.IsSafeRelativePath(relativePath)
            ? relativePath
            : null;
    }

    private static bool HasRelativeSuffix(
        string path,
        string relativePath)
    {
        string normalizedPath =
            Path.GetFullPath(path).Replace('\\', '/');
        return normalizedPath.EndsWith(
            "/" + relativePath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? PackagePopulationRoot(
        AssemblySetEntry selectedEntry,
        ApiSourceResult source)
    {
        if (selectedEntry.SourceKind != AssemblySetSourceKind.Package
            || PackageRelativePath(source) is not { } relativePath)
        {
            return null;
        }

        string normalizedPath =
            Path.GetFullPath(selectedEntry.Path).Replace('\\', '/');
        string suffix = "/" + relativePath;
        if (!normalizedPath.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalizedPath[..^suffix.Length]
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string? PackageAssetPath(
        AssemblySetEntry entry,
        string? packageExtractPath)
    {
        if (entry.SourceKind != AssemblySetSourceKind.Package
            || packageExtractPath is null)
        {
            return null;
        }

        string relativePath =
            Path.GetRelativePath(packageExtractPath, entry.Path)
                .Replace('\\', '/');
        return PackageEntryPath.IsSafeRelativePath(relativePath)
            ? relativePath
            : null;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static AssemblyReferenceIdentity? PreferredAssemblyIdentity(
        ApiSourceResult source)
    {
        string? path =
            File.Exists(source.SearchPath)
                ? source.SearchPath
                : source.RuntimeAssemblyPath is { } runtimePath
                    && File.Exists(runtimePath)
                        ? runtimePath
                        : null;
        return path is null
            ? null
            : ResolvedAssemblyReference.CreateFromPathIfManaged(
                    path,
                    AssemblyResolutionProvenance.Local(path))
                ?.Identity;
    }

    private static WorkspaceLibraryOccurrence AssertSingleOccurrence(
        WorkspaceLibraryAdmissionReceipt receipt) =>
        receipt.Occurrences.Length == 1
            ? receipt.Occurrences[0]
            : throw new InvalidOperationException(
                "One adapted hierarchy Library must produce one admitted "
                    + "occurrence.");

    private static AssemblyResolutionProvenance ProvenanceFor(
        AssemblySetEntry entry,
        TypeOptions options,
        string? packageExtractPath) =>
        entry.SourceKind switch
        {
            AssemblySetSourceKind.Package
                when entry.Version is { } version =>
                AssemblyResolutionProvenance.Package(
                    entry.Source,
                    version,
                    entry.Tfm,
                    rid: null,
                    assetPath: PackageAssetPath(
                        entry,
                        packageExtractPath)),
            AssemblySetSourceKind.PlatformAssembly
                or AssemblySetSourceKind.PlatformFramework =>
                AssemblyResolutionProvenance.Platform(
                    entry.Source,
                    entry.Version,
                    "CLI Type hierarchy population"),
            AssemblySetSourceKind.Project =>
                AssemblyResolutionProvenance.Project(
                    options.ProjectPath
                        ?? options.ProjectAssetsPath
                        ?? "restored project",
                    options.Tfm ?? entry.Tfm,
                    rid: null),
            _ => AssemblyResolutionProvenance.Local(
                string.IsNullOrWhiteSpace(entry.Source)
                    ? entry.Path
                    : entry.Source),
        };

    private static ExactLibrarySourceCoordinate CoordinateFor(
        AssemblySetEntry entry,
        TypeOptions options,
        ManagedMetadataIdentity.Assembly identity) =>
        entry.SourceKind switch
        {
            AssemblySetSourceKind.Package
                when entry.Version is { } version =>
                new ExactLibrarySourceCoordinate.Package(
                    PackageSourceCoordinate.Create(entry.Source, version),
                    identity),
            AssemblySetSourceKind.PlatformAssembly
                or AssemblySetSourceKind.PlatformFramework =>
                new ExactLibrarySourceCoordinate.Platform(
                    new(
                        IsAspNetCore(entry.Source, options)
                            ? PlatformFamily.AspNetCore
                            : PlatformFamily.DotNetRuntime),
                    identity),
            AssemblySetSourceKind.Project =>
                new ExactLibrarySourceCoordinate.Project(identity),
            _ => new ExactLibrarySourceCoordinate.Local(identity),
        };

    private static bool IsAspNetCore(
        string source,
        TypeOptions options) =>
        source.StartsWith(
            "aspnetcore",
            StringComparison.OrdinalIgnoreCase)
        || options.PlatformFramework?.StartsWith(
            "aspnetcore",
            StringComparison.OrdinalIgnoreCase) == true;

    private static string SourceFor(
        AssemblySetEntry entry,
        TypeOptions options,
        string? packageExtractPath) =>
        entry.SourceKind switch
        {
            AssemblySetSourceKind.Package =>
                PackageSourceFor(
                    entry,
                    packageExtractPath),
            AssemblySetSourceKind.Project =>
                options.ProjectPath
                    ?? options.ProjectAssetsPath
                    ?? entry.Source,
            AssemblySetSourceKind.PlatformAssembly
                or AssemblySetSourceKind.PlatformFramework =>
                entry.Version is null
                    ? entry.Source
                    : $"{entry.Source}@{entry.Version}",
            _ => entry.Path,
        };

    private static string PackageSourceFor(
        AssemblySetEntry entry,
        string? packageExtractPath)
    {
        string package =
            entry.Version is null
                ? entry.Source
                : $"{entry.Source}@{entry.Version}";
        return PackageAssetPath(
                entry,
                packageExtractPath) is { } assetPath
            ? $"{package} ({assetPath})"
            : package;
    }

    private static async Task<string> DigestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] digest =
            await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }

    private static string MaterializationFailure(
        AssemblyContextLibraryAdapterResult result) =>
        result switch
        {
            AssemblyContextLibraryAdapterResult.SnapshotRejected rejected =>
                rejected.Failure.Detail,
            AssemblyContextLibraryAdapterResult.Incomplete incomplete =>
                $"image size {incomplete.RequiredImageBytes} exceeds "
                    + $"{incomplete.MaxCapturedImageBytes}",
            AssemblyContextLibraryAdapterResult.PortablePdbRejected =>
                "Portable PDB rejected",
            AssemblyContextLibraryAdapterResult.ArtifactNotPublished =>
                "artifact publication failed",
            AssemblyContextLibraryAdapterResult.MetadataNotProjected =>
                "metadata projection failed",
            _ => result.GetType().Name,
        };
}

internal sealed record TypeHierarchyExactTypeInspection(
    InspectionEnvelope<SelectedContextExactTypeInspectionResult> ExactType,
    SelectedContextExactTypeLiveTarget? Target,
    SelectedContextExactTypeSource? Source,
    TypeHierarchyRelationsInspection? Relations,
    string? PackageExtractPath,
    AssemblyContextGroup? FocusGroup,
    AssemblySet AssemblySet,
    TypeHierarchyRelationBinding? RelationBinding = null);

internal sealed record TypeHierarchyRelationBinding
{
    internal TypeHierarchyRelationBinding(
        SelectedContextExactTypeSource subject,
        WorkspaceDeclarationOccurrence focusOccurrence,
        MetadataTypeDefinitionName focusType)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        FocusOccurrence = focusOccurrence
            ?? throw new ArgumentNullException(nameof(focusOccurrence));
        FocusType = focusType
            ?? throw new ArgumentNullException(nameof(focusType));
        if (!Subject.Type.Namespace.Equals(
                FocusType.Namespace,
                StringComparison.Ordinal)
            || !Subject.Type.Segments.SequenceEqual(
                FocusType.Segments,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A hierarchy relation binding must preserve the exact "
                    + "selected Type definition identity.",
                nameof(focusType));
        }
    }

    internal SelectedContextExactTypeSource Subject { get; }

    internal WorkspaceDeclarationOccurrence FocusOccurrence { get; }

    internal MetadataTypeDefinitionName FocusType { get; }
}

internal sealed record TypeHierarchyRelationCandidateSource(
    string Library,
    string Source);
