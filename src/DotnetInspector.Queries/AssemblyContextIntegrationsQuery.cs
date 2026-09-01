using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Identity and provenance for one context-group participant without its
/// content-opening capability.
/// </summary>
public sealed class AssemblyContextSubject
{
    internal AssemblyContextSubject(ResolvedAssemblyReference assembly)
    {
        Registration = assembly.Registration;
        Identity = assembly.Identity;
        Provenance = assembly.Provenance;
    }

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public AssemblyResolutionProvenance Provenance { get; }
}

/// <summary>One participant's outcome in a group-scoped Integrations query.</summary>
public abstract record AssemblyIntegrationsEntry(
    AssemblyContextSubject Subject)
{
    /// <summary>The participant produced its integration evidence.</summary>
    public sealed record Available(
        AssemblyContextSubject Subject,
        ImmutableArray<EcosystemIntegrationSignalInfo> EcosystemSignals,
        ImmutableArray<OpenTelemetrySignalInfo> OpenTelemetrySignals,
        EcosystemIntegrationPresence Presence)
        : AssemblyIntegrationsEntry(Subject);

    /// <summary>The participant's immutable image could not be acquired.</summary>
    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyIntegrationsEntry(Subject);

    /// <summary>The participant's acquired metadata was malformed.</summary>
    /// <remarks>
    /// Mapping late metadata decode failures independently of snapshot
    /// acquisition is currently unverified.
    /// </remarks>
    public sealed record Failed(
        AssemblyContextSubject Subject,
        BadImageFormatException Error)
        : AssemblyIntegrationsEntry(Subject);
}

/// <summary>
/// Ordered Integrations outcomes for every participant in one assembly context
/// group. Partial inspection is meaningful because each unavailable participant
/// carries its failure beside the available entries.
/// </summary>
public sealed record AssemblyContextIntegrationsResult(
    ImmutableArray<AssemblyIntegrationsEntry> Assemblies)
{
    public bool IsComplete =>
        Assemblies.All(
            static entry =>
                entry is AssemblyIntegrationsEntry.Available);
}

/// <summary>
/// Scans Integrations evidence across one binding-consistent assembly context
/// group in deterministic participant order.
/// </summary>
/// <remarks>
/// Ordering, snapshot reuse, and partial acquisition are gated by
/// <c>RegistryRun_ScansEveryParticipantInOrderAndReusesSnapshots</c> and
/// <c>Execute_CarriesAcquisitionFailureBesideLaterResults</c>. Budget-limited
/// partial inspection is gated by
/// <c>Execute_ReportsBudgetExhaustionAsIncompleteEntry</c>.
/// </remarks>
public static class AssemblyContextIntegrationsQuery
{
    public static InspectionQuery<AssemblyContextIntegrationsResult>
        Definition { get; } =
        new("Assembly context integrations", InspectionCost.Unbounded);

    public static AssemblyContextIntegrationsResult Execute(
        AssemblyContextGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var entries =
            ImmutableArray.CreateBuilder<AssemblyIntegrationsEntry>(
                group.Participants.Length);
        foreach (AssemblyContextParticipant participant
            in group.Participants)
        {
            entries.Add(ExecuteParticipantCore(group, participant));
        }

        return new AssemblyContextIntegrationsResult(
            entries.MoveToImmutable());
    }

    /// <summary>
    /// Scans a demand-local package-role view without exposing its shared
    /// assembly context group.
    /// </summary>
    public static AssemblyContextIntegrationsResult Execute(
        PackageAssemblyContextRoleProjection role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.Use(Execute);
    }

    /// <summary>
    /// Scans one participant without releasing its retained image, so a reusable group remains
    /// available to later queries.
    /// </summary>
    public static AssemblyIntegrationsEntry ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        if (!group.Participants.Any(candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                participant.Assembly.Registration)))
        {
            throw new ArgumentException(
                "The requested participant is not a member of the assembly context group.",
                nameof(participant));
        }

        return ExecuteParticipantCore(group, participant);
    }

    /// <summary>
    /// Scans one participant through a demand-local package-role view without
    /// releasing its retained shared image.
    /// </summary>
    public static AssemblyIntegrationsEntry ExecuteParticipant(
        PackageAssemblyContextRoleProjection role,
        PackageAssemblyRoleParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(participant);
        if (!role.Participants.Contains(participant))
        {
            throw new ArgumentException(
                "The requested participant is not a member of the package-role projection.",
                nameof(participant));
        }

        return role.Use(group =>
            ExecuteParticipantCore(
                group,
                participant.Participant));
    }

    /// <summary>
    /// Scans one participant and runs its asynchronous consumer before
    /// releasing the participant's retained group image.
    /// </summary>
    /// <remarks>
    /// Hosts invoke participants in group order. This streaming form keeps the
    /// complete binding universe while bounding retained image bytes to the
    /// participant currently being consumed. Release is terminal for that
    /// participant; callers must not run a later whole-group query over the
    /// same group. Gated by
    /// <c>UseAssemblyAsync_ReleasesParticipantBeforeAdvancing</c>.
    /// </remarks>
    public static async Task<TResult> ExecuteParticipantAsync<TResult>(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        Func<
            ResolvedAssemblyReference?,
            AssemblyIntegrationsEntry,
            Task<TResult>> callback)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(callback);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyImageAccessResult<TResult> access =
            await group.UseAndReleaseAssemblySessionAsync(
                    participant.Assembly,
                    async (session, retained) =>
                    {
                        AssemblyIntegrationsEntry entry =
                            Inspect(subject, session);
                        return await callback(retained, entry)
                            .ConfigureAwait(false);
                    })
                .ConfigureAwait(false);
        return access switch
        {
            AssemblyImageAccessResult<TResult>.Available available =>
                available.Value,
            AssemblyImageAccessResult<TResult>.Rejected rejected =>
                await callback(
                        null,
                        new AssemblyIntegrationsEntry.Rejected(
                            subject,
                            rejected.Failure))
                    .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    internal static AssemblyIntegrationsEntry Inspect(
        AssemblyContextSubject subject,
        AssemblyInspectionSession session)
    {
        try
        {
            ImmutableArray<EcosystemIntegrationSignalInfo> ecosystemSignals =
                session.EcosystemIntegrations().ToImmutableArray();
            ImmutableArray<OpenTelemetrySignalInfo> openTelemetrySignals =
                session.OpenTelemetrySignals().ToImmutableArray();
            return new AssemblyIntegrationsEntry.Available(
                subject,
                ecosystemSignals,
                openTelemetrySignals,
                session.EcosystemIntegrationPresence(
                    ecosystemSignals));
        }
        catch (BadImageFormatException ex)
        {
            return new AssemblyIntegrationsEntry.Failed(subject, ex);
        }
        catch (Exception ex) when (
            ex is ArgumentOutOfRangeException or OverflowException)
        {
            return new AssemblyIntegrationsEntry.Failed(
                subject,
                new BadImageFormatException(
                    "The selected image contains invalid metadata.",
                    ex));
        }
    }

    static AssemblyIntegrationsEntry ExecuteParticipantCore(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyImageAccessResult<AssemblyIntegrationsEntry> access =
            group.UseAssemblySession(
                participant.Assembly,
                session => Inspect(subject, session));
        return access switch
        {
            AssemblyImageAccessResult<
                AssemblyIntegrationsEntry>.Available available =>
                available.Value,
            AssemblyImageAccessResult<
                AssemblyIntegrationsEntry>.Rejected rejected =>
                new AssemblyIntegrationsEntry.Rejected(
                    subject,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    internal static AssemblyIntegrationsEntry ExecuteParticipant(
        AssemblyContextParticipant participant,
        AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(session);
        return Inspect(
            new AssemblyContextSubject(participant.Assembly),
            session);
    }
}
