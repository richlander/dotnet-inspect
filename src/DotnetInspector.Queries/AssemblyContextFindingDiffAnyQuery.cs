using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>The outcome of one Finding Diff Any over two retained endpoint images.</summary>
public abstract record AssemblyContextFindingDiffAnyOutcome
{
    private AssemblyContextFindingDiffAnyOutcome()
    {
    }

    /// <summary>Both images were compared; every Type has API and Body states.</summary>
    public sealed record Compared(FindingDiffAnyResult Result) : AssemblyContextFindingDiffAnyOutcome;

    /// <summary>The step's budget elapsed; the comparison continues in a later step.</summary>
    public sealed record Pending : AssemblyContextFindingDiffAnyOutcome;

    /// <summary>One endpoint image could not be opened or admitted.</summary>
    public sealed record Rejected(
        AssemblyContextSubject Subject,
        string Reason) : AssemblyContextFindingDiffAnyOutcome;
}

/// <summary>
/// Advances one <see cref="FindingDiffAnyComparison"/> by one bounded step over two
/// retained endpoint images, each read through its own group's retained
/// session.
/// </summary>
public static class AssemblyContextFindingDiffAnyQuery
{
    public static InspectionQuery<AssemblyContextFindingDiffAnyOutcome> Definition
    { get; } = new(
        "Assembly context Finding Diff Any",
        InspectionCost.Moderated);

    public static AssemblyContextFindingDiffAnyOutcome Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant afterParticipant,
        FindingDiffAnyComparison comparison,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(beforeParticipant);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(afterParticipant);

        var beforeSubject = new AssemblyContextSubject(beforeParticipant.Assembly);
        AssemblyImageAccessResult<AssemblyContextFindingDiffAnyOutcome> before =
            beforeGroup.UseAssemblySession(
                beforeParticipant,
                cancellationToken,
                (beforeSession, _) =>
                {
                    AssemblyImageAccessResult<AssemblyContextFindingDiffAnyOutcome> after =
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

    static AssemblyContextFindingDiffAnyOutcome Step(
        AssemblyInspectionSession beforeSession,
        AssemblyContextSubject beforeSubject,
        AssemblyInspectionSession afterSession,
        FindingDiffAnyComparison comparison,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return beforeSession.InspectImage(beforeImage =>
                afterSession.InspectImage<AssemblyContextFindingDiffAnyOutcome>(afterImage =>
                    comparison.Step(beforeImage, afterImage, budget, cancellationToken)
                        ? new AssemblyContextFindingDiffAnyOutcome.Compared(comparison.Result!)
                        : new AssemblyContextFindingDiffAnyOutcome.Pending()));
        }
        catch (BadImageFormatException ex)
        {
            // FindingDiffAny contains malformed rows per Type; only a whole image it
            // cannot admit reaches here, and the endpoint is not identified.
            return new AssemblyContextFindingDiffAnyOutcome.Rejected(
                beforeSubject,
                $"The endpoint images could not be compared: {ex.Message}");
        }
    }

    static AssemblyContextFindingDiffAnyOutcome Unwrap(
        AssemblyImageAccessResult<AssemblyContextFindingDiffAnyOutcome> access)
        => access switch
        {
            AssemblyImageAccessResult<AssemblyContextFindingDiffAnyOutcome>.Available available =>
                available.Value,
            AssemblyImageAccessResult<AssemblyContextFindingDiffAnyOutcome>.Rejected rejected =>
                new AssemblyContextFindingDiffAnyOutcome.Rejected(
                    new AssemblyContextSubject(rejected.Assembly),
                    rejected.Failure.ToString()),
            _ => throw new InvalidOperationException("Unknown assembly image access result."),
        };
}
