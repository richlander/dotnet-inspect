using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SymbolKeyRangePilot;

internal enum KeyRole : byte
{
    TypeDefinition,
    TypeReference,
    Method,
    Property,
    Event,
    Field,
}

internal readonly record struct KeyEntry(
    KeyRole Role,
    int ParentIndex,
    StringHandle Namespace,
    StringHandle Name,
    BlobHandle Signature);

internal sealed class NameAsset : IDisposable
{
    readonly FileStream _stream;
    readonly PEReader _image;
    readonly MetadataReader _reader;
    readonly Utf8Heap _names;
    readonly byte[] _query;

    NameAsset(
        string path,
        FileStream stream,
        PEReader image,
        MetadataReader reader,
        ImmutableArray<KeyEntry> entries)
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
        int encodedKeyBytes = 0;
        var encodedLengths = new int[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            KeyEntry entry = entries[index];
            int length = 1;
            if (entry.ParentIndex >= 0)
            {
                if ((uint)entry.ParentIndex >= (uint)index)
                {
                    throw new InvalidOperationException(
                        "A pilot key has no earlier parent Type key.");
                }
                length = checked(
                    length
                    + FramedLength(
                        encodedLengths[entry.ParentIndex]));
            }
            else if (entry.Role is KeyRole.TypeDefinition
                or KeyRole.TypeReference)
            {
                if (!entry.Namespace.IsNil)
                {
                    length = checked(
                        length
                        + 1
                        + FramedLength(
                            _names.Length(entry.Namespace)));
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "A pilot member key has no parent Type key.");
            }

            length = checked(
                length
                + FramedLength(_names.Length(entry.Name)));
            if (!entry.Signature.IsNil)
            {
                length = checked(
                    length
                    + FramedLength(
                        reader.GetBlobReader(entry.Signature)
                            .RemainingBytes));
            }
            encodedLengths[index] = length;
            encodedKeyBytes = checked(encodedKeyBytes + length);
        }
        EncodedKeyBytes = encodedKeyBytes;
        KeyEntry query = entries.First(
            entry => _names.Length(entry.Name) > 0);
        _query = _names.Read(query.Name);
    }

    internal string Name { get; }

    internal ImmutableArray<KeyEntry> Entries { get; }

    internal int KeyCount => Entries.Length;

    internal int EncodedKeyBytes { get; }

    internal PEReader Image => _image;

    internal MetadataReader Reader => _reader;

    internal Utf8Heap Names => _names;

    internal ReadOnlySpan<byte> Query => _query;

    internal byte[] QueryBytes => _query;

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
            var entries = ImmutableArray.CreateBuilder<KeyEntry>();
            var typeIndices =
                new Dictionary<TypeDefinitionHandle, int>();
            var pendingTypeDefinitions =
                new HashSet<TypeDefinitionHandle>();

            int AddTypeDefinition(TypeDefinitionHandle handle)
            {
                if (typeIndices.TryGetValue(handle, out int existing))
                    return existing;
                if (!pendingTypeDefinitions.Add(handle))
                {
                    throw new BadImageFormatException(
                        "A Type definition has a cyclic declaring chain.");
                }

                TypeDefinition row = reader.GetTypeDefinition(handle);
                TypeDefinitionHandle declaring = row.GetDeclaringType();
                int parentIndex = declaring.IsNil
                    ? -1
                    : AddTypeDefinition(declaring);
                int index = entries.Count;
                entries.Add(new(
                    KeyRole.TypeDefinition,
                    parentIndex,
                    parentIndex < 0 ? row.Namespace : default,
                    row.Name,
                    Signature: default));
                typeIndices.Add(handle, index);
                pendingTypeDefinitions.Remove(handle);
                return index;
            }

            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                _ = AddTypeDefinition(handle);
            }

            var typeReferenceIndices =
                new Dictionary<TypeReferenceHandle, int>();
            var pendingTypeReferences =
                new HashSet<TypeReferenceHandle>();

            int AddTypeReference(TypeReferenceHandle handle)
            {
                if (typeReferenceIndices.TryGetValue(
                        handle,
                        out int existing))
                {
                    return existing;
                }
                if (!pendingTypeReferences.Add(handle))
                {
                    throw new BadImageFormatException(
                        "A Type reference has a cyclic declaring chain.");
                }

                TypeReference row = reader.GetTypeReference(handle);
                EntityHandle scope = row.ResolutionScope;
                int parentIndex = scope.Kind is HandleKind.TypeReference
                    ? AddTypeReference((TypeReferenceHandle)scope)
                    : -1;
                int index = entries.Count;
                entries.Add(new(
                    KeyRole.TypeReference,
                    parentIndex,
                    parentIndex < 0 ? row.Namespace : default,
                    row.Name,
                    Signature: default));
                typeReferenceIndices.Add(handle, index);
                pendingTypeReferences.Remove(handle);
                return index;
            }

            foreach (TypeReferenceHandle handle
                in reader.TypeReferences)
            {
                _ = AddTypeReference(handle);
            }
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                int parentIndex = typeIndices[typeHandle];
                foreach (MethodDefinitionHandle handle
                    in type.GetMethods())
                {
                    MethodDefinition row =
                        reader.GetMethodDefinition(handle);
                    entries.Add(new(
                        KeyRole.Method,
                        parentIndex,
                        Namespace: default,
                        row.Name,
                        row.Signature));
                }
                foreach (PropertyDefinitionHandle handle
                    in type.GetProperties())
                {
                    PropertyDefinition row =
                        reader.GetPropertyDefinition(handle);
                    entries.Add(new(
                        KeyRole.Property,
                        parentIndex,
                        Namespace: default,
                        row.Name,
                        row.Signature));
                }
                foreach (EventDefinitionHandle handle
                    in type.GetEvents())
                {
                    EventDefinition row =
                        reader.GetEventDefinition(handle);
                    entries.Add(new(
                        KeyRole.Event,
                        parentIndex,
                        Namespace: default,
                        row.Name,
                        Signature: default));
                }
                foreach (FieldDefinitionHandle handle
                    in type.GetFields())
                {
                    FieldDefinition row =
                        reader.GetFieldDefinition(handle);
                    entries.Add(new(
                        KeyRole.Field,
                        parentIndex,
                        Namespace: default,
                        row.Name,
                        row.Signature));
                }
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

    internal KeyCheck Check()
    {
        KeyStore current = KeyStore.Build(
            this,
            KeyStrategy.PrivatePerKey,
            copies: 1);
        KeyStore candidate = KeyStore.Build(
            this,
            KeyStrategy.OpaqueRangePopulation,
            copies: 1);
        if (!current.HasSameBytes(candidate))
        {
            throw new InvalidOperationException(
                "The opaque-range keys differ from current Symbol Key.");
        }
        current.EnsureUniqueTypeDefinitions(Entries);
        if (!current.Matches(KeyStore.Build(
                this,
                KeyStrategy.PrivatePerKey,
                copies: 1)))
        {
            throw new InvalidOperationException(
                "Current Symbol Key cannot match an equal population.");
        }
        if (!candidate.Matches(KeyStore.Build(
                this,
                KeyStrategy.OpaqueRangePopulation,
                copies: 1)))
        {
            throw new InvalidOperationException(
                "Opaque ranges cannot match an equal population.");
        }

        int currentHash = current.KeyHash();
        int candidateHash = candidate.KeyHash();
        if (candidateHash != currentHash)
        {
            throw new InvalidOperationException(
                "Opaque-range hashing differs from current Symbol Key.");
        }

        int currentMatches = current.ValueTextEquals(Query);
        int candidateMatches = candidate.ValueTextEquals(Query);
        if (candidateMatches != currentMatches)
        {
            throw new InvalidOperationException(
                "ValueTextEquals differs from current Symbol Key parsing.");
        }

        return new(
            current.Count,
            current.EncodedBytes,
            _query.Length,
            currentHash,
            currentMatches);
    }

    public void Dispose()
    {
        _image.Dispose();
        _stream.Dispose();
    }

    static int VarintLength(uint value)
    {
        int length = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            length++;
        }
        return length;
    }

    static int FramedLength(int payloadLength)
        => checked(
            1
            + VarintLength((uint)payloadLength)
            + payloadLength);
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
        Copy(handle, bytes, 0, length);
        return bytes;
    }

    internal void Copy(
        StringHandle handle,
        byte[] destination,
        int offset,
        int length)
    {
        _heap.Offset = MetadataTokens.GetHeapOffset(handle);
        _heap.ReadBytes(length, destination, offset);
    }
}

internal readonly record struct KeyCheck(
    int KeyCount,
    int KeyBytes,
    int QueryBytes,
    int KeyHash,
    int QueryMatches);
