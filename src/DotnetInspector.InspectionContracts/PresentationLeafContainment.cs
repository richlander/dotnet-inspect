using System.Collections.Immutable;
using InertText;

namespace DotnetInspector.InspectionContracts;

/// <summary>One displayed value identified within an owner-issued document generation.</summary>
public sealed record PresentationLeaf(
    int Identity,
    TextPolicy Policy,
    string Text);

/// <summary>One displayed value contained under its owner-declared policy.</summary>
public sealed record ContainedPresentationLeaf(
    int Identity,
    TextPolicy Policy,
    InertString Text);

/// <summary>Bounds one atomic presentation-leaf containment operation.</summary>
public sealed record PresentationLeafContainmentLimits
{
    public PresentationLeafContainmentLimits(
        int maximumLeaves,
        long maximumInputCharacters,
        long maximumOutputCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLeaves);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumInputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumOutputCharacters);
        MaximumLeaves = maximumLeaves;
        MaximumInputCharacters = maximumInputCharacters;
        MaximumOutputCharacters = maximumOutputCharacters;
    }

    public int MaximumLeaves { get; }

    public long MaximumInputCharacters { get; }

    public long MaximumOutputCharacters { get; }
}

/// <summary>Why an atomic presentation-leaf containment operation produced no population.</summary>
public enum PresentationLeafContainmentFailureKind
{
    InvalidGeneration,
    InvalidLeafIdentity,
    DuplicateLeafIdentity,
    MissingLeafText,
    UnsupportedTextPolicy,
    TooManyLeaves,
    InputTextTooLarge,
    OutputTextTooLarge,
}

/// <summary>Bounded failure evidence without reproducing externally authored text.</summary>
public sealed record PresentationLeafContainmentFailure(
    PresentationLeafContainmentFailureKind Kind,
    int? Identity = null,
    long? Actual = null,
    long? Limit = null);

/// <summary>The atomic outcome for one owner-issued presentation-leaf generation.</summary>
public abstract record PresentationLeafContainmentOutcome
{
    private PresentationLeafContainmentOutcome()
    {
    }

    /// <summary>The complete contained population for the requested generation.</summary>
    public sealed record Contained(
        string Generation,
        ImmutableArray<ContainedPresentationLeaf> Leaves)
        : PresentationLeafContainmentOutcome;

    /// <summary>No contained population was constructed.</summary>
    public sealed record Rejected(
        string? Generation,
        PresentationLeafContainmentFailure Failure)
        : PresentationLeafContainmentOutcome;
}

/// <summary>Contains one complete, bounded generation of presentation leaves.</summary>
public static class PresentationLeafContainment
{
    public static PresentationLeafContainmentOutcome Execute(
        string? generation,
        IReadOnlyList<PresentationLeaf> leaves,
        PresentationLeafContainmentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(limits);

        if (string.IsNullOrWhiteSpace(generation))
        {
            return Reject(
                generation,
                PresentationLeafContainmentFailureKind.InvalidGeneration);
        }

        if (leaves.Count > limits.MaximumLeaves)
        {
            return Reject(
                generation,
                PresentationLeafContainmentFailureKind.TooManyLeaves,
                actual: leaves.Count,
                limit: limits.MaximumLeaves);
        }

        long inputCharacters = 0;
        var identities = new HashSet<int>();
        foreach (PresentationLeaf? leaf in leaves)
        {
            if (leaf is null || leaf.Identity < 0)
            {
                return Reject(
                    generation,
                    PresentationLeafContainmentFailureKind.InvalidLeafIdentity,
                    leaf?.Identity);
            }

            if (!identities.Add(leaf.Identity))
            {
                return Reject(
                    generation,
                    PresentationLeafContainmentFailureKind.DuplicateLeafIdentity,
                    leaf.Identity);
            }

            if (leaf.Text is null)
            {
                return Reject(
                    generation,
                    PresentationLeafContainmentFailureKind.MissingLeafText,
                    leaf.Identity);
            }

            if (!Enum.IsDefined(leaf.Policy))
            {
                return Reject(
                    generation,
                    PresentationLeafContainmentFailureKind.UnsupportedTextPolicy,
                    leaf.Identity);
            }

            inputCharacters = checked(inputCharacters + leaf.Text.Length);
            if (inputCharacters > limits.MaximumInputCharacters)
            {
                return Reject(
                    generation,
                    PresentationLeafContainmentFailureKind.InputTextTooLarge,
                    leaf.Identity,
                    inputCharacters,
                    limits.MaximumInputCharacters);
            }
        }

        long outputCharacters = 0;
        var contained = ImmutableArray.CreateBuilder<ContainedPresentationLeaf>(
            leaves.Count);
        foreach (PresentationLeaf leaf in leaves)
        {
            var text = new InertString(leaf.Policy, leaf.Text);
            outputCharacters = checked(
                outputCharacters + text.ToString().Length);
            if (outputCharacters > limits.MaximumOutputCharacters)
            {
                return Reject(
                    generation,
                    PresentationLeafContainmentFailureKind.OutputTextTooLarge,
                    leaf.Identity,
                    outputCharacters,
                    limits.MaximumOutputCharacters);
            }

            contained.Add(new(leaf.Identity, leaf.Policy, text));
        }

        return new PresentationLeafContainmentOutcome.Contained(
            generation,
            contained.MoveToImmutable());
    }

    private static PresentationLeafContainmentOutcome.Rejected Reject(
        string? generation,
        PresentationLeafContainmentFailureKind kind,
        int? identity = null,
        long? actual = null,
        long? limit = null) =>
        new(
            generation,
            new(kind, identity, actual, limit));
}
