using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Metadata;
using Mdi;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// A type that exists only so <see cref="MdiContainmentTests"/> has a long,
/// unique ASCII name to find in this assembly's <c>#Strings</c> heap and splice
/// a terminal escape sequence into. Nothing references it at runtime; its whole
/// contribution is the name recorded in metadata.
/// </summary>
internal sealed class TerminalEscapeCanaryPlaceholder;

/// <summary>
/// The gate behind the containment property that
/// <c>docs/design/untrusted-data-threat-model.md</c> asserts for the
/// Presentation boundary, where "Terminal control injection" is the named risk
/// and metadata names are named untrusted input.
/// <para>
/// The property under test is <em>inherited</em>, which is exactly why it needs
/// its own gate here. <c>mdi</c> performs no escaping of its own: it hands every
/// view to <c>MetadataProjectionRenderer</c>, which renders values already
/// neutralized by <c>MetadataTableProjector</c>. That makes <c>mdi</c> the
/// reference example of consuming the projection safely — and it also makes the
/// safety invisible at the call site, so nothing in <c>mdi</c> would fail if a
/// future renderer or projection path started emitting raw heap text. These
/// tests fail instead.
/// </para>
/// <para>
/// The fixture is a real assembly rather than a synthetic string, per the
/// <c>AGENTS.md</c> evidence rule: the claim is about what reaches a terminal
/// when a hostile <em>artifact</em> is inspected, so the payload is spliced into
/// a genuine <c>#Strings</c> entry and travels the whole decode-project-render
/// path. The splice preserves length, so every heap offset in the image stays
/// valid and the only thing that changes is the bytes of one name.
/// </para>
/// <para>
/// Verified by mutation: with <c>MetadataTableProjector.IsControl</c> forced to
/// <c>false</c>, the Markdown and TSV table and heap tests fail on the raw
/// control character itself. The JSONL cases keep passing, and that is a real
/// property rather than a gap in the fixture — JSONL is written through
/// <c>MarkoutTableMode.Jsonl</c>, whose string encoding escapes control
/// characters independently, so JSONL carries a second containment layer. The
/// projector is therefore pinned by the Markdown and TSV cases; the JSONL cases
/// pin the writer.
/// </para>
/// </summary>
public sealed class MdiContainmentTests
{
    /// <summary>
    /// A real CSI sequence (<c>ESC [ 3 1 m</c> — "set foreground red"). A bare
    /// ESC would prove less: this is a complete, effective control sequence, so
    /// emitting it raw would actually reprogram a terminal rather than merely
    /// look suspicious.
    /// </summary>
    static readonly byte[] Payload = [0x1B, (byte)'[', (byte)'3', (byte)'1', (byte)'m'];

    /// <summary>
    /// How the payload must appear once contained. Asserting on this — rather
    /// than only on the absence of raw ESC — is what keeps every test below
    /// non-vacuous: absence alone would also pass if the patch silently missed
    /// the heap, or if the row stopped being rendered at all.
    /// </summary>
    const string Neutralized = @"\u001B";

    const string CanaryName = nameof(TerminalEscapeCanaryPlaceholder);

    /// <summary>Offset into <see cref="CanaryName"/> where the payload is spliced.</summary>
    const int PayloadOffset = 8;

    /// <summary>
    /// The tail of the canary name that the splice leaves untouched. Locating the
    /// row by this — rather than by the neutralized payload — keeps
    /// <see cref="FindCanary"/> independent of the very escaping under test. When
    /// the lookup keyed on the escaped form, disabling escaping made the lookup
    /// throw, so the heap and reference tests "failed" without ever evaluating
    /// their own assertions.
    /// </summary>
    static readonly string CanaryTail = CanaryName[(PayloadOffset + 5)..];

    static readonly Lazy<string> HostileAssembly = new(CreateHostileAssembly, isThreadSafe: true);

    /// <summary>
    /// Drives coverage from the format enum itself, so adding a rendering format
    /// without contained output fails here rather than shipping unchecked. A
    /// hand-written list would silently keep passing.
    /// </summary>
    public static TheoryData<MetadataTableFormat> Formats()
    {
        var data = new TheoryData<MetadataTableFormat>();
        foreach (var format in Enum.GetValues<MetadataTableFormat>())
            data.Add(format);

        return data;
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void TableRendering_ContainsHostileHeapText_InEveryFormat(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.Execute(
            HostileAssembly.Value,
            new MetadataProjectionOptions { Tables = [TableIndex.TypeDef] },
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);

        string text = output.ToString();
        AssertNoRawControlCharacters(text, $"{format} table rendering");
        Assert.Contains(Neutralized, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The heap view reads the hostile value directly by address, bypassing the
    /// row projection that <see cref="TableRendering_ContainsHostileHeapText_InEveryFormat"/>
    /// covers, so it needs its own gate rather than inheriting that one's result.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void HeapRendering_ContainsHostileHeapText_InEveryFormat(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteHeapValue(
            HostileAssembly.Value,
            HeapKind.String,
            CanaryOffset,
            new MetadataProjectionOptions(),
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);

        string text = output.ToString();
        AssertNoRawControlCharacters(text, $"{format} heap rendering");
        Assert.Contains(Neutralized, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A regression net, not a payload-carrying case. The reference view renders
    /// only coordinates — target, table, row id, column, kind — so no heap text
    /// reaches it today and nothing here can detect a containment regression.
    /// It is asserted so that a future reference view which starts resolving
    /// names cannot introduce a raw control character unobserved.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void ReferenceRendering_EmitsNoRawControlCharacters(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            HostileAssembly.Value,
            TableIndex.TypeDef,
            CanaryRowId,
            maxReferences: 4096,
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        AssertNoRawControlCharacters(output.ToString(), $"{format} reference rendering");
    }

    /// <summary>
    /// The second regression net, for the same reason as the reference view: the
    /// overview reports heap sizes and row counts rather than heap text. The
    /// non-vacuity of this file rests on the table and heap tests above, both of
    /// which fail when escaping is removed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void OverviewRendering_EmitsNoRawControlCharacters(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteOverview(
            HostileAssembly.Value,
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        AssertNoRawControlCharacters(output.ToString(), $"{format} overview rendering");
    }

    /// <summary>
    /// Proves the fixture is actually hostile. Without this, every assertion
    /// above could pass against an unpatched assembly, and the file would gate
    /// nothing at all.
    /// </summary>
    [Fact]
    public void HostileFixture_ActuallyCarriesARawEscapeSequence()
    {
        byte[] bytes = File.ReadAllBytes(HostileAssembly.Value);
        Assert.True(
            IndexOf(bytes, Payload) >= 0,
            "The fixture assembly does not contain the raw escape sequence, so the containment tests would pass vacuously.");
    }

    /// <summary>
    /// Rejects any C0 control character other than the three that the formats
    /// themselves use structurally (tab separates TSV columns; CR/LF end lines).
    /// Excluding those three is safe because the projector escapes tab, CR, and
    /// LF when they arrive in heap <em>content</em>, so a surviving tab can only
    /// be one the renderer emitted.
    /// </summary>
    static void AssertNoRawControlCharacters(string text, string what)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\n' or '\r' or '\t' || !char.IsControl(c))
                continue;

            Assert.Fail(
                $"{what} emitted raw control character U+{(int)c:X4} at index {i}, " +
                "which a terminal would interpret rather than display.");
        }
    }

    static int CanaryRowId => Canary.Value.RowId;

    static int CanaryOffset => Canary.Value.Offset;

    static readonly Lazy<(int RowId, int Offset)> Canary = new(FindCanary, isThreadSafe: true);

    /// <summary>
    /// Locates the patched name in the projected TypeDef table, yielding both the
    /// row that carries it and the heap address it lives at.
    /// </summary>
    static (int RowId, int Offset) FindCanary()
    {
        using var stream = File.OpenRead(HostileAssembly.Value);
        using var peReader = new PEReader(stream);

        var projection = MetadataTableProjector.Project(
            peReader,
            new MetadataProjectionOptions { Tables = [TableIndex.TypeDef] });

        foreach (var table in projection.Tables)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell is MetadataValue.HeapReference { Heap: HeapKind.String, Text: { } text } heap
                        && text.Contains(CanaryTail, StringComparison.Ordinal))
                    {
                        return (row.RowId, heap.Offset);
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "The patched canary name was not found in the projected TypeDef table.");
    }

    /// <summary>
    /// Copies this test assembly and splices <see cref="Payload"/> into the
    /// <c>#Strings</c> entry for <see cref="TerminalEscapeCanaryPlaceholder"/>.
    /// The name is searched as ASCII, which reaches the UTF-8 <c>#Strings</c>
    /// heap while leaving any UTF-16 <c>#US</c> copy of the same identifier
    /// alone, and the replacement is length-preserving so no heap offset moves.
    /// </summary>
    static string CreateHostileAssembly()
    {
        string source = typeof(MdiContainmentTests).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(source);

        byte[] marker = Encoding.ASCII.GetBytes(CanaryName);
        int index = IndexOf(bytes, marker);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"'{CanaryName}' was not found in {source}; the containment fixture cannot be built.");
        }

        Payload.CopyTo(bytes, index + PayloadOffset);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"mdi-containment-{Guid.NewGuid():N}.dll");

        File.WriteAllBytes(path, bytes);
        return path;
    }

    static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
        => haystack.AsSpan().IndexOf(needle);
}
