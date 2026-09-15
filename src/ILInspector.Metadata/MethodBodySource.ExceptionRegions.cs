using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
                new MethodBodyData(il, regions, catalog));
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
