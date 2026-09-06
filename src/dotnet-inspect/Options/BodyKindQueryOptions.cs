using DotnetInspector.Sections;
using ILInspector.CSharp;
using ILInspector.Decompiler;

namespace DotnetInspector.Options;

/// <summary>
/// Exact rendered-syntax kind predicate for the Body Shapes section.
/// </summary>
public sealed record BodyKindQueryOptions
{
    /// <summary>No body-kind predicate.</summary>
    public static BodyKindQueryOptions Default { get; } = new();

    public static SectionQueryFacet QueryFacet { get; } = new(
        "Kind",
        ["--where"],
        ["="],
        "C# body kind (case-sensitive; exactly one required)",
        [.. BodyShapeSearch.SupportedKinds],
        "--where \"Kind=ObjectCreationExpression\"");

    /// <summary>The exact stable ID from the C# Body Kinds vocabulary.</summary>
    public string? Kind { get; init; }

    /// <summary>Whether a body-kind predicate was supplied.</summary>
    public bool HasFilter => Kind is not null;

    /// <summary>
    /// Extracts the Body Shapes predicate from shared <c>--where</c> expressions and leaves
    /// predicates owned by other sections for their existing parsers.
    /// </summary>
    public static bool TryExtract(
        IReadOnlyList<string> expressions,
        out BodyKindQueryOptions options,
        out string[] remaining,
        out OptionError error)
    {
        string? kind = null;
        List<string> unclaimed = [];

        foreach (var expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(
                    expression,
                    out var syntax,
                    out error))
            {
                options = Default;
                remaining = [];
                return false;
            }

            if (!RowPredicateSyntaxParser.NormalizeFieldName(syntax.Field).Equals(
                    "Kind",
                    StringComparison.OrdinalIgnoreCase))
            {
                unclaimed.Add(expression);
                continue;
            }

            if (syntax.Operator != RowPredicateOperator.Equals)
            {
                options = Default;
                remaining = [];
                error = "Field 'Kind' in section 'Body Shapes' supports only = predicates.";
                return false;
            }

            if (kind is not null)
            {
                options = Default;
                remaining = [];
                error = "Section 'Body Shapes' accepts exactly one --where Kind=... predicate.";
                return false;
            }

            if (!BodyShapeSearch.SupportedKinds.Contains(
                    syntax.Value,
                    StringComparer.Ordinal))
            {
                options = Default;
                remaining = [];
                string contained =
                    CSharpIdentifier.ContainRenderedText(syntax.Value);
                string? casingMatch = BodyShapeSearch.SupportedKinds.FirstOrDefault(candidate =>
                    candidate.Equals(
                        syntax.Value,
                        StringComparison.OrdinalIgnoreCase));
                error = casingMatch is null
                    ? new OptionError(
                        $"Unknown C# body kind '{contained}'.",
                        ["Run 'vocabulary -S \"C# Body Kinds\"' to list valid IDs."])
                    : new OptionError(
                        $"Unknown C# body kind '{contained}'.",
                        ["Body-kind IDs are case-sensitive.", $"Did you mean: {casingMatch}"]);
                return false;
            }

            kind = syntax.Value;
        }

        options = new BodyKindQueryOptions { Kind = kind };
        remaining = [.. unclaimed];
        error = "";
        return true;
    }
}
