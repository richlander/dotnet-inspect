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
        ImmutableArray<OpenTelemetrySignalInfo> OpenTelemetrySignals)
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
            var subject =
                new AssemblyContextSubject(participant.Assembly);
            AssemblyImageAccessResult<AssemblyIntegrationsEntry> access =
                group.UseAssemblySession(
                    participant.Assembly,
                    session => Inspect(subject, session));
            entries.Add(
                access switch
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
                });
        }

        return new AssemblyContextIntegrationsResult(
            entries.MoveToImmutable());
    }

    static AssemblyIntegrationsEntry Inspect(
        AssemblyContextSubject subject,
        AssemblyInspectionSession session)
    {
        try
        {
            return new AssemblyIntegrationsEntry.Available(
                subject,
                session.EcosystemIntegrations().ToImmutableArray(),
                session.OpenTelemetrySignals().ToImmutableArray());
        }
        catch (BadImageFormatException ex)
        {
            return new AssemblyIntegrationsEntry.Failed(subject, ex);
        }
    }
}
