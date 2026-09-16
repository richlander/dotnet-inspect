using System.Collections.ObjectModel;

namespace DotnetInspector.Queries.Definitions;

/// <summary>Host-issued identity ordering one restoration attempt.</summary>
public sealed class CompleteRestorationIntentIdentity
{
    public CompleteRestorationIntentIdentity()
    {
    }
}

public enum CompleteRestorationIntentStatus
{
    Current,
    Cancelled,
    Expired,
    Revoked,
    Superseded,
}

/// <summary>
/// Current host intent and cooperative revocation for one restoration attempt.
/// </summary>
public interface ICompleteRestorationIntentAuthority
{
    CompleteRestorationIntentIdentity Identity { get; }

    CompleteRestorationIntentStatus Status { get; }

    CancellationToken Revocation { get; }
}

/// <summary>Exact inert source supplied to complete restoration.</summary>
public abstract record CompleteRestorationRequestBasis
{
    private protected CompleteRestorationRequestBasis()
    {
    }

    public sealed record PacketInput(string Encoded)
        : CompleteRestorationRequestBasis
    {
        public string Encoded { get; } =
            Encoded ?? throw new ArgumentNullException(nameof(Encoded));
    }

    public sealed record DefinitionInput : CompleteRestorationRequestBasis
    {
        public DefinitionInput(
            string scenarioId,
            IReadOnlyList<InspectionDefinitionRecord> records)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
            ArgumentNullException.ThrowIfNull(records);
            if (records.Any(static record => record is null))
            {
                throw new ArgumentException(
                    "Definition request records cannot contain null.",
                    nameof(records));
            }

            ScenarioId = scenarioId;
            Records = new ReadOnlyCollection<InspectionDefinitionRecord>(
                [.. records]);
        }

        public string ScenarioId { get; }

        public IReadOnlyList<InspectionDefinitionRecord> Records { get; }
    }
}

public enum CompleteRestorationLegacySource
{
    PacketV1,
    DefinitionV1,
}

/// <summary>
/// Resource-free recipe retained until the exact fresh Workspace is built.
/// </summary>
public abstract record CompleteRestorationRecipe
{
    private protected CompleteRestorationRecipe()
    {
    }

    public sealed record Version2(CommittedScenarioDefinitionSet Definitions)
        : CompleteRestorationRecipe
    {
        public CommittedScenarioDefinitionSet Definitions { get; } =
            Definitions
            ?? throw new ArgumentNullException(nameof(Definitions));
    }

    public sealed record LegacyDirectPackage(
        CompleteRestorationLegacySource Source,
        Version1ScenarioDefinitionSet Definitions,
        string FocusNavigationId,
        string? Facet)
        : CompleteRestorationRecipe
    {
        public Version1ScenarioDefinitionSet Definitions { get; } =
            Definitions ?? throw new ArgumentNullException(nameof(Definitions));

        public string FocusNavigationId { get; } =
            string.IsNullOrWhiteSpace(FocusNavigationId)
                ? throw new ArgumentException(
                    "A legacy recipe requires a focused navigation id.",
                    nameof(FocusNavigationId))
                : FocusNavigationId;
    }
}

/// <summary>
/// Exact plan and unresolved recipe produced before any Workspace exists.
/// </summary>
public sealed record CompleteRestorationPlan
{
    internal CompleteRestorationPlan(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request,
        WorkspacePlan workspacePlan,
        CompleteRestorationRecipe recipe,
        IReadOnlyDictionary<
            string,
            PackageNavigationSource> packageSources)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        WorkspacePlan = workspacePlan
            ?? throw new ArgumentNullException(nameof(workspacePlan));
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        PackageSources = packageSources
            ?? throw new ArgumentNullException(nameof(packageSources));
    }

    public CompleteRestorationIntentIdentity Intent { get; }

    public CompleteRestorationRequestBasis Request { get; }

    public WorkspacePlan WorkspacePlan { get; }

    public CompleteRestorationRecipe Recipe { get; }

    internal IReadOnlyDictionary<
        string,
        PackageNavigationSource> PackageSources
        { get; }
}

/// <summary>Typed non-activation evidence from complete restoration.</summary>
public abstract record CompleteRestorationFailure
{
    private protected CompleteRestorationFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Message = message;
    }

    public string Message { get; }

    public sealed record InvalidPacket : CompleteRestorationFailure
    {
        internal InvalidPacket(
            WorkspaceSharePacketFailureKind kind,
            string message)
            : base(message)
        {
            Kind = kind;
        }

        public WorkspaceSharePacketFailureKind Kind { get; }
    }

    public sealed record InvalidDefinitionSet : CompleteRestorationFailure
    {
        internal InvalidDefinitionSet(string message)
            : base(message)
        {
        }
    }

    public sealed record LegacyLoweringFailed : CompleteRestorationFailure
    {
        internal LegacyLoweringFailed(string message)
            : base(message)
        {
        }
    }

    public sealed record AuthorityUnavailable : CompleteRestorationFailure
    {
        internal AuthorityUnavailable(
            CompleteRestorationIntentStatus status,
            string message)
            : base(message)
        {
            if (status is CompleteRestorationIntentStatus.Current)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            Status = status;
        }

        public CompleteRestorationIntentStatus Status { get; }
    }

    public sealed record Cancelled : CompleteRestorationFailure
    {
        internal Cancelled(string message)
            : base(message)
        {
        }
    }

    public sealed record WorkspaceConstructionFailed :
        CompleteRestorationFailure
    {
        internal WorkspaceConstructionFailed(string message)
            : base(message)
        {
        }
    }

    public sealed record ContextLoadFailed : CompleteRestorationFailure
    {
        internal ContextLoadFailed(
            int contextIndex,
            WorkspaceContextLoadOutcome outcome)
            : base($"Workspace context {contextIndex} failed to load.")
        {
            ArgumentNullException.ThrowIfNull(outcome);
            ContextIndex = contextIndex;
            Outcome = outcome;
        }

        public int ContextIndex { get; }

        public WorkspaceContextLoadOutcome Outcome { get; }
    }

    public sealed record ScopeReadFailed : CompleteRestorationFailure
    {
        internal ScopeReadFailed(ArtifactRootFailure failure)
            : base($"Workspace Scope could not be read: {failure}.")
        {
            Failure = failure;
        }

        public ArtifactRootFailure Failure { get; }
    }

    public sealed record ScopeMutationFailed : CompleteRestorationFailure
    {
        internal ScopeMutationFailed(WorkspaceScopeOperationResult outcome)
            : base("Workspace Scope replacement did not commit.")
        {
            Outcome = outcome
                ?? throw new ArgumentNullException(nameof(outcome));
        }

        public WorkspaceScopeOperationResult Outcome { get; }
    }

    public sealed record PackageEvaluationFailed :
        CompleteRestorationFailure
    {
        internal PackageEvaluationFailed(
            string navigationId,
            string message,
            ArtifactRootFailure? rootFailure = null)
            : base(message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(navigationId);
            NavigationId = navigationId;
            RootFailure = rootFailure;
        }

        public string NavigationId { get; }

        public ArtifactRootFailure? RootFailure { get; }
    }

    public sealed record SelectorResolutionFailed :
        CompleteRestorationFailure
    {
        internal SelectorResolutionFailed(
            CommittedSelectorResolutionFailure failure)
            : base(failure?.Message
                ?? throw new ArgumentNullException(nameof(failure)))
        {
            Failure = failure;
        }

        public CommittedSelectorResolutionFailure Failure { get; }
    }

    public sealed record NavigationFailed : CompleteRestorationFailure
    {
        internal NavigationFailed(
            NavigationRestorationPreparationResult outcome)
            : base("Navigation did not produce a complete prepared state.")
        {
            Outcome = outcome
                ?? throw new ArgumentNullException(nameof(outcome));
        }

        public NavigationRestorationPreparationResult Outcome { get; }
    }

    public sealed record SnapshotFailed : CompleteRestorationFailure
    {
        internal SnapshotFailed(ArtifactRootFailure failure)
            : base($"The complete Workspace snapshot failed: {failure}.")
        {
            Failure = failure;
        }

        public ArtifactRootFailure Failure { get; }
    }

    public sealed record ProjectionFailed : CompleteRestorationFailure
    {
        internal ProjectionFailed(string message)
            : base(message)
        {
        }
    }

    public sealed record HostConstructionFailed :
        CompleteRestorationFailure
    {
        public HostConstructionFailed(string message)
            : base(message)
        {
        }
    }

    public sealed record CleanupFailed : CompleteRestorationFailure
    {
        public CleanupFailed(string message)
            : base(message)
        {
        }
    }
}

/// <summary>
/// Closed resource-free dispatch result. Only <see cref="Ready"/> authorizes
/// host construction.
/// </summary>
public abstract record CompleteRestorationPreparationResult
{
    private protected CompleteRestorationPreparationResult(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public CompleteRestorationIntentIdentity Intent { get; }

    public CompleteRestorationRequestBasis Request { get; }

    public sealed record Ready : CompleteRestorationPreparationResult
    {
        internal Ready(CompleteRestorationPlan plan)
            : base(plan.Intent, plan.Request)
        {
            Plan = plan;
        }

        public CompleteRestorationPlan Plan { get; }
    }

    public sealed record Failed : CompleteRestorationPreparationResult
    {
        internal Failed(
            CompleteRestorationIntentIdentity intent,
            CompleteRestorationRequestBasis request,
            CompleteRestorationFailure failure)
            : base(intent, request)
        {
            Failure = failure
                ?? throw new ArgumentNullException(nameof(failure));
        }

        public CompleteRestorationFailure Failure { get; }
    }

    public sealed record Superseded : CompleteRestorationPreparationResult
    {
        internal Superseded(
            CompleteRestorationIntentIdentity intent,
            CompleteRestorationRequestBasis request)
            : base(intent, request)
        {
        }
    }
}

/// <summary>Resource-free front door for the complete restoration transaction.</summary>
public static class CompleteRestorationPreparation
{
    public static CompleteRestorationPreparationResult FromPacket(
        string encoded,
        ICompleteRestorationIntentAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(authority);
        var request = new CompleteRestorationRequestBasis.PacketInput(encoded);
        if (NonCurrent(authority, request) is { } unavailable)
            return unavailable;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(authority.Identity, request);

        WorkspaceSharePacket packet;
        WorkspaceSharePacketDefinitionSet definitions;
        try
        {
            packet = WorkspaceSharePacketCodec.Decode(encoded, cancellationToken);
            definitions = WorkspaceSharePacketTransposer.ToDefinitions(
                packet,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return NonCurrent(authority, request)
                ?? Cancelled(authority.Identity, request);
        }
        catch (WorkspaceSharePacketException failure)
        {
            return NonCurrent(authority, request)
                ?? new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.InvalidPacket(
                        failure.Kind,
                        failure.Message));
        }

        if (NonCurrent(authority, request) is { } superseded)
            return superseded;
        if (packet.Libraries.Count > 0)
        {
            return new CompleteRestorationPreparationResult.Failed(
                authority.Identity,
                request,
                new CompleteRestorationFailure.LegacyLoweringFailed(
                    "Packet format 1 Library scope requires a registered "
                        + "query-owner migration."));
        }

        var registry = new InspectionDefinitionRegistry();
        foreach (InspectionDefinitionRecord record in definitions.Records)
            registry.Add(record);
        try
        {
            return FromPreparedScenario(
                registry.PreparePacketScenario(definitions.Scenario.Id),
                authority,
                request,
                CompleteRestorationLegacySource.PacketV1);
        }
        catch (InspectionDefinitionException failure)
        {
            return NonCurrent(authority, request)
                ?? new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.InvalidDefinitionSet(
                        failure.Message));
        }
    }

    public static CompleteRestorationPreparationResult FromDefinition(
        InspectionDefinitionRegistry registry,
        string scenarioId,
        ICompleteRestorationIntentAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentNullException.ThrowIfNull(authority);
        var request = new CompleteRestorationRequestBasis.DefinitionInput(
            scenarioId,
            registry.Records.ToArray());
        if (NonCurrent(authority, request) is { } unavailable)
            return unavailable;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(authority.Identity, request);

        InspectionDefinitionScenarioPreparationResult prepared;
        try
        {
            prepared = registry.PrepareScenario(scenarioId);
        }
        catch (InspectionDefinitionException failure)
        {
            return NonCurrent(authority, request)
                ?? new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.InvalidDefinitionSet(
                        failure.Message));
        }

        if (NonCurrent(authority, request) is { } superseded)
            return superseded;
        try
        {
            return FromPreparedScenario(
                prepared,
                authority,
                request,
                CompleteRestorationLegacySource.DefinitionV1);
        }
        catch (InspectionDefinitionException failure)
        {
            return NonCurrent(authority, request)
                ?? new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.InvalidDefinitionSet(
                        failure.Message));
        }
    }

    private static CompleteRestorationPreparationResult FromPreparedScenario(
        InspectionDefinitionScenarioPreparationResult prepared,
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request,
        CompleteRestorationLegacySource source)
    {
        switch (prepared)
        {
            case InspectionDefinitionScenarioPreparationResult.Version2 version2:
                if (version2.Definitions.Workspace is null)
                    return FailedWorkspaceFree(authority.Identity, request);
                WorkspacePlan committedPlan =
                    InspectionDefinitionRegistry.CreateWorkspacePlan(
                        version2.Definitions.Workspace);
                IReadOnlyDictionary<
                    string,
                    PackageNavigationSource>
                    committedPackages =
                        InspectionDefinitionRegistry
                            .ResolvePackageNavigationSources(
                                version2.Definitions.Workspace,
                                version2.Definitions.Navigation);
                return new CompleteRestorationPreparationResult.Ready(
                    new CompleteRestorationPlan(
                        authority.Identity,
                        request,
                        committedPlan,
                        new CompleteRestorationRecipe.Version2(
                            version2.Definitions),
                        committedPackages));
            case InspectionDefinitionScenarioPreparationResult.Version1 version1:
                return PrepareVersion1(
                    version1.Definitions,
                    authority.Identity,
                    request,
                    source);
            default:
                throw new InvalidOperationException(
                    "Unknown definition preparation result.");
        }
    }

    internal sealed record LegacyFacetMapping(
        string? Facet,
        string? Failure);

    internal static class LegacyRestorationLowering
    {
        internal static LegacyFacetMapping MapPackageFacet(
            ViewDefinition? view)
        {
            if (view?.Libraries.Count > 0)
            {
                return new(
                    null,
                    "Definition schema version 1 Library scope requires a "
                        + "registered query-owner migration.");
            }

            if (view?.Type is not null)
            {
                string subject = view.MemberAnchor is not null
                    || view.MemberSignature is not null
                    || view.MemberKey is not null
                        ? "Member"
                        : "Type";
                return new(
                    null,
                    $"Legacy {subject} active requests are not supported by "
                        + "complete restoration.");
            }

            if (view?.Section is not null)
            {
                return new(
                    null,
                    "A legacy Package view cannot carry a member section.");
            }

            return view?.Lens switch
            {
                null => new(null, null),
                "overview" => new("package.overview", null),
                "dependencies" => new("package.dependencies", null),
                "integrations"
                    or "opportunities"
                    or "analysis"
                    or "metadata"
                    or "library:overview"
                    or "library:compare"
                    or "library:references"
                    or "library:integrations"
                    or "library:analysis"
                    or "library:metadata" =>
                        new(
                            null,
                            "Legacy Library active requests are not supported "
                                + "by complete restoration."),
                _ => new(
                    null,
                    $"Unknown legacy Package lens '{view!.Lens}'."),
            };
        }
    }

    private static CompleteRestorationPreparationResult PrepareVersion1(
        Version1ScenarioDefinitionSet definitions,
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request,
        CompleteRestorationLegacySource source)
    {
        if (definitions.Workspace is not { } workspace)
            return FailedWorkspaceFree(intent, request);
        if (definitions.Query is not null)
        {
            return LegacyFailure(
                intent,
                request,
                "Definition schema version 1 query presets require a "
                    + "registered query-owner migration.");
        }
        if (definitions.Navigation is not { } navigation)
        {
            return LegacyFailure(
                intent,
                request,
                "Complete restoration requires a focused direct-Package "
                    + "schema version 1 navigation.");
        }

        LegacyFacetMapping mapped =
            LegacyRestorationLowering.MapPackageFacet(definitions.View);
        if (mapped.Failure is not null)
            return LegacyFailure(intent, request, mapped.Failure);

        NavigationTargetMatchMode targetMatchMode =
            source is CompleteRestorationLegacySource.PacketV1
                ? NavigationTargetMatchMode.Exact
                : NavigationTargetMatchMode.InheritOmitted;
        IReadOnlyDictionary<string, PackageNavigationSource> packageSources =
            InspectionDefinitionRegistry.ResolvePackageNavigationSources(
                workspace,
                navigation,
                targetMatchMode);
        if (!packageSources.ContainsKey(navigation.Focus))
        {
            return LegacyFailure(
                intent,
                request,
                "Complete restoration requires a focused direct-Package "
                    + "schema version 1 navigation.");
        }

        WorkspacePlan workspacePlan;
        try
        {
            workspacePlan =
                InspectionDefinitionRegistry.CreateWorkspacePlan(workspace);
        }
        catch (InspectionDefinitionException failure)
        {
            return LegacyFailure(intent, request, failure.Message);
        }

        return new CompleteRestorationPreparationResult.Ready(
            new CompleteRestorationPlan(
                intent,
                request,
                workspacePlan,
                new CompleteRestorationRecipe.LegacyDirectPackage(
                    source,
                    definitions,
                    navigation.Focus,
                    mapped.Facet),
                packageSources));
    }

    private static CompleteRestorationPreparationResult FailedWorkspaceFree(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request) =>
        new CompleteRestorationPreparationResult.Failed(
            intent,
            request,
            new CompleteRestorationFailure.InvalidDefinitionSet(
                "Workspace-free scenarios do not enter complete Workspace "
                    + "restoration."));

    private static CompleteRestorationPreparationResult LegacyFailure(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request,
        string message) =>
        new CompleteRestorationPreparationResult.Failed(
            intent,
            request,
            new CompleteRestorationFailure.LegacyLoweringFailed(message));

    private static CompleteRestorationPreparationResult? NonCurrent(
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request) =>
        authority.Status switch
        {
            CompleteRestorationIntentStatus.Current
                when !authority.Revocation.IsCancellationRequested => null,
            CompleteRestorationIntentStatus.Superseded =>
                new CompleteRestorationPreparationResult.Superseded(
                    authority.Identity,
                    request),
            CompleteRestorationIntentStatus.Current =>
                new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.Cancelled(
                        "The restoration intent was cancelled.")),
            CompleteRestorationIntentStatus status =>
                new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.AuthorityUnavailable(
                        status,
                        $"The restoration intent is {status.ToString().ToLowerInvariant()}.")),
        };

    private static CompleteRestorationPreparationResult Cancelled(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request) =>
        new CompleteRestorationPreparationResult.Failed(
            intent,
            request,
            new CompleteRestorationFailure.Cancelled(
                "The restoration request was cancelled."));
}
