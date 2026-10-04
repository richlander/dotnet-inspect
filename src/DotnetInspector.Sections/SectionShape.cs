using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

/// <summary>
/// The semantic presentation shape a section declares once, from the result it
/// issues, per <c>docs/design/section-shapes.md</c>. The shape decides which
/// output formats can present the section, which is native when the section is
/// selected alone, and what each lowering preserves. It is independent of the
/// section's cardinality (<see cref="SectionCardinalityKind"/>) and of the
/// host-observable content kind of the operation that produced it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SectionShape>))]
public enum SectionShape
{
    /// <summary>An ordered set of rows sharing one column schema; native format is the row stream.</summary>
    Table,

    /// <summary>A Table whose rows carry an owner-issued parent; native format is the tree.</summary>
    Hierarchy,

    /// <summary>One text payload with its identifying facts; native format is the payload itself.</summary>
    Text,
}
