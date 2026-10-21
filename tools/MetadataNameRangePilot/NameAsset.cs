using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Unicode;

using InertText;
using InertText.Encoding;

namespace MetadataNameRangePilot;

internal enum NameRole : byte
{
    TypeNamespace,
    TypeName,
    TypeReferenceNamespace,
    TypeReferenceName,
    MethodName,
    ParameterName,
    GenericParameterName,
    PropertyName,
    EventName,
    FieldName,
}

internal readonly record struct NameEntry(
    NameRole Role,
    StringHandle Handle);

internal sealed class NameAsset : IDisposable
{
    readonly FileStream _stream;
    readonly PEReader _image;
    readonly MetadataReader _reader;
    readonly Utf8Heap _names;
    readonly byte[] _query;
    readonly InertString _containedQuery;

    NameAsset(
        string path,
        FileStream stream,
        PEReader image,
        MetadataReader reader,
        ImmutableArray<NameEntry> entries)
    {
        _stream = stream;
        _image = image;
        _reader = reader;
        _names = new(image, reader);
        Entries = entries;
        string version = reader.IsAssembly
            ? reader.GetAssemblyDefinition().Version.ToString()
            : "module";
        Guid mvid = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        Name =
            $"{Path.GetFileName(path)}@{version}+{mvid:N}";

        var occurrences = new Dictionary<string, (int Count, int Index)>(
            StringComparer.Ordinal);
        int utf8Bytes = 0;
        for (int index = 0; index < entries.Length; index++)
        {
            NameEntry entry = entries[index];
            byte[] bytes = _names.Read(entry.Handle);
            utf8Bytes = checked(utf8Bytes + bytes.Length);
            string key = Convert.ToBase64String(bytes);
            occurrences.TryGetValue(
                key,
                out (int Count, int Index) occurrence);
            occurrences[key] = (
                occurrence.Count + 1,
                occurrence.Count == 0 ? index : occurrence.Index);
        }
        Utf8Bytes = utf8Bytes;

        (int Count, int Index) selected = occurrences.Values
            .Where(value =>
                _names.Length(entries[value.Index].Handle) > 0)
            .OrderBy(value => value.Count)
            .ThenBy(value => value.Index)
            .First();
        QueryMatches = selected.Count;
        StringHandle queryHandle = entries[selected.Index].Handle;
        _query = _names.Read(queryHandle);
        _containedQuery = new(
            TextPolicy.Field,
            reader.GetString(queryHandle));
    }

    internal string Name { get; }

    internal ImmutableArray<NameEntry> Entries { get; }

    internal int NameCount => Entries.Length;

    internal int Utf8Bytes { get; }

    internal int QueryMatches { get; }

    internal MetadataReader Reader => _reader;

    internal ReadOnlySpan<byte> Query => _query;

    internal byte[] QueryBytes => _query;

    internal InertString ContainedQuery => _containedQuery;

    internal string ReadString(StringHandle handle) =>
        _reader.GetString(handle);

    internal unsafe OpaqueByteRange CopyRange(StringHandle handle)
    {
        BlobReader reader = _reader.GetBlobReader(handle);
        return OpaqueByteRange.Copy(
            new ReadOnlySpan<byte>(
                reader.StartPointer,
                reader.Length));
    }

    internal unsafe OpaqueByteRangeSlot Append(
        OpaqueByteRangeBuilder builder,
        StringHandle handle)
    {
        BlobReader reader = _reader.GetBlobReader(handle);
        return builder.Append(
            new ReadOnlySpan<byte>(
                reader.StartPointer,
                reader.Length));
    }

    internal static NameAsset Open(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        try
        {
            var image = new PEReader(stream);
            if (!image.HasMetadata)
            {
                throw new BadImageFormatException(
                    $"'{path}' has no CLI metadata.");
            }

            MetadataReader reader = image.GetMetadataReader(
                MetadataReaderOptions.None);
            var entries = ImmutableArray.CreateBuilder<NameEntry>();
            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                TypeDefinition row = reader.GetTypeDefinition(handle);
                entries.Add(new(NameRole.TypeNamespace, row.Namespace));
                entries.Add(new(NameRole.TypeName, row.Name));
            }
            foreach (TypeReferenceHandle handle
                in reader.TypeReferences)
            {
                TypeReference row = reader.GetTypeReference(handle);
                entries.Add(new(
                    NameRole.TypeReferenceNamespace,
                    row.Namespace));
                entries.Add(new(
                    NameRole.TypeReferenceName,
                    row.Name));
            }
            foreach (MethodDefinitionHandle handle
                in reader.MethodDefinitions)
            {
                entries.Add(new(
                    NameRole.MethodName,
                    reader.GetMethodDefinition(handle).Name));
            }
            int parameterRows =
                reader.GetTableRowCount(TableIndex.Param);
            for (int row = 1; row <= parameterRows; row++)
            {
                ParameterHandle handle =
                    MetadataTokens.ParameterHandle(row);
                entries.Add(new(
                    NameRole.ParameterName,
                    reader.GetParameter(handle).Name));
            }
            int genericRows =
                reader.GetTableRowCount(TableIndex.GenericParam);
            for (int row = 1; row <= genericRows; row++)
            {
                GenericParameterHandle handle =
                    MetadataTokens.GenericParameterHandle(row);
                entries.Add(new(
                    NameRole.GenericParameterName,
                    reader.GetGenericParameter(handle).Name));
            }
            foreach (PropertyDefinitionHandle handle
                in reader.PropertyDefinitions)
            {
                entries.Add(new(
                    NameRole.PropertyName,
                    reader.GetPropertyDefinition(handle).Name));
            }
            foreach (EventDefinitionHandle handle
                in reader.EventDefinitions)
            {
                entries.Add(new(
                    NameRole.EventName,
                    reader.GetEventDefinition(handle).Name));
            }
            foreach (FieldDefinitionHandle handle
                in reader.FieldDefinitions)
            {
                entries.Add(new(
                    NameRole.FieldName,
                    reader.GetFieldDefinition(handle).Name));
            }
            if (entries.Count == 0)
                throw new InvalidOperationException("The asset has no names.");

            return new(
                path,
                stream,
                image,
                reader,
                entries.ToImmutable());
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal NameCheck Check()
    {
        ValidateRealAssetNames();
        NameStore eager = NameStore.Build(
            this,
            NameStrategy.EagerInert,
            copies: 1);
        DisplayedName[] expected = eager.ProjectAll();
        ulong displayHash = StableDisplayHash(expected);
        foreach (NameStrategy strategy in Enum.GetValues<NameStrategy>())
        {
            NameStore actual = NameStore.Build(this, strategy, copies: 1);
            DisplayedName[] projected = actual.ProjectAll();
            if (!projected.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidOperationException(
                    $"{strategy} display disagrees with eager containment.");
            }
            if (actual.CountMatches(Query, ContainedQuery)
                != QueryMatches)
            {
                throw new InvalidOperationException(
                    $"{strategy} query count differs from the asset census.");
            }
            if (strategy is not NameStrategy.PopulationOpaqueBytes
                && actual.Budget != eager.Budget)
            {
                throw new InvalidOperationException(
                    $"{strategy} retained-text budget differs from eager containment.");
            }
        }

        NameStore byteBudget = NameStore.Build(
            this,
            NameStrategy.PopulationOpaqueBytes,
            copies: 1);
        if (byteBudget.Budget != Utf8Bytes)
        {
            throw new InvalidOperationException(
                "The byte-count strategy did not charge the exact stored bytes.");
        }

        return new(
            NameCount,
            Utf8Bytes,
            _query.Length,
            QueryMatches,
            displayHash);
    }

    internal static void CheckIllFormedKnownAnswers()
    {
        OpaqueByteRange ff = OpaqueByteRange.Copy([0xFF]);
        OpaqueByteRange fe = OpaqueByteRange.Copy([0xFE]);
        if (ff == fe)
        {
            throw new InvalidOperationException(
                "Distinct ill-formed bytes compared equal.");
        }
        string ffText = new InertUtf8String(
            TextPolicy.Field,
            Utf8RangeClassification.Read(ff)).ToString();
        string feText = new InertUtf8String(
            TextPolicy.Field,
            Utf8RangeClassification.Read(fe)).ToString();
        if (ffText != "\\uDCFF" || feText != "\\uDCFE")
        {
            throw new InvalidOperationException(
                "Ill-formed bytes did not receive their exact contained spellings.");
        }
    }

    void ValidateRealAssetNames()
    {
        foreach (NameEntry entry in Entries)
        {
            byte[] stored = _names.Read(entry.Handle);
            if (!Utf8.IsValid(stored))
            {
                throw new InvalidOperationException(
                    $"{Name} contains an ill-formed {entry.Role} name; "
                    + "the real-asset measurement requires well-formed names.");
            }

            string decoded = Reader.GetString(entry.Handle);
            byte[] roundTrip = Encoding.UTF8.GetBytes(decoded);
            if (!stored.AsSpan().SequenceEqual(roundTrip))
            {
                throw new InvalidOperationException(
                    $"{Name} contains a {entry.Role} name that does not "
                    + "round-trip through UTF-16.");
            }

            if (new InertString(
                    TextPolicy.Field,
                    decoded).WasEncoded)
            {
                throw new InvalidOperationException(
                    $"{Name} contains a {entry.Role} name that requires "
                    + "presentation spelling; use it as a separate scenario.");
            }
        }
    }

    public void Dispose()
    {
        _image.Dispose();
        _stream.Dispose();
    }

    static ulong StableDisplayHash(
        ReadOnlySpan<DisplayedName> values)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offset;
        foreach (DisplayedName value in values)
        {
            hash = (hash ^ (byte)value.Role) * prime;
            string text = value.Value.ToString();
            hash = (hash ^ (uint)text.Length) * prime;
            foreach (char character in text)
                hash = (hash ^ character) * prime;
        }
        return hash;
    }
}

internal sealed class Utf8Heap
{
    BlobReader _heap;

    internal Utf8Heap(PEReader image, MetadataReader reader)
        => _heap = image.GetMetadata().GetReader(
            reader.GetHeapMetadataOffset(HeapIndex.String),
            reader.GetHeapSize(HeapIndex.String));

    internal int Length(StringHandle handle)
    {
        int offset = MetadataTokens.GetHeapOffset(handle);
        if (offset < 0 || offset >= _heap.Length)
        {
            throw new BadImageFormatException(
                "A name lies outside the #Strings heap.");
        }
        _heap.Offset = offset;
        int length = _heap.IndexOf(0);
        return length >= 0
            ? length
            : throw new BadImageFormatException(
                "A #Strings entry is not terminated.");
    }

    internal byte[] Read(StringHandle handle)
    {
        int length = Length(handle);
        var bytes = new byte[length];
        _heap.Offset = MetadataTokens.GetHeapOffset(handle);
        _heap.ReadBytes(length, bytes, 0);
        return bytes;
    }
}

internal readonly record struct NameCheck(
    int NameCount,
    int Utf8Bytes,
    int QueryBytes,
    int QueryMatches,
    ulong DisplayHash);
