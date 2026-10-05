using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// One owner-issued context property of a section result, as defined by
/// <c>docs/design/section-shapes.md#properties</c>: a short named value such as
/// the acquisition source, target framework, or asset root that qualifies what
/// a result displays. Properties are neither content rows nor scalar-record
/// fields. A renderer prints the issued values in issued order and never
/// computes a property of its own.
/// </summary>
public sealed record ResultProperty
{
    public ResultProperty(string name, string value)
        : this(name, new InertString(TextPolicy.Field, value))
    {
    }

    public ResultProperty(string name, InertString value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A result property requires a name.",
                nameof(name));
        }
        if (value.IsEmpty)
        {
            throw new ArgumentException(
                "A result property requires a value.",
                nameof(value));
        }

        Name = name;
        Value = value;
    }

    /// <summary>The owner-issued property name, such as <c>source</c>.</summary>
    public string Name { get; }

    /// <summary>The inert display value.</summary>
    public InertString Value { get; }
}
