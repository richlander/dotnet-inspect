using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal static class CliExactTypeInspection
{
    static ApiSurfaceProjectionLimits SurfaceLimits { get; } =
        new(
            maxParticipants: 1,
            maxTypes: 100_000,
            maxMembers: 1_000_000,
            maxInspectionFailures: 100_000,
            maxTypeForwarders: 100_000,
            maxMetadataRows: 10_000_000,
            maxRetainedTextCharacters: 100_000_000);

    internal static bool IsEligible(TypeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.TypeName)
            || TypeMatcher.IsTypeGlobPattern(options.TypeName)
            || new TypeGestureIntent(options.TypeFilter)
                .SelectsListingCatalog(options.TypeName)
            || string.IsNullOrWhiteSpace(options.PackagePath)
            || options.PackageRangeAddress is not null
            || string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || options.AssemblyPath is not null
            || options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || options.DllPath is not null
            || options.PdbPath is not null
            || options.SourceRepositories.Length != 0
            || options.PerformanceTriage.HasFilters
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates
            || RequestsExcludedSection(options.IncludeSections)
            || options.DocsExplicitlySet && options.ShowDocs
            || !options.DocsExplicitlySet
                && options.Verbosity >= Verbosity.Normal
            || options.ShowSamples)
        {
            return false;
        }

        var (packageId, version) =
            PackageExtractor.ParsePackageReference(options.PackagePath);
        return !string.IsNullOrWhiteSpace(packageId)
            && !string.IsNullOrWhiteSpace(version)
            && PackageExtractor.TryNormalizePackageVersion(
                version,
                out string pinnedVersion)
            && PackageCoordinateResolver.Validate(
                new PackageCoordinate(packageId, pinnedVersion)) is null;
    }

    internal static async Task<
        InspectionEnvelope<ExactTypeInspectionResult>> ExecuteAsync(
        TypeOptions options,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(
                options,
                loadOptions: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<
        InspectionEnvelope<ExactTypeInspectionResult>> ExecuteAsync(
        TypeOptions options,
        WorkspaceContextLoadOptions? loadOptions,
        CancellationToken cancellationToken = default)
    {
        if (!IsEligible(options))
            throw new ArgumentException(
                "The CLI exact-type Workspace route requires one pinned package, one explicit target framework, and one exact type.",
                nameof(options));

        var (packageId, version) =
            PackageExtractor.ParsePackageReference(options.PackagePath!);
        if (!PackageExtractor.TryNormalizePackageVersion(
                version,
                out string pinnedVersion))
        {
            throw new InvalidOperationException(
                "An eligible package exact-type request has no exact version.");
        }
        var context = new WorkspaceContextInput
        {
            Framework = options.Tfm,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    packageId,
                    pinnedVersion,
                    options.Tfm),
            ],
        };
        var plan = new WorkspacePlan([], [context]);
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidateStartResult start =
            await coordinator.BeginCandidateAsync(plan, cancellationToken)
                .ConfigureAwait(false);
        if (start
            is not WorkspaceRealizationCandidateStartResult.Prepared prepared)
        {
            throw new InvalidOperationException(
                $"CLI exact type Workspace construction was unavailable ({start.GetType().Name}).");
        }

        PackageRootBinding binding;
        PackageAssemblyContextRealization realized;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            WorkspacePackageRootAcquisitionOutcome acquisition =
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                        context,
                        loadOptions
                        ?? new WorkspaceContextLoadOptions
                        {
                            HttpClient = HttpClientFactory.Shared,
                            SourceAuthorization =
                                new SourcePolicyPackageSourceAuthorization(
                                    options.SourceOptions),
                            PackageStore = new FileSystemPackageStore(),
                            UseVersionCache = false,
                            IncludePackageRootBindings = true,
                            Log = options.Verbose
                                ? CommandError.WriteLine
                                : null,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            if (acquisition is WorkspacePackageRootAcquisitionOutcome.Failed failed)
            {
                throw new InvalidOperationException(
                    "CLI exact type package acquisition failed: "
                    + string.Join("; ", failed.Failures.Select(failure => $"{failure.Kind}: {failure.Message}")));
            }
            binding = ((WorkspacePackageRootAcquisitionOutcome.Acquired)acquisition).Root;
            realized = construction.Workspace.RealizePackageAssemblyContextRoles(
                [binding.Root],
                new()
                {
                    MaxAggregateRetainedImageBytes = loadOptions?.MaxRetainedImageBytes
                        ?? AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes,
                },
                cancellationToken);
        }
        using PackageAssemblyContextRealization realization = realized;

        WorkspaceRealizationCandidateCompletionResult completion =
            await coordinator.CompleteCandidateAsync(
                    prepared.Candidate,
                    cancellationToken)
                .ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                    completion;
            throw new InvalidOperationException(
                "CLI exact type Workspace construction was rejected "
                    + $"({rejected.Reason}).");
        }

        WorkspaceRealizationCutoverResult cutover =
            coordinator.CutOver(prepared.Candidate);
        if (cutover is not WorkspaceRealizationCutoverResult.Activated)
        {
            var rejected =
                (WorkspaceRealizationCutoverResult.Rejected)cutover;
            throw new InvalidOperationException(
                "CLI exact type Workspace activation was rejected "
                    + $"({rejected.Reason}).");
        }

        WorkspaceRealizationOperationAdmission admission =
            await coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission is not WorkspaceRealizationOperationAdmission.Admitted)
        {
            var unavailable =
                (WorkspaceRealizationOperationAdmission.Unavailable)admission;
            throw new InvalidOperationException(
                "CLI exact type Workspace operation was unavailable "
                    + $"({unavailable.Reason}).");
        }

        using WorkspaceRealizationOperationLease operation =
            ((WorkspaceRealizationOperationAdmission.Admitted)admission).Lease;
        return await ExactTypeInspection.ExecuteAsync(
            new ExactTypeInspectionRequest(
                ContextIndex: 0,
                TypeSelector: options.TypeName!,
                Scope: options.IncludeAll
                    ? ApiSurfaceScope.IncludeAll
                    : ApiSurfaceScope.Public,
                SurfaceLimits),
            operation,
            binding,
            realization,
            cancellationToken).ConfigureAwait(false);
    }

    internal static (
        ApiSourceResult Source,
        ApiServices.LoadedApiSurface Loaded) AdaptAvailable(
        TypeOptions options,
        ExactTypeInspectionResult.Available available)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(available);
        if (available.Candidate.Supplier
            is not ExactLibrarySourceCoordinate.Package package)
        {
            throw new InvalidOperationException(
                "An eligible package exact-type result has no package supplier.");
        }
        // Keep the user's package casing for presentation; identity remains
        // the source-issued coordinate in the envelope.
        var (requestedPackageId, _) =
            PackageExtractor.ParsePackageReference(options.PackagePath!);
        string packageId = requestedPackageId.Equals(
            package.PackageCoordinate.PackageId, StringComparison.OrdinalIgnoreCase)
                ? requestedPackageId
                : package.PackageCoordinate.PackageId;
        string version = package.PackageCoordinate.Version;
        string library = available.Candidate.SupplierAssembly.Name;
        var source = new ApiSourceResult(
            SearchPath: library,
            RuntimeAssemblyPath: null,
            PackageName: packageId,
            PackageVersion: version,
            ResolvedPackagePath: $"{packageId}@{version}",
            PackageExtractPath: null,
            ApiSource: SourceKind.NuGet,
            ApiVersion: version,
            PlatformFramework: null,
            SelectedTfm: options.Tfm,
            ProjectAssetsPath: null,
            TempDir: null,
            TypeName: available.Type.FullName,
            PackageReplaySourceUrls: null,
            PackageReplayUsesOriginalSources: false,
            Context: new CommandContext(options.Verbose));
        // Compatibility container for existing rendering only, not an API
        // extraction result or a source that can be reopened.
        var surface = new ApiSurface
        {
            Name = packageId,
            Version = version,
            Source = "nuget",
            Tfm = options.Tfm,
            Types = [available.Type],
            InspectionFailures = [.. available.InspectionFailures],
        };
        foreach (ApiSurfaceInspectionFailure failure in available.InspectionFailures)
        {
            if (failure.Operation != ApiSurface.ConstraintResolutionOperation)
                continue;
            var subject = new ApiSurfaceInspectionSubject(null, failure.SubjectToken);
            if (!surface.ConstraintResolutionFailuresBySubject.TryGetValue(subject, out var failures))
                surface.ConstraintResolutionFailuresBySubject.Add(subject, failures = []);
            failures.Add(failure);
        }
        var loaded = new ApiServices.LoadedApiSurface(
            surface,
            library,
            library,
            new Dictionary<ApiType, ResolvedAssemblyReference>());
        return (source, loaded);
    }

    internal static int WriteUnavailable(
        ExactTypeInspectionResult result,
        IReadOnlyList<InspectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(diagnostics);
        switch (result)
        {
            case ExactTypeInspectionResult.NotFound notFound:
                CommandError.Write(
                    $"Type '{result.Request.TypeSelector}' not found.",
                    [
                        .. ApiTypeLookupResult.SuggestionDetails(
                            [
                                .. notFound.Suggestions.Select(
                                    static suggestion =>
                                        suggestion.ToMetadataFullName()),
                            ]),
                    ]);
                break;
            case ExactTypeInspectionResult.Ambiguous ambiguous:
                CommandError.Write(
                    $"Type '{result.Request.TypeSelector}' is ambiguous.",
                    [
                        "",
                        "Candidates:",
                        .. ambiguous.Candidates.Select(candidate =>
                            "  " + candidate.Definition.ToMetadataFullName()),
                    ]);
                break;
            default:
                CommandError.Write(
                    $"Type '{result.Request.TypeSelector}' could not be inspected.",
                    [
                        .. diagnostics.Select(diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.Summary}"),
                    ]);
                break;
        }

        return 1;
    }

    static bool RequestsExcludedSection(IReadOnlyCollection<string>? sections)
    {
        if (sections is null)
            return false;

        return sections.Any(section =>
            section.Equals(
                SectionNames.SourceFiles,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.SourceLocations,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.PdbSource,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.AnnotatedSource,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.AnnotatedSourceDocument,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.SourceDiff,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.DecompiledSource,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.ExceptionRegions,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.BodyShapes,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.BodyShapeSummary,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.CloneCandidates,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.CalledTypes,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.AllocationFacts,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.SafetyFacts,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.CostFacts,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.UnsafeMembers,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.TopLeverage,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                SectionNames.PerformanceTriage,
                StringComparison.OrdinalIgnoreCase));
    }
}
