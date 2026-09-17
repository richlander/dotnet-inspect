using System.Diagnostics;

namespace DotnetInspector.Sections;

/// <summary>
/// Selects ordinary or evidence-enabled inspection execution for a Debug host.
/// </summary>
/// <typeparam name="TContent">The owner-issued primary content result.</typeparam>
/// <typeparam name="TEvidence">The owner-issued supplemental evidence.</typeparam>
public sealed class EvidenceBuilder<TContent, TEvidence>
{
    private bool _evidenceRequested;

    /// <summary>
    /// Requests evidence-enabled execution when the caller is compiled with
    /// the <c>DEBUG</c> symbol and <paramref name="requested"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Release callers omit this call and do not evaluate its arguments.
    /// </remarks>
    [Conditional("DEBUG")]
    public void RequestEvidence(bool requested)
    {
        _evidenceRequested = requested;
    }

    /// <summary>
    /// Invokes exactly one inspection delegate and returns the ordinary
    /// inspection with its optional evidence envelope.
    /// </summary>
    /// <typeparam name="TState">
    /// The state supplied to the static inspection delegates.
    /// </typeparam>
    /// <param name="state">The operation state.</param>
    /// <param name="inspect">The ordinary inspection operation.</param>
    /// <param name="inspectWithEvidence">
    /// The evidence-enabled inspection operation.
    /// </param>
    /// <returns>
    /// The ordinary inspection and, when requested, an evidence envelope that
    /// contains that same inspection instance.
    /// </returns>
    public (
        InspectionEnvelope<TContent> Inspection,
        EvidenceInspectionEnvelope<TContent, TEvidence>? Evidence)
        Build<TState>(
            TState state,
            Func<TState, InspectionEnvelope<TContent>> inspect,
            Func<
                TState,
                EvidenceInspectionEnvelope<TContent, TEvidence>>
                inspectWithEvidence)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        ArgumentNullException.ThrowIfNull(inspectWithEvidence);

        if (!_evidenceRequested)
            return (inspect(state), null);

        EvidenceInspectionEnvelope<TContent, TEvidence> evidence =
            inspectWithEvidence(state);
        return (evidence.Inspection, evidence);
    }
}
