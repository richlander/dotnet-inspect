using System.Buffers.Binary;

namespace InspectWeb.Engine;

/// <summary>
/// Bounds ZIP central-directory work before <see cref="System.IO.Compression.ZipArchive"/>
/// materializes entry objects.
/// </summary>
internal static class BrowserPackageArchiveValidator
{
    internal const int MaxEntries = 4_096;

    const uint CentralDirectoryHeaderSignature = 0x02014b50;
    const uint EndOfCentralDirectorySignature = 0x06054b50;
    const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;
    const int CentralDirectoryHeaderLength = 46;
    const int EndOfCentralDirectoryLength = 22;
    const int Zip64EndOfCentralDirectoryLength = 56;
    const int Zip64EndOfCentralDirectoryLocatorLength = 20;

    public static void Validate(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<byte> archive = bytes;
        int endOffset = FindEndOfCentralDirectory(archive);
        if (endOffset < 0)
            throw Invalid("The package has no valid ZIP end-of-central-directory record.");

        ushort disk = ReadUInt16(archive, endOffset + 4);
        ushort centralDirectoryDisk = ReadUInt16(archive, endOffset + 6);
        ushort entriesOnDisk = ReadUInt16(archive, endOffset + 8);
        ushort totalEntries = ReadUInt16(archive, endOffset + 10);
        uint centralDirectorySize = ReadUInt32(archive, endOffset + 12);
        uint centralDirectoryOffset = ReadUInt32(archive, endOffset + 16);

        ulong entryCount = totalEntries;
        ulong directorySize = centralDirectorySize;
        ulong directoryOffset = centralDirectoryOffset;
        bool zip64 = disk == ushort.MaxValue
            || centralDirectoryDisk == ushort.MaxValue
            || entriesOnDisk == ushort.MaxValue
            || totalEntries == ushort.MaxValue
            || centralDirectorySize == uint.MaxValue
            || centralDirectoryOffset == uint.MaxValue;
        if (zip64)
        {
            (entryCount, directorySize, directoryOffset) =
                ReadZip64Directory(archive, endOffset);
        }
        else if (disk != 0 || centralDirectoryDisk != 0 || entriesOnDisk != totalEntries)
        {
            throw Invalid("Multi-disk package archives are not supported in the browser.");
        }

        if (entryCount > MaxEntries)
        {
            throw new InvalidOperationException(
                $"The package archive exceeds the browser entry-count limit of {MaxEntries} "
                + "before archive enumeration.");
        }
        if (directoryOffset > int.MaxValue || directorySize > int.MaxValue)
            throw Invalid("The package central directory exceeds the browser address space.");

        int position = (int)directoryOffset;
        int directoryEnd;
        try
        {
            directoryEnd = checked(position + (int)directorySize);
        }
        catch (OverflowException ex)
        {
            throw Invalid("The package central-directory range is invalid.", ex);
        }
        if (position < 0 || directoryEnd > endOffset)
            throw Invalid("The package central-directory range is outside the archive.");

        for (ulong index = 0; index < entryCount; index++)
        {
            if (position > directoryEnd - CentralDirectoryHeaderLength
                || ReadUInt32(archive, position) != CentralDirectoryHeaderSignature)
            {
                throw Invalid("The package central directory ended before its declared entries.");
            }

            int fileNameLength = ReadUInt16(archive, position + 28);
            int extraLength = ReadUInt16(archive, position + 30);
            int commentLength = ReadUInt16(archive, position + 32);
            try
            {
                position = checked(
                    position
                    + CentralDirectoryHeaderLength
                    + fileNameLength
                    + extraLength
                    + commentLength);
            }
            catch (OverflowException ex)
            {
                throw Invalid("A package central-directory entry range is invalid.", ex);
            }
            if (position > directoryEnd)
                throw Invalid("A package central-directory entry exceeds its declared range.");
        }

        if (position != directoryEnd)
            throw Invalid("The package central directory contains undeclared entry data.");
    }

    static (ulong Entries, ulong Size, ulong Offset) ReadZip64Directory(
        ReadOnlySpan<byte> archive,
        int endOffset)
    {
        int locatorOffset = endOffset - Zip64EndOfCentralDirectoryLocatorLength;
        if (locatorOffset < 0
            || ReadUInt32(archive, locatorOffset)
                != Zip64EndOfCentralDirectoryLocatorSignature)
        {
            throw Invalid("The ZIP64 package is missing its central-directory locator.");
        }
        if (ReadUInt32(archive, locatorOffset + 4) != 0
            || ReadUInt32(archive, locatorOffset + 16) != 1)
        {
            throw Invalid("Multi-disk ZIP64 package archives are not supported in the browser.");
        }

        ulong zip64Offset = ReadUInt64(archive, locatorOffset + 8);
        if (zip64Offset > int.MaxValue)
            throw Invalid("The ZIP64 central-directory record exceeds the browser address space.");
        int recordOffset = (int)zip64Offset;
        if (recordOffset < 0
            || recordOffset > archive.Length - Zip64EndOfCentralDirectoryLength
            || ReadUInt32(archive, recordOffset) != Zip64EndOfCentralDirectorySignature)
        {
            throw Invalid("The ZIP64 package has an invalid central-directory record.");
        }
        if (ReadUInt64(archive, recordOffset + 4) < 44
            || ReadUInt32(archive, recordOffset + 16) != 0
            || ReadUInt32(archive, recordOffset + 20) != 0)
        {
            throw Invalid("The ZIP64 package central-directory record is unsupported.");
        }

        ulong entriesOnDisk = ReadUInt64(archive, recordOffset + 24);
        ulong totalEntries = ReadUInt64(archive, recordOffset + 32);
        if (entriesOnDisk != totalEntries)
            throw Invalid("Multi-disk ZIP64 package archives are not supported in the browser.");
        return (
            totalEntries,
            ReadUInt64(archive, recordOffset + 40),
            ReadUInt64(archive, recordOffset + 48));
    }

    static int FindEndOfCentralDirectory(ReadOnlySpan<byte> archive)
    {
        int first = Math.Max(0, archive.Length - EndOfCentralDirectoryLength - ushort.MaxValue);
        for (int offset = archive.Length - EndOfCentralDirectoryLength;
            offset >= first;
            offset--)
        {
            if (ReadUInt32(archive, offset) != EndOfCentralDirectorySignature)
                continue;
            int commentLength = ReadUInt16(archive, offset + 20);
            if (offset + EndOfCentralDirectoryLength + commentLength == archive.Length)
                return offset;
        }
        return -1;
    }

    static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(ushort))
            throw Invalid("The package ZIP record is truncated.");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    }

    static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
            throw Invalid("The package ZIP record is truncated.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }

    static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(ulong))
            throw Invalid("The package ZIP record is truncated.");
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
    }

    static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new(message, inner);
}
