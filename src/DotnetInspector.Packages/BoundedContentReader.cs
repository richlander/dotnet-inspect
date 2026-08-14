namespace DotnetInspector.Packages;

/// <summary>Reads an untrusted stream into memory without crossing a caller-owned byte limit.</summary>
public static class BoundedContentReader
{
    public static async Task<byte[]> ReadAllBytesAsync(
        Stream source,
        long maxBytes,
        long? declaredLength = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (maxBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (declaredLength is < 0)
            throw new InvalidDataException("Content declared a negative length.");
        if (declaredLength > maxBytes || declaredLength > Array.MaxLength)
            throw new InvalidDataException("Content exceeds the configured byte limit.");

        if (declaredLength is long exactLength)
        {
            byte[] exact = GC.AllocateUninitializedArray<byte>((int)exactLength);
            int offset = 0;
            while (offset < exact.Length)
            {
                int read = await source.ReadAsync(
                    exact.AsMemory(offset),
                    cancellationToken);
                if (read == 0)
                    throw new InvalidDataException("Content ended before its declared length.");
                offset += read;
            }

            byte[] probe = new byte[1];
            if (await source.ReadAsync(probe, cancellationToken) != 0)
                throw new InvalidDataException("Content exceeds its declared length.");
            return exact;
        }

        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return output.ToArray();
            if (output.Length > maxBytes - read)
                throw new InvalidDataException("Content exceeds the configured byte limit.");
            output.Write(buffer, 0, read);
        }
    }
}
