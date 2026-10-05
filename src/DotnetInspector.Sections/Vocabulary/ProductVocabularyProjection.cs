using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections;

/// <summary>
/// Projects a host-composed product vocabulary snapshot to the Product
/// Vocabulary document: fields, operators, and rows per section. A snapshot
/// that lacks a product section fails visibly.
/// </summary>
public static class ProductVocabularyProjection
{
    /// <summary>Projects <paramref name="snapshot"/> to the Product Vocabulary document.</summary>
    public static VocabularyDocument ToDocument(VocabularySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        VocabularyDefinition index = Get(
            snapshot,
            ProductVocabularyComposition.SectionsId);
        VocabularyDefinition accessibility = Get(
            snapshot,
            ApiAccessibilityVocabulary.AccessibilityId);
        VocabularyDefinition tiers = Get(
            snapshot,
            StyleOptionVocabularies.StyleTiersId);
        VocabularyDefinition choices = Get(
            snapshot,
            StyleOptionVocabularies.StyleChoicesId);
        VocabularyDefinition bodyKinds = Get(
            snapshot,
            BodyShapeVocabulary.BodyKindsId);

        return new(
            2,
            [
                CreateSection(
                    index,
                    ["vocabulary"],
                    [
                        IdentityField("Stable section identity."),
                        DisplayField(
                            "section",
                            "Section",
                            "Human-facing section name.",
                            VocabularyOperator.Equals,
                            VocabularyOperator.NotEquals,
                            VocabularyOperator.Glob),
                        SummaryField("What the vocabulary values control."),
                        MapField(
                            index.GetMap("accepted_by"),
                            VocabularyOperator.Contains),
                        MapField(
                            index.GetMap("values"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.LessThan,
                            VocabularyOperator.GreaterThan),
                    ]),
                CreateSection(
                    accessibility,
                    AcceptedBy(index, accessibility),
                    [
                        IdentityField(
                            "Stable accessibility selection identity."),
                        DisplayField(
                            "label",
                            "Label",
                            "Product-owned display label.",
                            VocabularyOperator.Equals,
                            VocabularyOperator.Glob),
                        MapField(
                            accessibility.GetMap("order"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.LessThan,
                            VocabularyOperator.GreaterThan),
                        MapField(accessibility.GetMap("default")),
                    ]),
                CreateSection(
                    tiers,
                    AcceptedBy(index, tiers),
                    [
                        IdentityField("Stable style-tier identity."),
                        DisplayField(
                            "title",
                            "Title",
                            "Product-owned tier title.",
                            VocabularyOperator.Equals,
                            VocabularyOperator.Glob),
                        SummaryField("The fidelity contract of this tier."),
                        MapField(
                            tiers.GetMap("order"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.LessThan,
                            VocabularyOperator.GreaterThan),
                        MapField(tiers.GetMap("byte_divergent")),
                    ]),
                CreateSection(
                    choices,
                    AcceptedBy(index, choices),
                    [
                        IdentityField(
                            "Stable selectable style-choice identity."),
                        MapField(
                            choices.GetMap("option"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.NotEquals,
                            VocabularyOperator.In),
                        MapField(
                            choices.GetMap("value"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.NotEquals,
                            VocabularyOperator.In),
                        DisplayField(
                            "title",
                            "Title",
                            "Product-owned picker label.",
                            VocabularyOperator.Equals,
                            VocabularyOperator.Glob),
                        SummaryField("What the choice changes."),
                        MapField(
                            choices.GetMap("tier"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.NotEquals,
                            VocabularyOperator.In),
                        MapField(choices.GetMap("byte_divergent")),
                        MapField(choices.GetMap("oracle_endorsed")),
                        MapField(choices.GetMap("corpus_endorsed")),
                        MapField(
                            choices.GetMap("conflict_group"),
                            VocabularyOperator.Equals,
                            VocabularyOperator.NotEquals,
                            VocabularyOperator.In),
                    ]),
                CreateSection(
                    bodyKinds,
                    AcceptedBy(index, bodyKinds),
                    [
                        IdentityField(
                            "Exact stable rendered-syntax kind.",
                            VocabularyOperator.Equals),
                        DisplayField(
                            "label",
                            "Label",
                            "Product-owned display label.",
                            VocabularyOperator.Equals,
                            VocabularyOperator.Glob),
                    ]),
            ]);
    }

    private static VocabularyDefinition Get(
        VocabularySnapshot snapshot,
        string identity) =>
        snapshot.GetVocabulary(new(snapshot.Catalog, identity));

    private static VocabularySection CreateSection(
        VocabularyDefinition vocabulary,
        ImmutableArray<string> acceptedBy,
        ImmutableArray<VocabularyField> fields) =>
        new(
            vocabulary.Identity.Value,
            vocabulary.DisplayLabel,
            vocabulary.Summary
                ?? throw new InvalidOperationException(
                    $"Vocabulary '{vocabulary.Identity}' has no required "
                    + "Product Vocabulary summary."),
            acceptedBy,
            fields,
            [
                .. vocabulary.Terms.Select(term =>
                    CreateRow(vocabulary, term, fields)),
            ]);

    private static VocabularyRow CreateRow(
        VocabularyDefinition vocabulary,
        VocabularyTerm term,
        ImmutableArray<VocabularyField> fields)
    {
        var values = new List<(string, VocabularyValue)>();
        foreach (VocabularyField field in fields)
        {
            VocabularyValue? value = field.Id switch
            {
                "id" => VocabularyValue.FromText(term.Identity.Value),
                "label" or "title" or "section" =>
                    VocabularyValue.FromText(term.DisplayLabel),
                "summary" => VocabularyValue.FromText(
                    term.Summary
                    ?? throw new InvalidOperationException(
                        $"Term '{term.Identity}' has no required summary.")),
                _ => MapValue(vocabulary.GetMap(field.Id), term),
            };
            if (value is { } present)
                values.Add((field.Id, present));
        }
        return new([.. values]);
    }

    private static VocabularyValue? MapValue(
        VocabularyMapDefinition map,
        VocabularyTerm term)
    {
        ImmutableArray<VocabularyMapValue> values =
            term.GetRequiredValues(map.Identity);
        if (values.Length == 0)
            return null;

        if (map.Target is VocabularyMapTarget.Terms)
        {
            return VocabularyValue.FromText(
                ((VocabularyMapValue.Term)values[0]).Identity.Value);
        }

        var scalar = (VocabularyMapValue.Scalar)values[0];
        if (map.Cardinality is VocabularyMapCardinality.OneOrMore
            or VocabularyMapCardinality.ZeroOrMore)
        {
            return VocabularyValue.FromTextList(
                values.Select(value =>
                    ((VocabularyMapValue.Scalar)value).Value.Text!));
        }

        return scalar.Value.Kind switch
        {
            VocabularyScalarKind.Text =>
                VocabularyValue.FromText(scalar.Value.Text!),
            VocabularyScalarKind.Integer =>
                VocabularyValue.FromInteger(checked((int)scalar.Value.Integer)),
            VocabularyScalarKind.Boolean =>
                VocabularyValue.FromBoolean(scalar.Value.Boolean),
            _ => throw new InvalidOperationException(
                $"Unsupported scalar kind '{scalar.Value.Kind}'."),
        };
    }

    private static ImmutableArray<string> AcceptedBy(
        VocabularyDefinition index,
        VocabularyDefinition vocabulary)
    {
        VocabularyMapDefinition map = index.GetMap("accepted_by");
        VocabularyTerm term = index.GetTerm(vocabulary.Identity.Value);
        return
        [
            .. term.GetRequiredValues(map.Identity)
                .Select(value =>
                    ((VocabularyMapValue.Scalar)value).Value.Text!),
        ];
    }

    private static VocabularyField IdentityField(
        string summary,
        params VocabularyOperator[] operators) =>
        new(
            "id",
            "ID",
            summary,
            VocabularyValueKind.Text,
            operators.Length == 0
                ?
                [
                    VocabularyOperator.Equals,
                    VocabularyOperator.NotEquals,
                    VocabularyOperator.In,
                ]
                : [.. operators]);

    private static VocabularyField DisplayField(
        string identity,
        string label,
        string summary,
        params VocabularyOperator[] operators) =>
        new(
            identity,
            label,
            summary,
            VocabularyValueKind.Text,
            [.. operators]);

    private static VocabularyField SummaryField(string summary) =>
        new(
            "summary",
            "Summary",
            summary,
            VocabularyValueKind.Text,
            [VocabularyOperator.Glob]);

    private static VocabularyField MapField(
        VocabularyMapDefinition map,
        params VocabularyOperator[] operators)
    {
        VocabularyValueKind kind = map.Target switch
        {
            VocabularyMapTarget.Terms => VocabularyValueKind.Text,
            VocabularyMapTarget.Scalar
            {
                Kind: VocabularyScalarKind.Text
            } when map.Cardinality is VocabularyMapCardinality.OneOrMore
                or VocabularyMapCardinality.ZeroOrMore =>
                VocabularyValueKind.TextList,
            VocabularyMapTarget.Scalar
            {
                Kind: VocabularyScalarKind.Text
            } => VocabularyValueKind.Text,
            VocabularyMapTarget.Scalar
            {
                Kind: VocabularyScalarKind.Integer
            } => VocabularyValueKind.Integer,
            VocabularyMapTarget.Scalar
            {
                Kind: VocabularyScalarKind.Boolean
            } => VocabularyValueKind.Boolean,
            _ => throw new InvalidOperationException(
                $"Unsupported map target for '{map.Identity}'."),
        };
        ImmutableArray<VocabularyOperator> resolvedOperators =
            operators.Length > 0
                ? [.. operators]
                : kind == VocabularyValueKind.Boolean
                    ?
                    [
                        VocabularyOperator.Equals,
                        VocabularyOperator.NotEquals,
                    ]
                    : [];
        return new(
            map.Identity.Value,
            map.DisplayLabel,
            map.Summary,
            kind,
            resolvedOperators);
    }
}
