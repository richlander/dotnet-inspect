using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using InertText;

namespace NuGetFetch;

internal static class NuGetGalleryDiscoveryReader
{
    internal static string RequestUrl(NuGetGalleryDiscoveryRequest request)
    {
        var parameters = new List<string>();
        if (request.Text is { } text)
            parameters.Add($"q={Uri.EscapeDataString(text)}");
        if (request.PackageType is { } packageType)
            parameters.Add($"packageType={Uri.EscapeDataString(packageType.Name)}");
        parameters.Add(request.Order == NuGetGalleryDiscoveryOrder.MostDownloaded
            ? "sortBy=totalDownloads-desc"
            : "sortBy=relevance");
        parameters.Add(request.IncludePrerelease
            ? "prerelease=true"
            : "prerelease=false");
        parameters.Add("semVerLevel=2.0.0");
        parameters.Add("skip=0");
        parameters.Add($"take={request.Capacity.ToString(CultureInfo.InvariantCulture)}");
        return "https://azuresearch-usnc.nuget.org/search/query?"
            + string.Join('&', parameters);
    }

    internal static async ValueTask<NuGetGalleryDiscoveryResult> ReadAsync(
        Stream stream,
        NuGetGalleryDiscoveryRequest request,
        PackageSourceResultFactory results,
        NuGetOperationDeadline operation,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        JsonElement data = Required(root, "data", JsonValueKind.Array);
        if (data.GetArrayLength() > request.Capacity)
            throw Invalid("The Gallery response exceeded the requested capacity.");

        var matches = ImmutableArray.CreateBuilder<NuGetGalleryDiscoveryMatch>(
            data.GetArrayLength());
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement row in data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            // Yield periodically so single-threaded browser cancellation can run.
            if (OperatingSystem.IsBrowser() && matches.Count % 32 == 0)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
            }

            JsonElement registration = Required(
                row, "PackageRegistration", JsonValueKind.Object);
            string id = RequiredText(registration, "Id");
            string version = RequiredText(row, "Version");
            string normalizedVersion = RequiredText(row, "NormalizedVersion");
            PackageSourceCoordinate coordinate;
            try
            {
                coordinate = PackageSourceCoordinate.Create(id, version);
                if (coordinate != PackageSourceCoordinate.Create(id, normalizedVersion))
                    throw Invalid("The Gallery response contains inconsistent versions.");
            }
            catch (ArgumentException exception)
            {
                throw new JsonException(
                    "The Gallery response contains an invalid coordinate.", exception);
            }

            if (!packageIds.Add(coordinate.PackageId))
                throw Invalid("The Gallery response contains a repeated package ID.");
            if (!request.IncludePrerelease
                && NuGet.Versioning.NuGetVersion.Parse(coordinate.Version).IsPrerelease)
            {
                throw Invalid("The Gallery response contains a prerelease version for stable-only discovery.");
            }

            long? downloads = NonNegativeInteger(
                request.Order == NuGetGalleryDiscoveryOrder.MostDownloaded
                    ? Required(registration, "DownloadCount", JsonValueKind.Number)
                    : Optional(registration, "DownloadCount"));
            if (request.Order == NuGetGalleryDiscoveryOrder.MostDownloaded
                && downloads is null)
            {
                throw Invalid("Download-ranked Gallery rows require lifetime downloads.");
            }

            JsonElement verified = Optional(registration, "Verified");
            matches.Add(new NuGetGalleryDiscoveryMatch(
                results.Candidate(
                    coordinate,
                    PackageDiscoveryContract.KeywordSearch,
                    PackageListingState.Listed),
                id,
                version,
                downloads,
                verified.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                },
                Owners(Optional(registration, "Owners")),
                SafeText(Optional(row, "Description"), TextPolicy.Prose)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        return results.GalleryDiscovery(
            request,
            matches.MoveToImmutable(),
            NonNegativeInteger(Optional(root, "totalHits")),
            operation);
    }

    private static JsonElement Required(
        JsonElement parent,
        string name,
        JsonValueKind kind)
    {
        JsonElement value = Optional(parent, name);
        if (value.ValueKind != kind)
            throw Invalid("The Gallery response has missing, repeated, or invalid required metadata.");
        return value;
    }

    private static JsonElement Optional(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            return default;
        JsonElement value = default;
        bool found = false;
        foreach (JsonProperty property in parent.EnumerateObject())
        {
            if (!property.NameEquals(name))
                continue;
            if (found)
                return default;
            found = true;
            value = property.Value;
        }
        return value;
    }

    private static string RequiredText(JsonElement parent, string name) =>
        SafeText(Required(parent, name, JsonValueKind.String), TextPolicy.Field)
        ?? throw Invalid("The Gallery response has invalid coordinate text.");

    private static string? SafeText(JsonElement value, TextPolicy policy) =>
        value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text
            && !string.IsNullOrWhiteSpace(text)
            && InertString.IsPermitted(policy, text)
                ? text
                : null;

    private static long? NonNegativeInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number)
            && number >= 0
                ? number
                : null;

    private static ImmutableArray<string> Owners(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return [];
        var owners = ImmutableArray.CreateBuilder<string>(value.GetArrayLength());
        foreach (JsonElement owner in value.EnumerateArray())
        {
            if (SafeText(owner, TextPolicy.Field) is not { } text)
                return [];
            owners.Add(text);
        }
        return owners.MoveToImmutable();
    }

    private static JsonException Invalid(string message) => new(message);
}
