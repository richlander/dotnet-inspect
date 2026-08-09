using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// One <c>--all-libraries</c> section whose heading and columns are both runtime values.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate sections (<c>Opportunities</c>, <c>Switches</c>, and the
/// <c>LibraryIntegrationCatalog</c> descriptor sections) pool rows from every library in the
/// package, and decide their columns from the pooled data: a <c>Library</c> column appears only
/// when more than one library contributed, a <c>Kind</c> column only when the rows disagree on
/// kind, and the value column is headed <c>API</c> or <c>Type</c> depending on the signal shape.
/// A generated table cannot describe that, because the source generator derives columns from an
/// element type's properties at compile time.
/// </para>
/// <para>
/// <see cref="MarkoutTable"/> is the model shape for exactly this case, so these sections are
/// declared rather than appended as text. That is what lets <c>--rows</c> apply at the writer
/// seam: the writer sees the rows, so the window no longer has to be re-derived by parsing the
/// rendered document back into tables.
/// </para>
/// </remarks>
[MarkoutSerializable(TitleProperty = nameof(Name))]
public class AggregatedSectionView
{
    private string _name = "";

    /// <summary>
    /// The section heading. Ignored as a field so it drives only the heading rather than also
    /// appearing as a row.
    /// </summary>
    [MarkoutIgnore]
    public string Name
    {
        get => LibraryViewText.Contain(_name);
        set => _name = value;
    }

    /// <summary>The pooled rows. Ignored in table context because a shape cannot be a cell.</summary>
    [MarkoutIgnoreInTable]
    public MarkoutTable? Body { get; set; }
}

/// <summary>
/// Carries a single <see cref="AggregatedSectionView"/> so it is emitted as a level-2 section.
/// </summary>
/// <remarks>
/// Sections are rendered one at a time rather than as a batch because the all-libraries document
/// interleaves aggregate sections with per-library ones in the caller's requested order.
/// </remarks>
[MarkoutSerializable]
public class AggregatedSectionDocument
{
    /// <summary>The section to emit.</summary>
    [MarkoutUnwrap]
    public List<AggregatedSectionView> Sections { get; set; } = [];
}
