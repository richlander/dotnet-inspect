using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public sealed record BodyUseScorecardWorkShape(
    string Asset,
    BodyUseScorecardClosing Closing,
    int TerminalValue,
    BodyUseScorecardDisposition Disposition,
    AnalysisLibraryBodyUseCoverage Coverage);

public static class BodyUseScorecardWorkShapes
{
    static readonly BodyUseScorecardClosing[] Closings =
    [
        BodyUseScorecardClosing.Exists,
        BodyUseScorecardClosing.Count,
        BodyUseScorecardClosing.Rows,
    ];

    public static IReadOnlyList<BodyUseScorecardWorkShape> Capture(
        IReadOnlyList<BodyUseScorecardAsset> assets,
        AnalysisLibraryBodyUseLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var workShapes = new List<BodyUseScorecardWorkShape>();
        foreach (BodyUseScorecardAsset asset in assets)
        {
            foreach (BodyUseScorecardClosing closing in Closings)
            {
                BodyUseScorecardExecution execution =
                    BodyUseScorecard.Execute(
                        BodyUseScorecardColumn.NLinq,
                        closing,
                        asset,
                        limits,
                        cancellationToken);
                workShapes.Add(
                    WorkShape(
                        asset.Name,
                        closing,
                        execution.Answer
                        ?? throw new InvalidOperationException(
                            "NLinq rejected the scorecard asset: "
                                + execution.Rejection)));
            }
        }
        return workShapes;
    }

    static BodyUseScorecardWorkShape WorkShape(
        string asset,
        BodyUseScorecardClosing closing,
        BodyUseScorecardAnswer answer) =>
        answer switch
        {
            BodyUseScorecardAnswer.Exists exists =>
                new(
                    asset,
                    closing,
                    exists.Value ? 1 : 0,
                    exists.Evidence.Disposition,
                    exists.Evidence.Coverage),
            BodyUseScorecardAnswer.Count count =>
                new(
                    asset,
                    closing,
                    count.Value,
                    count.Evidence.Disposition,
                    count.Evidence.Coverage),
            BodyUseScorecardAnswer.Rows rows =>
                new(
                    asset,
                    closing,
                    rows.Occurrences.Length,
                    Normalize(rows.Disposition),
                    rows.Coverage),
            _ => throw new InvalidOperationException(
                "The scorecard returned an unknown answer."),
        };

    static BodyUseScorecardDisposition Normalize(
        AnalysisLibraryBodyUseDisposition disposition) =>
        disposition switch
        {
            AnalysisLibraryBodyUseDisposition.Complete =>
                BodyUseScorecardDisposition.Complete,
            AnalysisLibraryBodyUseDisposition.Qualified =>
                BodyUseScorecardDisposition.Qualified,
            AnalysisLibraryBodyUseDisposition.Partial =>
                BodyUseScorecardDisposition.Partial,
            _ => throw new ArgumentOutOfRangeException(
                nameof(disposition)),
        };
}
