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

/// <summary>
/// Resource-free recipe retained until metadata-dependent selectors can be
/// resolved inside the exact fresh Workspace.
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

    public sealed record Version3(CommittedScenarioDefinitionSet Definitions)
        : CompleteRestorationRecipe
    {
        public CommittedScenarioDefinitionSet Definitions { get; } =
            Definitions
            ?? throw new ArgumentNullException(nameof(Definitions));
    }

    public sealed record Version4(CommittedScenarioDefinitionSet Definitions)
        : CompleteRestorationRecipe
    {
        public CommittedScenarioDefinitionSet Definitions { get; } =
            Definitions
            ?? throw new ArgumentNullException(nameof(Definitions));
    }

    public sealed record Version5(CommittedScenarioDefinitionSet Definitions)
        : CompleteRestorationRecipe
    {
        public CommittedScenarioDefinitionSet Definitions { get; } =
            Definitions
            ?? throw new ArgumentNullException(nameof(Definitions));
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
        IReadOnlyList<WorkspacePackageSourceDefinition>
            configuredPackageSources,
        IReadOnlyDictionary<
            string,
            PackageNavigationSource> packageSources,
        IReadOnlyDictionary<string, GroupNavigationSource> groupSources)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        WorkspacePlan = workspacePlan
            ?? throw new ArgumentNullException(nameof(workspacePlan));
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        ConfiguredPackageSources = configuredPackageSources
            ?? throw new ArgumentNullException(
                nameof(configuredPackageSources));
        PackageSources = packageSources
            ?? throw new ArgumentNullException(nameof(packageSources));
        GroupSources = groupSources
            ?? throw new ArgumentNullException(nameof(groupSources));
    }

    public CompleteRestorationIntentIdentity Intent { get; }

    public CompleteRestorationRequestBasis Request { get; }

    public WorkspacePlan WorkspacePlan { get; }

    public CompleteRestorationRecipe Recipe { get; }

    public IReadOnlyList<WorkspacePackageSourceDefinition>
        ConfiguredPackageSources { get; }

    internal IReadOnlyDictionary<
        string,
        PackageNavigationSource> PackageSources
        { get; }

    internal IReadOnlyDictionary<string, GroupNavigationSource> GroupSources
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

    public sealed record UnsupportedVersion : CompleteRestorationFailure
    {
        internal UnsupportedVersion(int version, string message)
            : base(message)
        {
            Version = version;
        }

        public int Version { get; }
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
            : base("Workspace Scope admission did not commit.")
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
    internal static CompleteRestorationPreparationResult
        FromCommittedDefinitions(
            CommittedScenarioDefinitionSet definitions,
            ICompleteRestorationIntentAuthority authority,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(authority);
        var request = new CompleteRestorationRequestBasis.DefinitionInput(
            definitions.Scenario.Id,
            definitions.Records);
        if (NonCurrent(authority, request) is { } unavailable)
            return unavailable;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(authority.Identity, request);

        return definitions.Scenario.SchemaVersion switch
        {
            InspectionDefinitionSchema.Version2 =>
                PrepareVersion2(definitions, authority, request),
            InspectionDefinitionSchema.Version3 =>
                PrepareVersion3(definitions, authority, request),
            InspectionDefinitionSchema.Version4 =>
                PrepareVersion4(definitions, authority, request),
            InspectionDefinitionSchema.Version5 =>
                PrepareVersion5(definitions, authority, request),
            int version =>
                new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.UnsupportedVersion(
                        version,
                        $"Portable Package-coordinate replacement requires "
                            + $"schema version "
                            + $"{InspectionDefinitionSchema.Version2}, "
                            + $"{InspectionDefinitionSchema.Version3}, "
                            + $"{InspectionDefinitionSchema.Version4}, or "
                            + $"{InspectionDefinitionSchema.Version5}.")),
        };
    }

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
        try
        {
            packet = WorkspaceSharePacketCodec.Decode(encoded, cancellationToken);
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
        if (packet.FormatVersion is not (
            WorkspaceSharePacketCodec.Format2Version
            or WorkspaceSharePacketCodec.CurrentFormatVersion
            or WorkspaceSharePacketCodec.Format4Version
            or WorkspaceSharePacketCodec.Format5Version))
        {
            return new CompleteRestorationPreparationResult.Failed(
                authority.Identity,
                request,
                new CompleteRestorationFailure.UnsupportedVersion(
                    packet.FormatVersion,
                    "Complete Workspace restoration requires packet format "
                        + $"{WorkspaceSharePacketCodec.Format2Version}, "
                        + $"{WorkspaceSharePacketCodec.CurrentFormatVersion}, "
                        + $"{WorkspaceSharePacketCodec.Format4Version}, or "
                        + $"{WorkspaceSharePacketCodec.Format5Version}; "
                        + $"format {packet.FormatVersion} is not supported."));
        }

        try
        {
            CommittedScenarioDefinitionSet definitions =
                WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    packet,
                    cancellationToken);
            return packet.FormatVersion switch
            {
                WorkspaceSharePacketCodec.Format2Version =>
                    PrepareVersion2(definitions, authority, request),
                WorkspaceSharePacketCodec.CurrentFormatVersion =>
                    PrepareVersion3(definitions, authority, request),
                WorkspaceSharePacketCodec.Format4Version =>
                    PrepareVersion4(definitions, authority, request),
                WorkspaceSharePacketCodec.Format5Version =>
                    PrepareVersion5(definitions, authority, request),
                _ => throw new InvalidOperationException(
                    "Unknown admitted Workspace packet format."),
            };
        }
        catch (OperationCanceledException)
        {
            return NonCurrent(authority, request)
                ?? Cancelled(authority.Identity, request);
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
        return FromDefinition(
            registry,
            request,
            authority,
            cancellationToken);
    }

    public static CompleteRestorationPreparationResult FromDefinition(
        CompleteRestorationRequestBasis.DefinitionInput request,
        ICompleteRestorationIntentAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authority);
        if (NonCurrent(authority, request) is { } unavailable)
            return unavailable;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(authority.Identity, request);

        var registry = new InspectionDefinitionRegistry();
        try
        {
            foreach (InspectionDefinitionRecord record in request.Records)
                registry.Add(record);
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

        return FromDefinition(
            registry,
            request,
            authority,
            cancellationToken);
    }

    private static CompleteRestorationPreparationResult FromDefinition(
        InspectionDefinitionRegistry registry,
        CompleteRestorationRequestBasis.DefinitionInput request,
        ICompleteRestorationIntentAuthority authority,
        CancellationToken cancellationToken)
    {
        if (NonCurrent(authority, request) is { } unavailable)
            return unavailable;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled(authority.Identity, request);

        InspectionDefinitionScenarioPreparationResult prepared;
        try
        {
            prepared = registry.PrepareScenario(request.ScenarioId);
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
                request);
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
        CompleteRestorationRequestBasis request)
    {
        return prepared switch
        {
            InspectionDefinitionScenarioPreparationResult.Version2 version2 =>
                PrepareVersion2(version2.Definitions, authority, request),
            InspectionDefinitionScenarioPreparationResult.Version3 version3 =>
                PrepareVersion3(version3.Definitions, authority, request),
            InspectionDefinitionScenarioPreparationResult.Version4 version4 =>
                PrepareVersion4(version4.Definitions, authority, request),
            InspectionDefinitionScenarioPreparationResult.Version5 version5 =>
                PrepareVersion5(version5.Definitions, authority, request),
            InspectionDefinitionScenarioPreparationResult.Version1 =>
                new CompleteRestorationPreparationResult.Failed(
                    authority.Identity,
                    request,
                    new CompleteRestorationFailure.UnsupportedVersion(
                        InspectionDefinitionSchema.Version1,
                        "Complete Workspace restoration requires schema "
                            + $"version {InspectionDefinitionSchema.Version2}, "
                            + $"{InspectionDefinitionSchema.Version3}, "
                            + $"{InspectionDefinitionSchema.Version4}, or "
                            + $"{InspectionDefinitionSchema.Version5}; "
                            + "schema version 1 is not supported.")),
            _ => throw new InvalidOperationException(
                "Unknown definition preparation result."),
        };
    }

    private static CompleteRestorationPreparationResult PrepareVersion2(
        CommittedScenarioDefinitionSet definitions,
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request) =>
        PrepareCommitted(
            definitions,
            authority,
            request,
            new CompleteRestorationRecipe.Version2(definitions));

    private static CompleteRestorationPreparationResult PrepareVersion3(
        CommittedScenarioDefinitionSet definitions,
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request) =>
        PrepareCommitted(
            definitions,
            authority,
            request,
            new CompleteRestorationRecipe.Version3(definitions));

    private static CompleteRestorationPreparationResult PrepareVersion4(
        CommittedScenarioDefinitionSet definitions,
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request) =>
        PrepareCommitted(
            definitions,
            authority,
            request,
            new CompleteRestorationRecipe.Version4(definitions));

    private static CompleteRestorationPreparationResult PrepareVersion5(
        CommittedScenarioDefinitionSet definitions,
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request) =>
        PrepareCommitted(
            definitions,
            authority,
            request,
            new CompleteRestorationRecipe.Version5(definitions));

    private static CompleteRestorationPreparationResult PrepareCommitted(
        CommittedScenarioDefinitionSet definitions,
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationRequestBasis request,
        CompleteRestorationRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (definitions.Workspace is null)
            return FailedWorkspaceFree(authority.Identity, request);

        WorkspacePlan workspacePlan =
            InspectionDefinitionRegistry.CreateCompleteRestorationWorkspacePlan(
                definitions.Workspace);
        IReadOnlyDictionary<string, PackageNavigationSource> packageSources =
            InspectionDefinitionRegistry.ResolvePackageNavigationSources(
                definitions.Workspace,
                definitions.Navigation,
                definitions.NavigationTargetMatchMode);
        return new CompleteRestorationPreparationResult.Ready(
            new CompleteRestorationPlan(
                authority.Identity,
                request,
                workspacePlan,
                recipe,
                definitions.Workspace.PackageSources,
                packageSources,
                InspectionDefinitionRegistry.ResolveGroupNavigationSources(
                    definitions.Workspace,
                    definitions.Navigation,
                    definitions.NavigationTargetMatchMode)));
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
