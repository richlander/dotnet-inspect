using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
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
            inspectionExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);

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
        using (AssemblyContextGroup group = loaded.Group)
        {
            if (!TryResolveFocus(
                    group,
                    loaded.Members,
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
            var inspectionRequest =
                new PackageDependencyMemberCallGraphInspectionRequest(
                    loaded.PackageRoots[0],
                    new PackageDependencyMemberCallGraphInspectionFocus(
                        focus.ModuleVersionId,
                        focus.MethodToken),
                    options.Tfm is null
                        ? TraversalTargetFrameworkPolicy.ProductDefault
                        : new TraversalTargetFrameworkPolicy(
                            options.Tfm),
                    new MemberCallGraphCalleeNeighborhoodRequest(
                        options.Depth,
                        options.MaxNodes),
                    realizationOperation,
                    DateTimeOffset.UtcNow
                        .Add(fetchOptions.OperationTimeout)
                        .Add(fetchOptions.OperationTimeout));
            InspectionEnvelope<
                PackageDependencyMemberCallGraphInspectionOutcome> envelope =
                inspectionExecutor is null
                    ? await ExecuteInspectionAsync(
                            inspectionRequest,
                            options,
                            fetchOptions,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await inspectionExecutor(
                            inspectionRequest,
                            cancellationToken)
                        .ConfigureAwait(false);
            if (envelope.Content
                is PackageDependencyMemberCallGraphInspectionOutcome
                    .Unavailable unavailable)
            {
                CommandError.Write(
                    "The dependency-aware external call graph was unavailable.",
                    [$"{unavailable.Reason}: {unavailable.Detail}"]);
                return 1;
            }
            var available =
                (PackageDependencyMemberCallGraphInspectionOutcome
                    .Available)envelope.Content;
            WriteRouteDiagnostics(envelope.Diagnostics);
            return ExternalCallGraphOutputAdapter.Write(
                available.Document.Graph,
                options);
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

    static WorkspaceContextLoadOptions CreateLoadOptions(
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

    static async ValueTask<
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>>
        ExecuteInspectionAsync(
            PackageDependencyMemberCallGraphInspectionRequest request,
            ExternalCallGraphOptions options,
            NuGetFetchOptions fetchOptions,
            CancellationToken cancellationToken)
    {
        await using var composition =
            new DesktopPackageSourceComposition(
                fetchOptions.RequestTimeout);
        var candidateSource =
            new DesktopPackageDependencyCandidateSource(
                composition,
                options.SourceOptions,
                options.Verbose
                    ? CommandError.WriteLine
                    : null);
        return await PackageDependencyMemberCallGraphInspection.ExecuteAsync(
                request,
                new PackageDependencyMemberCallGraphInspectionSource(
                    new PackageDependencyTraversalCandidateAdapter(
                        candidateSource),
                    new DesktopPackageDependencyTraversalManifestSource(
                        composition),
                    composition.CreateDependencySettlementHouse(
                        (_, _) => new FileSystemPackageStore(),
                        options.SourceOptions,
                        options.Verbose
                            ? CommandError.WriteLine
                            : null),
                    (operation, token) =>
                        composition.IssueSettlementOperation(token)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    static bool TryResolveFocus(
        AssemblyContextGroup group,
        IReadOnlyList<WorkspaceContextMember> loadedMembers,
        WorkspaceMemberCoordinate.PackageMember rootMember,
        ExternalCallGraphOptions options,
        out ResolvedFocus focus)
    {
        var matches = new List<FocusMatch>();
        var suggestions =
            new SortedSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        ApiSurfaceScope scope = options.IncludeAll
            ? ApiSurfaceScope.IncludeAll
            : ApiSurfaceScope.Public;

        foreach (WorkspaceContextMember member in loadedMembers)
        {
            if (!Equals(member.Declared, rootMember))
                continue;

            AssemblyContextEntry<AssemblyApiSurface> result =
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    member.Participant,
                    scope);
            switch (result)
            {
                case AssemblyContextEntry<AssemblyApiSurface>.Available
                    available:
                {
                    ApiTypeLookupResult lookup =
                        ApiTypeLookupService.LookupType(
                            available.Value.Surface,
                            options.TypeName);
                    foreach (string suggestion in lookup.Suggestions)
                        suggestions.Add(suggestion);
                    if (lookup.Type is not null)
                    {
                        matches.Add(
                            new FocusMatch(
                                member.Participant,
                                lookup.Type));
                    }
                    break;
                }
                case AssemblyContextEntry<AssemblyApiSurface>.Rejected
                    rejected:
                    failures.Add(
                        $"{rejected.Subject.Identity.Name}: {rejected.Failure.Kind}: {rejected.Failure.Detail}");
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                    failures.Add(
                        $"{failed.Subject.Identity.Name}: {failed.Error.Message}");
                    break;
            }
        }

        if (matches.Count == 0)
        {
            List<string> details = [.. failures];
            details.AddRange(
                ApiTypeLookupResult.SuggestionDetails([.. suggestions]));
            if (!options.IncludeAll)
            {
                details.Add(string.Empty);
                details.Add(
                    "Pass --all when the focus type is non-public.");
            }
            CommandError.Write(
                $"Type '{options.TypeName}' was not found in root package '{options.RootPackage}'.",
                [.. details]);
            focus = default!;
            return false;
        }

        if (matches.Count > 1)
        {
            CommandError.Write(
                $"Type '{options.TypeName}' matched multiple root-package assemblies.",
                [
                    .. matches.Select(match =>
                        $"  {match.Participant.Assembly.Identity.Name}: {match.Type.FullName}"),
                ]);
            focus = default!;
            return false;
        }

        FocusMatch match = matches[0];
        MemberTargetResolution resolution =
            MemberTargetResolver.Resolve(
                match.Type,
                MemberTargetSelector.Parse(options.Member));
        if (resolution.Diagnostic is { } diagnostic)
        {
            CommandError.Write(
                diagnostic.Message,
                [.. diagnostic.CandidateDetails()]);
            focus = default!;
            return false;
        }

        ResolvedMemberTarget target = resolution.Target!;
        if (target.Body?.MetadataToken is not { } methodToken)
        {
            CommandError.Write(
                $"Member selector '{target.NormalizedSelector}' has no exact MethodDef body.",
                [
                    "Select a method, constructor, or exact property/event accessor.",
                ]);
            focus = default!;
            return false;
        }

        using var metadata =
            PdbContext.OpenMetadataOnly(match.Participant.Assembly);
        bool? hasBody = metadata.MethodHasBody(methodToken);
        if (hasBody is not true)
        {
            CommandError.Write(
                hasBody is false
                    ? $"Member selector '{target.NormalizedSelector}' selects a MethodDef without a managed implementation body."
                    : $"The managed implementation body for member selector '{target.NormalizedSelector}' could not be confirmed.",
                [
                    hasBody is false
                        ? "Select a non-abstract managed method, constructor, or exact property/event accessor."
                        : "Use an implementation assembly that carries the selected method body.",
                ]);
            focus = default!;
            return false;
        }

        focus = new ResolvedFocus(
            metadata.ModuleVersionId(),
            methodToken);
        return true;
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

    sealed record FocusMatch(
        AssemblyContextParticipant Participant,
        ApiType Type);

    sealed record ResolvedFocus(
        Guid ModuleVersionId,
        int MethodToken);
}
