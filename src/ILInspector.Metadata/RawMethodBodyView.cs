using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

internal static class RawMethodBodyHeader
{
    internal static bool TryRead(
        PEReader peReader,
        int rva,
        out PEMemoryBlock section,
        out int headerSize,
        out int ilByteCount,
        out bool tiny)
    {
        section = default;
        headerSize = 0;
        ilByteCount = 0;
        tiny = false;
        try
        {
            section = peReader.GetSectionData(rva);
            if (section.Length == 0)
                return false;

            BlobReader header = section.GetReader();
            byte first = header.ReadByte();
            switch (first & 0x03)
            {
                case 0x02:
                    tiny = true;
                    headerSize = 1;
                    ilByteCount = first >> 2;
                    break;
                case 0x03:
                    header.Offset = 0;
                    int flagsAndSize = header.ReadUInt16();
                    headerSize = (flagsAndSize >> 12) * 4;
                    if (headerSize < 12)
                        return false;
                    header.ReadUInt16();
                    uint declared = header.ReadUInt32();
                    if (declared > int.MaxValue)
                        return false;
                    ilByteCount = (int)declared;
                    break;
                default:
                    return false;
            }

            return headerSize <= section.Length
                && ilByteCount <= section.Length - headerSize;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException
            or InvalidOperationException)
        {
            return false;
        }
    }
}

internal readonly ref struct RawMethodBodyView
{
    const int SignatureTokenType = 0x11000000;

    readonly BlobReader _regions;
    readonly int _regionCount;
    readonly bool _fatRegions;

    RawMethodBodyView(
        BlobReader il,
        BlobReader regions,
        int regionCount,
        bool fatRegions)
    {
        IL = il;
        _regions = regions;
        _regionCount = regionCount;
        _fatRegions = fatRegions;
    }

    internal BlobReader IL { get; }

    internal RawExceptionRegionEnumerator ExceptionRegions =>
        new(_regions, _regionCount, _fatRegions);

    internal static RawMethodBodyView Read(PEReader peReader, int rva)
    {
        if (!RawMethodBodyHeader.TryRead(
                peReader,
                rva,
                out PEMemoryBlock section,
                out int headerSize,
                out int ilByteCount,
                out bool tiny))
        {
            throw new BadImageFormatException("The method body header is malformed.");
        }

        BlobReader body = section.GetReader();
        byte first = body.ReadByte();
        if (tiny)
        {
            return new RawMethodBodyView(
                section.GetReader(headerSize, ilByteCount),
                default,
                regionCount: 0,
                fatRegions: false);
        }

        byte second = body.ReadByte();
        if ((second >> 4) != 0x03)
            throw new BadImageFormatException("The fat method body header size is invalid.");

        bool hasExceptionRegions = (first & 0x08) != 0;
        body.ReadUInt16();
        body.ReadInt32();
        int localSignatureToken = body.ReadInt32();
        if (localSignatureToken != 0
            && (localSignatureToken & unchecked((int)0xFF000000)) != SignatureTokenType)
        {
            throw new BadImageFormatException("The method body local signature token is invalid.");
        }

        BlobReader il = section.GetReader(headerSize, ilByteCount);
        if (!hasExceptionRegions)
            return new RawMethodBodyView(il, default, regionCount: 0, fatRegions: false);

        body.Offset = Align4(headerSize + ilByteCount);
        byte sectionHeader = body.ReadByte();
        if ((sectionHeader & 0x01) != 0x01)
            throw new BadImageFormatException("The method body exception section header is invalid.");

        bool fat = (sectionHeader & 0x40) != 0;
        int dataSize = body.ReadByte();
        if (fat)
            dataSize += body.ReadUInt16() << 8;
        else
            body.ReadUInt16();

        int count = dataSize / (fat ? 24 : 12);
        int regionBytes = checked(count * (fat ? 24 : 12));
        BlobReader regions = section.GetReader(body.Offset, regionBytes);

        // Match MethodBodyBlock.Create: malformed records fail before any IL
        // comparison can decide a difference.
        var validation = new RawExceptionRegionEnumerator(regions, count, fat);
        while (validation.MoveNext(out _))
        {
        }

        return new RawMethodBodyView(il, regions, count, fat);
    }

    static int Align4(int value)
    {
        if (value < 0 || value > int.MaxValue - 3)
            throw new BadImageFormatException("The method body exception section offset is invalid.");

        return (value + 3) & ~3;
    }
}

internal readonly record struct RawExceptionRegion(
    ExceptionRegionKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    int ClassTokenOrFilterOffset)
{
    internal EntityHandle CatchType =>
        Kind == ExceptionRegionKind.Catch
            ? MetadataTokens.EntityHandle(ClassTokenOrFilterOffset)
            : default;

    internal int FilterOffset =>
        Kind == ExceptionRegionKind.Filter
            ? ClassTokenOrFilterOffset
            : -1;
}

internal ref struct RawExceptionRegionEnumerator
{
    BlobReader _reader;
    int _remaining;
    readonly bool _fat;

    internal RawExceptionRegionEnumerator(
        BlobReader reader,
        int count,
        bool fat)
    {
        _reader = reader;
        _remaining = count;
        _fat = fat;
    }

    internal bool MoveNext(out RawExceptionRegion region)
    {
        if (_remaining == 0)
        {
            region = default;
            return false;
        }

        _remaining--;
        if (_fat)
        {
            region = new RawExceptionRegion(
                (ExceptionRegionKind)_reader.ReadUInt32(),
                _reader.ReadInt32(),
                _reader.ReadInt32(),
                _reader.ReadInt32(),
                _reader.ReadInt32(),
                _reader.ReadInt32());
        }
        else
        {
            region = new RawExceptionRegion(
                (ExceptionRegionKind)_reader.ReadUInt16(),
                _reader.ReadUInt16(),
                _reader.ReadByte(),
                _reader.ReadUInt16(),
                _reader.ReadByte(),
                _reader.ReadInt32());
        }

        return true;
    }
}
