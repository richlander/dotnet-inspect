using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// One participant's outcome in the group-scoped integration-opportunity query.
/// </summary>
public abstract record AssemblyIntegrationOpportunitiesEntry(
    AssemblyContextSubject Subject)
{
    /// <summary>The participant produced its opportunity evidence.</summary>
    public sealed record Available(
        AssemblyContextSubject Subject,
        ImmutableArray<IntegrationOpportunityInfo> Opportunities)
        : AssemblyIntegrationOpportunitiesEntry(Subject);

    /// <summary>The participant's immutable image could not be acquired.</summary>
    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyIntegrationOpportunitiesEntry(Subject);

    /// <summary>The participant's acquired metadata was malformed.</summary>
    /// <remarks>
    /// Mapping a late opportunity metadata-decode failure independently of the
    /// prerequisite query is currently unverified.
    /// </remarks>
    public sealed record Failed(
        AssemblyContextSubject Subject,
        BadImageFormatException Error)
        : AssemblyIntegrationOpportunitiesEntry(Subject);
}

/// <summary>
/// Ordered integration-opportunity outcomes for every participant in one
/// assembly context group.
/// </summary>
public sealed record AssemblyContextIntegrationOpportunitiesResult(
    ImmutableArray<AssemblyIntegrationOpportunitiesEntry> Assemblies)
{
    public bool IsComplete =>
        Assemblies.All(
            static entry =>
                entry is AssemblyIntegrationOpportunitiesEntry.Available);
}

/// <summary>
/// Composes integration-opportunity evidence from the group-scoped
/// Integrations result and each participant's immutable image.
/// </summary>
public static class AssemblyContextIntegrationOpportunitiesQuery
{
    public static InspectionQuery<
        AssemblyContextIntegrationOpportunitiesResult> Definition { get; } =
        new(
            "Assembly context integration opportunities",
            InspectionCost.NetworkFree);

    public static AssemblyContextIntegrationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        InspectionQueryResults prerequisites)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(prerequisites);

        AssemblyContextIntegrationsResult integrations =
            prerequisites.Get(AssemblyContextIntegrationsQuery.Definition);
        if (integrations.Assemblies.Length != group.Participants.Length)
        {
            throw new InspectionQueryException(
                "Assembly context integrations did not produce one result per participant.");
        }

        var entries =
            ImmutableArray.CreateBuilder<
                AssemblyIntegrationOpportunitiesEntry>(
                    group.Participants.Length);
        for (int i = 0; i < group.Participants.Length; i++)
        {
            AssemblyContextParticipant participant = group.Participants[i];
            AssemblyIntegrationsEntry integration = integrations.Assemblies[i];
            EnsureSameParticipant(participant, integration.Subject);
            entries.Add(
                integration switch
                {
                    AssemblyIntegrationsEntry.Rejected rejected =>
                        new AssemblyIntegrationOpportunitiesEntry.Rejected(
                            rejected.Subject,
                            rejected.Failure),
                    AssemblyIntegrationsEntry.Failed failed =>
                        new AssemblyIntegrationOpportunitiesEntry.Failed(
                            failed.Subject,
                            failed.Error),
                    AssemblyIntegrationsEntry.Available available =>
                        Inspect(group, participant, available),
                    _ => throw new InvalidOperationException(
                        $"Unknown assembly integrations entry '{integration.GetType().Name}'."),
                });
        }

        return new AssemblyContextIntegrationOpportunitiesResult(
            entries.MoveToImmutable());
    }

    static AssemblyIntegrationOpportunitiesEntry Inspect(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyIntegrationsEntry.Available integrations)
    {
        var existing = new HashSet<string>(
            integrations.EcosystemSignals.Select(
                static signal => signal.Integration),
            StringComparer.Ordinal);
        if (!integrations.OpenTelemetrySignals.IsDefaultOrEmpty)
            existing.Add(EcosystemIntegrationNames.OpenTelemetry);

        AssemblyImageAccessResult<
            AssemblyIntegrationOpportunitiesEntry> access =
            group.UseAssemblySession<
                AssemblyIntegrationOpportunitiesEntry>(
                participant.Assembly,
                session =>
                {
                    try
                    {
                        return new AssemblyIntegrationOpportunitiesEntry.Available(
                            integrations.Subject,
                            session.IntegrationOpportunities(existing)
                                .ToImmutableArray());
                    }
                    catch (BadImageFormatException ex)
                    {
                        return new AssemblyIntegrationOpportunitiesEntry.Failed(
                            integrations.Subject,
                            ex);
                    }
                });

        return access switch
        {
            AssemblyImageAccessResult<
                AssemblyIntegrationOpportunitiesEntry>.Available available =>
                available.Value,
            AssemblyImageAccessResult<
                AssemblyIntegrationOpportunitiesEntry>.Rejected rejected =>
                new AssemblyIntegrationOpportunitiesEntry.Rejected(
                    integrations.Subject,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    static void EnsureSameParticipant(
        AssemblyContextParticipant participant,
        AssemblyContextSubject subject)
    {
        if (!ReferenceEquals(
                participant.Assembly.Registration,
                subject.Registration))
        {
            throw new InspectionQueryException(
                "Assembly context integrations result order does not match the group participants.");
        }
    }
}
