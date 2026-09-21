using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one participant-scoped Integrations inspection and returns its
/// detached host-neutral envelope.
/// </summary>
public static class AssemblyIntegrationsInspection
{
    public static InspectionEnvelope<AssemblyIntegrationsEntry> Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant) =>
        Envelope(
            AssemblyContextIntegrationsQuery.ExecuteParticipant(
                group,
                participant));

    /// <summary>
    /// Executes against one streamed participant, lends its retained image to
    /// the callback, then returns the detached envelope after release.
    /// </summary>
    public static async Task<InspectionEnvelope<AssemblyIntegrationsEntry>>
        ExecuteAndReleaseAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            Func<
                ResolvedAssemblyReference?,
                InspectionEnvelope<AssemblyIntegrationsEntry>,
                Task> callback)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(callback);

        InspectionEnvelope<AssemblyIntegrationsEntry>? completed = null;
        await AssemblyContextIntegrationsQuery
            .ExecuteParticipantAsync(
                group,
                participant,
                async (retained, integrations) =>
                {
                    InspectionEnvelope<AssemblyIntegrationsEntry> inspection =
                        Envelope(integrations);
                    await callback(retained, inspection)
                        .ConfigureAwait(false);
                    completed = inspection;
                    return true;
                })
            .ConfigureAwait(false);

        return completed
            ?? throw new InvalidOperationException(
                "The streamed Integrations inspection did not complete.");
    }

    static InspectionEnvelope<AssemblyIntegrationsEntry> Envelope(
        AssemblyIntegrationsEntry integrations) =>
        new(
            new ResourcePath("assembly-integrations"),
            InspectionContentKind.Outcome,
            integrations,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
}
