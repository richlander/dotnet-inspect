using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using Mdi;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// Gates the one case where the metadata root's version stamp can outgrow its
/// display budget.
/// <para>
/// The stamp is a counted string read straight out of the image, and
/// neutralizing a control character expands it to six characters, so the budget
/// is sized at 255 * 6 — the widest a conforming stamp can become, since
/// ECMA-335 II.24.2.1 caps the field at 255 bytes. `MetadataRootBuilder` refuses
/// to write a longer one even with `suppressValidation: true`, so no compiler
/// or emitter can produce this input.
/// </para>
/// <para>
/// A hand-built image can, and `MetadataReader` reads it back without complaint.
/// That makes an oversized stamp reachable in exactly the population this code
/// exists to survive — malformed images — so the truncation must be visible
/// rather than silent. This fixture is what proves the state is reachable at
/// all; without it, the `MetadataVersionTruncated` flag would be dead code that
/// no test could distinguish from a constant `false`.
/// </para>
/// <para>
/// The fixture technique came from the adversarial review of PR #3518, which
/// found the truncation flag being discarded.
/// </para>
/// </summary>
public sealed class OversizedMetadataVersionTests(OversizedVersionFixture fixture)
    : IClassFixture<OversizedVersionFixture>
{
    /// <summary>
    /// The metadata layer must report that it clipped the value. Everything
    /// downstream keys off this flag, so if `Describe` does not set it, no
    /// renderer can mark the value however carefully it is written.
    /// </summary>
    [Fact]
    public void Describe_ReportsTheStampAsTruncated()
    {
        using var peReader = new PEReader(new MemoryStream(fixture.Bytes, writable: false));
        var overview = MetadataImageInspector.Describe(peReader);

        Assert.NotNull(overview);
        Assert.True(
            overview.MetadataVersionTruncated,
            "A stamp too long to neutralize within the budget must be reported as truncated.");
        Assert.Equal(OversizedVersionFixture.Budget, overview.MetadataVersion.Length);
    }

    /// <summary>
    /// The end-to-end claim, and the one that matters to a reader: the rendered
    /// stamp carries the ellipsis, so a 1530-character prefix cannot be mistaken
    /// for the whole 1547-character value. Asserted through `mdi` rather than the
    /// renderer alone so the flag is proven to survive the whole path.
    /// </summary>
    [Fact]
    public void Overview_RendersTheTruncationMarkerSoAPrefixIsNotReadAsTheWholeStamp()
    {
        var output = new StringWriter();
        int code = MdiCommand.ExecuteOverview(
            fixture.Path, MetadataTableFormat.Markdown, output, new StringWriter());

        Assert.Equal(0, code);

        string line = output.ToString()
            .Split('\n')
            .Single(static l => l.Contains("Metadata version", StringComparison.Ordinal));

        Assert.Contains('…', line);
    }

    /// <summary>
    /// The close negative case. A conforming image must not acquire the marker,
    /// or it would mean nothing — and it also pins that the budget does not bind
    /// on ordinary input.
    /// </summary>
    [Fact]
    public void ConformingImage_IsNeitherTruncatedNorMarked()
    {
        string self = typeof(OversizedMetadataVersionTests).Assembly.Location;
        using var peReader = new PEReader(new MemoryStream(File.ReadAllBytes(self)));
        var overview = MetadataImageInspector.Describe(peReader);

        Assert.NotNull(overview);
        Assert.False(overview.MetadataVersionTruncated);
        Assert.DoesNotContain('…', overview.MetadataVersion);
    }

    /// <summary>
    /// Gates the repairs `MetadataReader` does not check for us.
    /// <para>
    /// Widening the version field grows the section that holds it, so the
    /// containing section's raw size, every later section's raw pointer, and the
    /// optional header's <c>SizeOfCode</c> all have to grow with it. SRM reads
    /// none of those, so omitting any of them leaves the rest of this class
    /// green while the image carries headers that contradict each other — review
    /// of this file demonstrated exactly that, twice. Anything a fixture claims
    /// to maintain but nothing checks is a claim that will eventually stop being
    /// true.
    /// </para>
    /// <para>
    /// Self-consistency is not enough on its own, and neither is any one
    /// relationship. Every gate here was first written narrowly and then beaten
    /// by a change that kept the relationship it checked intact:
    /// <c>SizeOfCode</c> still equals the code sections' raw sizes when neither
    /// grew, and every offset still lines up when a section grows by one byte
    /// short of a file-alignment unit. What survives is a set of assertions that
    /// no single coherent-looking edit satisfies at once — the optional header's
    /// size fields against the section table, metadata inside its own section,
    /// and sections aligned and disjoint on disk.
    /// </para>
    /// <para>
    /// Every invariant is asserted against a compiler-produced assembly first,
    /// so each is anchored in what real toolchains emit rather than being a rule
    /// invented for the fixture's convenience. The set is not a general PE
    /// validator and does not try to be. What it does claim is narrower and
    /// checkable: every repair
    /// <see cref="OversizedVersionFixture"/> makes is gated by something —
    /// `MetadataReader` itself for the stream offsets and the virtual size, a
    /// test here for the rest — and every field the builder deliberately leaves
    /// alone has the premise that makes leaving it alone correct gated too.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_KeepsOptionalHeaderSizesConsistentWithItsSectionTable()
    {
        AssertOptionalHeaderSizesMatchTheSectionTable(SelfImage);

        AssertOptionalHeaderSizesMatchTheSectionTable(fixture.Bytes);
    }

    /// <summary>
    /// The metadata a PE declares must lie inside the section that holds it.
    /// This is the assertion that fails when the version field is widened
    /// without growing its section, which is the corruption the fixture is most
    /// likely to reintroduce, because it leaves the image parseable.
    /// </summary>
    [Fact]
    public void HostileImage_KeepsMetadataInsideItsOwningSection()
    {
        AssertMetadataFitsItsSection(SelfImage);

        AssertMetadataFitsItsSection(fixture.Bytes);
    }

    /// <summary>
    /// Sections may not overlap once mapped, and must start on a
    /// section-alignment boundary.
    /// <para>
    /// The builder refuses an expansion that would push the grown section into
    /// the next one's address space, but that guard is only as good as its own
    /// continued existence: raise <see cref="OversizedVersionFixture"/>'s
    /// expansion and drop the guard together and the image stays perfectly
    /// coherent on disk while two sections claim the same memory. Review of this
    /// file did exactly that. Asserting the property here means the guard is no
    /// longer the only thing holding it up.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_KeepsSectionsAlignedAndDisjointInMemory()
    {
        AssertSectionsAreAlignedAndDisjointInMemory(SelfImage);

        AssertSectionsAreAlignedAndDisjointInMemory(fixture.Bytes);
    }

    /// <summary>
    /// Sections must start and end on file-alignment boundaries and may not
    /// overlap on disk. The start check fails when a section grows without the
    /// later sections' raw pointers moving out of its way; the size check fails
    /// when a section grows by an amount that is not a whole number of
    /// file-alignment units, which leaves every offset relationship intact and
    /// so is invisible to every other assertion here.
    /// </summary>
    [Fact]
    public void HostileImage_KeepsSectionsAlignedAndDisjointOnDisk()
    {
        AssertSectionsAreAlignedAndDisjoint(SelfImage);

        AssertSectionsAreAlignedAndDisjoint(fixture.Bytes);
    }

    /// <summary>
    /// Pins the premise that lets the fixture leave <c>SizeOfInitializedData</c>
    /// alone: the section it grows contributes to <c>SizeOfCode</c> and to
    /// nothing else.
    /// <para>
    /// `ManagedPEBuilder` emits `.text` as code-only, so widening the version
    /// stamp moves no initialized-data total and the field needs no repair. That
    /// is a fact about the builder, not a law — were `.text` ever to gain
    /// <see cref="SectionCharacteristics.ContainsInitializedData"/>, the fixture
    /// would silently owe a repair it does not make. Reviewers reached for
    /// <c>SizeOfInitializedData</c> twice on the reasonable assumption that
    /// growing a section must move it, which is exactly the kind of premise that
    /// should be checked rather than remembered.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_GrowsASectionThatOnlyCountsAsCode()
    {
        using var peReader = new PEReader(new MemoryStream(fixture.Bytes, writable: false));
        PEHeaders headers = peReader.PEHeaders;
        int start = headers.MetadataStartOffset;

        SectionHeader owner = Assert.Single(
            headers.SectionHeaders.Where(s => start >= s.PointerToRawData
                && start < s.PointerToRawData + s.SizeOfRawData));

        Assert.Equal(
            SectionCharacteristics.ContainsCode,
            owner.SectionCharacteristics & (SectionCharacteristics.ContainsCode
                | SectionCharacteristics.ContainsInitializedData
                | SectionCharacteristics.ContainsUninitializedData));
    }

    /// <summary>A compiler-produced assembly, used to anchor each invariant.</summary>
    static byte[] SelfImage
        => File.ReadAllBytes(typeof(OversizedMetadataVersionTests).Assembly.Location);

    static void AssertOptionalHeaderSizesMatchTheSectionTable(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        PEHeaders headers = peReader.PEHeaders;
        PEHeader header = headers.PEHeader!;

        Assert.Equal(SumOfRawSizes(headers, SectionCharacteristics.ContainsCode), header.SizeOfCode);
        Assert.Equal(
            SumOfRawSizes(headers, SectionCharacteristics.ContainsInitializedData),
            header.SizeOfInitializedData);

        int end = headers.SectionHeaders
            .Max(static s => s.VirtualAddress + s.VirtualSize);

        Assert.Equal(AlignUp(end, header.SectionAlignment), header.SizeOfImage);
    }

    static int SumOfRawSizes(PEHeaders headers, SectionCharacteristics characteristic)
        => headers.SectionHeaders
            .Where(s => (s.SectionCharacteristics & characteristic) != 0)
            .Sum(static s => s.SizeOfRawData);

    static int AlignUp(int value, int alignment)
        => checked((value + alignment - 1) / alignment * alignment);

    static void AssertMetadataFitsItsSection(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        PEHeaders headers = peReader.PEHeaders;

        int start = headers.MetadataStartOffset;
        int end = start + headers.MetadataSize;

        SectionHeader owner = Assert.Single(
            headers.SectionHeaders.Where(s => start >= s.PointerToRawData
                && start < s.PointerToRawData + s.SizeOfRawData));

        Assert.InRange(end, start, owner.PointerToRawData + owner.SizeOfRawData);
    }

    static void AssertSectionsAreAlignedAndDisjointInMemory(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        PEHeaders headers = peReader.PEHeaders;
        int sectionAlignment = headers.PEHeader!.SectionAlignment;

        var mapped = headers.SectionHeaders
            .OrderBy(static s => s.VirtualAddress)
            .ToArray();

        Assert.NotEmpty(mapped);

        int previousEnd = 0;
        foreach (SectionHeader section in mapped)
        {
            Assert.Equal(0, section.VirtualAddress % sectionAlignment);
            Assert.True(
                section.VirtualAddress >= previousEnd,
                $"Section {section.Name} is mapped at {section.VirtualAddress}, inside the section ending at {previousEnd}.");

            previousEnd = section.VirtualAddress + section.VirtualSize;
        }
    }

    static void AssertSectionsAreAlignedAndDisjoint(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        PEHeaders headers = peReader.PEHeaders;
        int fileAlignment = headers.PEHeader!.FileAlignment;

        var occupied = headers.SectionHeaders
            .Where(static s => s.SizeOfRawData > 0)
            .OrderBy(static s => s.PointerToRawData)
            .ToArray();

        Assert.NotEmpty(occupied);

        int previousEnd = 0;
        foreach (SectionHeader section in occupied)
        {
            Assert.Equal(0, section.PointerToRawData % fileAlignment);
            Assert.Equal(0, section.SizeOfRawData % fileAlignment);
            Assert.True(
                section.PointerToRawData >= previousEnd,
                $"Section {section.Name} starts at {section.PointerToRawData}, inside the section ending at {previousEnd}.");

            previousEnd = section.PointerToRawData + section.SizeOfRawData;
        }

        Assert.True(
            previousEnd <= image.Length,
            $"Sections run to {previousEnd}, past the {image.Length}-byte image.");
    }
}

/// <summary>
/// Builds a managed PE whose metadata root carries a version stamp far longer
/// than ECMA-335 allows, while remaining internally consistent enough that
/// `MetadataReader` parses it — heaps, tables, and all.
/// <para>
/// The stamp cannot simply be passed to `MetadataRootBuilder`, which rejects an
/// over-long value. So a conforming image is built first and the version field
/// is then widened in place, shifting everything after it and repairing what
/// records a position: the stream header offsets, the CLI header's metadata
/// size, the containing section's virtual and raw sizes, the raw pointer of
/// every later section, and the optional header's `SizeOfCode`.
/// </para>
/// <para>
/// Those repairs are not equally self-enforcing, and it matters which is which.
/// Two of them `MetadataReader` checks itself, and dropping either fails loudly:
/// without the stream-offset repair it throws `BadImageFormatException: Unknown
/// tables: 0x4141414141414141`, the padding bytes being read as a table mask,
/// and without the containing section's virtual-size growth it throws
/// `BadImageFormatException: Section too small.`
/// </para>
/// <para>
/// The other three it never reads: the containing section's raw size, the later
/// sections' raw pointers, and `SizeOfCode`. Dropping any of those leaves an
/// image SRM parses happily while its headers contradict each other, so each is
/// gated by a test instead —
/// <see cref="OversizedMetadataVersionTests.HostileImage_KeepsMetadataInsideItsOwningSection"/>,
/// <see cref="OversizedMetadataVersionTests.HostileImage_KeepsSectionsAlignedAndDisjointOnDisk"/>, and
/// <see cref="OversizedMetadataVersionTests.HostileImage_KeepsOptionalHeaderSizesConsistentWithItsSectionTable"/>.
/// Review of this file found two of the three ungated, on separate rounds.
/// </para>
/// <para>
/// Two fields deliberately need no repair, and that is also checked rather than
/// remembered. `SizeOfInitializedData` does not move because `ManagedPEBuilder`
/// emits `.text` as code-only, which
/// <see cref="OversizedMetadataVersionTests.HostileImage_GrowsASectionThatOnlyCountsAsCode"/>
/// pins; `SizeOfImage` does not move because the expansion stays inside the
/// containing section's existing virtual footprint, which the overlap guard
/// below enforces and
/// <see cref="OversizedMetadataVersionTests.HostileImage_KeepsSectionsAlignedAndDisjointInMemory"/>
/// asserts independently of that guard. Both are covered by the optional-header
/// gate, so if either premise ever changes the fixture fails rather than quietly
/// emitting a contradictory image.
/// </para>
/// </summary>
public sealed class OversizedVersionFixture : IDisposable
{
    /// <summary>
    /// Bytes added to the version field. A whole number of file-alignment units
    /// keeps both the grown section's raw size and every later section's raw
    /// pointer aligned, so no other header needs rewriting.
    /// <para>
    /// The builder rejects any other value, because an expansion that is even one
    /// byte short leaves every offset relationship intact — metadata still inside
    /// its section, sections still disjoint, `SizeOfCode` still matching — while
    /// producing a section header PE/COFF does not permit. Review of this file
    /// found exactly that gap;
    /// <see cref="OversizedMetadataVersionTests.HostileImage_KeepsSectionsAlignedAndDisjointOnDisk"/>
    /// now catches it, and this guard stops it being reached by accident.
    /// </para>
    /// </summary>
    const int Expansion = 1536;

    /// <summary>
    /// The widest a neutralized stamp may render: `MetadataImageInspector`'s
    /// budget, restated here so a change to it fails this test rather than
    /// silently changing what "truncated" means.
    /// </summary>
    public const int Budget = 255 * 6;

    public OversizedVersionFixture()
    {
        Bytes = ExpandMetadataVersion(BuildBaselineImage(), Expansion);
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"mdi-oversized-version-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(Path, Bytes);
    }

    /// <summary>The hostile image.</summary>
    public byte[] Bytes { get; }

    /// <summary>The same image on disk, for the commands that take a path.</summary>
    public string Path { get; }

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

    static byte[] BuildBaselineImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("LongVersion.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        metadata.AddAssembly(
            metadata.GetOrAddString("LongVersion"),
            new Version(1, 0, 0, 0),
            default,
            default,
            (AssemblyFlags)0,
            AssemblyHashAlgorithm.None);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            new PEHeaderBuilder(),
            new MetadataRootBuilder(metadata, "v4.0.30319"),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);

        return image.ToArray();
    }

    static byte[] ExpandMetadataVersion(byte[] original, int expansion)
    {
        int peSignature = ReadInt32(original, 0x3c);
        int coffHeader = peSignature + 4;
        int sectionCount = ReadUInt16(original, coffHeader + 2);
        int optionalHeader = coffHeader + 20;
        int sectionHeaders = optionalHeader + ReadUInt16(original, coffHeader + 16);

        int metadataRoot, metadataSize, corHeader, fileAlignment, sectionAlignment;
        SectionInfo[] sections;

        using (var peReader = new PEReader(new MemoryStream(original, writable: false)))
        {
            PEHeaders headers = peReader.PEHeaders;
            metadataRoot = headers.MetadataStartOffset;
            metadataSize = headers.MetadataSize;
            corHeader = headers.CorHeaderStartOffset;
            fileAlignment = headers.PEHeader!.FileAlignment;
            sectionAlignment = headers.PEHeader.SectionAlignment;
            sections = headers.SectionHeaders
                .Select(static s => new SectionInfo(
                    s.VirtualAddress, s.VirtualSize, s.PointerToRawData, s.SizeOfRawData, s.SectionCharacteristics))
                .ToArray();
        }

        if (expansion <= 0 || expansion % 4 != 0 || expansion % fileAlignment != 0)
        {
            throw new InvalidOperationException(
                $"An expansion of {expansion} would misalign the image; it must be a positive multiple of {fileAlignment}.");
        }

        int oldVersionLength = ReadInt32(original, metadataRoot + 12);
        int versionStart = metadataRoot + 16;
        int insertionPoint = versionStart + oldVersionLength;
        int newVersionLength = checked(oldVersionLength + expansion);

        byte[] patched = new byte[checked(original.Length + expansion)];
        original.AsSpan(0, versionStart).CopyTo(patched);
        original.AsSpan(insertionPoint).CopyTo(patched.AsSpan(insertionPoint + expansion));

        // The declared length counts the terminator, so the readable stamp is
        // one shorter than the field.
        patched[versionStart] = (byte)'v';
        patched.AsSpan(versionStart + 1, newVersionLength - 2).Fill((byte)'A');
        patched[versionStart + newVersionLength - 1] = 0;
        WriteInt32(patched, metadataRoot + 12, newVersionLength);

        // Every stream header records an offset from the metadata root, and every
        // stream now sits `expansion` bytes further along.
        int cursor = metadataRoot + 16 + newVersionLength;
        int streamCount = ReadUInt16(patched, cursor + 2);
        cursor += 4;
        for (int i = 0; i < streamCount; i++)
        {
            WriteInt32(patched, cursor, checked(ReadInt32(patched, cursor) + expansion));

            int name = cursor + 8;
            while (patched[name] != 0)
                name++;

            cursor = AlignUp(name + 1, 4);
        }

        // IMAGE_COR20_HEADER.MetaData.Size; its RVA is unchanged because the
        // metadata root did not move.
        WriteInt32(patched, corHeader + 12, checked(metadataSize + expansion));

        int containing = Array.FindIndex(
            sections,
            section => insertionPoint >= section.RawPointer
                && insertionPoint < section.RawPointer + section.RawSize);

        if (containing < 0)
            throw new InvalidOperationException("The metadata root is outside every section.");

        SectionInfo owner = sections[containing];
        int newVirtualSize = checked(owner.VirtualSize + expansion);
        int nextVirtualAddress = containing + 1 < sections.Length
            ? sections[containing + 1].VirtualAddress
            : int.MaxValue;

        if ((long)owner.VirtualAddress + AlignUp(newVirtualSize, sectionAlignment) > nextVirtualAddress)
            throw new InvalidOperationException("The expansion would overlap the next section.");

        for (int i = 0; i < sectionCount; i++)
        {
            int header = sectionHeaders + (40 * i);
            if (i == containing)
            {
                WriteInt32(patched, header + 8, newVirtualSize);
                WriteInt32(patched, header + 16, checked(sections[i].RawSize + expansion));
            }
            else if (sections[i].RawPointer >= insertionPoint)
            {
                WriteInt32(patched, header + 20, checked(sections[i].RawPointer + expansion));
            }
        }

        if ((owner.Characteristics & SectionCharacteristics.ContainsCode) == 0)
            throw new InvalidOperationException("Expected the metadata root to sit in a code section.");

        WriteInt32(patched, optionalHeader + 4, checked(ReadInt32(patched, optionalHeader + 4) + expansion));
        return patched;
    }

    static int AlignUp(int value, int alignment)
        => checked((value + alignment - 1) / alignment * alignment);

    static int ReadInt32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    static int ReadUInt16(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    static void WriteInt32(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    readonly record struct SectionInfo(
        int VirtualAddress,
        int VirtualSize,
        int RawPointer,
        int RawSize,
        SectionCharacteristics Characteristics);
}
