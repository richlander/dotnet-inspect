using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// One participant's typed Integrations prerequisite and dependent
/// Opportunities outcome.
/// </summary>
public sealed record AssemblyIntegrationOpportunitiesInspectionResult
{
    internal AssemblyIntegrationOpportunitiesInspectionResult(
        AssemblyIntegrationsEntry integrations,
        AssemblyIntegrationOpportunitiesEntry opportunities)
    {
        Integrations = integrations
            ?? throw new ArgumentNullException(nameof(integrations));
        Opportunities = opportunities
            ?? throw new ArgumentNullException(nameof(opportunities));
        if (!ReferenceEquals(
                Integrations.Subject.Registration,
                Opportunities.Subject.Registration))
        {
            throw new ArgumentException(
                "The Integrations prerequisite and Opportunities outcome belong to different participants.",
                nameof(opportunities));
        }
    }

    public AssemblyIntegrationsEntry Integrations { get; }

    public AssemblyIntegrationOpportunitiesEntry Opportunities { get; }
}

/// <summary>
/// Executes one participant-scoped Integration Opportunities inspection and
/// returns its detached host-neutral envelope.
/// </summary>
public static class AssemblyIntegrationOpportunitiesInspection
{
    public static InspectionEnvelope<
        AssemblyIntegrationOpportunitiesInspectionResult> Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        var result =
            AssemblyContextIntegrationOpportunitiesQuery
                .ExecuteParticipantWithPrerequisite(
                    group,
                    participant);
        return Envelope(result.Integrations, result.Opportunities);
    }

    /// <summary>
    /// Executes against one streamed participant, lends its retained image to
    /// the callback, then returns the detached envelope after release.
    /// </summary>
    public static async Task<InspectionEnvelope<
        AssemblyIntegrationOpportunitiesInspectionResult>>
        ExecuteAndReleaseAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            Func<
                ResolvedAssemblyReference?,
                InspectionEnvelope<
                    AssemblyIntegrationOpportunitiesInspectionResult>,
                Task> callback)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(callback);

        InspectionEnvelope<
            AssemblyIntegrationOpportunitiesInspectionResult>? completed =
            null;
        await AssemblyContextIntegrationOpportunitiesQuery
            .ExecuteParticipantAsync(
                group,
                participant,
                async (retained, integrations, opportunities) =>
                {
                    InspectionEnvelope<
                        AssemblyIntegrationOpportunitiesInspectionResult>
                        inspection = Envelope(
                            integrations,
                            opportunities);
                    await callback(retained, inspection)
                        .ConfigureAwait(false);
                    completed = inspection;
                    return true;
                })
            .ConfigureAwait(false);

        return completed
            ?? throw new InvalidOperationException(
                "The streamed Integration Opportunities inspection did not complete.");
    }

    static InspectionEnvelope<
        AssemblyIntegrationOpportunitiesInspectionResult> Envelope(
        AssemblyIntegrationsEntry integrations,
        AssemblyIntegrationOpportunitiesEntry opportunities) =>
        new(
            new ResourcePath("assembly-integration-opportunities"),
            InspectionContentKind.Result,
            new(
                integrations,
                opportunities),
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
}
