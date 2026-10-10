using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Unicode;

namespace ApiQualifiedNamePilot;

internal enum NameRole : byte
{
    TypeNamespace,
    TypeName,
    TypeReferenceNamespace,
    TypeReferenceName,
    MethodName,
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
        Name = System.IO.Path.GetFileName(path);
        int utf8Bytes = 0;
        foreach (NameEntry entry in entries)
            utf8Bytes = checked(utf8Bytes + _names.Length(entry.Handle));
        Utf8Bytes = utf8Bytes;
    }

    internal string Name { get; }

    internal ImmutableArray<NameEntry> Entries { get; }

    internal int NameCount => Entries.Length;

    internal int Utf8Bytes { get; }

    internal MetadataReader Reader => _reader;

    internal Utf8Heap Names => _names;

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
                throw new BadImageFormatException(
                    $"'{path}' has no CLI metadata.");
            MetadataReader reader = image.GetMetadataReader(
                MetadataReaderOptions.None);
            var entries = ImmutableArray.CreateBuilder<NameEntry>();
            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                TypeDefinition row = reader.GetTypeDefinition(handle);
                entries.Add(new(
                    NameRole.TypeNamespace,
                    row.Namespace));
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
        ValidateUtf8RoundTrip();
        NameStore eager = NameStore.Build(
            this,
            NameStrategy.EagerInert,
            copies: 1);
        InertText.InertString[] expected = eager.ProjectAll();
        int displayHash = eager.DisplayHash(expected);
        foreach (NameStrategy strategy in Enum.GetValues<NameStrategy>())
        {
            NameStore actual = NameStore.Build(this, strategy, copies: 1);
            InertText.InertString[] projected = actual.ProjectAll();
            if (!projected.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidOperationException(
                    $"{strategy} display disagrees with eager containment.");
            }
            if (actual.DisplayHash(projected) != displayHash)
            {
                throw new InvalidOperationException(
                    $"{strategy} display hash disagrees with eager containment.");
            }
            if (!actual.Matches(
                    NameStore.Build(this, strategy, copies: 1)))
            {
                throw new InvalidOperationException(
                    $"{strategy} cannot match equal stored names.");
            }
        }
        return new(
            NameCount,
            Utf8Bytes,
            StableDisplayHash(expected));
    }

    void ValidateUtf8RoundTrip()
    {
        foreach (NameEntry entry in Entries)
        {
            byte[] stored = Names.Read(entry.Handle);
            if (!Utf8.IsValid(stored))
            {
                throw new InvalidOperationException(
                    $"{Name} contains an ill-formed {entry.Role} name; "
                    + "the real-asset pilot requires well-formed names.");
            }

            byte[] roundTrip = Encoding.UTF8.GetBytes(
                Reader.GetString(entry.Handle));
            if (!stored.AsSpan().SequenceEqual(roundTrip))
            {
                throw new InvalidOperationException(
                    $"{Name} contains a {entry.Role} name that does not "
                    + "round-trip through UTF-16.");
            }
        }
    }

    ulong StableDisplayHash(
        ReadOnlySpan<InertText.InertString> values)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offset;
        for (int index = 0; index < values.Length; index++)
        {
            hash = (hash ^ (byte)Entries[index].Role) * prime;
            string text = values[index].ToString();
            hash = (hash ^ (uint)text.Length) * prime;
            foreach (char value in text)
                hash = (hash ^ value) * prime;
        }
        return hash;
    }

    public void Dispose()
    {
        _image.Dispose();
        _stream.Dispose();
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

internal readonly record struct NameCheck(
    int NameCount,
    int Utf8Bytes,
    ulong DisplayHash);
