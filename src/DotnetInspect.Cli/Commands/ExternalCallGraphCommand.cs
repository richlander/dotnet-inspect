using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.ResearchQueries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public static class ExternalCallGraphCommand
{
    public const string Name = "calls";

    public static async Task<int> ExecuteAsync(
        ExternalCallGraphOptions options,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            options,
            CreateLoadOptions(options),
            cancellationToken);

    internal static async Task<int> ExecuteAsync(
        ExternalCallGraphOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken,
        Func<
            PackageDependencyMemberCallGraphInspectionRequest,
            CancellationToken,
            ValueTask<
                InspectionEnvelope<
                    PackageDependencyMemberCallGraphInspectionOutcome>>>?
            inspectionExecutor = null,
        Func<
            PackageDependencyMemberCallGraphInspectionRequest,
            CancellationToken,
            ValueTask<EvidenceInspectionEnvelope<
                PackageDependencyMemberCallGraphInspectionOutcome,
                PackageDependencyMemberCallGraphEvidenceDocument>>>?
            evidenceInspectionExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);
        if (TypeMatcher.IsTypeGlobPattern(options.TypeName))
        {
            CommandError.Write("An external call-graph root requires an exact Type name.");
            return 1;
        }

        string? evidencePath = null;
#if DEBUG
        if (options.EvidenceEnvelopePath is { } requestedEvidencePath
            && !EvidenceEnvelopeOutput.TryResolvePath(
                requestedEvidencePath,
                out evidencePath,
                out string? evidenceError))
        {
            CommandError.Write(evidenceError!);
            return 1;
        }
#endif

        string[] packageInputs = [options.RootPackage];
        if (!InspectionGraphCommand.TryCreateMembers(
                packageInputs,
                out WorkspaceMemberCoordinate[] members))
        {
            return 1;
        }

        var rootMember =
            (WorkspaceMemberCoordinate.PackageMember)members[0];
        var contextInput = new WorkspaceContextInput
        {
            Framework = options.RootTfm,
            Members = members,
        };

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                contextInput,
                loadOptions,
                cancellationToken);

        if (outcome is WorkspaceContextLoadOutcome.Failed failed)
        {
            CommandError.Write(
                "The external call-graph workspace could not be loaded.",
                [
                    .. failed.Failures.Select(static failure =>
                        $"{failure.Kind}: {failure.Message}"),
                ]);
            return 1;
        }

        WorkspaceContextLoadOutcome.Loaded loaded =
            (WorkspaceContextLoadOutcome.Loaded)outcome;
        ResolvedFocus focus;
        using (loaded.Group)
        {
            if (!TryResolveFocus(
                    workspace,
                    loaded,
                    rootMember,
                    options,
                    out focus))
            {
                return 1;
            }
        }

        if (loaded.PackageRoots.Length != 1)
        {
            CommandError.Write(
                "The external call-graph root did not produce one exact package binding.");
            return 1;
        }

        try
        {
            NuGetFetchOptions fetchOptions =
                NuGetFetchOptions.FromRequestTimeout(
                    loadOptions.HttpClient.Timeout);
            PackageHouseOperation realizationOperation =
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize,
                    fetchOptions.RequestTimeout,
                    fetchOptions.OperationTimeout);
            TraversalTargetFrameworkPolicy traversalTargetPolicy =
                options.Tfm is null
                    ? TraversalTargetFrameworkPolicy.ProductDefault
                    : new TraversalTargetFrameworkPolicy(
                        options.Tfm);
            WorkspacePlan workspacePlan = CreateWorkspacePlan(options);
            var inspectionRequest =
                new PackageDependencyMemberCallGraphInspectionRequest(
                    loaded.PackageRoots[0],
                    new PackageDependencyMemberCallGraphInspectionFocus(
                        focus.ModuleVersionId,
                        focus.MethodToken),
                    traversalTargetPolicy,
                    new MemberCallGraphCalleeNeighborhoodRequest(
                        options.Depth,
                        options.MaxNodes),
                    realizationOperation,
                    DateTimeOffset.UtcNow
                        .Add(fetchOptions.OperationTimeout)
                        .Add(fetchOptions.OperationTimeout),
                    supplyChainBaseline:
                        options.SupplyChainBaseline,
                    workspacePlan:
                        workspacePlan,
                    platformPruning:
                        CreateRuntimePlatformPruning(
                            traversalTargetPolicy,
                            workspacePlan,
                            PackageDependencyMemberCallGraphContinuationSource
                                .FindActiveDotnetRoot(),
                            InstalledPlatformPruneSource.Read));
            InspectionEnvelope<
                PackageDependencyMemberCallGraphInspectionOutcome> envelope;
            EvidenceInspectionEnvelope<
                PackageDependencyMemberCallGraphInspectionOutcome,
                PackageDependencyMemberCallGraphEvidenceDocument>? enriched = null;
            if (evidencePath is not null && evidenceInspectionExecutor is not null)
            {
                enriched = await evidenceInspectionExecutor(
                    inspectionRequest, cancellationToken).ConfigureAwait(false);
                envelope = enriched.Inspection;
            }
            else if (inspectionExecutor is not null)
            {
                envelope = await inspectionExecutor(
                    inspectionRequest, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                (envelope, enriched) = await ExecuteInspectionAsync(
                            inspectionRequest,
                            options,
                            fetchOptions,
                            cancellationToken,
                            captureEvidence: evidencePath is not null)
                        .ConfigureAwait(false);
            }
            int exitCode;
            if (envelope.Content
                is PackageDependencyMemberCallGraphInspectionOutcome
                    .Unavailable unavailable)
            {
                CommandError.Write(
                    "The dependency-aware external call graph was unavailable.",
                    [$"{unavailable.Reason}: {unavailable.Detail}"]);
                exitCode = 1;
            }
            else
            {
                var available =
                    (PackageDependencyMemberCallGraphInspectionOutcome
                        .Available)envelope.Content;
                WriteRouteDiagnostics(envelope.Diagnostics);
                exitCode = ExternalCallGraphOutputAdapter.Write(
                    available.Document.Graph,
                    available.Document.NodeClassifications,
                    available.Document.IntrinsicCoreLibraryContinuation,
                    options);
            }
#if DEBUG
            if (evidencePath is not null)
            {
                if (enriched is null)
                {
                    CommandError.Write("The call-graph inspection did not produce its requested evidence.");
                    return 1;
                }
                var json = PackageDependencyMemberCallGraphEvidenceJsonContext.Default;
                var contract = new InspectionEnvelopeJsonContract<
                    PackageDependencyMemberCallGraphInspectionOutcome>(
                        "package-dependency-member-call-graph",
                        1,
                        PackageDependencyMemberCallGraphOutcomeJsonContext.Default
                            .PackageDependencyMemberCallGraphInspectionOutcome);
                if (!InspectionEnvelopeOutput.TrySerializeEvidence(
                        enriched,
                        contract,
                        json.PackageDependencyMemberCallGraphEvidenceDocument,
                        compactJson: false,
                        out byte[] payload,
                        out Exception? serializationError))
                {
                    CommandError.Write(
                        $"Evidence envelope serialization failed for '{evidencePath}': {serializationError!.Message}");
                    return 1;
                }
                if (!EvidenceEnvelopeOutput.TryPublish(
                        evidencePath, payload, out Exception? publicationError))
                {
                    CommandError.Write(
                        $"Evidence envelope publication failed for '{evidencePath}': {publicationError!.Message}");
                    return 1;
                }
                CommandError.WriteLine($"Evidence envelope: {evidencePath}");
            }
#endif
            return exitCode;
        }
        catch (MemberCallGraphAcquisitionException ex)
        {
            CommandError.Write(
                ex.Message,
                [.. ex.Failures.Select(FormatAcquisitionFailure)]);
            return 1;
        }
        catch (InspectionQueryException ex)
        {
            CommandError.Write(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    internal static WorkspaceContextLoadOptions CreateLoadOptions(
        ExternalCallGraphOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            IncludePackageRootBindings = true,
            UseVersionCache = true,
            Log = options.Verbose
                ? CommandError.WriteLine
                : null,
        };

    internal static WorkspacePlan CreateWorkspacePlan(
        ExternalCallGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        WorkspacePlan product =
            options.SupplyChainBaseline
                is PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems
                ? EcosystemPackCatalog.CreatePlatformWorkspacePlan()
                : WorkspacePlan.Empty;
        WorkspaceRegistration[] firstParty =
        [
            .. options.FirstPartyPackagePrefixes.Select(prefix =>
                new WorkspaceRegistration.PackagePrefix(
                    new PackagePrefixDeclaration(prefix))),
        ];
        return new WorkspacePlan(
            [
                .. firstParty,
                .. product.Registrations,
            ]);
    }

    internal static PackageDependencyMemberCallGraphPlatformPruning?
        CreateRuntimePlatformPruning(
            TraversalTargetFrameworkPolicy traversalTargetPolicy,
            WorkspacePlan workspacePlan,
            string? activeDotnetRoot,
            Func<
                string,
                string?,
                InstalledPlatformPruneSource.Result> pruneSource)
    {
        ArgumentNullException.ThrowIfNull(traversalTargetPolicy);
        ArgumentNullException.ThrowIfNull(workspacePlan);
        ArgumentNullException.ThrowIfNull(pruneSource);

        bool runtimePopulationSelected =
            workspacePlan.Registrations.Any(
                static registration =>
                    registration
                        is WorkspaceRegistration.Ecosystem ecosystem
                    && ecosystem.Declaration.Id.Value
                        == "ecosystem.runtime"
                    && ecosystem.Declaration.Populations.Any(
                        static population =>
                            population
                                is WorkspaceEcosystemPopulationDeclaration
                                    .Platform platform
                            && platform.Population.Family
                                == PlatformFamily.DotNetRuntime));
        if (!runtimePopulationSelected
            || string.IsNullOrWhiteSpace(activeDotnetRoot)
            || !PlatformResolver.TryGetFrameworkSpecsForTargetFramework(
                traversalTargetPolicy.TargetFramework,
                out IReadOnlyList<string> frameworkSpecs))
        {
            return null;
        }

        string? runtimeSpec = frameworkSpecs.FirstOrDefault(
            static framework =>
                framework.StartsWith(
                    "runtime@",
                    StringComparison.Ordinal));
        if (runtimeSpec is null)
        {
            return null;
        }

        InstalledPlatformPruneSource.Result result =
            pruneSource(
                runtimeSpec,
                Path.Combine(activeDotnetRoot, "packs"));
        if (!PackageDependencyMemberCallGraphPlatformPruning
                .TryCreateDotNetRuntime(
                    result.Inventory,
                    out PackageDependencyMemberCallGraphPlatformPruning?
                        pruning)
            || !string.Equals(
                pruning.Target.TargetFramework.ToString(),
                traversalTargetPolicy.TargetFramework,
                StringComparison.Ordinal))
        {
            return null;
        }

        return pruning;
    }

    internal static async ValueTask<
        (InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> Inspection,
        EvidenceInspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome,
            PackageDependencyMemberCallGraphEvidenceDocument>? Evidence)>
        ExecuteInspectionAsync(
            PackageDependencyMemberCallGraphInspectionRequest request,
            ExternalCallGraphOptions options,
            NuGetFetchOptions fetchOptions,
            CancellationToken cancellationToken,
            bool captureEvidence = false)
    {
        await using var composition =
            new DesktopPackageSourceComposition(
                fetchOptions.RequestTimeout);
        Action<string>? log = options.Verbose
            ? CommandError.WriteLine
            : null;
        var source = new PackageDependencyMemberCallGraphInspectionSource(
            new PackageDependencyTraversalCandidateAdapter(
                composition.CreateDependencySourceAuthorization(
                    options.SourceOptions),
                log),
            new AuthorizedPackageDependencyManifestSource(),
            composition.CreateDependencySettlementHouse(
                (_, _) => new FileSystemPackageStore(),
                options.SourceOptions,
                log),
            (operation, token) =>
                composition.IssueSettlementOperation(
                    operation.OperationTimeout,
                    token),
            composition.CreateDependencyContentQueryHouse(
                (_, _) => new FileSystemPackageStore(),
                options.SourceOptions,
                log));
        await using var continuationSource =
            new PackageDependencyMemberCallGraphContinuationSource(
                source,
                request.RealizationOperation,
                () => new DesktopPackageSourceComposition(
                    fetchOptions.RequestTimeout),
                options.SourceOptions);
        var continuation =
            new PackageDependencyMemberCallGraphContinuation(
                continuationSource,
                CreateContinuationBudget(request));
        var builder = new EvidenceInspectionBuilder<
            PackageDependencyMemberCallGraphInspectionOutcome,
            PackageDependencyMemberCallGraphEvidenceDocument>();
        builder.RequestEvidence(captureEvidence);
        return await builder.BuildAsync(
                (request, source, continuation),
                static (state, token) =>
                    PackageDependencyMemberCallGraphInspection.ExecuteAsync(
                        state.request, state.source, state.continuation, token),
                static (state, token) =>
                    PackageDependencyMemberCallGraphInspection.ExecuteWithEvidenceAsync(
                        state.request, state.source, state.continuation, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    static AssemblyReferenceResolutionWorkBudget CreateContinuationBudget(
        PackageDependencyMemberCallGraphInspectionRequest request) =>
        new(
            maxPackageRouteOccurrences: 4_096,
            maxPackageCandidateOperations: 4_096,
            maxSourceOperations: 64,
            maxAcquisitions: 32,
            maxRealizedAssemblies: 8_192,
            maxTransferBytes: 2L * 1024 * 1024 * 1024,
            maxRetainedAssemblyBytes: 2L * 1024 * 1024 * 1024,
            maxWorkspaceReplacements: 16,
            deadline: request.WorkspaceDeadline);

    static bool TryResolveFocus(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome.Loaded loaded,
        WorkspaceMemberCoordinate.PackageMember rootMember,
        ExternalCallGraphOptions options,
        out ResolvedFocus focus)
    {
        RealizedMemberCoordinate.Package? coordinate = loaded.Members
            .Where(member => Equals(member.Declared, rootMember))
            .Select(member => member.Realized)
            .OfType<RealizedMemberCoordinate.Package>()
            .FirstOrDefault();
        if (coordinate?.Framework is null)
        {
            CommandError.Write("The external call-graph root has no exact Package framework.");
            focus = default!;
            return false;
        }
        var request = new ExactTypeInspectionRequest(
            coordinate.PackageId, coordinate.Version, coordinate.Framework, options.TypeName);
        ResolvedFocus? resolved = ExactTypeInspectionOperation.ExecuteWithLiveTarget(
            workspace, loaded, request,
            options.IncludeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
            (envelope, target) =>
            {
                if (!envelope.Content.IsAvailable || target is null)
                {
                    TypeCommand.WriteExactTypeNonSuccess(envelope.Content, envelope.Diagnostics);
                    return null;
                }
                return ResolveTarget(target);
            });
        focus = resolved!;
        return resolved is not null;

        ResolvedFocus? ResolveTarget(SelectedContextExactTypeLiveTarget selected)
        {
            MemberTargetResolution resolution = MemberTargetResolver.Resolve(
                selected.Type, MemberTargetSelector.Parse(options.Member));
            if (resolution.Diagnostic is { } diagnostic)
            {
                CommandError.Write(diagnostic.Message, [.. diagnostic.CandidateDetails()]);
                return null;
            }
            ResolvedMemberTarget target = resolution.Target!;
            if (target.Body?.MetadataToken is not { } methodToken)
            {
                CommandError.Write(
                    $"Member selector '{target.NormalizedSelector}' has no exact MethodDef body.",
                    ["Select a method, constructor, or exact property/event accessor."]);
                return null;
            }
            using var metadata = PdbContext.OpenMetadataOnly(selected.Assembly);
            bool? hasBody = metadata.MethodHasBody(methodToken);
            if (hasBody is not true)
            {
                CommandError.Write(
                    hasBody is false
                        ? $"Member selector '{target.NormalizedSelector}' selects a MethodDef without a managed implementation body."
                        : $"The managed implementation body for member selector '{target.NormalizedSelector}' could not be confirmed.",
                    [hasBody is false
                        ? "Select a non-abstract managed method, constructor, or exact property/event accessor."
                        : "Use an implementation assembly that carries the selected method body."]);
                return null;
            }
            return new ResolvedFocus(metadata.ModuleVersionId(), methodToken);
        }
    }

    static void WriteRouteDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics.Where(
            static diagnostic =>
                diagnostic.Code
                    == "package-dependency-member-call-graph.route-unavailable"))
        {
            CommandError.WriteWarning(
                diagnostic.Summary.ToString());
        }
    }

    static string FormatAcquisitionFailure(
        MemberCallGraphAcquisitionFailure failure) =>
        failure switch
        {
            MemberCallGraphAcquisitionFailure.Rejected rejected =>
                $"{rejected.Assembly.Identity.Name}: {rejected.Failure.Kind}: {rejected.Failure.Detail}",
            MemberCallGraphAcquisitionFailure.InvalidImage invalid =>
                $"{invalid.Assembly.Identity.Name}: {invalid.Error.Message}",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

    sealed record ResolvedFocus(
        Guid ModuleVersionId,
        int MethodToken);
}
