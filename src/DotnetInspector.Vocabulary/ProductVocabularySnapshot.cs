using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Vocabulary;

internal static class ProductVocabularySnapshot
{
    internal const string CatalogId = "dotnet-inspect.product";
    internal const string SectionsId = "vocabulary.sections";
    internal const string AccessibilityId = "api.accessibility";
    internal const string StyleTiersId = "csharp.style-tiers";
    internal const string StyleChoicesId = "csharp.style-choices";
    internal const string BodyKindsId = "csharp.body-kinds";

    internal static VocabularySnapshot Create()
    {
        var catalog = new VocabularyCatalogIdentity(CatalogId);
        var accessibility = new VocabularyIdentity(catalog, AccessibilityId);
        var styleTiers = new VocabularyIdentity(catalog, StyleTiersId);
        var styleChoices = new VocabularyIdentity(catalog, StyleChoicesId);
        var bodyKinds = new VocabularyIdentity(catalog, BodyKindsId);
        var sections = new VocabularyIdentity(catalog, SectionsId);

        VocabularyDefinition accessibilityVocabulary =
            CreateAccessibility(accessibility);
        VocabularyDefinition styleTiersVocabulary =
            CreateStyleTiers(styleTiers);
        VocabularyDefinition styleChoicesVocabulary =
            CreateStyleChoices(styleChoices, styleTiers);
        VocabularyDefinition bodyKindsVocabulary =
            CreateBodyKinds(bodyKinds);

        VocabularyDefinition sectionIndex = CreateSectionIndex(
            sections,
            [
                (
                    accessibilityVocabulary,
                    ["api.type-inventory", "api.member-inventory"]),
                (
                    styleTiersVocabulary,
                    ["decompiler.style-picker"]),
                (
                    styleChoicesVocabulary,
                    ["decompiler.style-picker", "decompiler.render"]),
                (
                    bodyKindsVocabulary,
                    ["decompiler.body-kind"]),
            ]);

        return VocabularySnapshot.Create(
            1,
            catalog,
            [
                sectionIndex,
                accessibilityVocabulary,
                styleTiersVocabulary,
                styleChoicesVocabulary,
                bodyKindsVocabulary,
            ]);
    }

    private static VocabularyDefinition CreateSectionIndex(
        VocabularyIdentity identity,
        ImmutableArray<(
            VocabularyDefinition Vocabulary,
            ImmutableArray<string> AcceptedBy)> vocabularies)
    {
        VocabularyMapDefinition acceptedBy = ScalarMap(
            identity,
            "accepted_by",
            "Accepted By",
            "Typed query inputs that consume these values.",
            VocabularyScalarKind.Text,
            VocabularyMapCardinality.OneOrMore);
        VocabularyMapDefinition values = ScalarMap(
            identity,
            "values",
            "Values",
            "Number of legal values.",
            VocabularyScalarKind.Integer);

        return new(
            identity,
            VocabularyCatalog.SectionsSection,
            "Product-owned vocabularies available as rich-query inputs.",
            [acceptedBy, values],
            vocabularies.Select(item => new VocabularyTerm(
                new(identity, item.Vocabulary.Identity.Value),
                item.Vocabulary.DisplayLabel,
                item.Vocabulary.Summary,
                [
                    Entry(
                        acceptedBy,
                        item.AcceptedBy.Select(Text)),
                    Entry(
                        values,
                        Integer(item.Vocabulary.Terms.Length)),
                ])));
    }

    private static VocabularyDefinition CreateAccessibility(
        VocabularyIdentity identity)
    {
        VocabularyMapDefinition order = ScalarMap(
            identity,
            "order",
            "Order",
            "Product-owned presentation order.",
            VocabularyScalarKind.Integer);
        VocabularyMapDefinition defaultMap = ScalarMap(
            identity,
            "default",
            "Default",
            "Whether this value participates without an explicit selection.",
            VocabularyScalarKind.Boolean);

        return new(
            identity,
            VocabularyCatalog.AccessibilitySection,
            "Accessibility facets accepted by API type and member inventory queries.",
            [order, defaultMap],
            ApiAccessibility.Values.Select(bucket => new VocabularyTerm(
                new(identity, bucket.Id),
                bucket.Label,
                summary: null,
                [
                    Entry(order, Integer(bucket.Order)),
                    Entry(defaultMap, Boolean(bucket.IsDefault)),
                ])));
    }

    private static VocabularyDefinition CreateStyleTiers(
        VocabularyIdentity identity)
    {
        VocabularyMapDefinition order = ScalarMap(
            identity,
            "order",
            "Order",
            "Product-owned presentation order.",
            VocabularyScalarKind.Integer);
        VocabularyMapDefinition byteDivergent = ScalarMap(
            identity,
            "byte_divergent",
            "Byte Divergent",
            "Whether every choice in the tier may change emitted IL bytes.",
            VocabularyScalarKind.Boolean);

        return new(
            identity,
            VocabularyCatalog.StyleTiersSection,
            "Fidelity and presentation tiers used to group C# style choices.",
            [order, byteDivergent],
            StyleOptionCatalog.Tiers.Select(tier => new VocabularyTerm(
                new(identity, tier.Id.ToString()),
                tier.Title,
                tier.Summary,
                [
                    Entry(order, Integer(tier.Order)),
                    Entry(byteDivergent, Boolean(tier.ByteDivergent)),
                ])));
    }

    private static VocabularyDefinition CreateStyleChoices(
        VocabularyIdentity identity,
        VocabularyIdentity styleTiers)
    {
        VocabularyMapDefinition option = ScalarMap(
            identity,
            "option",
            "Option",
            "Owning style option identity.",
            VocabularyScalarKind.Text);
        VocabularyMapDefinition value = ScalarMap(
            identity,
            "value",
            "Value",
            "Selected value token on the owning option axis.",
            VocabularyScalarKind.Text);
        var tier = new VocabularyMapDefinition(
            new(identity, "tier"),
            "Tier",
            "Owning fidelity/presentation tier.",
            new VocabularyMapTarget.Terms(
                new VocabularyTermSetReference.Local(styleTiers)),
            VocabularyMapCardinality.ExactlyOne,
            VocabularyMapCoverage.Complete);
        VocabularyMapDefinition byteDivergent = ScalarMap(
            identity,
            "byte_divergent",
            "Byte Divergent",
            "Whether this choice may change emitted IL bytes.",
            VocabularyScalarKind.Boolean);
        VocabularyMapDefinition oracleEndorsed = ScalarMap(
            identity,
            "oracle_endorsed",
            "Oracle Endorsed",
            "Whether the declared runtime style oracle endorses this choice.",
            VocabularyScalarKind.Boolean);
        VocabularyMapDefinition corpusEndorsed = ScalarMap(
            identity,
            "corpus_endorsed",
            "Corpus Endorsed",
            "Whether the runtime source corpus endorses this choice.",
            VocabularyScalarKind.Boolean);
        VocabularyMapDefinition conflictGroup = ScalarMap(
            identity,
            "conflict_group",
            "Conflict Group",
            "Product-owned mutually-exclusive selection group.",
            VocabularyScalarKind.Text,
            VocabularyMapCardinality.OptionalOne);

        return new(
            identity,
            VocabularyCatalog.StyleChoicesSection,
            "Selectable product-owned C# rendering choices.",
            [
                option,
                value,
                tier,
                byteDivergent,
                oracleEndorsed,
                corpusEndorsed,
                conflictGroup,
            ],
            StyleOptionCatalog.Choices.Select(choice => new VocabularyTerm(
                new(identity, choice.Id),
                choice.Title,
                choice.Summary,
                [
                    Entry(option, Text(choice.OptionId)),
                    Entry(value, Text(choice.ValueToken)),
                    Entry(
                        tier,
                        new VocabularyMapValue.Term(
                            new(styleTiers, choice.Tier.ToString()))),
                    Entry(byteDivergent, Boolean(choice.ByteDivergent)),
                    Entry(oracleEndorsed, Boolean(choice.OracleEndorsed)),
                    Entry(corpusEndorsed, Boolean(choice.CorpusEndorsed)),
                    Entry(
                        conflictGroup,
                        choice.ConflictGroup is null
                            ? []
                            : [Text(choice.ConflictGroup)]),
                ])));
    }

    private static VocabularyDefinition CreateBodyKinds(
        VocabularyIdentity identity) =>
        new(
            identity,
            VocabularyCatalog.BodyKindsSection,
            "Exact rendered C# syntax kinds accepted by body queries.",
            maps: [],
            BodyShapeSearch.SupportedKinds.Select(kind => new VocabularyTerm(
                new(identity, kind),
                AnnotatedSourceNodeKinds.GetDisplayLabel(kind),
                summary: null)));

    private static VocabularyMapDefinition ScalarMap(
        VocabularyIdentity source,
        string identity,
        string label,
        string summary,
        VocabularyScalarKind kind,
        VocabularyMapCardinality cardinality =
            VocabularyMapCardinality.ExactlyOne) =>
        new(
            new(source, identity),
            label,
            summary,
            new VocabularyMapTarget.Scalar(kind),
            cardinality,
            VocabularyMapCoverage.Complete);

    private static VocabularyMapEntry Entry(
        VocabularyMapDefinition map,
        params VocabularyMapValue[] values) =>
        new(map.Identity, values);

    private static VocabularyMapEntry Entry(
        VocabularyMapDefinition map,
        IEnumerable<VocabularyMapValue> values) =>
        new(map.Identity, values);

    private static VocabularyMapValue Text(string value) =>
        new VocabularyMapValue.Scalar(
            VocabularyScalarValue.FromText(value));

    private static VocabularyMapValue Integer(long value) =>
        new VocabularyMapValue.Scalar(
            VocabularyScalarValue.FromInteger(value));

    private static VocabularyMapValue Boolean(bool value) =>
        new VocabularyMapValue.Scalar(
            VocabularyScalarValue.FromBoolean(value));
}

internal static class ProductVocabularyCompatibility
{
    internal static VocabularyDocument Create(VocabularySnapshot snapshot)
    {
        VocabularyDefinition index = Get(
            snapshot,
            ProductVocabularySnapshot.SectionsId);
        VocabularyDefinition accessibility = Get(
            snapshot,
            ProductVocabularySnapshot.AccessibilityId);
        VocabularyDefinition tiers = Get(
            snapshot,
            ProductVocabularySnapshot.StyleTiersId);
        VocabularyDefinition choices = Get(
            snapshot,
            ProductVocabularySnapshot.StyleChoicesId);
        VocabularyDefinition bodyKinds = Get(
            snapshot,
            ProductVocabularySnapshot.BodyKindsId);

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
