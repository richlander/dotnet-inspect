using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>A stable machine identity for one authored View Facet set.</summary>
public sealed record ViewFacetSetId
{
    public const int MaximumLength = 80;

    public ViewFacetSetId(string value)
    {
        if (!IsCanonical(value))
        {
            throw new ArgumentException(
                "A view-facet set id must be a canonical lower-kebab identifier.",
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
            || value.Length > MaximumLength
            || !IsLowerAsciiLetter(value[0]))
        {
            return false;
        }

        bool afterSeparator = false;
        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if (character == '-')
            {
                if (afterSeparator || i == value.Length - 1)
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
/// One authored set with independent display text and explicit ordered View Facet membership.
/// </summary>
public sealed record ViewFacetSetDescriptor
{
    public ViewFacetSetDescriptor(
        ViewFacetSetId id,
        string title,
        ViewFacetRegistry registry,
        IEnumerable<ViewFacetId> facets)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(facets);

        ImmutableArray<ViewFacetId> members = [.. facets];
        if (members.IsEmpty)
        {
            throw new ArgumentException(
                "A view-facet set must contain at least one exact facet identity.",
                nameof(facets));
        }
        if (members.Any(static facet => facet is null))
        {
            throw new ArgumentException(
                "View-facet set membership cannot contain null.",
                nameof(facets));
        }
        if (members.Distinct().Count() != members.Length)
        {
            throw new ArgumentException(
                "A view-facet set cannot contain duplicate facet identities.",
                nameof(facets));
        }
        if (members.Any(facet =>
                !registry.TryGetDescriptor(facet.Value, out _)))
        {
            throw new ArgumentException(
                "A view-facet set can contain only Registry-issued facet identities.",
                nameof(facets));
        }

        Id = id;
        Title = title;
        Facets = members;
    }

    public ViewFacetSetId Id { get; }
    public string Title { get; }
    public ImmutableArray<ViewFacetId> Facets { get; }
}
