using Markout;

namespace DotnetInspect.Cli.Views;

internal static class GraphStructureViewSections
{
    internal const string Namespaces = "Namespaces";
    internal const string NamespaceEdges = "Namespace Edges";
    internal const string Cycles = "Cycles";
    internal const string ExternalDependencies = "External Dependencies";
    internal const string TypeEdges = "Type Edges";

    internal static readonly string[] All =
        [Namespaces, NamespaceEdges, Cycles, ExternalDependencies, TypeEdges];

    internal static readonly string[] Default =
        [Namespaces, NamespaceEdges, Cycles];
}

/// <summary>
/// Library Dependency Structure presentation. Every identifier originates in
/// the inspected library and passes through <see cref="LibraryViewText.Contain"/>.
/// </summary>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class GraphStructureView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    } = "Dependency Structure";

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = string.Join(
            "\n",
            value.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(static line => LibraryViewText.Contain(line) ?? ""));
    }

    [MarkoutSection(Name = GraphStructureViewSections.Namespaces)]
    public List<GraphStructureNamespaceRow>? Namespaces { get; init; }

    [MarkoutSection(Name = GraphStructureViewSections.NamespaceEdges)]
    public List<GraphStructureNamespaceEdgeRow>? NamespaceEdges { get; init; }

    [MarkoutSection(Name = GraphStructureViewSections.Cycles)]
    public List<GraphStructureCycleRow>? Cycles { get; init; }

    [MarkoutSection(Name = GraphStructureViewSections.ExternalDependencies)]
    public List<GraphStructureExternalEdgeRow>? ExternalDependencies { get; init; }

    [MarkoutSection(Name = GraphStructureViewSections.TypeEdges)]
    public List<GraphStructureTypeEdgeRow>? TypeEdges { get; init; }
}

public sealed class GraphStructureNamespaceRow
{
    public required int Level { get; init; }

    public required string Namespace
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required int Types { get; init; }

    [MarkoutPropertyName("Internal Calls")]
    public required int InternalCalls { get; init; }

    public required string Cycle { get; init; }
}

public sealed class GraphStructureNamespaceEdgeRow
{
    public required string Source
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required string Target
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required int Calls { get; init; }

    [MarkoutPropertyName("Function References")]
    public required int FunctionReferences { get; init; }

    [MarkoutPropertyName("Type Edges")]
    public required int TypeEdges { get; init; }

    [MarkoutPropertyName("Strongest Type Edge")]
    public required string StrongestTypeEdge
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }
}

public sealed class GraphStructureCycleRow
{
    public required int Cycle { get; init; }

    public required int Size { get; init; }

    public required string Namespaces
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }
}

public sealed class GraphStructureExternalEdgeRow
{
    public required string Source
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required string Assembly
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required string Namespace
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required int Calls { get; init; }

    [MarkoutPropertyName("Function References")]
    public required int FunctionReferences { get; init; }

    [MarkoutPropertyName("Type Edges")]
    public required int TypeEdges { get; init; }
}

public sealed class GraphStructureTypeEdgeRow
{
    [MarkoutPropertyName("Source Type")]
    public required string SourceType
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    [MarkoutPropertyName("Target Type")]
    public required string TargetType
    {
        get => field;
        init => field = LibraryViewText.Contain(value) ?? "";
    }

    public required int Calls { get; init; }

    [MarkoutPropertyName("Function References")]
    public required int FunctionReferences { get; init; }
}

[MarkoutContext(typeof(GraphStructureView))]
[MarkoutContext(typeof(GraphStructureNamespaceRow))]
[MarkoutContext(typeof(GraphStructureNamespaceEdgeRow))]
[MarkoutContext(typeof(GraphStructureCycleRow))]
[MarkoutContext(typeof(GraphStructureExternalEdgeRow))]
[MarkoutContext(typeof(GraphStructureTypeEdgeRow))]
public partial class GraphStructureViewContext : MarkoutSerializerContext
{
}
