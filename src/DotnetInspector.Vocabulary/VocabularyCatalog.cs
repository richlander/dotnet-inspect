using System.Collections.Immutable;
using System.Globalization;

using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Vocabulary;

/// <summary>The primitive shape of one vocabulary field.</summary>
public enum VocabularyValueKind
{
    /// <summary>One text value.</summary>
    Text,

    /// <summary>One integer value.</summary>
    Integer,

    /// <summary>One Boolean value.</summary>
    Boolean,

    /// <summary>An ordered list of text values.</summary>
    TextList,
}

/// <summary>An operator a rich query may apply to a vocabulary field.</summary>
public enum VocabularyOperator
{
    /// <summary>Exact equality.</summary>
    Equals,

    /// <summary>Exact inequality.</summary>
    NotEquals,

    /// <summary>Membership in a set.</summary>
    In,

    /// <summary>Ordered less-than comparison.</summary>
    LessThan,

    /// <summary>Ordered greater-than comparison.</summary>
    GreaterThan,

    /// <summary>String glob matching.</summary>
    Glob,

    /// <summary>Membership of one value in a list-valued field.</summary>
    Contains,
}

/// <summary>The discoverable contract of one field in a vocabulary section.</summary>
public sealed record VocabularyField(
    string Id,
    string Label,
    string Summary,
    VocabularyValueKind Kind,
    ImmutableArray<VocabularyOperator> Operators);

/// <summary>One typed cell in a vocabulary value row.</summary>
public readonly record struct VocabularyValue
{
    private VocabularyValue(
        VocabularyValueKind kind,
        string? text,
        int integer,
        bool boolean,
        ImmutableArray<string> textList)
    {
        Kind = kind;
        Text = text;
        Integer = integer;
        Boolean = boolean;
        TextList = textList;
    }

    /// <summary>The cell's primitive shape.</summary>
    public VocabularyValueKind Kind { get; }

    /// <summary>The text payload when <see cref="Kind"/> is <see cref="VocabularyValueKind.Text"/>.</summary>
    public string? Text { get; }

    /// <summary>The integer payload when <see cref="Kind"/> is <see cref="VocabularyValueKind.Integer"/>.</summary>
    public int Integer { get; }

    /// <summary>The Boolean payload when <see cref="Kind"/> is <see cref="VocabularyValueKind.Boolean"/>.</summary>
    public bool Boolean { get; }

    /// <summary>The list payload when <see cref="Kind"/> is <see cref="VocabularyValueKind.TextList"/>.</summary>
    public ImmutableArray<string> TextList { get; }

    /// <summary>Creates a text value.</summary>
    public static VocabularyValue FromText(string value) =>
        new(VocabularyValueKind.Text, value, 0, false, []);

    /// <summary>Creates an integer value.</summary>
    public static VocabularyValue FromInteger(int value) =>
        new(VocabularyValueKind.Integer, null, value, false, []);

    /// <summary>Creates a Boolean value.</summary>
    public static VocabularyValue FromBoolean(bool value) =>
        new(VocabularyValueKind.Boolean, null, 0, value, []);

    /// <summary>Creates an ordered text-list value.</summary>
    public static VocabularyValue FromTextList(IEnumerable<string> values) =>
        new(VocabularyValueKind.TextList, null, 0, false, [.. values]);

    /// <summary>Formats the value for a tabular projection.</summary>
    public string ToDisplayString() => Kind switch
    {
        VocabularyValueKind.Text => Text ?? "",
        VocabularyValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        VocabularyValueKind.Boolean => Boolean ? "true" : "false",
        VocabularyValueKind.TextList => string.Join(", ", TextList),
        _ => throw new InvalidOperationException($"Unsupported vocabulary value kind '{Kind}'."),
    };
}

/// <summary>One stable query value and its data fields.</summary>
public sealed record VocabularyRow
{
    private readonly IReadOnlyDictionary<string, VocabularyValue> _values;

    /// <summary>Creates one row from unique field/value cells.</summary>
    public VocabularyRow(params (string Field, VocabularyValue Value)[] values)
    {
        var cells = new Dictionary<string, VocabularyValue>(StringComparer.Ordinal);
        foreach ((string field, VocabularyValue value) in values)
        {
            if (!cells.TryAdd(field, value))
                throw new ArgumentException($"Vocabulary field '{field}' occurs more than once.", nameof(values));
        }
        _values = cells;
    }

    /// <summary>Returns whether this row carries <paramref name="field"/>.</summary>
    public bool TryGetValue(string field, out VocabularyValue value) =>
        _values.TryGetValue(field, out value);

    /// <summary>Returns one required field value.</summary>
    public VocabularyValue GetRequired(string field) =>
        _values.TryGetValue(field, out VocabularyValue value)
            ? value
            : throw new InvalidOperationException($"Vocabulary row does not define required field '{field}'.");
}

/// <summary>One discoverable vocabulary section whose rows are legal query values.</summary>
public sealed record VocabularySection(
    string Id,
    string Name,
    string Summary,
    ImmutableArray<string> Categories,
    ImmutableArray<string> AcceptedBy,
    ImmutableArray<VocabularyField> Fields,
    ImmutableArray<VocabularyRow> Values);

/// <summary>The complete static product-owned vocabulary document.</summary>
public sealed record VocabularyDocument(
    int SchemaVersion,
    ImmutableArray<VocabularySection> Sections);

/// <summary>Composes product-owned query vocabularies without reclassifying their values.</summary>
public static class VocabularyCatalog
{
    /// <summary>The section that describes the vocabulary sections themselves.</summary>
    public const string SectionsSection = "Vocabulary Sections";

    /// <summary>The API accessibility vocabulary section.</summary>
    public const string AccessibilitySection = "Accessibility";

    /// <summary>The C# style-tier vocabulary section.</summary>
    public const string StyleTiersSection = "C# Style Tiers";

    /// <summary>The selectable C# style-choice vocabulary section.</summary>
    public const string StyleChoicesSection = "C# Style Choices";

    /// <summary>The exact rendered C# body-kind vocabulary section.</summary>
    public const string BodyKindsSection = "C# Body Kinds";

    /// <summary>The current static vocabulary document.</summary>
    public static VocabularyDocument Document { get; } = CreateDocument();

    /// <summary>Returns the section with the exact stable <paramref name="id"/>.</summary>
    public static VocabularySection GetById(string id) =>
        Document.Sections.FirstOrDefault(section => section.Id == id)
        ?? throw new ArgumentException($"Unknown vocabulary section ID '{id}'.", nameof(id));

    private static VocabularyDocument CreateDocument()
    {
        ImmutableArray<VocabularySection> values =
        [
            CreateAccessibility(),
            CreateStyleTiers(),
            CreateStyleChoices(),
            CreateBodyKinds(),
        ];
        VocabularySection index = CreateSectionIndex(values);
        var document = new VocabularyDocument(1, [index, .. values]);
        Validate(document);
        return document;
    }

    private static VocabularySection CreateSectionIndex(
        ImmutableArray<VocabularySection> sections)
    {
        ImmutableArray<VocabularyField> fields =
        [
            TextField("id", "ID", "Stable section identity.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            TextField("section", "Section", "Human-facing section name.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.Glob),
            TextField("summary", "Summary", "What the vocabulary values control.", VocabularyOperator.Glob),
            TextListField("categories", "Categories", "Section categories.", VocabularyOperator.Contains),
            TextListField("accepted_by", "Accepted By", "Typed query inputs that consume these values.", VocabularyOperator.Contains),
            IntegerField("values", "Values", "Number of legal values.", VocabularyOperator.Equals, VocabularyOperator.LessThan, VocabularyOperator.GreaterThan),
        ];
        var definition = new VocabularySection(
            "vocabulary.sections",
            SectionsSection,
            "Product-owned vocabularies available as rich-query inputs.",
            ["@Vocabulary"],
            ["vocabulary"],
            fields,
            []);
        ImmutableArray<VocabularySection> indexedSections = [definition, .. sections];
        ImmutableArray<VocabularyRow> rows =
        [
            .. indexedSections.Select(section => new VocabularyRow(
                ("id", VocabularyValue.FromText(section.Id)),
                ("section", VocabularyValue.FromText(section.Name)),
                ("summary", VocabularyValue.FromText(section.Summary)),
                ("categories", VocabularyValue.FromTextList(section.Categories)),
                ("accepted_by", VocabularyValue.FromTextList(section.AcceptedBy)),
                ("values", VocabularyValue.FromInteger(
                    ReferenceEquals(section, definition)
                        ? indexedSections.Length
                        : section.Values.Length)))),
        ];
        return definition with { Values = rows };
    }

    private static VocabularySection CreateAccessibility()
    {
        ImmutableArray<VocabularyField> fields =
        [
            TextField("id", "ID", "Stable accessibility selection identity.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            TextField("label", "Label", "Product-owned display label.", VocabularyOperator.Equals, VocabularyOperator.Glob),
            IntegerField("order", "Order", "Product-owned presentation order.", VocabularyOperator.Equals, VocabularyOperator.LessThan, VocabularyOperator.GreaterThan),
            BooleanField("default", "Default", "Whether this value participates without an explicit selection."),
        ];
        ImmutableArray<VocabularyRow> rows =
        [
            .. ApiAccessibility.Values.Select(bucket => new VocabularyRow(
                ("id", VocabularyValue.FromText(bucket.Id)),
                ("label", VocabularyValue.FromText(bucket.Label)),
                ("order", VocabularyValue.FromInteger(bucket.Order)),
                ("default", VocabularyValue.FromBoolean(bucket.IsDefault)))),
        ];
        return new(
            "api.accessibility",
            AccessibilitySection,
            "Accessibility facets accepted by API type and member inventory queries.",
            ["@Vocabulary", "@API"],
            ["api.type-inventory", "api.member-inventory"],
            fields,
            rows);
    }

    private static VocabularySection CreateStyleTiers()
    {
        ImmutableArray<VocabularyField> fields =
        [
            TextField("id", "ID", "Stable style-tier identity.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            TextField("title", "Title", "Product-owned tier title.", VocabularyOperator.Equals, VocabularyOperator.Glob),
            TextField("summary", "Summary", "The fidelity contract of this tier.", VocabularyOperator.Glob),
            IntegerField("order", "Order", "Product-owned presentation order.", VocabularyOperator.Equals, VocabularyOperator.LessThan, VocabularyOperator.GreaterThan),
            BooleanField("byte_divergent", "Byte Divergent", "Whether every choice in the tier may change emitted IL bytes."),
        ];
        ImmutableArray<VocabularyRow> rows =
        [
            .. StyleOptionCatalog.Tiers.Select(tier => new VocabularyRow(
                ("id", VocabularyValue.FromText(tier.Id.ToString())),
                ("title", VocabularyValue.FromText(tier.Title)),
                ("summary", VocabularyValue.FromText(tier.Summary)),
                ("order", VocabularyValue.FromInteger(tier.Order)),
                ("byte_divergent", VocabularyValue.FromBoolean(tier.ByteDivergent)))),
        ];
        return new(
            "csharp.style-tiers",
            StyleTiersSection,
            "Fidelity and presentation tiers used to group C# style choices.",
            ["@Vocabulary", "@Decompiler"],
            ["decompiler.style-picker"],
            fields,
            rows);
    }

    private static VocabularySection CreateStyleChoices()
    {
        ImmutableArray<VocabularyField> fields =
        [
            TextField("id", "ID", "Stable selectable style-choice identity.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            TextField("option", "Option", "Owning style option identity.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            TextField("value", "Value", "Selected value token on the owning option axis.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            TextField("title", "Title", "Product-owned picker label.", VocabularyOperator.Equals, VocabularyOperator.Glob),
            TextField("summary", "Summary", "What the choice changes.", VocabularyOperator.Glob),
            TextField("tier", "Tier", "Owning fidelity/presentation tier.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
            BooleanField("byte_divergent", "Byte Divergent", "Whether this choice may change emitted IL bytes."),
            BooleanField("oracle_endorsed", "Oracle Endorsed", "Whether the declared runtime style oracle endorses this choice."),
            BooleanField("corpus_endorsed", "Corpus Endorsed", "Whether the runtime source corpus endorses this choice."),
            TextField("conflict_group", "Conflict Group", "Product-owned mutually-exclusive selection group.", VocabularyOperator.Equals, VocabularyOperator.NotEquals, VocabularyOperator.In),
        ];
        ImmutableArray<VocabularyRow> rows =
        [
            .. StyleOptionCatalog.Choices.Select(choice =>
            {
                var values = new List<(string, VocabularyValue)>
                {
                    ("id", VocabularyValue.FromText(choice.Id)),
                    ("option", VocabularyValue.FromText(choice.OptionId)),
                    ("value", VocabularyValue.FromText(choice.ValueToken)),
                    ("title", VocabularyValue.FromText(choice.Title)),
                    ("summary", VocabularyValue.FromText(choice.Summary)),
                    ("tier", VocabularyValue.FromText(choice.Tier.ToString())),
                    ("byte_divergent", VocabularyValue.FromBoolean(choice.ByteDivergent)),
                    ("oracle_endorsed", VocabularyValue.FromBoolean(choice.OracleEndorsed)),
                    ("corpus_endorsed", VocabularyValue.FromBoolean(choice.CorpusEndorsed)),
                };
                if (choice.ConflictGroup is not null)
                    values.Add(("conflict_group", VocabularyValue.FromText(choice.ConflictGroup)));
                return new VocabularyRow([.. values]);
            }),
        ];
        return new(
            "csharp.style-choices",
            StyleChoicesSection,
            "Selectable product-owned C# rendering choices.",
            ["@Vocabulary", "@Decompiler"],
            ["decompiler.style-picker", "decompiler.render"],
            fields,
            rows);
    }

    private static VocabularySection CreateBodyKinds()
    {
        ImmutableArray<VocabularyField> fields =
        [
            TextField(
                "id",
                "ID",
                "Exact stable rendered-syntax kind.",
                VocabularyOperator.Equals,
                VocabularyOperator.NotEquals,
                VocabularyOperator.In),
            TextField(
                "label",
                "Label",
                "Product-owned display label.",
                VocabularyOperator.Equals,
                VocabularyOperator.Glob),
        ];
        ImmutableArray<VocabularyRow> rows =
        [
            .. BodyShapeSearch.SupportedKinds.Select(kind => new VocabularyRow(
                ("id", VocabularyValue.FromText(kind)),
                ("label", VocabularyValue.FromText(
                    AnnotatedSourceNodeKinds.GetDisplayLabel(kind))))),
        ];
        return new(
            "csharp.body-kinds",
            BodyKindsSection,
            "Exact rendered C# syntax kinds accepted by body queries.",
            ["@Vocabulary", "@Decompiler"],
            ["decompiler.body-kind"],
            fields,
            rows);
    }

    private static VocabularyField TextField(
        string id,
        string label,
        string summary,
        params VocabularyOperator[] operators) =>
        new(id, label, summary, VocabularyValueKind.Text, [.. operators]);

    private static VocabularyField IntegerField(
        string id,
        string label,
        string summary,
        params VocabularyOperator[] operators) =>
        new(id, label, summary, VocabularyValueKind.Integer, [.. operators]);

    private static VocabularyField BooleanField(
        string id,
        string label,
        string summary) =>
        new(
            id,
            label,
            summary,
            VocabularyValueKind.Boolean,
            [VocabularyOperator.Equals, VocabularyOperator.NotEquals]);

    private static VocabularyField TextListField(
        string id,
        string label,
        string summary,
        params VocabularyOperator[] operators) =>
        new(id, label, summary, VocabularyValueKind.TextList, [.. operators]);

    private static void Validate(VocabularyDocument document)
    {
        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        var sectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VocabularySection section in document.Sections)
        {
            if (!sectionIds.Add(section.Id))
                throw new InvalidOperationException($"Vocabulary section ID '{section.Id}' occurs more than once.");
            if (!sectionNames.Add(section.Name))
                throw new InvalidOperationException($"Vocabulary section name '{section.Name}' occurs more than once.");

            var fields = section.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
            if (!fields.ContainsKey("id"))
                throw new InvalidOperationException($"Vocabulary section '{section.Id}' has no identity field.");

            var valueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (VocabularyRow row in section.Values)
            {
                foreach (VocabularyField field in section.Fields)
                {
                    if (!row.TryGetValue(field.Id, out VocabularyValue value))
                        continue;
                    if (value.Kind != field.Kind)
                    {
                        throw new InvalidOperationException(
                            $"Vocabulary section '{section.Id}' field '{field.Id}' expects {field.Kind} but received {value.Kind}.");
                    }
                }

                VocabularyValue id = row.GetRequired("id");
                if (id.Kind != VocabularyValueKind.Text || !valueIds.Add(id.Text!))
                    throw new InvalidOperationException($"Vocabulary section '{section.Id}' has a duplicate or non-text value ID.");
            }
        }
    }
}
