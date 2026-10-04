using System.Collections.Immutable;
using System.Globalization;

namespace DotnetInspector.Sections;

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
    ImmutableArray<string> AcceptedBy,
    ImmutableArray<VocabularyField> Fields,
    ImmutableArray<VocabularyRow> Values);

/// <summary>The complete static product-owned vocabulary document.</summary>
public sealed record VocabularyDocument(
    int SchemaVersion,
    ImmutableArray<VocabularySection> Sections);
