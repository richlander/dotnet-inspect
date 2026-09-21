using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Presentation;

/// <summary>One nonterminal observation from an ecosystem change report.</summary>
public abstract record EcosystemChangeReportNonterminalEvent
{
    private EcosystemChangeReportNonterminalEvent()
    {
    }

    public sealed record Progress(EcosystemChangeReportProgressPresentation Value)
        : EcosystemChangeReportNonterminalEvent;

    public sealed record Row(EcosystemChangeReportRowPresentation Value)
        : EcosystemChangeReportNonterminalEvent;

    public sealed record Failure(EcosystemChangeReportFailurePresentation Value)
        : EcosystemChangeReportNonterminalEvent;
}

/// <summary>Receives nonterminal report observations in producer order.</summary>
public interface IEcosystemChangeReportNonterminalSink
{
    ValueTask ReportAsync(
        EcosystemChangeReportNonterminalEvent reportEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Materializes one ecosystem change report while projecting its nonterminal
/// observations to an optional host sink.
/// </summary>
public static class EcosystemChangeReportInspection
{
    public static async ValueTask<
        InspectionEnvelope<EcosystemChangeReportDocument>> ExecuteAsync(
            IAsyncEnumerable<EcosystemChangeReportEvent> events,
            IEcosystemChangeReportNonterminalSink? nonterminalSink = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();

        var collected = new List<EcosystemChangeReportEvent>();
        bool completed = false;
        await foreach (EcosystemChangeReportEvent item
            in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(item);
            if (completed)
            {
                throw new InvalidOperationException(
                    "An ecosystem report event appeared after terminal completion.");
            }

            collected.Add(item);
            EcosystemChangeReportNonterminalEvent? nonterminal = item switch
            {
                EcosystemChangeReportEvent.Progress progress =>
                    new EcosystemChangeReportNonterminalEvent.Progress(
                        EcosystemChangeReportPresentation.Project(
                            progress.Value)),
                EcosystemChangeReportEvent.Row row =>
                    new EcosystemChangeReportNonterminalEvent.Row(
                        EcosystemChangeReportPresentation.Project(row.Value)),
                EcosystemChangeReportEvent.Failure failure =>
                    new EcosystemChangeReportNonterminalEvent.Failure(
                        EcosystemChangeReportPresentation.Project(failure)),
                EcosystemChangeReportEvent.Completed =>
                    null,
                _ => throw new InvalidOperationException(
                    "Unknown ecosystem report event kind."),
            };

            if (nonterminal is null)
            {
                completed = true;
                continue;
            }

            if (nonterminalSink is not null)
            {
                await nonterminalSink.ReportAsync(
                    nonterminal,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        EcosystemChangeReportDocument document =
            EcosystemChangeReportPresentation.Create(collected);
        return new(
            InspectionContentKind.Document,
            document,
            new InspectionPortableProjection.NonProjectable(
                "package-changes/share",
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
