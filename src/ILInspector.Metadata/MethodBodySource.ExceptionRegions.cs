using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed partial class MethodBodySource
{
    /// <summary>
    /// Reads one complete method body and its physical exception clauses.
    /// </summary>
    public MethodBodyReadResult Read(int methodToken) =>
        Read(methodToken, int.MaxValue);

    /// <summary>
    /// Reads one complete method body and its physical exception clauses under
    /// a hard IL-byte limit.
    /// </summary>
    public MethodBodyReadResult Read(int methodToken, int maxILBytes)
    {
        _ensureAlive();
        ArgumentOutOfRangeException.ThrowIfNegative(maxILBytes);

        if (!TryGetMethodDefinition(
                methodToken,
                out MethodDefinitionHandle handle,
                out MethodDefinition method,
                out var failure))
        {
            MetadataMethodAddress? failedMethod =
                TryCreateMethodAddress(handle, out MetadataMethodAddress failedAddress)
                    ? failedAddress
                    : null;
            return new MethodBodyReadResult.Unavailable(
                failedMethod,
                MapUnavailableReason(failure));
        }

        if (!TryCreateMethodAddress(handle, out MetadataMethodAddress address))
        {
            return new MethodBodyReadResult.Unavailable(
                method: null,
                new MethodBodyUnavailableReason.MalformedBody());
        }

        int rva = method.RelativeVirtualAddress;
        if (rva == 0)
            return new MethodBodyReadResult.NoBody(address);

        if ((method.ImplAttributes & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL)
        {
            return new MethodBodyReadResult.Unavailable(
                address,
                new MethodBodyUnavailableReason.UnsupportedImplementation());
        }

        if (!TryReadILExtent(rva, out _, out int ilByteCount))
        {
            return new MethodBodyReadResult.Unavailable(
                address,
                new MethodBodyUnavailableReason.MalformedBody());
        }

        if (ilByteCount > maxILBytes)
        {
            return new MethodBodyReadResult.Unavailable(
                address,
                new MethodBodyUnavailableReason.ILByteLimitExceeded(
                    ilByteCount,
                    maxILBytes));
        }

        try
        {
            MethodBodyBlock block = _peReader.GetMethodBody(rva);
            ImmutableArray<byte> il = (block.GetILBytes() ?? []).ToImmutableArray();
            if (il.Length != ilByteCount)
            {
                return new MethodBodyReadResult.Unavailable(
                    address,
                    new MethodBodyUnavailableReason.MalformedBody());
            }

            ImmutableArray<ExceptionRegion> regions =
                block.ExceptionRegions.ToImmutableArray();
            if (!TryValidateExceptionSections(
                    rva,
                    il.Length,
                    regions))
            {
                return new MethodBodyReadResult.Unavailable(
                    address,
                    new MethodBodyUnavailableReason.MalformedBody());
            }

            var evidence = new MethodBodyEvidenceId(address, Guid.NewGuid());
            if (!TryBuildExceptionCatalog(
                    evidence,
                    il.Length,
                    regions,
                    out MethodExceptionRegionCatalog catalog))
            {
                return new MethodBodyReadResult.Unavailable(
                    address,
                    new MethodBodyUnavailableReason.MalformedBody());
            }

            return new MethodBodyReadResult.Available(
                new MethodBodyData(il, catalog));
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return new MethodBodyReadResult.Unavailable(
                address,
                new MethodBodyUnavailableReason.MalformedBody());
        }
    }

    bool TryBuildExceptionCatalog(
        MethodBodyEvidenceId evidence,
        int ilLength,
        ImmutableArray<ExceptionRegion> regions,
        out MethodExceptionRegionCatalog catalog)
    {
        catalog = null!;
        var clauses = ImmutableArray.CreateBuilder<MethodExceptionClause>(regions.Length);
        for (int ordinal = 0; ordinal < regions.Length; ordinal++)
        {
            ExceptionRegion region = regions[ordinal];
            if (region.Kind is not (ExceptionRegionKind.Catch
                or ExceptionRegionKind.Filter
                or ExceptionRegionKind.Finally
                or ExceptionRegionKind.Fault)
                || !TryCreateExtent(
                    region.TryOffset,
                    region.TryLength,
                    ilLength,
                    out MethodBodyExtent protectedExtent)
                || !TryCreateExtent(
                    region.HandlerOffset,
                    region.HandlerLength,
                    ilLength,
                    out MethodBodyExtent handlerExtent))
            {
                return false;
            }

            MethodBodyExtent? filterExtent = null;
            if (region.Kind == ExceptionRegionKind.Filter)
            {
                if (region.FilterOffset < 0
                    || region.FilterOffset > region.HandlerOffset
                    || region.HandlerOffset > ilLength)
                {
                    return false;
                }

                filterExtent = new MethodBodyExtent(
                    region.FilterOffset,
                    region.HandlerOffset);
            }

            MethodExceptionCatchType? catchType = null;
            if (region.Kind == ExceptionRegionKind.Catch)
            {
                int catchToken = region.CatchType.IsNil
                    ? 0
                    : MetadataTokens.GetToken(region.CatchType);
                catchType = new MethodExceptionCatchType(
                    catchToken,
                    TypeResolver.ResolveTypeName(_reader, region.CatchType));
            }

            clauses.Add(new MethodExceptionClause(
                new MethodExceptionClauseId(evidence, ordinal),
                region.Kind,
                protectedExtent,
                handlerExtent,
                filterExtent,
                catchType));
        }

        catalog = new MethodExceptionRegionCatalog(evidence, clauses.MoveToImmutable());
        return true;
    }

    bool TryValidateExceptionSections(
        int rva,
        int ilLength,
        ImmutableArray<ExceptionRegion> regions)
    {
        try
        {
            PEMemoryBlock body = _peReader.GetSectionData(rva);
            if (body.Length == 0)
                return false;

            BlobReader reader = body.GetReader();
            byte first = reader.ReadByte();
            if ((first & 0x03) == 0x02)
                return regions.IsEmpty;
            if ((first & 0x03) != 0x03)
                return false;

            reader.Offset = 0;
            int flagsAndSize = reader.ReadUInt16();
            int headerSize = (flagsAndSize >> 12) * 4;
            bool hasSections = (flagsAndSize & 0x08) != 0;
            if (!hasSections)
                return regions.IsEmpty;

            int sectionOffset = Align4(headerSize + ilLength);
            int regionIndex = 0;
            bool hasNext;
            do
            {
                if (sectionOffset < 0 || sectionOffset > body.Length - 4)
                    return false;

                reader.Offset = sectionOffset;
                byte kindAndFlags = reader.ReadByte();
                hasNext = (kindAndFlags & 0x80) != 0;
                bool fat = (kindAndFlags & 0x40) != 0;
                int kind = kindAndFlags & 0x3F;
                int dataSize;
                if (fat)
                {
                    dataSize = reader.ReadByte()
                        | reader.ReadByte() << 8
                        | reader.ReadByte() << 16;
                }
                else
                {
                    dataSize = reader.ReadByte();
                    reader.ReadUInt16();
                }

                if (dataSize < 4 || dataSize > body.Length - sectionOffset)
                    return false;

                if (kind == 0x01)
                {
                    int clauseSize = fat ? 24 : 12;
                    int clauseBytes = dataSize - 4;
                    if (clauseBytes % clauseSize != 0)
                        return false;

                    int clauseCount = clauseBytes / clauseSize;
                    for (int clause = 0; clause < clauseCount; clause++)
                    {
                        if ((uint)regionIndex >= (uint)regions.Length
                            || !MatchesEncodedClause(
                                ref reader,
                                fat,
                                regions[regionIndex++]))
                        {
                            return false;
                        }
                    }
                }
                else if (kind != 0x02)
                {
                    return false;
                }

                sectionOffset = Align4(sectionOffset + dataSize);
            }
            while (hasNext);

            return regionIndex == regions.Length;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool MatchesEncodedClause(
        ref BlobReader reader,
        bool fat,
        ExceptionRegion region)
    {
        uint flags;
        int tryOffset;
        int tryLength;
        int handlerOffset;
        int handlerLength;
        int classTokenOrFilterOffset;
        if (fat)
        {
            flags = reader.ReadUInt32();
            tryOffset = reader.ReadInt32();
            tryLength = reader.ReadInt32();
            handlerOffset = reader.ReadInt32();
            handlerLength = reader.ReadInt32();
            classTokenOrFilterOffset = reader.ReadInt32();
        }
        else
        {
            flags = reader.ReadUInt16();
            tryOffset = reader.ReadUInt16();
            tryLength = reader.ReadByte();
            handlerOffset = reader.ReadUInt16();
            handlerLength = reader.ReadByte();
            classTokenOrFilterOffset = reader.ReadInt32();
        }

        ExceptionRegionKind? kind = flags switch
        {
            0x00 => ExceptionRegionKind.Catch,
            0x01 => ExceptionRegionKind.Filter,
            0x02 => ExceptionRegionKind.Finally,
            0x04 => ExceptionRegionKind.Fault,
            _ => null,
        };
        if (kind != region.Kind
            || tryOffset != region.TryOffset
            || tryLength != region.TryLength
            || handlerOffset != region.HandlerOffset
            || handlerLength != region.HandlerLength)
        {
            return false;
        }

        return region.Kind switch
        {
            ExceptionRegionKind.Catch =>
                classTokenOrFilterOffset
                    == (region.CatchType.IsNil
                        ? 0
                        : MetadataTokens.GetToken(region.CatchType)),
            ExceptionRegionKind.Filter =>
                classTokenOrFilterOffset == region.FilterOffset,
            ExceptionRegionKind.Finally or ExceptionRegionKind.Fault =>
                classTokenOrFilterOffset == 0,
            _ => false,
        };
    }

    static int Align4(int value)
    {
        if (value < 0 || value > int.MaxValue - 3)
            return -1;

        return (value + 3) & ~3;
    }

    bool TryCreateMethodAddress(
        MethodDefinitionHandle handle,
        out MetadataMethodAddress address)
    {
        address = default;
        if (handle.IsNil)
            return false;

        try
        {
            address = MetadataMethodAddress.Create(_reader, handle);
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool TryCreateExtent(
        int start,
        int length,
        int ilLength,
        out MethodBodyExtent extent)
    {
        extent = default;
        if (start < 0
            || length < 0
            || start > ilLength
            || length > ilLength - start)
        {
            return false;
        }

        extent = new MethodBodyExtent(start, start + length);
        return true;
    }

    static MethodBodyUnavailableReason MapUnavailableReason(
        MethodBodyReadFailure failure) =>
        failure switch
        {
            MethodBodyReadFailure.NotMethodDefinitionToken =>
                new MethodBodyUnavailableReason.NotMethodDefinitionToken(),
            MethodBodyReadFailure.RowOutOfRange =>
                new MethodBodyUnavailableReason.RowOutOfRange(),
            MethodBodyReadFailure.UnsupportedImplementation =>
                new MethodBodyUnavailableReason.UnsupportedImplementation(),
            _ => new MethodBodyUnavailableReason.MalformedBody(),
        };
}
