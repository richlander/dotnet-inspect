using System.Text;

using InertText;

namespace ApiQualifiedNamePilot;

internal enum NameStrategy
{
    EagerInert,
    Utf8PerName,
    Utf8Arena,
}

internal abstract class NameStore
{
    internal abstract int Count { get; }

    internal abstract bool Matches(NameStore other);

    internal abstract int MatchHash();

    internal abstract InertString[] ProjectAll();

    internal abstract int ProjectAndHash();

    internal abstract int DisplayHash(InertString[]? projected);

    internal static NameStore Build(
        NameAsset asset,
        NameStrategy strategy,
        int copies) =>
        strategy switch
        {
            NameStrategy.EagerInert =>
                EagerNameStore.Build(asset, copies),
            NameStrategy.Utf8PerName =>
                PerNameByteStore.Build(asset, copies),
            NameStrategy.Utf8Arena =>
                ArenaByteStore.Build(asset, copies),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy)),
        };

    protected static int HashDisplay(
        ReadOnlySpan<InertString> values)
    {
        var hash = new HashCode();
        foreach (InertString value in values)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

internal readonly record struct EagerName(
    NameRole Role,
    InertString Value);

internal sealed class EagerNameStore(EagerName[] names) : NameStore
{
    readonly EagerName[] _names = names;

    internal override int Count => _names.Length;

    internal static EagerNameStore Build(
        NameAsset asset,
        int copies)
    {
        var names = new EagerName[
            checked(asset.NameCount * copies)];
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                names[index++] = new(
                    entry.Role,
                    new InertString(
                        TextPolicy.Field,
                        asset.Reader.GetString(entry.Handle)));
            }
        }
        return new(names);
    }

    internal override bool Matches(NameStore other)
    {
        if (other is not EagerNameStore right
            || right._names.Length != _names.Length)
        {
            return false;
        }
        for (int index = 0; index < _names.Length; index++)
        {
            if (_names[index].Role != right._names[index].Role
                || _names[index].Value != right._names[index].Value)
            {
                return false;
            }
        }
        return true;
    }

    internal override int MatchHash()
    {
        var hash = new HashCode();
        foreach (EagerName name in _names)
        {
            hash.Add(name.Role);
            hash.Add(name.Value);
        }
        return hash.ToHashCode();
    }

    internal override InertString[] ProjectAll()
    {
        var values = new InertString[_names.Length];
        for (int index = 0; index < values.Length; index++)
            values[index] = _names[index].Value;
        return values;
    }

    internal override int ProjectAndHash() =>
        DisplayHash(projected: null);

    internal override int DisplayHash(InertString[]? projected)
    {
        var hash = new HashCode();
        foreach (EagerName name in _names)
            hash.Add(name.Value);
        return hash.ToHashCode();
    }
}

internal readonly record struct PerNameBytes(
    NameRole Role,
    byte[] Bytes);

internal sealed class PerNameByteStore(
    PerNameBytes[] names) : NameStore
{
    readonly PerNameBytes[] _names = names;

    internal override int Count => _names.Length;

    internal static PerNameByteStore Build(
        NameAsset asset,
        int copies)
    {
        var names = new PerNameBytes[
            checked(asset.NameCount * copies)];
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                names[index++] = new(
                    entry.Role,
                    asset.Names.Read(entry.Handle));
            }
        }
        return new(names);
    }

    internal override bool Matches(NameStore other)
    {
        if (other is not PerNameByteStore right
            || right._names.Length != _names.Length)
        {
            return false;
        }
        for (int index = 0; index < _names.Length; index++)
        {
            if (_names[index].Role != right._names[index].Role
                || !_names[index].Bytes.AsSpan().SequenceEqual(
                    right._names[index].Bytes))
            {
                return false;
            }
        }
        return true;
    }

    internal override int MatchHash()
    {
        var hash = new HashCode();
        foreach (PerNameBytes name in _names)
        {
            hash.Add(name.Role);
            hash.AddBytes(name.Bytes);
        }
        return hash.ToHashCode();
    }

    internal override InertString[] ProjectAll()
    {
        var values = new InertString[_names.Length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = new(
                TextPolicy.Field,
                Encoding.UTF8.GetString(_names[index].Bytes));
        }
        return values;
    }

    internal override int ProjectAndHash()
    {
        var hash = new HashCode();
        foreach (PerNameBytes name in _names)
        {
            hash.Add(
                new InertString(
                    TextPolicy.Field,
                    Encoding.UTF8.GetString(name.Bytes)));
        }
        return hash.ToHashCode();
    }

    internal override int DisplayHash(InertString[]? projected)
        => HashDisplay(
            projected
            ?? throw new ArgumentNullException(nameof(projected)));
}

internal readonly record struct ArenaName(
    NameRole Role,
    int Offset,
    int Length);

internal sealed class ArenaByteStore(
    byte[] bytes,
    ArenaName[] names) : NameStore
{
    readonly byte[] _bytes = bytes;
    readonly ArenaName[] _names = names;

    internal override int Count => _names.Length;

    internal static ArenaByteStore Build(
        NameAsset asset,
        int copies)
    {
        int count = checked(asset.NameCount * copies);
        int byteCount = checked(asset.Utf8Bytes * copies);
        var bytes = new byte[byteCount];
        var names = new ArenaName[count];
        int index = 0;
        int offset = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                int length = asset.Names.Length(entry.Handle);
                asset.Names.Copy(
                    entry.Handle,
                    bytes,
                    offset,
                    length);
                names[index++] = new(
                    entry.Role,
                    offset,
                    length);
                offset += length;
            }
        }
        return new(bytes, names);
    }

    internal override bool Matches(NameStore other)
    {
        if (other is not ArenaByteStore right
            || right._names.Length != _names.Length)
        {
            return false;
        }
        for (int index = 0; index < _names.Length; index++)
        {
            ArenaName leftName = _names[index];
            ArenaName rightName = right._names[index];
            if (leftName.Role != rightName.Role
                || !Bytes(leftName).SequenceEqual(
                    right.Bytes(rightName)))
            {
                return false;
            }
        }
        return true;
    }

    internal override int MatchHash()
    {
        var hash = new HashCode();
        foreach (ArenaName name in _names)
        {
            hash.Add(name.Role);
            hash.AddBytes(Bytes(name));
        }
        return hash.ToHashCode();
    }

    internal override InertString[] ProjectAll()
    {
        var values = new InertString[_names.Length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = new(
                TextPolicy.Field,
                Encoding.UTF8.GetString(Bytes(_names[index])));
        }
        return values;
    }

    internal override int ProjectAndHash()
    {
        var hash = new HashCode();
        foreach (ArenaName name in _names)
        {
            hash.Add(
                new InertString(
                    TextPolicy.Field,
                    Encoding.UTF8.GetString(Bytes(name))));
        }
        return hash.ToHashCode();
    }

    internal override int DisplayHash(InertString[]? projected)
        => HashDisplay(
            projected
            ?? throw new ArgumentNullException(nameof(projected)));

    ReadOnlySpan<byte> Bytes(ArenaName name)
        => _bytes.AsSpan(name.Offset, name.Length);
}
