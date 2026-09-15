using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// The <c>#US</c> stream's position within one image's metadata block.
///
/// <c>System.Reflection.Metadata</c> exposes the user-string heap only through
/// <see cref="MetadataReader.GetUserString(UserStringHandle)"/>, which decodes a whole entry
/// before its caller can learn how large that entry is. A caller that must refuse an
/// over-budget literal instead of allocating it therefore needs the entry's compressed length
/// prefix, and that requires locating the stream itself. This type is that structural locator
/// and nothing more: it reads the ECMA-335 §II.24.2 metadata root's stream headers and reports
/// where <c>#US</c> lives.
/// </summary>
readonly record struct UserStringHeapWindow(PEMemoryBlock Metadata, int Offset, int Length)
{
    const uint MetadataRootSignature = 0x424A5342;
    const int StreamHeaderFixedSize = 8;
    const int MaxStreamNameLength = 32;

    /// <summary>
    /// Locates the <c>#US</c> stream in <paramref name="peReader"/>'s metadata block. Returns
    /// false when the image declares no user-string stream or its root is structurally
    /// unreadable; nothing is thrown for malformed input, because the caller reports a typed
    /// failure rather than propagating one.
    /// </summary>
    internal static bool TryLocate(PEReader peReader, out UserStringHeapWindow window)
    {
        window = default;
        try
        {
            PEMemoryBlock metadata = peReader.GetMetadata();
            if (metadata.Length == 0)
                return false;

            BlobReader root = metadata.GetReader();
            if (root.ReadUInt32() != MetadataRootSignature)
                return false;

            root.ReadUInt16();  // major version
            root.ReadUInt16();  // minor version
            root.ReadUInt32();  // reserved
            int versionLength = root.ReadInt32();
            if (versionLength < 0 || versionLength > root.RemainingBytes)
                return false;

            root.Offset = checked(root.Offset + versionLength);
            root.Offset = checked((root.Offset + 3) & ~3);
            root.ReadUInt16();  // flags
            int streamCount = root.ReadUInt16();

            // Each header is two fixed uint32s plus at least one padded name dword, so a count
            // larger than the block can hold is malformed rather than merely unterminated.
            if (streamCount > root.RemainingBytes / (StreamHeaderFixedSize + 4))
                return false;

            for (int index = 0; index < streamCount; index++)
            {
                int offset = checked((int)root.ReadUInt32());
                int length = checked((int)root.ReadUInt32());
                if (!TryReadStreamName(ref root, out bool isUserString))
                    return false;

                if (!isUserString)
                    continue;

                if (offset < 0 || length < 0 || offset > metadata.Length - length)
                    return false;

                window = new UserStringHeapWindow(metadata, offset, length);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException
            or OverflowException
            or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The decoded character count of the entry at <paramref name="heapOffset"/>, read from its
    /// compressed length prefix without decoding the characters themselves. False when the entry
    /// is out of range or its length prefix does not fit the stream.
    /// </summary>
    internal bool TryReadCharacterCount(int heapOffset, out int characterCount)
    {
        characterCount = 0;
        if (heapOffset < 0 || heapOffset >= Length)
            return false;

        try
        {
            BlobReader entry = Metadata.GetReader(
                checked(Offset + heapOffset),
                Length - heapOffset);
            int length = entry.ReadCompressedInteger();
            if (length <= 0 || (length & 1) == 0 || length > entry.RemainingBytes)
                return false;

            // The encoded entry is UTF-16 followed by a one-byte 0/1 flag.
            entry.Offset = checked(entry.Offset + length - 1);
            if (entry.ReadByte() > 1)
                return false;

            characterCount = (length - 1) / 2;
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or ArgumentOutOfRangeException
            or OverflowException)
        {
            return false;
        }
    }

    static bool TryReadStreamName(ref BlobReader root, out bool isUserString)
    {
        isUserString = false;
        Span<byte> name = stackalloc byte[MaxStreamNameLength];
        int nameLength = 0;
        while (true)
        {
            byte value = root.ReadByte();
            if (value == 0)
                break;
            if (nameLength == name.Length)
                return false;
            name[nameLength++] = value;
        }

        root.Offset = checked((root.Offset + 3) & ~3);
        isUserString = nameLength == 3
            && name[0] == (byte)'#'
            && name[1] == (byte)'U'
            && name[2] == (byte)'S';
        return true;
    }
}
