using System.Collections.Immutable;

namespace DotnetInspector.Sections;

/// <summary>
/// An exact host-neutral identity for an operation related to a command or
/// resolved subject.
/// </summary>
public sealed record RelatedOperationAffordanceId
{
    public const int MaximumLength = 96;

    public RelatedOperationAffordanceId(string value)
    {
        if (!IsCanonical(value))
        {
            throw new ArgumentException(
                "A related-operation affordance id must be a canonical dotted identifier.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsCanonical(string? value)
    {
        if (value is null
            || value.Length == 0
            || value.Length > MaximumLength)
        {
            return false;
        }

        ReadOnlySpan<char> remaining = value;
        int segmentCount = 0;
        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOf('.');
            ReadOnlySpan<char> segment =
                separator < 0
                    ? remaining
                    : remaining[..separator];
            if (!IsCanonicalSegment(segment))
                return false;

            segmentCount++;
            if (separator < 0)
                break;

            remaining = remaining[(separator + 1)..];
        }

        return segmentCount >= 2;
    }

    private static bool IsCanonicalSegment(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty || !IsLowerAsciiLetter(segment[0]))
            return false;

        bool afterSeparator = false;
        for (int i = 1; i < segment.Length; i++)
        {
            char character = segment[i];
            if (character == '-')
            {
                if (afterSeparator || i == segment.Length - 1)
                    return false;
                afterSeparator = true;
                continue;
            }

            if (!IsLowerAsciiLetter(character)
                && !IsAsciiDigit(character))
            {
                return false;
            }

            afterSeparator = false;
        }

        return true;
    }

    private static bool IsLowerAsciiLetter(char value) =>
        value is >= 'a' and <= 'z';

    private static bool IsAsciiDigit(char value) =>
        value is >= '0' and <= '9';
}

/// <summary>
/// One owner-issued semantic relationship to another product operation.
/// </summary>
public sealed record RelatedOperationAffordance
{
    public RelatedOperationAffordance(
        RelatedOperationAffordanceId id,
        string title,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        Id = id;
        Title = title;
        Summary = summary;
    }

    public RelatedOperationAffordanceId Id { get; }
    public string Title { get; }
    public string Summary { get; }
}

/// <summary>
/// Related operations issued by the Member inspection owner.
/// </summary>
public static class MemberRelatedOperationAffordances
{
    public static RelatedOperationAffordance InspectMember { get; } =
        Create(
            "member.inspect",
            "Inspect member",
            "Inspect one member overload and its implementation evidence.");

    public static RelatedOperationAffordance InspectMemberIndex { get; } =
        Create(
            "member.index",
            "Inspect member index",
            "Inspect the complete member selector and identity inventory.");

    public static RelatedOperationAffordance InspectTypeHierarchy { get; } =
        Create(
            "type.hierarchy",
            "Inspect type hierarchy",
            "Inspect the declaring type as a hierarchy.");

    public static RelatedOperationAffordance CompareTypeVersions { get; } =
        Create(
            "type.compare",
            "Compare type versions",
            "Compare the declaring type across package versions.");

    public static ImmutableArray<RelatedOperationAffordance> All { get; } =
    [
        InspectMember,
        InspectMemberIndex,
        InspectTypeHierarchy,
        CompareTypeVersions,
    ];

    private static RelatedOperationAffordance Create(
        string id,
        string title,
        string summary) =>
        new(new RelatedOperationAffordanceId(id), title, summary);
}
