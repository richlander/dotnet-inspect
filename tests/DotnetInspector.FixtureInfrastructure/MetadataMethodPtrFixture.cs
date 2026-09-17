using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Fixtures;

public static class MetadataMethodPtrFixture
{
    public static byte[] BuildDuplicate() => Build(1, 1);

    public static byte[] BuildAliased()
    {
        byte[] image = BuildDuplicate();
        WriteMethodListStart(
            image,
            typeDefRow: 1,
            start: 2);
        return image;
    }

    public static byte[] BuildOutOfRange() => Build(1, 99);

    public static byte[] BuildCountMismatch() =>
        Build(1, 2, 1, 2);

    public static byte[] BuildUncovered()
    {
        byte[] image = Build(1, 2);
        WriteMethodListStart(
            image,
            typeDefRow: 0,
            start: 2);
        WriteMethodListStart(
            image,
            typeDefRow: 1,
            start: 2);
        return image;
    }

    public static byte[] BuildDescending()
    {
        byte[] image = Build(2, 1);
        WriteMethodListStart(
            image,
            typeDefRow: 0,
            start: 2);
        return image;
    }

    public static byte[] Build(params ushort[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Length == 0
            || rows.Length % 2 != 0)
        {
            throw new ArgumentException(
                "MethodPtr rows must be a non-empty even-length "
                    + "sequence.",
                nameof(rows));
        }

        var metadata = new MetadataBuilder();
        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle first =
            AddSyntheticMethod(metadata, encoder, "M0");
        AddSyntheticMethod(metadata, encoder, "M1");
        metadata.AddAssembly(
            metadata.GetOrAddString("MethodPtrFixture"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("MethodPtrFixture.dll"),
            metadata.GetOrAddGuid(
                new Guid("3F2A6C18-9D74-4E51-B0C3-6E8A1D2F4B77")),
            encId: default,
            encBaseId: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: first);
        metadata.AddTypeDefinition(
            TypeAttributes.Class | TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: first);

        metadata.GetOrAddUserString("PADPADPADPADPADPAD");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return InsertMethodPtrTable(
            image.ToArray(),
            rows);
    }

    static byte[] InsertMethodPtrTable(
        byte[] image,
        ushort[] rows)
    {
        int growth = 4 + (2 * rows.Length);
        Require(
            growth % 4 == 0,
            "MethodPtr growth must preserve stream alignment.");
        int metadataStart;
        int metadataSize;
        using (var peReader =
            new PEReader(ImmutableArray.Create(image)))
        {
            metadataStart =
                peReader.PEHeaders.MetadataStartOffset;
            metadataSize = peReader.PEHeaders.MetadataSize;
        }

        Require(
            ReadUInt32At(image, metadataStart) == 0x424A5342u,
            "The image lacks a metadata root.");
        int versionLength =
            ReadInt32At(image, metadataStart + 12);
        int cursor =
            metadataStart + 16 + AlignTo4(versionLength) + 2;
        int streamCount = ReadUInt16At(image, cursor);
        cursor += 2;

        var streams = new List<MetadataStreamHeader>();
        for (int i = 0; i < streamCount; i++)
        {
            var header = new MetadataStreamHeader
            {
                HeaderPosition = cursor,
                Offset = ReadInt32At(image, cursor),
                Size = ReadInt32At(image, cursor + 4),
            };
            cursor += 8;
            int nameStart = cursor;
            while (image[cursor] != 0)
            {
                cursor++;
            }

            header.Name =
                System.Text.Encoding.ASCII.GetString(
                    image,
                    nameStart,
                    cursor - nameStart);
            cursor =
                nameStart
                + AlignTo4(cursor - nameStart + 1);
            streams.Add(header);
        }

        MetadataStreamHeader tables =
            streams.Single(stream => stream.Name == "#~");
        MetadataStreamHeader userStrings =
            streams.Single(stream => stream.Name == "#US");
        Require(
            userStrings.Offset > tables.Offset,
            "#US must follow the tables stream.");

        var contents = new Dictionary<string, byte[]>();
        foreach (MetadataStreamHeader stream in streams)
        {
            var body = new byte[stream.Size];
            Array.Copy(
                image,
                metadataStart + stream.Offset,
                body,
                0,
                stream.Size);
            contents[stream.Name] = body;
        }

        contents["#~"] =
            GrowTablesWithMethodPtr(contents["#~"], rows);
        byte[] originalUserStrings = contents["#US"];
        Require(
            originalUserStrings.Length >= growth + 4,
            "#US is too small to reclaim padding from.");
        var trimmed =
            new byte[originalUserStrings.Length - growth];
        Array.Copy(
            originalUserStrings,
            trimmed,
            trimmed.Length);
        contents["#US"] = trimmed;

        List<MetadataStreamHeader> ordered =
            streams.OrderBy(stream => stream.Offset).ToList();
        int next = ordered[0].Offset;
        foreach (MetadataStreamHeader stream in ordered)
        {
            next = AlignTo4(next);
            stream.Offset = next;
            stream.Size = contents[stream.Name].Length;
            next += stream.Size;
        }

        Require(
            next == metadataSize,
            "The patched metadata size changed.");

        byte[] patched = (byte[])image.Clone();
        foreach (MetadataStreamHeader stream in streams)
        {
            WriteInt32At(
                patched,
                stream.HeaderPosition,
                stream.Offset);
            WriteInt32At(
                patched,
                stream.HeaderPosition + 4,
                stream.Size);
        }

        patched[tables.HeaderPosition + 9] = (byte)'-';
        foreach (MetadataStreamHeader stream in ordered)
        {
            byte[] body = contents[stream.Name];
            Array.Copy(
                body,
                0,
                patched,
                metadataStart + stream.Offset,
                body.Length);
        }

        return patched;
    }

    static byte[] GrowTablesWithMethodPtr(
        byte[] tables,
        ushort[] rows)
    {
        Require(
            (tables[6] & 0x07) == 0,
            "The fixture requires two-byte heap indexes.");
        ulong valid = ReadUInt64At(tables, 8);
        Require(
            (valid & (1UL << (int)TableIndex.MethodPtr))
                == 0,
            "The source image already has MethodPtr rows.");

        var present = new List<int>();
        for (int table = 0; table < 64; table++)
        {
            if ((valid & (1UL << table)) != 0)
            {
                present.Add(table);
            }
        }

        var counts = new Dictionary<int, int>();
        for (int i = 0; i < present.Count; i++)
        {
            counts[present[i]] =
                ReadInt32At(tables, 24 + (4 * i));
        }

        foreach (KeyValuePair<int, int> entry in counts)
        {
            Require(
                entry.Value < 0x10000,
                "The fixture requires two-byte table indexes.");
        }

        int insertAt =
            present.Count(
                table => table < (int)TableIndex.MethodPtr);
        int methodDefStart = 0;
        foreach (int table in present)
        {
            if (table >= (int)TableIndex.MethodDef)
            {
                break;
            }

            methodDefStart +=
                RowSizeBeforeMethodDef(table, counts)
                * counts[table];
        }

        var grown =
            new byte[tables.Length + 4 + (2 * rows.Length)];
        Array.Copy(tables, 0, grown, 0, 24);
        WriteUInt64At(
            grown,
            8,
            valid | (1UL << (int)TableIndex.MethodPtr));

        int write = 24;
        for (int i = 0; i < present.Count; i++)
        {
            if (i == insertAt)
            {
                WriteInt32At(grown, write, rows.Length);
                write += 4;
            }

            WriteInt32At(
                grown,
                write,
                ReadInt32At(tables, 24 + (4 * i)));
            write += 4;
        }

        if (insertAt == present.Count)
        {
            WriteInt32At(grown, write, rows.Length);
            write += 4;
        }

        int source = 24 + (4 * present.Count);
        Array.Copy(
            tables,
            source,
            grown,
            write,
            methodDefStart);
        int inserted = write + methodDefStart;
        for (int i = 0; i < rows.Length; i++)
        {
            WriteUInt16At(
                grown,
                inserted + (2 * i),
                rows[i]);
        }

        Array.Copy(
            tables,
            source + methodDefStart,
            grown,
            inserted + (2 * rows.Length),
            tables.Length - source - methodDefStart);
        return grown;
    }

    static int RowSizeBeforeMethodDef(
        int table,
        Dictionary<int, int> counts)
    {
        const int HeapIndex = 2;
        const int TableIndexSize = 2;
        const int CodedTypeDefOrRef = 2;
        foreach (TableIndex related in (TableIndex[])
            [
                TableIndex.TypeDef,
                TableIndex.TypeRef,
                TableIndex.TypeSpec,
            ])
        {
            Require(
                !counts.TryGetValue(
                    (int)related,
                    out int rows)
                    || rows < 1 << 14,
                "The fixture requires a two-byte "
                    + "TypeDefOrRef index.");
        }

        return table switch
        {
            (int)TableIndex.Module =>
                2
                + HeapIndex
                + HeapIndex
                + HeapIndex
                + HeapIndex,
            (int)TableIndex.TypeDef =>
                4
                + HeapIndex
                + HeapIndex
                + CodedTypeDefOrRef
                + TableIndexSize
                + TableIndexSize,
            (int)TableIndex.Field =>
                2 + HeapIndex + HeapIndex,
            _ => throw new InvalidOperationException(
                $"Unexpected table 0x{table:X2} before "
                    + "MethodDef."),
        };
    }

    static MethodDefinitionHandle AddSyntheticMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodies,
        string name)
    {
        var code = new BlobBuilder();
        code.WriteByte(0x2A);
        int body = bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 0);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            body,
            MetadataTokens.ParameterHandle(1));
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    static int AlignTo4(int value) => (value + 3) & ~3;

    static void WriteMethodListStart(
        byte[] image,
        int typeDefRow,
        ushort start)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        int offset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.TypeDef);
        int rowSize =
            reader.GetTableRowSize(TableIndex.TypeDef);
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(
                offset
                + (typeDefRow * rowSize)
                + rowSize
                - sizeof(ushort),
                sizeof(ushort)),
            start);
    }

    static ushort ReadUInt16At(
        byte[] buffer,
        int position) =>
        (ushort)(
            buffer[position]
            | (buffer[position + 1] << 8));

    static uint ReadUInt32At(
        byte[] buffer,
        int position) =>
        (uint)(
            buffer[position]
            | (buffer[position + 1] << 8)
            | (buffer[position + 2] << 16)
            | (buffer[position + 3] << 24));

    static int ReadInt32At(
        byte[] buffer,
        int position) =>
        (int)ReadUInt32At(buffer, position);

    static ulong ReadUInt64At(
        byte[] buffer,
        int position) =>
        ReadUInt32At(buffer, position)
        | ((ulong)ReadUInt32At(buffer, position + 4)
            << 32);

    static void WriteUInt16At(
        byte[] buffer,
        int position,
        ushort value)
    {
        buffer[position] = (byte)value;
        buffer[position + 1] = (byte)(value >> 8);
    }

    static void WriteInt32At(
        byte[] buffer,
        int position,
        int value)
    {
        buffer[position] = (byte)value;
        buffer[position + 1] = (byte)(value >> 8);
        buffer[position + 2] = (byte)(value >> 16);
        buffer[position + 3] = (byte)(value >> 24);
    }

    static void WriteUInt64At(
        byte[] buffer,
        int position,
        ulong value)
    {
        WriteInt32At(buffer, position, (int)value);
        WriteInt32At(
            buffer,
            position + 4,
            (int)(value >> 32));
    }

    sealed class MetadataStreamHeader
    {
        internal int HeaderPosition { get; init; }

        internal int Offset { get; set; }

        internal int Size { get; set; }

        internal string Name { get; set; } = "";
    }
}
