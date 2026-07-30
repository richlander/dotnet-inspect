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
    /// Self-consistency is not enough on its own, and neither is any one
    /// relationship. Every gate here was first written narrowly and then beaten
    /// by a change that kept the relationship it checked intact:
    /// <c>SizeOfCode</c> still equals the code sections' raw sizes when neither
    /// grew, and every offset still lines up when a section grows by one byte
    /// short of a file-alignment unit. What survives is a set of assertions that
    /// no single coherent-looking edit satisfies at once.
    /// <para>
    /// The set is not a general PE validator and does not try to be — the image
    /// is deliberately not loadable, and
    /// <see cref="HostileImage_BreaksOnlyTheRvasItIsKnownToBreak"/> says exactly
    /// how. What it claims is narrower and checkable: every repair
    /// <see cref="OversizedVersionFixture"/> makes is gated by something —
    /// `MetadataReader` itself for the stream offsets and the virtual size, a
    /// test here for the rest — and everything the expansion knowingly breaks is
    /// enumerated rather than assumed harmless.
    /// </para>
    /// <para>
    /// Layout invariants are anchored against compiler-produced assemblies, and
    /// the anchor is 2,432 shipped images rather than the handful that first
    /// seemed enough; see <see cref="OversizedVersionFixture.Baseline"/> for why
    /// header sizes are no longer anchored that way at all.
    /// </para>
    /// </summary>
    /// Gates the repairs `MetadataReader` does not check for us, by comparing the
    /// patched image against the conforming one it was cut from.
    /// <para>
    /// Widening the version field grows the section that holds it, so
    /// <c>SizeOfCode</c> grows by exactly the expansion while
    /// <c>SizeOfInitializedData</c> and <c>SizeOfImage</c> must not move at all.
    /// SRM reads none of the three, so omitting any of the repairs leaves the
    /// rest of this class green while the image carries headers that contradict
    /// each other — review of this file demonstrated exactly that, twice.
    /// </para>
    /// <para>
    /// An earlier version asserted a general PE rule instead: that each field
    /// equals the sum of the matching sections' raw sizes. Review disproved it
    /// for <c>SizeOfInitializedData</c>, and a scan of 2,432 shipped assemblies
    /// confirmed 69 counter-examples, all native. The rule held for every image
    /// this fixture happened to be anchored against, which is exactly how an
    /// invented law survives review. Comparing against the fixture's own baseline
    /// asserts what the edit did rather than what PE law is presumed to say, and
    /// is both stronger here and true everywhere.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_ChangesExactlyTheHeaderSizesTheExpansionShould()
    {
        PEHeader baseline = OptionalHeader(fixture.Baseline);
        PEHeader patched = OptionalHeader(fixture.Bytes);

        Assert.Equal(baseline.SizeOfCode + OversizedVersionFixture.Expansion, patched.SizeOfCode);
        Assert.Equal(baseline.SizeOfInitializedData, patched.SizeOfInitializedData);
        Assert.Equal(baseline.SizeOfImage, patched.SizeOfImage);
    }

    /// <summary>
    /// Records what the expansion knowingly breaks, so that the set cannot grow
    /// unnoticed and cannot silently shrink either.
    /// <para>
    /// Inserting bytes in the middle of `.text` moves everything after the
    /// insertion point, and the fixture repairs only what `MetadataReader`
    /// traverses. Everything else pointing into that section past the cut is left
    /// aimed at its old home: the PE entry point, the import directory and the
    /// whole chain it addresses, the relocation entry covering the entry stub's
    /// operand, and the strong-name signature placeholder. Eight in all.
    /// </para>
    /// <para>
    /// Successive reviews found them a few at a time, each against a claim that
    /// the previous list was complete. The list is not maintained here any more:
    /// the structures are walked, and what the walkers cover is itself asserted
    /// by <see cref="HostileImage_HasOnlyTheRvaBearingStructuresTheWalkersKnow"/>.
    /// </para>
    /// <para>
    /// They are not repaired because this is a metadata fixture and nothing here
    /// loads or executes the image; SRM reaches metadata through the CLI header,
    /// which sits before the insertion point and stays valid.
    /// </para>
    /// <para>
    /// The classification compares each RVA between the two images rather than
    /// testing the baseline alone, which matters in both directions. A first
    /// version asked only whether an RVA sat past the insertion point in the
    /// baseline; review showed it stayed green after the entry point was
    /// *repaired*, because it never looked at the patched image at all. It would
    /// also have misfiled the base relocation *directory*, whose RVA is past the
    /// insertion point and yet correct, since only `.reloc`'s raw pointer moves.
    /// Position in the baseline does not determine staleness; disagreement
    /// between the two images does.
    /// </para>
    /// <para>
    /// RVAs are not only declared in headers. The relocation block inside
    /// `.reloc` encodes one of its own, pointing at the entry stub's operand, and
    /// a version of this test that walked only the optional header reported the
    /// image clean while that target hung over the moved stub — and said in this
    /// comment that `.reloc` was "entirely correct", which was true of the
    /// directory and false of what it points at. Section content is walked too.
    /// </para>
    /// <para>
    /// The scope, precisely: every RVA *reachable from a structure the image
    /// declares* — the optional header's scalar address fields, the section
    /// table, the data directories, the relocation blocks, the import directory
    /// and its thunks, and the CLI header. It is not every four-byte value in the
    /// file. Review raised that gap by writing a plausible RVA into section
    /// padding and observing that nothing failed, which is correct and is not
    /// fixed here: no structure points at those bytes, so nothing in the image
    /// says they are an address rather than data, and a scan for values that
    /// merely *look* like RVAs would be a guess that reports padding, string
    /// bytes, and IL as findings. The reachable-set claim is kept honest from the
    /// other end instead —
    /// <see cref="HostileImage_HasOnlyTheRvaBearingStructuresTheWalkersKnow"/>
    /// fails if the fixture gains a structure the walkers do not follow, which is
    /// the case that would make unwalked bytes meaningful.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_BreaksOnlyTheRvasItIsKnownToBreak()
    {
        PEHeaders baseline = Headers(fixture.Baseline);
        int insertionPoint = InsertionPointOf(fixture.Baseline);
        int containing = ContainingSectionIndex(baseline, insertionPoint);

        var stale = new List<string>();

        foreach ((string name, int before, int after) in RvaFields(fixture.Baseline, fixture.Bytes))
        {
            int correct;

            if (before == 0)
            {
                // A directory the baseline does not have must not appear. This is
                // the case a reviewer planted: writing a stale `DelayImportTable`
                // into the patched image only, where a `continue` on an empty
                // baseline entry would have skipped straight past it.
                correct = 0;
            }
            else
            {
                int fileOffset = FileOffsetOf(baseline, before);
                bool shifted = fileOffset >= insertionPoint
                    && ContainingSectionIndex(baseline, fileOffset) == containing;

                correct = shifted ? before + OversizedVersionFixture.Expansion : before;
            }

            if (after != correct)
                stale.Add($"{name} 0x{after:X} should be 0x{correct:X}");
        }

        Assert.Equal(
            new[]
            {
                "AddressOfEntryPoint 0x220A should be 0x280A",
                "ImportTable 0x21B8 should be 0x27B8",
                "BaseRelocation[0] 0x220C should be 0x280C",
                "Import[0].LookupTable 0x21E0 should be 0x27E0",
                "Import[0].Name 0x21FA should be 0x27FA",
                "Import[0].Lookup[0].HintName 0x21EC should be 0x27EC",
                "Import[0].Address[0].HintName 0x21EC should be 0x27EC",
                "Cli.StrongNameSignature 0x2138 should be 0x2738",
            },
            stale);
    }

    /// <summary>
    /// Every RVA reachable from a structure the image declares — the optional
    /// header's scalar address fields, the section table, the data directories,
    /// the relocation blocks, the import directory and its thunks, and the CLI
    /// header — paired across the two images.
    /// <para>
    /// The directories are read out of the image rather than named one by one.
    /// Two reviewers, independently, defeated a hand-written list: one by
    /// planting a stale `DelayImportTable`, which the list did not mention, and
    /// one by noting that seven more entries were unlisted. A list that has to be
    /// maintained to stay exhaustive is not a gate on exhaustiveness. Walking
    /// `NumberOfRvaAndSizes` from the header means a directory this fixture has
    /// never seen — including a sixteenth, past the names below — is still
    /// classified.
    /// </para>
    /// <para>
    /// The relocation targets are here because "every RVA in the header" was a
    /// third narrower claim than the one the test makes. `.reloc` encodes a
    /// target of its own, and it went unexamined until review found it stale.
    /// </para>
    /// </summary>
    static IEnumerable<(string Name, int Before, int After)> RvaFields(byte[] baseline, byte[] patched)
    {
        foreach (var pair in Pair(OptionalHeaderRvas(baseline), OptionalHeaderRvas(patched)))
            yield return pair;

        foreach (var pair in Pair(SectionRvas(baseline), SectionRvas(patched)))
            yield return pair;

        foreach (var pair in Pair(DataDirectories(baseline), DataDirectories(patched)))
            yield return pair;

        foreach (var pair in Pair(RelocationTargets(baseline), RelocationTargets(patched)))
            yield return pair;

        foreach (var pair in ImportTargets(baseline, patched))
            yield return pair;

        foreach (var pair in Pair(CliHeaderTargets(baseline), CliHeaderTargets(patched)))
            yield return pair;
    }

    /// <summary>
    /// The optional header's scalar RVA fields.
    /// <para>
    /// `BaseOfCode` and `BaseOfData` are here because review planted a stale
    /// `BaseOfData` and nothing failed, against a scope statement that claimed
    /// every RVA reachable from the optional header. They are typed address
    /// fields, not incidental bytes, so the argument that rejected a heuristic
    /// scan does not cover them. `BaseOfData` does not exist in PE32+, where the
    /// slot is the low half of `ImageBase`;
    /// <see cref="HostileImage_IsPe32AsTheseWalkersAssume"/> is what makes
    /// reading it here safe.
    /// </para>
    /// </summary>
    static (string Name, int Rva)[] OptionalHeaderRvas(byte[] image)
    {
        PEHeader header = OptionalHeader(image);

        return
        [
            ("AddressOfEntryPoint", header.AddressOfEntryPoint),
            ("BaseOfCode", header.BaseOfCode),
            ("BaseOfData", header.BaseOfData),
        ];
    }

    /// <summary>
    /// Each section header's virtual address. These stay put — the expansion
    /// moves raw pointers, not RVAs — but the scope statement covers every RVA
    /// the image declares, and a section table is where an image declares most of
    /// them.
    /// </summary>
    static (string Name, int Rva)[] SectionRvas(byte[] image)
        => [.. Headers(image).SectionHeaders
            .Select(s => ($"Section[{s.Name}].VirtualAddress", s.VirtualAddress))];

    static IEnumerable<(string Name, int Before, int After)> Pair(
        (string Name, int Rva)[] before, (string Name, int Rva)[] after)
    {
        Assert.Equal(before.Length, after.Length);

        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].Name, after[i].Name);
            yield return (before[i].Name, before[i].Rva, after[i].Rva);
        }
    }

    /// <summary>
    /// The RVAs encoded inside the base relocation blocks.
    /// <para>
    /// Each block is a page RVA, a byte count covering the header and its
    /// entries, then 16-bit entries whose top four bits are the relocation type
    /// and whose low twelve are an offset into that page. Type 0 is padding to a
    /// 32-bit boundary and addresses nothing, so it is skipped rather than
    /// reported as a target of the page itself.
    /// </para>
    /// </summary>
    static (string Name, int Rva)[] RelocationTargets(byte[] image)
    {
        PEHeaders headers = Headers(image);
        DirectoryEntry directory = headers.PEHeader!.BaseRelocationTableDirectory;

        if (directory.RelativeVirtualAddress == 0)
            return [];

        var targets = new List<(string, int)>();
        int cursor = FileOffsetOf(headers, directory.RelativeVirtualAddress);
        int end = cursor + directory.Size;

        while (cursor + 8 <= end)
        {
            int pageRva = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(cursor, 4));
            int blockSize = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(cursor + 4, 4));

            Assert.InRange(blockSize, 8, end - cursor);

            for (int entry = cursor + 8; entry + 2 <= cursor + blockSize; entry += 2)
            {
                ushort fixup = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(entry, 2));

                if ((fixup >> 12) != 0)
                    targets.Add(($"BaseRelocation[{targets.Count}]", pageRva + (fixup & 0xFFF)));
            }

            cursor += blockSize;
        }

        return [.. targets];
    }

    /// <summary>
    /// The RVAs encoded inside the import directory: each descriptor's lookup
    /// table, name string, and address table, and every hint/name pointer in the
    /// two thunk arrays.
    /// <para>
    /// Unlike the other walkers this one cannot follow the patched image's own
    /// pointers, because the directory pointing at these structures is itself one
    /// of the things the expansion breaks — following it lands in version
    /// padding, where the first "RVA" read is `0x41414141`. So each structure is
    /// located in the baseline and its field re-read at the offset the expansion
    /// moved that byte to. The content is intact and unmodified; only what points
    /// at it is stale, which is the distinction this test exists to record.
    /// That makes the baseline the sole authority on shape — where the descriptor
    /// array ends, where each thunk list ends, which entries are ordinals — so a
    /// structure present only in the patched image would not be walked at all.
    /// <see cref="HostileImage_HasTheImportShapeItsBaselineWalkAssumes"/> is what
    /// makes that safe, by failing if the two images ever disagree on shape.
    /// </para>
    /// <para>
    /// Entries with the ordinal flag set carry an ordinal rather than an RVA and
    /// address nothing, so they are skipped. This fixture is PE32, where a thunk
    /// is four bytes; <see cref="HostileImage_IsPe32AsTheseWalkersAssume"/> pins
    /// that rather than letting a PE32+ image be walked with the wrong stride.
    /// </para>
    /// </summary>
    static IEnumerable<(string Name, int Before, int After)> ImportTargets(byte[] baseline, byte[] patched)
    {
        PEHeaders headers = Headers(baseline);
        DirectoryEntry directory = headers.PEHeader!.ImportTableDirectory;

        if (directory.RelativeVirtualAddress == 0)
            yield break;

        int insertionPoint = InsertionPointOf(baseline);
        int descriptor = FileOffsetOf(headers, directory.RelativeVirtualAddress);
        int end = descriptor + directory.Size;

        for (int i = 0; descriptor + 20 <= end; i++, descriptor += 20)
        {
            int lookupTable = ReadRva(baseline, descriptor);
            int addressTable = ReadRva(baseline, descriptor + 16);

            if (lookupTable == 0 && addressTable == 0 && ReadRva(baseline, descriptor + 12) == 0)
                break;

            foreach (var field in new[]
            {
                ($"Import[{i}].LookupTable", descriptor),
                ($"Import[{i}].Name", descriptor + 12),
                ($"Import[{i}].AddressTable", descriptor + 16),
            })
            {
                if (ReadRva(baseline, field.Item2) != 0)
                    yield return Sited(field.Item1, field.Item2);
            }

            foreach (var thunk in Thunks($"Import[{i}].Lookup", lookupTable))
                yield return thunk;

            foreach (var thunk in Thunks($"Import[{i}].Address", addressTable))
                yield return thunk;
        }

        IEnumerable<(string, int, int)> Thunks(string label, int tableRva)
        {
            if (tableRva == 0)
                yield break;

            int site = FileOffsetOf(headers, tableRva);

            for (int slot = 0; ; slot++, site += 4)
            {
                int entry = ReadRva(baseline, site);

                if (entry == 0)
                    yield break;

                // The high bit means the import is by ordinal, which is a number
                // rather than a pointer into the image.
                if ((entry & unchecked((int)0x80000000)) == 0)
                    yield return Sited($"{label}[{slot}].HintName", site);
            }
        }

        (string, int, int) Sited(string label, int site)
            => (label,
                ReadRva(baseline, site),
                ReadRva(patched, site < insertionPoint ? site : site + OversizedVersionFixture.Expansion));
    }

    /// <summary>
    /// The RVAs in the CLI header. Its metadata pointer is what the fixture is
    /// built around, so a walk that skipped this structure would leave the one
    /// thing SRM follows unclassified.
    /// <para>
    /// Offset 20 is the one field whose *meaning* is conditional: it holds a
    /// managed entry-point token normally, and an RVA when the header sets
    /// `NativeEntryPoint`. Review planted a stale RVA there behind that flag and
    /// the walk did not see it, because the field was omitted on the unstated
    /// premise that the flag is never set. It is read as an RVA exactly when the
    /// flag says it is one.
    /// </para>
    /// <para>
    /// These are the header's own fields. Several of them point at structures
    /// with RVAs of their own, and this walk does not descend into any of them;
    /// <see cref="HostileImage_HasOnlyTheRvaBearingStructuresTheWalkersKnow"/> is
    /// what keeps that honest, by failing if the fixture ever contains one.
    /// </para>
    /// </summary>
    static (string Name, int Rva)[] CliHeaderTargets(byte[] image)
    {
        PEHeaders headers = Headers(image);
        DirectoryEntry directory = headers.PEHeader!.CorHeaderTableDirectory;

        if (directory.RelativeVirtualAddress == 0)
            return [];

        int header = FileOffsetOf(headers, directory.RelativeVirtualAddress);
        var flags = (CorFlags)ReadRva(image, header + 16);

        (string Name, int Offset)[] fields =
        [
            ("Cli.MetaData", 8),
            .. flags.HasFlag(CorFlags.NativeEntryPoint)
                ? new[] { ("Cli.NativeEntryPoint", 20) }
                : [],
            ("Cli.Resources", 24),
            ("Cli.StrongNameSignature", 32),
            ("Cli.CodeManagerTable", 40),
            ("Cli.VTableFixups", 48),
            ("Cli.ExportAddressTableJumps", 56),
            ("Cli.ManagedNativeHeader", 64),
        ];

        return [.. fields
            .Select(f => (f.Name, Rva: ReadRva(image, header + f.Offset)))
            .Where(f => f.Rva != 0)];
    }

    static int ReadRva(byte[] image, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset, 4));

    /// <summary>
    /// <see cref="ImportTargets"/> walks four-byte thunks and
    /// <see cref="DataDirectories"/> locates the directory count at the PE32
    /// offset. Both are wrong for a PE32+ image, so this fails rather than
    /// letting them read the wrong bytes and report a clean walk.
    /// </summary>
    [Fact]
    public void HostileImage_IsPe32AsTheseWalkersAssume()
    {
        Assert.Equal(PEMagic.PE32, OptionalHeader(fixture.Baseline).Magic);
        Assert.Equal(PEMagic.PE32, OptionalHeader(fixture.Bytes).Magic);
    }

    /// <summary>
    /// Pins which RVA-bearing structures this image actually contains.
    /// <para>
    /// <see cref="HostileImage_BreaksOnlyTheRvasItIsKnownToBreak"/> walks the
    /// optional header, the relocation blocks, the import directory, and the CLI
    /// header. A directory it does not know how to walk would still have its own
    /// RVA classified and its *contents* silently skipped — which is exactly how
    /// the relocation target went unnoticed until review found it. So the set of
    /// present directories is asserted here: a fixture that gains a debug
    /// directory, a TLS block, or a delay-import table fails until someone
    /// teaches the walker about it.
    /// </para>
    /// <para>
    /// The same argument applies one level further down, and asserting it only at
    /// the directory level is how review defeated this gate: the CLI header's
    /// sub-directories are themselves pointers to structures, and two of them —
    /// `VTableFixups` (an array of `COR_VTABLEFIXUP`, whose first field is an
    /// RVA) and `ManagedNativeHeader` — carry addresses inside. Review built a
    /// reachable `VTableFixups`, repaired the CLI pointer to it so the pointer
    /// itself classified as correct, and left the nested RVA stale; every test
    /// passed. So the present CLI sub-directories are pinned too. This
    /// deliberately also fails on the harmless additions — managed resources,
    /// say, whose blobs hold no addresses — because the point is that someone
    /// classifies the new structure, not that the gate guesses correctly on their
    /// behalf.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_HasOnlyTheRvaBearingStructuresTheWalkersKnow()
    {
        string[] present = [.. DataDirectories(fixture.Bytes)
            .Where(d => d.Rva != 0)
            .Select(d => d.Name)];

        Assert.Equal(
            new[] { "ImportTable", "BaseRelocationTable", "ImportAddressTable", "CorHeaderTable" },
            present);

        Assert.Equal(
            new[] { "Cli.MetaData", "Cli.StrongNameSignature" },
            CliHeaderTargets(fixture.Bytes).Select(f => f.Name));

        // Metadata stores RVAs too, in the MethodDef bodies and the FieldRva
        // table. This fixture has neither, which is why the walkers stop at the
        // CLI header — but that is a property of the fixture, not a law, so it is
        // asserted rather than assumed.
        using var peReader = new PEReader(new MemoryStream(fixture.Bytes, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();

        Assert.Equal(0, reader.GetTableRowCount(TableIndex.FieldRva));
        Assert.Empty(reader.MethodDefinitions
            .Where(h => reader.GetMethodDefinition(h).RelativeVirtualAddress != 0));
    }

    /// <summary>
    /// The optional header's data directory array, read from the image so that
    /// the count comes from `NumberOfRvaAndSizes` rather than from this file.
    /// </summary>
    static (string Name, int Rva)[] DataDirectories(byte[] image)
        => [.. DirectoryEntries(image).Select(d => (d.Name, d.Rva))];

    /// <summary>
    /// Each data directory's declared extent, paired with its name.
    /// <para>
    /// A size is not an address, so
    /// <see cref="HostileImage_BreaksOnlyTheRvasItIsKnownToBreak"/> never looks
    /// at one — it classifies RVAs. That left every declared extent in the image
    /// unchecked, and review shrank the patched import directory's size to
    /// exclude its own null terminator, leaving all 147 tests green.
    /// </para>
    /// </summary>
    static (string Name, int Size)[] DataDirectorySizes(byte[] image)
        => [.. DirectoryEntries(image).Select(d => (d.Name, d.Size))];

    static (string Name, int Rva, int Size)[] DirectoryEntries(byte[] image)
    {
        PEHeaders headers = Headers(image);
        int optionalHeader = headers.PEHeaderStartOffset;
        bool pe32Plus = headers.PEHeader!.Magic == PEMagic.PE32Plus;

        // NumberOfRvaAndSizes is the last scalar in the optional header, and the
        // directory array begins immediately after it. Its offset differs because
        // five fields widen to 64 bits in PE32+.
        int countOffset = optionalHeader + (pe32Plus ? 108 : 92);
        int count = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(countOffset, 4));
        int first = countOffset + 4;

        return [.. Enumerable.Range(0, count).Select(i => (
            i < DirectoryNames.Length ? DirectoryNames[i] : $"Directory{i}",
            BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(first + (i * 8), 4)),
            BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(first + (i * 8) + 4, 4))))];
    }

    /// <summary>
    /// The expansion moves bytes; it does not change how much of the image any
    /// directory claims. Every declared extent must therefore survive it
    /// unchanged.
    /// <para>
    /// This is the one thing in the header that the RVA inventory structurally
    /// cannot cover: a directory entry is an address *and* a size, and only the
    /// address half is an RVA to classify. Review used the other half — shrinking
    /// the patched import directory to 20 bytes so that its own null terminator
    /// fell outside the declared extent, without altering a single byte the walk
    /// reads. Every test passed.
    /// </para>
    /// <para>
    /// <see cref="ImportShape"/> depends on this directly: it bounds both
    /// traversals with the baseline's `ImportTableDirectory.Size`, which is only
    /// legitimate while the patched image declares the same extent.
    /// </para>
    /// <para>
    /// The second reviewer attacked the same seam from the other side, planting a
    /// descriptor-shaped run of bytes *past* the declared extent while leaving the
    /// extent alone. That is not a finding, for the reason a byte scan was
    /// rejected earlier: the import region here is 79 bytes covering the
    /// descriptors, the lookup table and the name strings, the descriptor array
    /// ends at its null terminator, and bytes beyond both are declared by nothing.
    /// Reporting them would mean recognising structures by their shape rather
    /// than by reachability. The half of that attack which *is* real — growing
    /// the patched extent so the new bytes become declared — is what this test
    /// fails on.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_KeepsEveryDeclaredDirectoryExtent()
    {
        Assert.Equal(DataDirectorySizes(fixture.Baseline), DataDirectorySizes(fixture.Bytes));
    }

    static readonly string[] DirectoryNames =
    [
        "ExportTable",
        "ImportTable",
        "ResourceTable",
        "ExceptionTable",
        "CertificateTable",
        "BaseRelocationTable",
        "DebugTable",
        "ArchitectureTable",
        "GlobalPointerTable",
        "ThreadLocalStorageTable",
        "LoadConfigTable",
        "BoundImportTable",
        "ImportAddressTable",
        "DelayImportTable",
        "CorHeaderTable",
        "ReservedTable",
    ];

    /// <summary>
    /// The import walk is driven entirely by the baseline: which descriptor is
    /// the null terminator, where each thunk list ends, and which entries carry
    /// the ordinal flag are all decided by baseline bytes, because the patched
    /// image's import directory is itself stale and following it lands in version
    /// padding. That is sound only while the two images have the same import
    /// *shape*, and nothing said so.
    /// <para>
    /// Review broke exactly that premise: it replaced the moved null descriptor
    /// in the patched image with one carrying three typed RVAs. The baseline
    /// stopped at its terminator, the walk never reached the new descriptor, and
    /// all 146 tests passed. So the premise is asserted rather than assumed — the
    /// same traversal runs twice, once reading baseline bytes and once reading
    /// the patched bytes each site moved to, and the two shapes must agree.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_HasTheImportShapeItsBaselineWalkAssumes()
    {
        int insertionPoint = InsertionPointOf(fixture.Baseline);

        Assert.Equal(
            ImportShape(fixture.Baseline, site => ReadRva(fixture.Baseline, site)),
            ImportShape(fixture.Baseline, site => ReadRva(
                fixture.Bytes,
                site < insertionPoint ? site : site + OversizedVersionFixture.Expansion)));
    }

    /// <summary>
    /// The structural decisions <see cref="ImportTargets"/> makes — how many
    /// descriptors there are, how many thunks each table holds, and which of
    /// those are ordinals — expressed as a list, so the same traversal can be run
    /// against either image and the results compared.
    /// <para>
    /// Sites are always baseline file offsets; it is the reader that decides
    /// which image those offsets are read from. Only shape is recorded, never an
    /// RVA value — the RVAs are expected to differ between the two images, and
    /// classifying that difference is what
    /// <see cref="HostileImage_BreaksOnlyTheRvasItIsKnownToBreak"/> is for.
    /// </para>
    /// </summary>
    static string[] ImportShape(byte[] baseline, Func<int, int> read)
    {
        PEHeaders headers = Headers(baseline);
        DirectoryEntry directory = headers.PEHeader!.ImportTableDirectory;

        if (directory.RelativeVirtualAddress == 0)
            return [];

        List<string> shape = [];
        int descriptor = FileOffsetOf(headers, directory.RelativeVirtualAddress);
        int end = descriptor + directory.Size;
        int i = 0;

        for (; descriptor + 20 <= end; i++, descriptor += 20)
        {
            int lookupTable = read(descriptor);
            int name = read(descriptor + 12);
            int addressTable = read(descriptor + 16);

            if (lookupTable == 0 && addressTable == 0 && name == 0)
                break;

            shape.Add(
                $"Import[{i}] lookup={lookupTable != 0} name={name != 0} address={addressTable != 0}");

            Record($"Import[{i}].Lookup", lookupTable);
            Record($"Import[{i}].Address", addressTable);
        }

        shape.Add($"Descriptors={i}");

        return [.. shape];

        void Record(string label, int tableRva)
        {
            if (tableRva == 0)
                return;

            int site = FileOffsetOf(headers, tableRva);

            for (int slot = 0; ; slot++, site += 4)
            {
                int entry = read(site);

                if (entry == 0)
                {
                    shape.Add($"{label} slots={slot}");
                    return;
                }

                shape.Add($"{label}[{slot}] ordinal={(entry & unchecked((int)0x80000000)) != 0}");
            }
        }
    }

    /// <summary>
    /// The expansion is a byte-for-byte copy with a widened version field and a
    /// handful of position repairs. This asserts exactly that, over the whole
    /// image: every byte outside the payload and the documented repairs is
    /// identical to its baseline counterpart at the offset the expansion moved
    /// it to.
    /// <para>
    /// The shape of this assertion is the point. Four consecutive rounds of
    /// review each found one more unchecked field — `BaseOfCode` and
    /// `BaseOfData`, the CLI sub-directories, the import shape, the directory
    /// extents — because every gate named what it checked, so a field nobody had
    /// named was checked by nobody. Naming two more (`SizeOfHeaders` and the CLI
    /// extents, the fields review found next) would have bought one more round.
    /// A complement cannot go stale: a field added to this image tomorrow is
    /// covered the moment it exists, because it was never the naming that made
    /// it covered.
    /// </para>
    /// <para>
    /// Earlier versions of this test covered the PE header region and the CLI
    /// header separately, which left the metadata region uncovered — a byte
    /// could be flipped anywhere inside it and no test failed. Splitting the
    /// image into regions to check reintroduces exactly the naming problem, so
    /// there is one region: all of it.
    /// </para>
    /// <para>
    /// Non-vacuity matters as much as the bound. Every documented repair is
    /// asserted to have happened *and* to have the value it should, so an
    /// expansion that quietly stopped repairing anything fails here rather than
    /// passing a complement that has nothing left to exclude.
    /// </para>
    /// </summary>
    [Fact]
    public void HostileImage_DiffersFromItsBaselineOnlyWhereDocumented()
    {
        byte[] baseline = fixture.Baseline;
        byte[] patched = fixture.Bytes;
        const int expansion = OversizedVersionFixture.Expansion;

        PEHeaders headers = Headers(baseline);
        int optionalHeader = headers.PEHeaderStartOffset;
        int sectionTable = optionalHeader + BinaryPrimitives.ReadUInt16LittleEndian(
            baseline.AsSpan(optionalHeader - 20 + 16, 2));
        int metadataRoot = headers.MetadataStartOffset;
        int corHeader = headers.CorHeaderStartOffset;

        int oldVersionLength = ReadRva(baseline, metadataRoot + 12);
        int versionStart = metadataRoot + 16;
        int insertionPoint = versionStart + oldVersionLength;

        Assert.Equal(baseline.Length + expansion, patched.Length);

        // Everything the expansion rewrites in place, all of which happens to
        // precede the insertion point. Each grows by exactly the expansion.
        (string Name, int Offset)[] repairs =
        [
            ("SizeOfCode", optionalHeader + 4),
            (".text VirtualSize", sectionTable + 8),
            (".text SizeOfRawData", sectionTable + 16),
            (".reloc PointerToRawData", sectionTable + 40 + 20),
            ("Cli.MetaData.Size", corHeader + 12),
            ("MetadataRoot.VersionLength", metadataRoot + 12),
            .. StreamHeaderOffsetSites(baseline, insertionPoint)
                .Select((site, i) => ($"Stream[{i}].Offset", site)),
        ];

        // The split above is only sound if the in-place repairs really do precede
        // the payload; a repair inside or after it would be compared against the
        // wrong bytes below.
        foreach ((string name, int offset) in repairs.Where(r => r.Offset < insertionPoint))
        {
            Assert.True(
                offset + 4 <= versionStart,
                $"{name} overlaps the version payload and cannot be compared in place.");
        }

        int[] allowed = [.. repairs.SelectMany(r => Enumerable.Range(r.Offset, 4))];
        List<string> undocumented = [];

        // Before the payload the two images are at the same offsets.
        for (int i = 0; i < versionStart; i++)
        {
            if (baseline[i] != patched[i] && !allowed.Contains(i))
                undocumented.Add($"0x{i:X} (before the payload)");
        }

        // After it, every baseline byte moved forward by the expansion.
        for (int i = insertionPoint; i < baseline.Length; i++)
        {
            if (baseline[i] != patched[i + expansion] && !allowed.Contains(i))
                undocumented.Add($"0x{i:X} (after the payload)");
        }

        Assert.Equal(string.Empty, string.Join(", ", undocumented));

        foreach ((string name, int offset) in repairs)
        {
            Assert.Equal(
                (name, ReadRva(baseline, offset) + expansion),
                (name, ReadRva(patched, offset < insertionPoint ? offset : offset + expansion)));
        }
    }

    /// <summary>
    /// The file offset of each stream header's `Offset` field, located in the
    /// image whose bytes are passed in. The storage header sits immediately after
    /// the version string and gives the stream count; each header is an offset, a
    /// size, and a NUL-terminated name padded to a four-byte boundary.
    /// </summary>
    static int[] StreamHeaderOffsetSites(byte[] image, int storageHeader)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(storageHeader + 2, 2));
        int cursor = storageHeader + 4;
        int[] sites = new int[count];

        for (int i = 0; i < count; i++)
        {
            sites[i] = cursor;

            int name = cursor + 8;
            while (image[name] != 0)
                name++;

            cursor = (name + 1 + 3) & ~3;
        }

        return sites;
    }


    /// <summary>
    /// The certificate directory is the one entry whose value is a file offset
    /// rather than an RVA, so <see cref="HostileImage_BreaksOnlyTheRvasItIsKnownToBreak"/>
    /// would classify it against the wrong space. It is empty in both images;
    /// this pins that, so a fixture that ever gains a signature fails here rather
    /// than being quietly misfiled there.
    /// </summary>
    [Fact]
    public void HostileImage_HasNoCertificateTableToMisclassify()
    {
        Assert.Equal(0, OptionalHeader(fixture.Baseline).CertificateTableDirectory.RelativeVirtualAddress);
        Assert.Equal(0, OptionalHeader(fixture.Bytes).CertificateTableDirectory.RelativeVirtualAddress);
    }

    static int ContainingSectionIndex(PEHeaders headers, int fileOffset)
    {
        for (int i = 0; i < headers.SectionHeaders.Length; i++)
        {
            SectionHeader section = headers.SectionHeaders[i];
            if (fileOffset >= section.PointerToRawData
                && fileOffset < section.PointerToRawData + section.SizeOfRawData)
            {
                return i;
            }
        }

        return -1;
    }

    static int FileOffsetOf(PEHeaders headers, int rva)
    {
        SectionHeader owner = Assert.Single(
            headers.SectionHeaders.Where(s => rva >= s.VirtualAddress
                && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData)));

        return owner.PointerToRawData + (rva - owner.VirtualAddress);
    }

    /// <summary>
    /// The file offset the expansion is inserted at: the end of the baseline's
    /// version field, which is where <see cref="OversizedVersionFixture"/> cuts.
    /// </summary>
    static int InsertionPointOf(byte[] image)
    {
        PEHeaders headers = Headers(image);
        int root = headers.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(root + 12, 4));

        return root + 16 + versionLength;
    }

    static PEHeaders Headers(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        return peReader.PEHeaders;
    }

    static PEHeader OptionalHeader(byte[] image) => Headers(image).PEHeader!;

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

    /// <summary>
    /// A compiler-produced assembly, used to anchor the layout invariants. Those
    /// are anchored on real output because they are claims about PE layout in
    /// general; the header-size assertions are not, because that turned out to be
    /// a claim the sample could not support.
    /// </summary>
    static byte[] SelfImage
        => File.ReadAllBytes(typeof(OversizedMetadataVersionTests).Assembly.Location);

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
    public const int Expansion = 1536;

    /// <summary>
    /// The widest a neutralized stamp may render: `MetadataImageInspector`'s
    /// budget, restated here so a change to it fails this test rather than
    /// silently changing what "truncated" means.
    /// </summary>
    public const int Budget = 255 * 6;

    public OversizedVersionFixture()
    {
        Baseline = BuildBaselineImage();
        Bytes = ExpandMetadataVersion(Baseline, Expansion);
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"mdi-oversized-version-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(Path, Bytes);
    }

    /// <summary>The hostile image.</summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// The conforming image the hostile one was cut from.
    /// <para>
    /// Header assertions compare against this rather than against a general PE
    /// rule. The difference matters: review of this file established that
    /// <c>SizeOfInitializedData</c> is not the sum of the initialized sections'
    /// raw sizes in general — 69 of 2,432 shipped assemblies disagree, all of
    /// them native — so a test asserting that formula would have been asserting
    /// something false and passing only by luck of the sample. What the fixture
    /// can honestly claim is what its own edit did to its own image, and that is
    /// what <see cref="OversizedMetadataVersionTests.HostileImage_ChangesExactlyTheHeaderSizesTheExpansionShould"/>
    /// checks.
    /// </para>
    /// </summary>
    public byte[] Baseline { get; }

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
