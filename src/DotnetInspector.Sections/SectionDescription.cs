using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

/// <summary>
/// The owner's plain statement of what a section reports, per
/// <c>docs/design/section-model.md#description</c>. It is product-authored
/// text, issued beside the descriptor and projected unchanged. It is
/// section-level only: row count belongs to the section's cardinality and row
/// shape to its structural schema and declared shape.
/// </summary>
public sealed record SectionDescription
{
    [JsonConstructor]
    public SectionDescription(
        string summary,
        string basis,
        string empty,
        string? boundary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        ArgumentException.ThrowIfNullOrWhiteSpace(empty);
        if (boundary is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        Summary = summary;
        Basis = basis;
        Empty = empty;
        Boundary = boundary;
    }

    /// <summary>One sentence naming what the section's rows report about the subject.</summary>
    public string Summary { get; }

    /// <summary>The evidence the producer reads.</summary>
    public string Basis { get; }

    /// <summary>What an empty result establishes.</summary>
    public string Empty { get; }

    /// <summary>Interpretation limits the owner states, when it states any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Boundary { get; }
}
