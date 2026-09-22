using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<SectionSemanticShape>))]
public enum SectionSemanticShape
{
    Scalar,
    Inventory,
}

[JsonConverter(typeof(JsonStringEnumConverter<SectionTerminalCapability>))]
public enum SectionTerminalCapability
{
    Rows,
    Count,
}

public sealed record SectionCardinalityDeclaration
{
    public static SectionCardinalityDeclaration Scalar { get; } =
        new(SectionSemanticShape.Scalar, []);

    public static SectionCardinalityDeclaration Inventory { get; } =
        new(
            SectionSemanticShape.Inventory,
            [
                SectionTerminalCapability.Rows,
                SectionTerminalCapability.Count,
            ]);

    public SectionCardinalityDeclaration(
        SectionSemanticShape shape,
        IEnumerable<SectionTerminalCapability>? terminals)
        : this(
            shape,
            (terminals ?? []).ToImmutableArray())
    {
    }

    [JsonConstructor]
    public SectionCardinalityDeclaration(
        SectionSemanticShape shape,
        ImmutableArray<SectionTerminalCapability> terminals)
    {
        if (!Enum.IsDefined(shape))
            throw new ArgumentOutOfRangeException(nameof(shape));

        ImmutableArray<SectionTerminalCapability> declared =
            terminals.IsDefault ? [] : terminals;
        foreach (SectionTerminalCapability terminal in declared)
        {
            if (!Enum.IsDefined(terminal))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(terminals),
                    terminal,
                    "Unknown section terminal capability.");
            }
        }
        if (declared.Distinct().Count() != declared.Length)
        {
            throw new ArgumentException(
                "Section terminal capabilities must be unique.",
                nameof(terminals));
        }

        bool rows =
            declared.Contains(SectionTerminalCapability.Rows);
        bool count =
            declared.Contains(SectionTerminalCapability.Count);
        if (rows != count)
        {
            throw new ArgumentException(
                "Rows and Count must be declared together.",
                nameof(terminals));
        }
        if (shape == SectionSemanticShape.Scalar
            && !declared.IsEmpty)
        {
            throw new ArgumentException(
                "A scalar section cannot declare Rows or Count.",
                nameof(terminals));
        }
        if (shape == SectionSemanticShape.Inventory
            && (!rows || declared.Length != 2))
        {
            throw new ArgumentException(
                "An inventory section must declare exactly Rows and Count.",
                nameof(terminals));
        }

        Shape = shape;
        Terminals = declared;
    }

    public SectionSemanticShape Shape { get; }

    public ImmutableArray<SectionTerminalCapability> Terminals { get; }
}
