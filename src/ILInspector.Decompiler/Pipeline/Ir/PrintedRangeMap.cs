using System.Collections;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One node's contribution to the printer's output: the characters it emitted.
/// <see cref="Characters"/> indexes <see cref="PrintedRangeMap.Output"/>, so
/// <c>map.Output[range.Characters]</c> is the exact printed text of the node.
/// Always a non-empty slice — see <see cref="PrintedRangeMap"/>.
/// </summary>
public readonly record struct PrintedRange(IrNode Node, Range Characters);

/// <summary>
/// Which characters of the printer's own output each IR node emitted, recorded
/// during emission. Nothing can re-derive this once the string exists — the
/// printer is the only component that knows which characters belong to which
/// node — so it is captured as the text is built rather than recovered after.
/// </summary>
/// <remarks>
/// <para>
/// Both access patterns this serves are real, so it is a lookup that also
/// enumerates, in the tradition of <see cref="IReadOnlyDictionary{K, V}"/> and
/// <c>ILookup</c>: annotation anchoring walks the parent chain doing keyed
/// lookups, while the correlation seams enumerate to build line tables.
/// </para>
/// <para>
/// Enumeration order is <em>emission-completion</em> order — a node completes
/// after its children, so nesting reads post-order. Ordering by start position
/// is deliberately <em>not</em> part of this contract: promising it would force
/// a sort that no current consumer needs. Anything requiring sorted or
/// containment-ordered access should say so explicitly at its own call site.
/// </para>
/// <para>
/// Every recorded range is <em>non-empty</em>: a node the printer visits but
/// which emits nothing has no entry, because there is no printed text to point
/// at. The implicit parameterless <c>base()</c> chain call is the case that
/// makes this real — <c>ConstructorChainText</c> gives it no rendered form, so
/// unlike a chain call with arguments (which is lifted out of the body onto the
/// signature and never walked) it stays in the body and prints nothing.
/// Recording it would stamp a zero-width range at whatever position emission
/// had reached, which reads as "this node printed here" while resolving to the
/// line of the <em>next</em> statement. Measured over 63,722 ranges from 9,114
/// printed methods, 890 such degenerate entries arose, every one of them an
/// implicit chain call. Dropping them is what lets a caller slice or place a
/// caret on any range in this map without a width guard. The visible
/// consequence is that IL owned only by such a node — the implicit base call's
/// own opcodes — no longer buckets onto a C# line in the mixed IL view. That is
/// the honest outcome: it joins the IL that already has no C# line to sit
/// beside, rather than being attributed to the statement printed after it.
/// </para>
/// <para>
/// A <see cref="Range"/> is only meaningful against the exact string it was
/// measured from, which is why <see cref="Complete"/> binds
/// <see cref="Output"/> and the two travel together. Any transform applied to
/// the text after printing invalidates every range that follows the edit, so
/// display concerns such as escaping must happen during emission, never as a
/// post-pass over the finished string.
/// </para>
/// </remarks>
public sealed class PrintedRangeMap : IReadOnlyList<PrintedRange>
{
    /// <summary>The map a failed print yields: no ranges, no text.</summary>
    public static PrintedRangeMap Empty { get; } = new();

    readonly List<PrintedRange> _ranges = [];
    readonly Dictionary<IrNode, int> _index = [];
    int[]? _lineStarts;

    /// <summary>The printed text every <see cref="PrintedRange"/> here indexes.</summary>
    public string Output { get; private set; } = "";

    public int Count => _ranges.Count;

    public PrintedRange this[int index] => _ranges[index];

    /// <summary>Struct enumerator, so <c>foreach</c> over this map allocates nothing.</summary>
    public List<PrintedRange>.Enumerator GetEnumerator() => _ranges.GetEnumerator();

    IEnumerator<PrintedRange> IEnumerable<PrintedRange>.GetEnumerator() => _ranges.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _ranges.GetEnumerator();

    /// <summary>The characters <paramref name="node"/> emitted, if it was recorded.</summary>
    public bool TryGetRange(IrNode node, out Range range)
    {
        if (_index.TryGetValue(node, out int slot))
        {
            range = _ranges[slot].Characters;
            return true;
        }
        range = default;
        return false;
    }

    /// <summary>
    /// The 0-based output line <paramref name="node"/> starts on — a projection
    /// of its recorded range, not separately stored. The line index table is
    /// built once on first use and reused, so this is a binary search per query
    /// rather than a scan of the whole text per node.
    /// </summary>
    public bool TryGetLine(IrNode node, out int line)
    {
        if (!TryGetRange(node, out var range))
        {
            line = 0;
            return false;
        }
        line = LineAt(range.Start.GetOffset(Output.Length));
        return true;
    }

    /// <summary>
    /// Records what <paramref name="node"/> emitted, between the output lengths
    /// captured either side of its emission. A node that emitted nothing
    /// (<paramref name="end"/> equal to <paramref name="start"/>) is not
    /// recorded: it printed no text, so it owns no range and no line.
    /// </summary>
    internal void Record(IrNode node, int start, int end)
    {
        if (end <= start || _index.ContainsKey(node))
            return;
        _index[node] = _ranges.Count;
        _ranges.Add(new PrintedRange(node, start..end));
    }

    /// <summary>
    /// Binds the text the ranges were measured against, so a range and the
    /// string it indexes always travel together.
    /// </summary>
    /// <remarks>
    /// <c>PrintBody</c> trims trailing whitespace before returning, which shifts
    /// nothing earlier in the text. Measured over 62,838 ranges across 8,618
    /// printed methods, no recorded range ends past the returned string and no
    /// empty-output method had recorded ranges at all, so nothing is clamped or
    /// dropped here. If that ever stops holding, a consumer slicing the output
    /// throws rather than reading a quietly wrong span, which is the failure
    /// worth having.
    /// </remarks>
    internal PrintedRangeMap Complete(string output)
    {
        Output = output;
        _lineStarts = null;
        return this;
    }

    int LineAt(int position)
    {
        var starts = _lineStarts ??= BuildLineStarts(Output);
        int found = Array.BinarySearch(starts, position);
        return found >= 0 ? found : ~found - 1;
    }

    static int[] BuildLineStarts(string output)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < output.Length; i++)
            if (output[i] == '\n')
                starts.Add(i + 1);
        return [.. starts];
    }
}
