using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

using InertText;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The shared Symbol Key's gates (<c>docs/design/symbol-key.md#evidence-and-gates</c>),
/// PR-fast lane: injectivity, name sources, lone surrogates, domain separation,
/// kind totality, shared backing, name boundary, name matching, and name
/// printing. The persisted profile and interning are later steps.
/// </summary>
public sealed class SymbolKeyTests
{
    enum TestPart : byte
    {
        Type = SymbolKeyDomain.FirstMarker,
        Method,
        Namespace,
        Return,
    }

    enum OtherPart : byte
    {
        Type = SymbolKeyDomain.FirstMarker,
    }

    enum LowPart : byte
    {
        Type = 0x05,
    }

    enum WidePart : int
    {
        Type = 0x10,
    }

    static StagedSymbolKey<TestPart> Build(SymbolKeyBuilder<TestPart> keys, Action<SymbolKeyBuilder<TestPart>> parts)
    {
        int mark = keys.Begin();
        keys.Mark(TestPart.Type);
        parts(keys);
        return keys.End(mark);
    }

    [Fact]
    public void Injectivity_DistinctPartSequencesAreDistinctBytes()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart>[] distinct =
        [
            Build(keys, k => k.Name("A/B"u8)),
            Build(keys, k => k.Name("A"u8).Name("B"u8)),
            Build(keys, k => k.Mark(TestPart.Namespace).Name("N"u8).Name("A.B"u8)),
            Build(keys, k => k.Mark(TestPart.Namespace).Name("N.A"u8).Name("B"u8)),
            Build(keys, k => k.Name(""u8)),
            Build(keys, k => k.Name(""u8).Name(""u8)),
            Build(keys, k => k.Name([0xFF, 0x41])),
            Build(keys, k => k.Name([0xC3, 0x28])),
            Build(keys, k => k.Int(1)),
            Build(keys, k => k.Int(-1)),
            Build(keys, k => k.Int(0)),
            Build(keys, k => k.Int(long.MaxValue)),
            Build(keys, k => k.Int(long.MinValue)),
            Build(keys, k => k.Blob([0x01])),
            Build(keys, k => k.Name([0x01])),
            Build(keys, k => k.Mark(TestPart.Return)),
        ];
        StagedSymbolKey<TestPart> nested = Build(keys, k => k.Key(distinct[0]));
        StagedSymbolKey<TestPart> nestedTwice = Build(keys, k => k.Key(distinct[0]).Key(distinct[0]));
        StagedSymbolKey<TestPart>[] all = [.. distinct, nested, nestedTwice];

        for (int i = 0; i < all.Length; i++)
        {
            for (int j = 0; j < all.Length; j++)
                Assert.Equal(i == j, all[i].Equals(all[j]));
        }
        Assert.Equal(all.Length, all.Select(key => key.GetHashCode()).Distinct().Count());

        // The same parts built twice are one key, in staged and in frozen form.
        StagedSymbolKey<TestPart> again = Build(keys, k => k.Name("A/B"u8));
        Assert.Equal(distinct[0], again);
        SymbolKeyPopulation<TestPart> population = keys.Freeze();
        Assert.Equal(population.Resolve(distinct[0]), population.Resolve(again));
        Assert.NotEqual(population.Resolve(distinct[0]), population.Resolve(distinct[1]));
    }

    [Fact]
    public void NameSources_StringsHeapUtf8AndUtf16_WriteOnePart()
    {
        using var pe = new PEReader(File.OpenRead(typeof(object).Assembly.Location));
        MetadataReader md = pe.GetMetadataReader();
        var keys = new SymbolKeyBuilder<TestPart> { Names = new MetadataStringHeap(md) };
        int compared = 0;
        foreach (StringHandle handle in md.TypeDefinitions
            .SelectMany(type => (StringHandle[])[md.GetTypeDefinition(type).Namespace, md.GetTypeDefinition(type).Name])
            .Concat(md.MethodDefinitions.Select(method => md.GetMethodDefinition(method).Name))
            .Concat(md.FieldDefinitions.Select(field => md.GetFieldDefinition(field).Name)))
        {
            string text = md.GetString(handle);
            StagedSymbolKey<TestPart> fromHeap = Build(keys, k => k.Name(handle));
            StagedSymbolKey<TestPart> fromUtf8 = Build(keys, k => k.Name(Encoding.UTF8.GetBytes(text)));
            StagedSymbolKey<TestPart> fromUtf16 = Build(keys, k => k.Name(text.AsSpan()));
            Assert.Equal(fromHeap, fromUtf8);
            Assert.Equal(fromHeap, fromUtf16);
            compared++;
            if (compared % 64 == 0)
                keys.Reset();
        }
        Assert.True(compared > 10_000);
    }

    [Fact]
    public void LoneSurrogate_IsRefused()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        keys.Begin();
        keys.Mark(TestPart.Type);
        Assert.Throws<ArgumentException>(() => keys.Name("a\uD800b".AsSpan()));
        // A well-formed pair is one name.
        keys.Abandon();
        StagedSymbolKey<TestPart> pair = Build(keys, k => k.Name("\U0001F600".AsSpan()));
        Assert.Equal(Build(keys, k => k.Name(Encoding.UTF8.GetBytes("\U0001F600"))), pair);
    }

    [Fact]
    public void Domains_AreSeparateTypes_AndInvalidDomainsAreRejectedAtFirstUse()
    {
        // Keys of two domains with equal bytes are different types; equality does not compile across them,
        // and a builder is typed by its domain.
        var test = new SymbolKeyBuilder<TestPart>();
        var other = new SymbolKeyBuilder<OtherPart>();
        int mark = test.Begin();
        StagedSymbolKey<TestPart> a = test.Mark(TestPart.Type).Int(1).End(mark);
        mark = other.Begin();
        StagedSymbolKey<OtherPart> b = other.Mark(OtherPart.Type).Int(1).End(mark);
        Assert.Equal(a.Length, b.Length);
        Assert.False(a.Equals((object)b));

        Assert.Throws<InvalidOperationException>(() => new SymbolKeyBuilder<LowPart>());
        Assert.Throws<InvalidOperationException>(() => new SymbolKeyBuilder<WidePart>());
    }

    [Fact]
    public void KindIsTotal_AKeyBeginsWithADomainMarker()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        int mark = keys.Begin();
        Assert.Throws<SymbolKeyStateException>(() => keys.End(mark));
        keys.Int(1);
        Assert.Throws<SymbolKeyStateException>(() => keys.End(mark));
        keys.Abandon();

        StagedSymbolKey<TestPart> method = Build(keys, k => k.Mark(TestPart.Method).Int(2));
        Assert.Equal(TestPart.Type, method.Kind);
        Assert.Equal(TestPart.Type, keys.Freeze().Resolve(method).Kind);
    }

    [Fact]
    public void OneKeyIsOpenAtATime()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        keys.Begin();
        Assert.Throws<SymbolKeyStateException>(() => keys.Begin());
        Assert.Throws<SymbolKeyStateException>(() => keys.End(1));
        Assert.Throws<SymbolKeyStateException>(keys.Freeze);
        keys.Abandon();
        Assert.False(keys.IsOpen);
        Assert.Throws<SymbolKeyStateException>(() => keys.Int(1));
    }

    [Fact]
    public void SharedBacking_EveryKeyOfAPopulationRetainsOneBacking_AndResolutionAllocatesNothing()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        var staged = new List<StagedSymbolKey<TestPart>>();
        for (int i = 0; i < 1_000; i++)
            staged.Add(Build(keys, k => k.Int(i).Name("name"u8)));
        StagedSymbolKey<TestPart> embedded = Build(keys, k => k.Key(staged[0]).Key(staged[999]));
        int stagedBytes = keys.Length;
        SymbolKeyPopulation<TestPart> population = keys.Freeze();
        Assert.Equal(stagedBytes, population.Length);
        Assert.Equal(0, keys.Length);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var resolved = new SymbolKey<TestPart>[staged.Count];
        for (int i = 0; i < staged.Count; i++)
            resolved[i] = population.Resolve(staged[i]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // The array of views is the only allocation; no backing, name, or nested key is copied.
        Assert.True(allocated <= 64L + staged.Count * 24L, $"resolving allocated {allocated} bytes");

        Assert.Equal(staged.Sum(key => key.Length) + embedded.Length, population.Length);
        SymbolKey<TestPart> frozenEmbedded = population.Resolve(embedded);
        SymbolKeyPartReader<TestPart> parts = frozenEmbedded.Parts;
        Assert.True(parts.Read() && parts.Kind == SymbolKeyPartKind.Marker);
        Assert.True(parts.Read() && parts.Kind == SymbolKeyPartKind.Key);
        Assert.Equal(resolved[0], parts.Key);
        Assert.True(parts.Read() && parts.Kind == SymbolKeyPartKind.Key);
        Assert.Equal(resolved[999], parts.Key);
        Assert.False(parts.Read());

        // A staged key of a frozen population no longer reads; it resolves.
        Assert.Throws<SymbolKeyStateException>(() => staged[0].Kind);
        Assert.Throws<SymbolKeyStateException>(() => keys.Freeze().Resolve(staged[0]));
    }

    [Fact]
    public void StagedAndFrozenKeys_CompareAndHashAlike_AcrossBuilders()
    {
        var a = new SymbolKeyBuilder<TestPart>();
        var b = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart> stagedA = Build(a, k => k.Name("x"u8).Int(7));
        StagedSymbolKey<TestPart> stagedB = Build(b, k => k.Name("x"u8).Int(7));
        StagedSymbolKey<TestPart> otherB = Build(b, k => k.Name("x"u8).Int(8));
        Assert.Equal(stagedA, stagedB);
        Assert.NotEqual(stagedA, otherB);

        SymbolKey<TestPart> frozenA = a.Freeze().Resolve(stagedA);
        Assert.True(frozenA.Equals(stagedB));
        Assert.True(stagedB.Equals(frozenA));
        Assert.Equal(frozenA.GetHashCode(), stagedB.GetHashCode());
        Assert.Equal(0, frozenA.CompareTo(b.Freeze().Resolve(stagedB)));

        var index = new Dictionary<SymbolKey<TestPart>, int>(SymbolKeyComparer<TestPart>.Instance) { [frozenA] = 1 };
        StagedSymbolKey<TestPart> probe = Build(b, k => k.Name("x"u8).Int(7));
        Assert.True(index.GetAlternateLookup<StagedSymbolKey<TestPart>>().TryGetValue(probe, out int value));
        Assert.Equal(1, value);
        Assert.False(index.GetAlternateLookup<StagedSymbolKey<TestPart>>().ContainsKey(Build(b, k => k.Int(7))));
    }

    [Fact]
    public void Slots_SortCompareAndResolveAsTheirStagedKeys_AndASlotPastTheStagedLengthIsRefused()
    {
        var a = new SymbolKeyBuilder<TestPart>();
        var b = new SymbolKeyBuilder<TestPart>();
        List<StagedSymbolKey<TestPart>> keysA =
        [
            Build(a, k => k.Name("zeta"u8).Int(1)),
            Build(a, k => k.Name("alpha"u8).Int(2)),
            Build(a, k => k.Name("alpha"u8)),
            Build(a, k => k.Int(-3)),
        ];
        List<SymbolKeySlot> slotsA = keysA.ConvertAll(key => key.Slot);
        Assert.All(slotsA, slot => Assert.Equal(TestPart.Type, a.Kind(slot)));
        Assert.Equal(keysA, slotsA.ConvertAll(a.Resolve));

        a.Sort(slotsA);
        keysA.Sort();
        Assert.Equal(keysA, slotsA.ConvertAll(a.Resolve));

        // Slots of two populations compare by their keys' bytes, in order.
        List<SymbolKeySlot> nestedB = keysA.ConvertAll(key => Build(b, k => k.Key(key)).Slot);
        Assert.False(a.SequenceEqual(CollectionsMarshal.AsSpan(slotsA), b, CollectionsMarshal.AsSpan(nestedB)));
        List<SymbolKeySlot> copiesB = keysA.ConvertAll(key => b.Copy(key).Slot);
        Assert.True(a.SequenceEqual(CollectionsMarshal.AsSpan(slotsA), b, CollectionsMarshal.AsSpan(copiesB)));
        Assert.False(a.SequenceEqual(CollectionsMarshal.AsSpan(slotsA), b, CollectionsMarshal.AsSpan(copiesB)[..^1]));

        SymbolKeySlot last = slotsA[^1];
        a.Reset();
        Assert.Throws<SymbolKeyStateException>(() => a.Resolve(last));
        Assert.Throws<SymbolKeyStateException>(() => a.Kind(last));
        Assert.Throws<SymbolKeyStateException>(() => a.Sort([last]));
    }

    [Fact]
    public void StringsHeap_RefusesAnOffsetOutsideTheHeap_AndAnUnterminatedEntry()
    {
        byte[] image = BuildNamedImage();
        using (var pe = new PEReader(new MemoryStream(image)))
        {
            MetadataReader md = pe.GetMetadataReader(MetadataReaderOptions.None);
            var names = new MetadataStringHeap(md);
            int heapSize = md.GetHeapSize(HeapIndex.String);
            Assert.Throws<BadImageFormatException>(() => names.Length(MetadataTokens.StringHandle(heapSize)));
            Assert.Throws<BadImageFormatException>(() => names.Length(MetadataTokens.StringHandle(heapSize + 1)));
            Assert.Equal("Named"u8.Length, names.Length(md.GetTypeDefinition(md.TypeDefinitions.Last()).Name));
        }

        // Overwrite from the Type name's terminator through the end of the raw
        // #Strings stream, padding included: the reader trims a heap to one
        // trailing terminator, so only a stream whose last byte is not zero
        // leaves an entry unterminated.
        byte[] patched = (byte[])image.Clone();
        using (var pe = new PEReader(new MemoryStream(patched)))
        {
            MetadataReader md = pe.GetMetadataReader(MetadataReaderOptions.None);
            (int streamOffset, int streamSize) = StringsStream(patched, pe.PEHeaders.MetadataStartOffset);
            Assert.Equal(md.GetHeapMetadataOffset(HeapIndex.String), streamOffset);
            int heapStart = pe.PEHeaders.MetadataStartOffset + streamOffset;
            StringHandle last = md.GetTypeDefinition(md.TypeDefinitions.Last()).Name;
            int terminator = MetadataTokens.GetHeapOffset(last) + "Named"u8.Length;
            Assert.Equal(0, patched[heapStart + terminator]);
            patched.AsSpan(heapStart + terminator, streamSize - terminator).Fill((byte)'x');
        }
        using (var pe = new PEReader(new MemoryStream(patched)))
        {
            MetadataReader md = pe.GetMetadataReader(MetadataReaderOptions.None);
            var names = new MetadataStringHeap(md);
            StringHandle last = md.GetTypeDefinition(md.TypeDefinitions.Last()).Name;
            Assert.Throws<BadImageFormatException>(() => names.Length(last));
            var keys = new SymbolKeyBuilder<TestPart> { Names = names };
            keys.Begin();
            Assert.Throws<BadImageFormatException>(() => keys.Mark(TestPart.Type).Name(last));
        }
    }

    // The #Strings stream header: offset from the metadata root, and raw size.
    static (int Offset, int Size) StringsStream(byte[] image, int metadataStart)
    {
        ReadOnlySpan<byte> root = image.AsSpan(metadataStart);
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(root[12..]);
        int position = 16 + versionLength;
        int streams = BinaryPrimitives.ReadUInt16LittleEndian(root[(position + 2)..]);
        position += 4;
        for (int i = 0; i < streams; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(root[position..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(root[(position + 4)..]);
            int nameStart = position + 8;
            int nameLength = root[nameStart..].IndexOf((byte)0);
            if (root.Slice(nameStart, nameLength).SequenceEqual("#Strings"u8))
                return (offset, size);
            position = nameStart + ((nameLength + 1 + 3) & ~3);
        }
        throw new InvalidOperationException("The image has no #Strings stream.");
    }

    // One module with a Type named "Named".
    static byte[] BuildNamedImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Named.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Named.dll"), new Version(1, 0), default, default, default, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, default,
            metadata.GetOrAddString("Named"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        return image.ToArray();
    }

    enum SharedPart : byte
    {
        Type = SymbolKeyDomain.FirstMarker,
        Other = 0x7F,
    }

    [Fact]
    public void MarkShared_WritesADeclaredSharedMarker_AndRefusesAnUndeclaredOne()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        int mark = keys.Begin();
        StagedSymbolKey<TestPart> shared = keys.MarkShared(SharedPart.Type).Int(1).End(mark);
        StagedSymbolKey<TestPart> own = Build(keys, k => k.Int(1));
        Assert.Equal(own, shared);
        Assert.Equal(TestPart.Type, shared.Kind);

        keys.Begin();
        Assert.Throws<SymbolKeyStateException>(() => keys.MarkShared(SharedPart.Other));
        Assert.Throws<SymbolKeyStateException>(() => keys.MarkShared(WidePart.Type));
    }

    [Fact]
    public void NameBoundary_ThePartReaderReturnsExactlyTheName()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart> staged = Build(keys, k => k.Int(3).Name("Enumerable"u8).Name(""u8).Blob([0xAA, 0xBB]));
        SymbolKey<TestPart> key = keys.Freeze().Resolve(staged);

        SymbolKeyPartReader<TestPart> parts = key.Parts;
        Assert.True(parts.Read());
        Assert.Equal(SymbolKeyPartKind.Marker, parts.Kind);
        Assert.Equal(TestPart.Type, parts.Marker);
        Assert.True(parts.Read());
        Assert.Equal(SymbolKeyPartKind.Int, parts.Kind);
        Assert.Equal(3, parts.Int);
        Assert.True(parts.Read());
        Assert.Equal(SymbolKeyPartKind.Name, parts.Kind);
        SymbolKeyName name = parts.Name;
        Assert.Equal(10, name.Length);
        Assert.True(name.ValueTextEquals("Enumerable"u8));
        Assert.False(name.ValueTextEquals("Enumerabl"u8));
        Assert.False(name.ValueTextEquals("Enumerable\0"u8));
        Assert.True(parts.Read());
        Assert.Equal(0, parts.Name.Length);
        Assert.True(parts.Read());
        Assert.Equal(SymbolKeyPartKind.Blob, parts.Kind);
        Assert.Equal(2, parts.Blob.Length);
        Assert.Equal(0xAA, parts.Blob.ByteAt(0));
        Assert.False(parts.Read());
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0x42 })]
    [InlineData(new byte[] { 0xC3, 0xA9 })]
    [InlineData(new byte[] { 0xE2, 0x80, 0xAE })]
    [InlineData(new byte[] { 0xFF, 0x41 })]
    public void NameMatching_IsByteExact_WithoutDecodingOrContainment(byte[] stored)
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart> staged = Build(keys, k => k.Name(stored));
        SymbolKey<TestPart> key = keys.Freeze().Resolve(staged);
        SymbolKeyPartReader<TestPart> parts = key.Parts;
        Assert.True(parts.Read() && parts.Read());
        SymbolKeyName name = parts.Name;
        byte[] longer = [.. stored, 0x00];

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool equal = name.ValueTextEquals(stored);
        bool readerEqual = name.Read().ValueTextEquals(stored);
        bool different = name.ValueTextEquals(longer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(equal);
        Assert.True(readerEqual);
        Assert.False(different);
    }

    [Fact]
    public void NamePrinting_ContainsOnlyWhenPresentationIsRequested()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart> staged = Build(keys, k => k.Name([0x61, 0xE2, 0x80, 0xAE, 0x62]));
        SymbolKey<TestPart> key = keys.Freeze().Resolve(staged);
        SymbolKeyPartReader<TestPart> parts = key.Parts;
        Assert.True(parts.Read() && parts.Read());
        SymbolKeyName name = parts.Name;

        // Matching stays byte-exact without containment.
        Assert.True(name.ValueTextEquals([0x61, 0xE2, 0x80, 0xAE, 0x62]));

        // Presentation constructs the contained value, which spells the format character.
        var contained = new InertUtf8String(TextPolicy.Field, name.Read());
        InertString text = contained.ToInertString();
        Assert.Equal("a\\u202Eb", text.ToString());
        Assert.NotEqual(TextConcern.None, contained.Concerns);
        Assert.Contains("\"a\\u202Eb\"", SymbolKeyText.Describe(key));
    }

    [Fact]
    public void Describe_RendersMarkersIntegersNamesKeysAndBlobs()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart> inner = Build(keys, k => k.Mark(TestPart.Namespace).Name("System"u8).Name("Object"u8));
        StagedSymbolKey<TestPart> outer = Build(keys, k => k.Mark(TestPart.Method).Key(inner).Name("ToString"u8).Int(0).Blob([0x20, 0x00]));
        Assert.Equal(
            "Type Method (Type Namespace \"System\" \"Object\") \"ToString\" 0 0x2000",
            SymbolKeyText.Describe(outer));
        Assert.Equal(SymbolKeyText.Describe(outer), SymbolKeyText.Describe(keys.Freeze().Resolve(outer)));
    }

    [Fact]
    public void Detach_RetainsOneKeyAlone()
    {
        var keys = new SymbolKeyBuilder<TestPart>();
        StagedSymbolKey<TestPart> staged = Build(keys, k => k.Name("alone"u8));
        SymbolKey<TestPart> detached = staged.Detach();
        keys.Reset();
        Assert.Equal(TestPart.Type, detached.Kind);
        Assert.Throws<SymbolKeyStateException>(() => staged.Kind);
        Assert.Equal(detached, Build(keys, k => k.Name("alone"u8)).Detach());
    }
}
