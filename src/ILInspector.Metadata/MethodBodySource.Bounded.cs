using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// One <c>MethodDef</c> row's scalar identity: what a producer needs to decide whether the row is
/// worth reading, with no name, signature, or body decoded.
/// </summary>
/// <param name="RowNumber">The 1-based <c>MethodDef</c> row this describes.</param>
/// <param name="MetadataToken">The row's <c>MethodDef</c> token.</param>
/// <param name="HasBody">
/// Whether the row declares an IL body (a non-zero RVA). False is a definite "no body here" for
/// an implementation assembly; a reference assembly's synthesized RVAs make only the false answer
/// meaningful, exactly as for <see cref="MethodBodyMember.HasBody"/>.
/// </param>
public readonly record struct MethodRowDescription(
    int RowNumber,
    int MetadataToken,
    bool HasBody);

/// <summary>Why a bounded method-body read could not answer.</summary>
public enum MethodBodyReadFailure
{
    /// <summary>The token is not a well-formed <c>MethodDef</c> token.</summary>
    NotMethodDefinitionToken,

    /// <summary>The token names a <c>MethodDef</c> row the image does not contain.</summary>
    RowOutOfRange,

    /// <summary>The method declares a non-IL implementation.</summary>
    UnsupportedImplementation,

    /// <summary>The body header or its IL extent is not readable as ECMA-335 §II.25.4.</summary>
    MalformedBody,
}

/// <summary>The outcome of one bounded method-body read.</summary>
/// <remarks>
/// Closed and total: every read reports exactly one of a materialized IL image, a definite absence
/// of one, a refusal to materialize, or a typed failure. There is no null and no shortened body,
/// so a consumer can never read "too large" or "malformed" as "no IL".
/// </remarks>
public abstract record BoundedMethodBodyRead
{
    private protected BoundedMethodBodyRead()
    {
    }

    /// <summary>
    /// The body's IL fit the declared byte limit and was copied whole. Only IL is materialized:
    /// exception regions and local signatures are not materialized. Beyond the session's
    /// retained image, the read needs this array plus constant-size bookkeeping.
    /// A consumer that needs whole-body facts uses
    /// <see cref="MethodBodySource.TryRead(int, out MethodBodyData?, out string?)"/>, which is
    /// unbounded by design.
    /// </summary>
    public sealed record Available(ImmutableArray<byte> IL) : BoundedMethodBodyRead;

    /// <summary>The row declares no IL body.</summary>
    public sealed record NoBody : BoundedMethodBodyRead;

    /// <summary>
    /// The body's IL is larger than the caller's limit. It was refused before any IL was copied,
    /// and <see cref="ILByteCount"/> is the size the caller would have had to admit.
    /// </summary>
    public sealed record ByteLimitExceeded(int ILByteCount, int MaxILBytes) : BoundedMethodBodyRead;

    /// <summary>The read could not answer, for the stated structural reason.</summary>
    public sealed record Unreadable(MethodBodyReadFailure Reason) : BoundedMethodBodyRead;
}

/// <summary>Why a bounded user-string read could not answer.</summary>
public enum UserStringReadFailure
{
    /// <summary>The token is not a <c>#US</c> (<c>0x70</c>) token.</summary>
    NotUserStringToken,

    /// <summary>The token's heap offset is past the end of this image's user-string heap.</summary>
    OffsetOutOfRange,

    /// <summary>
    /// The <c>#US</c> stream, or the entry at this offset, is not readable as ECMA-335 §II.24.2.4.
    /// </summary>
    MalformedEntry,
}

/// <summary>The outcome of one bounded user-string read.</summary>
/// <remarks>
/// Closed and total, for the same reason as <see cref="BoundedMethodBodyRead"/>: the legacy
/// <see cref="MethodBodySource.ResolveUserString(int)"/> returns null for an absent entry, a
/// malformed entry, and a rejected token alike, which is exactly the conflation a consumer that
/// charges for work cannot afford.
/// </remarks>
public abstract record BoundedUserStringRead
{
    private protected BoundedUserStringRead()
    {
    }

    /// <summary>
    /// The entry fit the declared character limit. <see cref="Value"/> is the exact decoded
    /// content of the heap entry: no escaping, normalization, trimming, or case folding. It is
    /// operation input for its consumer, not durable presentable evidence.
    /// </summary>
    public sealed record Available(string Value) : BoundedUserStringRead;

    /// <summary>
    /// The entry is longer than the caller's limit. It was refused before the string was decoded,
    /// and <see cref="CharacterCount"/> is the length the caller would have had to admit.
    /// </summary>
    public sealed record CharacterLimitExceeded(int CharacterCount, int MaxCharacters)
        : BoundedUserStringRead;

    /// <summary>The read could not answer, for the stated structural reason.</summary>
    public sealed record Unreadable(UserStringReadFailure Reason) : BoundedUserStringRead;
}

public sealed partial class MethodBodySource
{
    const int UserStringTokenType = 0x70000000;
    const int HeapOffsetMask = 0x00FFFFFF;

    UserStringHeapWindow _userStrings;
    bool _userStringsLocated;
    bool _userStringsProbed;

    /// <summary>
    /// The number of <c>MethodDef</c> rows in this image.
    ///
    /// This is the row count a producer charges its budget against before it reads anything. It
    /// is a table-header read: no row is touched and no name is decoded, unlike
    /// <see cref="EnumerateMethods"/>, which materializes a display name and a list entry for
    /// every method in the image.
    ///
    /// Gate: <c>MethodRows_CountAndDescribeMatchTheEnumeratedInventory</c>.
    /// </summary>
    public int MethodDefinitionCount
    {
        get
        {
            _ensureAlive();
            return _reader.GetTableRowCount(TableIndex.MethodDef);
        }
    }

    /// <summary>
    /// Describes one <c>MethodDef</c> row by its 1-based row number, without decoding its name,
    /// its declaring type, or its body. Returns false when <paramref name="rowNumber"/> is
    /// outside <see cref="MethodDefinitionCount"/>.
    ///
    /// Allocation-free per row: the description is a value and the row read decodes no heap
    /// content.
    ///
    /// Gate: <c>MethodRows_CountAndDescribeMatchTheEnumeratedInventory</c>.
    /// </summary>
    public bool TryDescribeMethod(int rowNumber, out MethodRowDescription description)
    {
        _ensureAlive();
        description = default;
        if (rowNumber < 1 || rowNumber > _reader.GetTableRowCount(TableIndex.MethodDef))
            return false;

        var handle = MetadataTokens.MethodDefinitionHandle(rowNumber);
        var method = _reader.GetMethodDefinition(handle);
        description = new MethodRowDescription(
            rowNumber,
            MetadataTokens.GetToken(handle),
            method.RelativeVirtualAddress != 0);
        return true;
    }

    /// <summary>
    /// Reads one method body under a hard IL byte limit.
    /// </summary>
    /// <param name="methodToken">The <c>MethodDef</c> token to read.</param>
    /// <param name="maxILBytes">
    /// The most IL bytes this read may materialize. Zero is legal and means "no remaining
    /// budget", which is what a caller spending one budget across many methods has left when it
    /// is full.
    /// </param>
    /// <remarks>
    /// <para>
    /// Beyond the session's already-retained image, an admitted read materializes one IL array
    /// of at most <paramref name="maxILBytes"/> bytes plus constant-size bookkeeping.
    /// An over-limit read does not materialize IL. Exception regions, local signatures, and the
    /// body block itself are not materialized; those whole-body facts remain on
    /// <see cref="TryRead(int, out MethodBodyData?, out string?)"/>.
    /// </para>
    /// <para>
    /// The limit is enforced before the allocation it governs: the IL code size comes from the
    /// method's tiny or fat header (ECMA-335 §II.25.4) in the already-mapped image, and an
    /// over-limit body is refused with its true size before any IL is copied. The IL is then
    /// copied straight out of the mapped body, which is the same content
    /// <c>PEReader.GetMethodBody</c> would expose — a correspondence
    /// <c>BoundedBody_ReturnsExactILWithinTheLimit</c> gates against that reader for both a tiny
    /// and a fat body.
    /// </para>
    /// <para>
    /// Unlike <see cref="TryRead(int, out MethodBodyData?, out string?)"/> — which is unbounded,
    /// materializes exception regions, and reports every refusal as a message string — each
    /// outcome here is typed, so "no body", "too large", and "malformed" stay distinguishable. A
    /// consumer that needs whole-body facts uses that path deliberately.
    /// </para>
    /// <para>
    /// Gates: <c>BoundedBody_ReturnsExactILWithinTheLimit</c>,
    /// <c>BoundedBody_MaterializesILWithoutExceptionRegions</c>,
    /// <c>BoundedBody_RefusesAnOverLimitBodyWithItsTrueSize</c>,
    /// <c>BoundedBody_DistinguishesNoBodyFromAMissingRow</c>.
    /// </para>
    /// </remarks>
    public BoundedMethodBodyRead ReadBounded(int methodToken, int maxILBytes)
    {
        _ensureAlive();
        ArgumentOutOfRangeException.ThrowIfNegative(maxILBytes);

        if (!TryGetMethodDefinition(methodToken, out var method, out var tokenFailure))
            return new BoundedMethodBodyRead.Unreadable(tokenFailure);

        int rva = method.RelativeVirtualAddress;
        if (rva == 0)
            return new BoundedMethodBodyRead.NoBody();

        if ((method.ImplAttributes & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL)
        {
            return new BoundedMethodBodyRead.Unreadable(
                MethodBodyReadFailure.UnsupportedImplementation);
        }

        if (!TryReadILExtent(rva, out int headerSize, out int ilByteCount))
            return new BoundedMethodBodyRead.Unreadable(MethodBodyReadFailure.MalformedBody);

        if (ilByteCount > maxILBytes)
            return new BoundedMethodBodyRead.ByteLimitExceeded(ilByteCount, maxILBytes);

        try
        {
            return new BoundedMethodBodyRead.Available(
                _peReader.GetSectionData(rva).GetContent(headerSize, ilByteCount));
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return new BoundedMethodBodyRead.Unreadable(MethodBodyReadFailure.MalformedBody);
        }
    }

    /// <summary>
    /// Reads one <c>#US</c> heap entry under a hard decoded-character limit.
    /// </summary>
    /// <param name="token">A user-string (<c>0x70</c>) token, as carried by <c>ldstr</c>.</param>
    /// <param name="maxCharacters">
    /// The most decoded characters this read may materialize. Zero is legal and admits only the
    /// empty entry.
    /// </param>
    /// <remarks>
    /// <para>
    /// The token's type byte and heap offset are validated against this image's user-string heap,
    /// and the entry's compressed length prefix is read to compute its character count, all before
    /// the entry is decoded. An over-budget entry is therefore refused without allocating its
    /// string, and a returned value never exceeds <paramref name="maxCharacters"/>.
    /// </para>
    /// <para>
    /// Offset zero is the heap's nil entry and reads as the empty string, matching
    /// <c>System.Reflection.Metadata</c>.
    /// </para>
    /// <para>
    /// Unlike <see cref="ResolveUserString(int)"/>, this path neither decodes unbounded content
    /// nor folds a rejected token, an out-of-range offset, and a malformed entry into one null.
    /// </para>
    /// <para>
    /// Gates: <c>BoundedUserString_ReturnsRawContentWithinTheLimit</c>,
    /// <c>BoundedUserString_RefusesAnOverLimitEntryWithItsTrueLength</c>,
    /// <c>BoundedUserString_ReportsTokenAndRangeFailuresDistinctly</c>.
    /// </para>
    /// </remarks>
    public BoundedUserStringRead ReadBoundedUserString(int token, int maxCharacters)
    {
        _ensureAlive();
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        if ((token & unchecked((int)0xFF000000)) != UserStringTokenType)
            return new BoundedUserStringRead.Unreadable(UserStringReadFailure.NotUserStringToken);

        int heapOffset = token & HeapOffsetMask;
        if (heapOffset == 0)
            return new BoundedUserStringRead.Available(string.Empty);

        if (heapOffset >= _reader.GetHeapSize(HeapIndex.UserString))
            return new BoundedUserStringRead.Unreadable(UserStringReadFailure.OffsetOutOfRange);

        if (!TryGetUserStringHeap(out var heap)
            || !heap.TryReadCharacterCount(heapOffset, out int characterCount))
        {
            return new BoundedUserStringRead.Unreadable(UserStringReadFailure.MalformedEntry);
        }

        if (characterCount > maxCharacters)
        {
            return new BoundedUserStringRead.CharacterLimitExceeded(
                characterCount,
                maxCharacters);
        }

        try
        {
            return new BoundedUserStringRead.Available(
                _reader.GetUserString(MetadataTokens.UserStringHandle(heapOffset)));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new BoundedUserStringRead.Unreadable(UserStringReadFailure.MalformedEntry);
        }
    }

    bool TryGetMethodDefinition(
        int methodToken,
        out MethodDefinition method,
        out MethodBodyReadFailure failure)
    {
        method = default;
        failure = MethodBodyReadFailure.NotMethodDefinitionToken;

        if ((methodToken & unchecked((int)0xFF000000)) != 0x06000000)
            return false;

        int rowNumber = methodToken & HeapOffsetMask;
        if (rowNumber < 1 || rowNumber > _reader.GetTableRowCount(TableIndex.MethodDef))
        {
            failure = MethodBodyReadFailure.RowOutOfRange;
            return false;
        }

        try
        {
            method = _reader.GetMethodDefinition(
                MetadataTokens.MethodDefinitionHandle(rowNumber));
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException)
        {
            failure = MethodBodyReadFailure.MalformedBody;
            return false;
        }
    }

    /// <summary>
    /// Where the IL starts within the body at <paramref name="rva"/> and how many bytes of it
    /// there are, read from the body header without constructing a body block. This is the
    /// measurement that lets an over-limit body be refused before anything is materialized, and
    /// the extent the admitted copy uses.
    /// </summary>
    bool TryReadILExtent(int rva, out int headerSize, out int ilByteCount)
    {
        headerSize = 0;
        ilByteCount = 0;
        try
        {
            PEMemoryBlock section = _peReader.GetSectionData(rva);
            if (section.Length == 0)
                return false;

            BlobReader header = section.GetReader();
            byte first = header.ReadByte();
            int prefix;
            int codeSize;
            switch (first & 0x03)
            {
                case 0x02:  // CorILMethod_TinyFormat: size in the upper six bits.
                    prefix = 1;
                    codeSize = first >> 2;
                    break;
                case 0x03:  // CorILMethod_FatFormat: flags/header size, max stack, code size.
                    header.Offset = 0;
                    int flagsAndSize = header.ReadUInt16();
                    prefix = (flagsAndSize >> 12) * 4;
                    if (prefix < 12)
                        return false;
                    header.ReadUInt16();  // max stack
                    uint declared = header.ReadUInt32();
                    if (declared > int.MaxValue)
                        return false;
                    codeSize = (int)declared;
                    break;
                default:
                    return false;
            }

            if (prefix > section.Length || codeSize > section.Length - prefix)
                return false;

            headerSize = prefix;
            ilByteCount = codeSize;
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException
            or InvalidOperationException)
        {
            return false;
        }
    }

    bool TryGetUserStringHeap(out UserStringHeapWindow heap)
    {
        if (!_userStringsProbed)
        {
            _userStringsLocated = UserStringHeapWindow.TryLocate(_peReader, out _userStrings);
            _userStringsProbed = true;
        }

        heap = _userStrings;
        return _userStringsLocated;
    }
}
