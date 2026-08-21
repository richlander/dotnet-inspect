using System.Text.Json;

namespace NuGetFetch;

internal sealed record NuGetGalleryRegistrationIndex(
    IReadOnlyList<NuGetGalleryRegistrationPage> Pages);

internal sealed record NuGetGalleryRegistrationPage(
    string? ExternalId,
    IReadOnlyDictionary<string, PackageListingState>? Items);

internal sealed class NuGetGalleryRegistrationBudget
{
    internal const int MaximumPageCount = 128;
    internal const int MinimumLeafCount = 4_096;
    private const int LeafCountMultiplier = 4;

    private readonly long _maximumBytes;
    private readonly int _maximumLeafCount;
    private long _remainingBytes;
    private int _observedLeafCount;

    internal NuGetGalleryRegistrationBudget(
        int candidateCount,
        long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumBytes = maximumBytes;
        _remainingBytes = maximumBytes;
        long scaledCount = (long)candidateCount * LeafCountMultiplier;
        _maximumLeafCount = (int)Math.Min(
            int.MaxValue,
            Math.Max(MinimumLeafCount, scaledCount));
    }

    internal void EnsurePageCount(int pageCount)
    {
        if (pageCount > MaximumPageCount)
        {
            throw Invalid(
                $"NuGet Gallery registration exceeded {MaximumPageCount} pages.");
        }
    }

    internal Stream LimitBytes(Stream stream) =>
        new BudgetedReadStream(stream, this);

    internal void ObserveLeaf()
    {
        if (Interlocked.Increment(ref _observedLeafCount)
            > _maximumLeafCount)
        {
            throw Invalid(
                $"NuGet Gallery registration exceeded {_maximumLeafCount} leaves.");
        }
    }

    private static JsonException Invalid(string message) =>
        new(message);

    private int ReserveBytes(int requested)
    {
        while (true)
        {
            long remaining = Volatile.Read(ref _remainingBytes);
            if (remaining <= 0)
                return 0;

            int reserved = (int)Math.Min(requested, remaining);
            if (Interlocked.CompareExchange(
                    ref _remainingBytes,
                    remaining - reserved,
                    remaining) == remaining)
            {
                return reserved;
            }
        }
    }

    private void ReturnBytes(int count)
    {
        if (count > 0)
            Interlocked.Add(ref _remainingBytes, count);
    }

    private void ThrowByteLimitExceeded() =>
        throw new NuGetMetadataResponseTooLargeException(_maximumBytes);

    private sealed class BudgetedReadStream(
        Stream inner,
        NuGetGalleryRegistrationBudget budget) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            int reserved = budget.ReserveBytes(buffer.Length);
            if (reserved == 0)
            {
                if (inner.Read(buffer[..1]) == 0)
                    return 0;

                budget.ThrowByteLimitExceeded();
            }

            try
            {
                int read = inner.Read(buffer[..reserved]);
                budget.ReturnBytes(reserved - read);
                return read;
            }
            catch
            {
                budget.ReturnBytes(reserved);
                throw;
            }
        }

        public override int ReadByte()
        {
            int reserved = budget.ReserveBytes(1);
            if (reserved == 0)
            {
                if (inner.ReadByte() < 0)
                    return -1;

                budget.ThrowByteLimitExceeded();
            }

            try
            {
                int value = inner.ReadByte();
                if (value < 0)
                    budget.ReturnBytes(reserved);
                return value;
            }
            catch
            {
                budget.ReturnBytes(reserved);
                throw;
            }
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;

            int reserved = budget.ReserveBytes(buffer.Length);
            if (reserved == 0)
            {
                int sentinel = await inner.ReadAsync(
                    buffer[..1],
                    cancellationToken).ConfigureAwait(false);
                if (sentinel == 0)
                    return 0;

                budget.ThrowByteLimitExceeded();
            }

            try
            {
                int read = await inner.ReadAsync(
                    buffer[..reserved],
                    cancellationToken).ConfigureAwait(false);
                budget.ReturnBytes(reserved - read);
                return read;
            }
            catch
            {
                budget.ReturnBytes(reserved);
                throw;
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}

internal static class NuGetGalleryRegistration
{
    private static JsonDocumentOptions DocumentOptions =>
        new() { AllowDuplicateProperties = false };

    public static async ValueTask<NuGetGalleryRegistrationIndex>
        DeserializeIndexAsync(
            Stream json,
            IReadOnlySet<string> candidateVersions,
            NuGetGalleryRegistrationBudget budget,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            json,
            DocumentOptions,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement pages = GetRequiredArray(
            document.RootElement,
            "items",
            "registration index");
        budget.EnsurePageCount(pages.GetArrayLength());
        var result =
            new List<NuGetGalleryRegistrationPage>(pages.GetArrayLength());
        foreach (JsonElement page in pages.EnumerateArray())
        {
            if (page.ValueKind != JsonValueKind.Object)
                throw Invalid("Registration index page must be an object.");

            if (page.TryGetProperty("items", out JsonElement inlineItems))
            {
                result.Add(
                    new NuGetGalleryRegistrationPage(
                        ExternalId: null,
                        ParseItems(
                            inlineItems,
                            candidateVersions,
                            budget)));
                continue;
            }

            if (!page.TryGetProperty("@id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString()))
            {
                throw Invalid(
                    "Registration index page must contain inline items or an external page ID.");
            }

            result.Add(
                new NuGetGalleryRegistrationPage(
                    id.GetString(),
                    Items: null));
        }

        return new NuGetGalleryRegistrationIndex(result);
    }

    public static async ValueTask<
        IReadOnlyDictionary<string, PackageListingState>>
        DeserializePageAsync(
            Stream json,
            IReadOnlySet<string> candidateVersions,
            NuGetGalleryRegistrationBudget budget,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            json,
            DocumentOptions,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseItems(
            GetRequiredArray(
                document.RootElement,
                "items",
                "registration page"),
            candidateVersions,
            budget);
    }

    private static IReadOnlyDictionary<string, PackageListingState> ParseItems(
        JsonElement items,
        IReadOnlySet<string> candidateVersions,
        NuGetGalleryRegistrationBudget budget)
    {
        if (items.ValueKind != JsonValueKind.Array)
            throw Invalid("Registration items must be an array.");

        var result =
            new Dictionary<string, PackageListingState>(
                StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in items.EnumerateArray())
        {
            budget.ObserveLeaf();
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(
                    "catalogEntry",
                    out JsonElement catalogEntry)
                || catalogEntry.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(
                    "Registration item must contain a catalog entry.");
            }

            if (!catalogEntry.TryGetProperty(
                    "version",
                    out JsonElement versionElement)
                || versionElement.ValueKind != JsonValueKind.String
                || versionElement.GetString() is not string version)
            {
                throw Invalid(
                    "Registration catalog entry must contain a version.");
            }

            string normalizedVersion;
            try
            {
                normalizedVersion =
                    PackageCoordinateValidation.NormalizeVersion(
                        version,
                        "version");
            }
            catch (ArgumentException exception)
            {
                throw Invalid(
                    "Registration catalog entry contains an invalid version.",
                    exception);
            }

            PackageListingState listingState = PackageListingState.Listed;
            if (catalogEntry.TryGetProperty(
                    "listed",
                    out JsonElement listed))
            {
                listingState = listed.ValueKind switch
                {
                    JsonValueKind.True => PackageListingState.Listed,
                    JsonValueKind.False => PackageListingState.Unlisted,
                    _ => throw Invalid(
                        "Registration catalog entry has a non-Boolean listed value."),
                };
            }

            if (!candidateVersions.Contains(normalizedVersion))
                continue;

            if (result.TryGetValue(
                    normalizedVersion,
                    out PackageListingState prior)
                && prior != listingState)
            {
                throw Invalid(
                    "The NuGet Gallery registration response reported conflicting listing states.");
            }

            result[normalizedVersion] = listingState;
        }

        return result;
    }

    private static JsonElement GetRequiredArray(
        JsonElement root,
        string propertyName,
        string documentName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(
                propertyName,
                out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(
                $"NuGet Gallery {documentName} must contain an '{propertyName}' array.");
        }

        return value;
    }

    private static JsonException Invalid(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}
