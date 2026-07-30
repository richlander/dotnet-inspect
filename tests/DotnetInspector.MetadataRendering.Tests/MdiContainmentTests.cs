using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Metadata;
using Mdi;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// A type that exists only so <see cref="HostileAssemblyFixture"/> has a long,
/// unique ASCII name to find in this assembly's <c>#Strings</c> heap and splice
/// control characters into. Nothing references it at runtime; its whole
/// contribution is the name recorded in metadata.
/// </summary>
internal sealed class TerminalEscapeCanaryPlaceholder;

/// <summary>
/// The gate behind the containment property that
/// <c>docs/design/untrusted-data-threat-model.md</c> asserts for the
/// Presentation boundary, where "Terminal control injection" is the named risk
/// and metadata names are named untrusted input.
/// <para>
/// The property is mostly <em>inherited</em>, which is why it needs its own
/// gate. <c>mdi</c> performs almost no escaping of its own: it hands every view
/// to <c>MetadataProjectionRenderer</c>, which renders values already
/// neutralized by <c>MetadataTableProjector</c>. That makes <c>mdi</c> the
/// reference example of consuming the projection safely — and it also makes the
/// safety invisible at the call site, so nothing in <c>mdi</c> would fail if a
/// projection path started emitting raw heap text. These tests fail instead.
/// </para>
/// <para>
/// That gap was not hypothetical. Review of the first version of this file found
/// the overview reporting the metadata root's version stamp — an artifact-derived
/// counted string — straight to the renderer, bypassing the projector entirely
/// and emitting a raw ESC in Markdown and TSV. The fix neutralizes it in
/// <c>MetadataImageInspector</c>, and
/// <see cref="OverviewRendering_ContainsHostileVersionStamp_InEveryFormat"/> is
/// the case that would have caught it.
/// </para>
/// <para>
/// Each test asserts both halves of the containment claim: no raw control
/// character survived, <em>and</em> the neutralized form of every control in the
/// payload is present. Absence alone would also be satisfied by rendering
/// nothing at all.
/// </para>
/// </summary>
public sealed class MdiContainmentTests(HostileAssemblyFixture fixture)
    : IClassFixture<HostileAssemblyFixture>
{
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
            fixture.Path,
            new MetadataProjectionOptions { Tables = [TableIndex.TypeDef] },
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        AssertContained(output.ToString(), $"{format} table rendering");
    }

    /// <summary>
    /// The heap view reads the hostile value directly by address, bypassing the
    /// row projection the table test covers, so it needs its own case rather than
    /// inheriting that one's result.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void HeapRendering_ContainsHostileHeapText_InEveryFormat(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteHeapValue(
            fixture.Path,
            HeapKind.String,
            fixture.CanaryHeapOffset,
            new MetadataProjectionOptions(),
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        AssertContained(output.ToString(), $"{format} heap rendering");
    }

    /// <summary>
    /// The overview reports the metadata root's version stamp, which reaches the
    /// renderer without passing through the row projection. This is the case that
    /// caught a real raw-ESC leak; see the type remarks.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void OverviewRendering_ContainsHostileVersionStamp_InEveryFormat(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteOverview(
            fixture.Path,
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        AssertContained(output.ToString(), $"{format} overview rendering");
    }

    /// <summary>
    /// A regression net, not a payload-carrying case: the reference view renders
    /// only coordinates — target, table, row id, column, kind — so no artifact
    /// text reaches it today and nothing here can detect a containment
    /// regression. It is asserted so that a future reference view which starts
    /// resolving names cannot introduce a raw control character unobserved.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void ReferenceRendering_EmitsNoRawControlCharacters(MetadataTableFormat format)
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            fixture.Path,
            TableIndex.TypeDef,
            fixture.CanaryRowId,
            maxReferences: 4096,
            format,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        AssertNoRawControlCharacters(output.ToString(), $"{format} reference rendering");
    }

    /// <summary>
    /// Proves the fixture is actually hostile, at the two exact byte ranges the
    /// splices target. An earlier version scanned the whole image for the payload
    /// and passed against an unpatched assembly, because the payload's own array
    /// initializer puts those bytes in this test assembly — the check confirmed
    /// its own source code rather than the fixture.
    /// </summary>
    [Fact]
    public void HostileFixture_CarriesControlCharactersAtBothSpliceSites()
    {
        byte[] patched = File.ReadAllBytes(fixture.Path);
        byte[] original = File.ReadAllBytes(typeof(MdiContainmentTests).Assembly.Location);
        int length = HostileAssemblyFixture.Payload.Length;

        foreach (int site in new[] { fixture.NameSpliceOffset, fixture.VersionSpliceOffset })
        {
            Assert.Equal(HostileAssemblyFixture.Payload, patched[site..(site + length)]);
            Assert.NotEqual(HostileAssemblyFixture.Payload, original[site..(site + length)]);
        }
    }

    static void AssertContained(string text, string what)
    {
        AssertNoRawControlCharacters(text, what);

        foreach (string form in HostileAssemblyFixture.NeutralizedForms)
            Assert.Contains(form, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects any control character other than the three the formats themselves
    /// use structurally (tab separates TSV columns; CR and LF end lines).
    /// Excluding those three is safe because the projector escapes tab, CR, and
    /// LF when they arrive in content, so a surviving one can only be a separator
    /// the renderer emitted.
    /// </summary>
    static void AssertNoRawControlCharacters(string text, string what)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\n' or '\r' or '\t')
                continue;

            // Mirrors MetadataTableProjector.IsControl: C0, DEL, and C1.
            if (c >= ' ' && c != '\x7f' && !(c >= '\x80' && c <= '\x9f'))
                continue;

            Assert.Fail(
                $"{what} emitted raw control character U+{(int)c:X4} at index {i}, " +
                "which a terminal would interpret rather than display.");
        }
    }
}

/// <summary>
/// Builds — and afterwards deletes — a real assembly carrying terminal control
/// characters in two artifact-derived strings that reach presentation by
/// different routes.
/// <para>
/// The fixture is a real assembly rather than a synthetic string, per the
/// <c>AGENTS.md</c> evidence rule: the claim is about what reaches a terminal
/// when a hostile <em>artifact</em> is inspected, so the payload is spliced into
/// genuine metadata and travels the whole decode-project-render path. Both
/// splices are byte-for-byte length preserving, so heap offsets stay valid and
/// the only thing that changes is the bytes of one name and one version stamp.
/// </para>
/// </summary>
public sealed class HostileAssemblyFixture : IDisposable
{
    /// <summary>
    /// The hostile bytes, as they sit in a UTF-8 <c>#Strings</c> entry:
    /// <c>ESC [ 3 1 m</c> (a real "set foreground red" CSI sequence, so emitting
    /// it raw would actually reprogram a terminal rather than merely look
    /// suspicious), then BEL, then DEL, then the two UTF-8 bytes for U+009F.
    /// <para>
    /// The payload spans all three ranges <c>MetadataTableProjector.IsControl</c>
    /// recognizes — C0, DEL, and C1 — because a single ESC would let a regression
    /// that narrowed containment to ESC alone pass the whole gate while leaving
    /// every other terminal control raw. Review demonstrated exactly that
    /// mutation surviving an ESC-only fixture.
    /// </para>
    /// </summary>
    public static readonly byte[] Payload =
    [
        0x1B, (byte)'[', (byte)'3', (byte)'1', (byte)'m',
        0x07,
        0x7F,
        0xC2, 0x9F,
    ];

    /// <summary>How each control in <see cref="Payload"/> must appear once contained.</summary>
    public static readonly string[] NeutralizedForms =
        [@"\u001B", @"\u0007", @"\u007F", @"\u009F"];

    const string CanaryName = nameof(TerminalEscapeCanaryPlaceholder);

    /// <summary>Offset into <see cref="CanaryName"/> where the payload is spliced.</summary>
    const int NamePayloadOffset = 8;

    /// <summary>
    /// The tail of the canary name the splice leaves untouched. Locating the row
    /// by this — rather than by the neutralized payload — keeps the lookup
    /// independent of the very escaping under test. When it keyed on the escaped
    /// form, disabling escaping made the lookup throw, so the heap tests "failed"
    /// without ever evaluating their own assertions. Derived from
    /// <c>Payload.Length</c> so the two cannot drift apart.
    /// </summary>
    static readonly string CanaryTail = CanaryName[(NamePayloadOffset + Payload.Length)..];

    public HostileAssemblyFixture()
    {
        string source = typeof(HostileAssemblyFixture).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(source);

        NameSpliceOffset = Splice(
            bytes, Encoding.ASCII.GetBytes(CanaryName), NamePayloadOffset, "canary type name");
        VersionSpliceOffset = Splice(
            bytes, Encoding.ASCII.GetBytes(ReadMetadataVersion(source)), 1, "metadata version stamp");

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"mdi-containment-{Guid.NewGuid():N}.dll");

        File.WriteAllBytes(Path, bytes);
        (CanaryRowId, CanaryHeapOffset) = FindCanary();
    }

    /// <summary>The hostile assembly on disk, valid for the lifetime of the fixture.</summary>
    public string Path { get; }

    /// <summary>Absolute offset of the payload spliced into the canary type name.</summary>
    public int NameSpliceOffset { get; }

    /// <summary>Absolute offset of the payload spliced into the metadata version stamp.</summary>
    public int VersionSpliceOffset { get; }

    /// <summary>The TypeDef row whose name carries the payload.</summary>
    public int CanaryRowId { get; }

    /// <summary>The <c>#Strings</c> address the patched name lives at.</summary>
    public int CanaryHeapOffset { get; }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing an otherwise green run over.
        }
    }

    /// <summary>
    /// Locates the patched name in the projected TypeDef table, yielding both the
    /// row that carries it and the heap address it lives at.
    /// </summary>
    (int RowId, int Offset) FindCanary()
    {
        using var stream = File.OpenRead(Path);
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
    /// Overwrites the first occurrence of <paramref name="within"/> at offset
    /// <paramref name="at"/> with the payload, returning the absolute offset
    /// written so the fixture test can verify that exact range.
    /// </summary>
    static int Splice(byte[] bytes, byte[] within, int at, string what)
    {
        if (within.Length < at + Payload.Length)
        {
            throw new InvalidOperationException(
                $"The {what} is too short ({within.Length} bytes) to carry the " +
                $"{Payload.Length}-byte payload at offset {at}.");
        }

        int index = bytes.AsSpan().IndexOf(within);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The {what} was not found; the containment fixture cannot be built.");
        }

        Payload.CopyTo(bytes, index + at);
        return index + at;
    }

    static string ReadMetadataVersion(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return peReader.GetMetadataReader(MetadataReaderOptions.None).MetadataVersion;
    }
}
