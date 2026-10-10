using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>The outcome of one Fast Diff over two retained endpoint images.</summary>
public abstract record AssemblyContextFastDiffOutcome
{
    private AssemblyContextFastDiffOutcome()
    {
    }

    /// <summary>Both images were compared; every Type has API and Body states.</summary>
    public sealed record Compared(FastDiffResult Result) : AssemblyContextFastDiffOutcome;

    /// <summary>The step's budget elapsed; the comparison continues in a later step.</summary>
    public sealed record Pending : AssemblyContextFastDiffOutcome;

    /// <summary>One endpoint image could not be opened or admitted.</summary>
    public sealed record Rejected(
        AssemblyContextSubject Subject,
        string Reason) : AssemblyContextFastDiffOutcome;
}

/// <summary>
/// Advances one <see cref="FastDiffComparison"/> by one bounded step over two
/// retained endpoint images, each read through its own group's retained
/// session.
/// </summary>
public static class AssemblyContextFastDiffQuery
{
    public static InspectionQuery<AssemblyContextFastDiffOutcome> Definition
    { get; } = new(
        "Assembly context fast diff",
        InspectionCost.Moderated);

    public static AssemblyContextFastDiffOutcome Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant afterParticipant,
        FastDiffComparison comparison,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(beforeParticipant);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(afterParticipant);

        var beforeSubject = new AssemblyContextSubject(beforeParticipant.Assembly);
        AssemblyImageAccessResult<AssemblyContextFastDiffOutcome> before =
            beforeGroup.UseAssemblySession(
                beforeParticipant,
                cancellationToken,
                (beforeSession, _) =>
                {
                    AssemblyImageAccessResult<AssemblyContextFastDiffOutcome> after =
                        afterGroup.UseAssemblySession(
                            afterParticipant,
                            cancellationToken,
                            (afterSession, _) => Step(
                                beforeSession,
                                beforeSubject,
                                afterSession,
                                comparison,
                                budget,
                                cancellationToken));
                    return Unwrap(after);
                });
        return Unwrap(before);
    }

    static AssemblyContextFastDiffOutcome Step(
        AssemblyInspectionSession beforeSession,
        AssemblyContextSubject beforeSubject,
        AssemblyInspectionSession afterSession,
        FastDiffComparison comparison,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return beforeSession.InspectImage(beforeImage =>
                afterSession.InspectImage<AssemblyContextFastDiffOutcome>(afterImage =>
                    comparison.Step(beforeImage, afterImage, budget, cancellationToken)
                        ? new AssemblyContextFastDiffOutcome.Compared(comparison.Result!)
                        : new AssemblyContextFastDiffOutcome.Pending()));
        }
        catch (BadImageFormatException ex)
        {
            // FastDiff contains malformed rows per Type; only a whole image it
            // cannot admit reaches here, and the endpoint is not identified.
            return new AssemblyContextFastDiffOutcome.Rejected(
                beforeSubject,
                $"The endpoint images could not be compared: {ex.Message}");
        }
    }

    static AssemblyContextFastDiffOutcome Unwrap(
        AssemblyImageAccessResult<AssemblyContextFastDiffOutcome> access)
        => access switch
        {
            AssemblyImageAccessResult<AssemblyContextFastDiffOutcome>.Available available =>
                available.Value,
            AssemblyImageAccessResult<AssemblyContextFastDiffOutcome>.Rejected rejected =>
                new AssemblyContextFastDiffOutcome.Rejected(
                    new AssemblyContextSubject(rejected.Assembly),
                    rejected.Failure.ToString()),
            _ => throw new InvalidOperationException("Unknown assembly image access result."),
        };
}
