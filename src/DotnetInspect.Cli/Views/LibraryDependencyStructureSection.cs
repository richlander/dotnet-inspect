using DotnetInspector.Queries;
using DotnetInspect.Cli.Output;
using ILInspector.Research;
using Markout;

namespace DotnetInspect.Cli.Views;

/// <summary>
/// Document view of the Dependency Structure section: the complete cycle
/// count and membership, followed by the selected namespace edges.
/// </summary>
/// <remarks>
/// The count and membership are Document summary. They come from the
/// complete document rather than from row selection, so a limited edge
/// table never changes them, and Table demand omits them so that a row
/// stream carries only edge rows. The format is owned by
/// docs/design/library-dependency-structure.md.
/// </remarks>
[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
public sealed class LibraryDependencyStructureSection
{
    internal static LibraryDependencyStructureSection From(
        LibraryDependencyStructureQueryResult.Available available,
        bool tableDemand)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (tableDemand)
        {
            return new LibraryDependencyStructureSection
            {
                Edges = LibraryDependencyStructureOutputAdapter.ToGraph(available),
            };
        }

        LibraryDependencyStructureDocument document = available.Document;
        int count = document.Cycles.Length;
        return new LibraryDependencyStructureSection
        {
            Cycles = document.Completeness
                is LibraryDependencyCompleteness.Complete
                    ? $"{count}"
                    : $"{count} among admitted call evidence",
            CycleRows = count == 0 ? null :
            [
                .. document.Cycles.Select((cycle, index) =>
                    new LibraryDependencyCycleRow(
                        LibraryDependencyStructureOutputAdapter.CycleName(
                            index,
                            count),
                        cycle.Namespaces.Length,
                        string.Join(
                            ", ",
                            cycle.Namespaces.Select(
                                LibraryDependencyStructureOutputAdapter
                                    .NamespaceName)))),
            ],
            Edges = LibraryDependencyStructureOutputAdapter.ToGraph(available),
        };
    }

    /// <inheritdoc cref="LibraryViewText"/>
    public string? Cycles
    {
        get => field;
        init => field = value is null ? null : LibraryViewText.Contain(value);
    }

    [MarkoutSection(Name = "Cycle Members", Level = 3)]
    public List<LibraryDependencyCycleRow>? CycleRows { get; init; }

    [MarkoutSection(
        Name = "Namespace Edges",
        Level = 3,
        EmptyText = "No internal namespace dependencies.")]
    public Markout.Graph? Edges { get; init; }
}

/// <summary>One namespace cycle: its name, size, and members.</summary>
/// <remarks>
/// Members are namespace names from the inspected assembly's metadata, so
/// the row contains its own text.
/// </remarks>
[MarkoutSerializable]
public sealed record LibraryDependencyCycleRow(
    string Cycle,
    int Namespaces,
    string Members)
{
    /// <inheritdoc cref="LibraryViewText"/>
    public string Cycle { get; init; } = LibraryViewText.Contain(Cycle);

    public int Namespaces { get; init; } = Namespaces;

    /// <inheritdoc cref="LibraryViewText"/>
    public string Members { get; init; } = LibraryViewText.Contain(Members);
}
