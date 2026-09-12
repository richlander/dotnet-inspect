using System.Globalization;
using System.Text.Json;

namespace NuGetFetch;

internal sealed partial class NuGetV3PackageSourceClient
{
    public async Task<
        PackageSourceOperationResult<NuGetCatalogPackageReceipt>>
        GetPackageReceiptAsync(
            NuGetCatalogEvent detailsEvent,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        _results.ValidateCatalogDetailsEvent(detailsEvent);
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureCatalogPackageReceiptAsync(
            _results,
            detailsEvent.Coordinate,
            async () =>
            {
                ParsedCatalogPackageReceipt parsed =
                    await ReadPackageReceiptAsync(
                        detailsEvent,
                        operation).ConfigureAwait(false);
                return _results.CatalogPackageReceipt(
                    detailsEvent,
                    parsed.ReceivedAt,
                    parsed.Basis,
                    operation);
            },
            operation,
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    private async Task<ParsedCatalogPackageReceipt>
        ReadPackageReceiptAsync(
            NuGetCatalogEvent detailsEvent,
            NuGetOperationDeadline operation)
    {
        string serviceIndexUrl =
            NuGetSourceRequest.EndpointUrl(_endpoint);
        PackageSourceCredential? credential =
            NuGetSourceRequest.CredentialForEndpoint(
                serviceIndexUrl,
                detailsEvent.LeafUrl,
                _credential);
        return await NuGetHttpRetry.RunRequestAsync(
            operation,
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGetPreservingPathAndQuery(
                        detailsEvent.LeafUrl);
                NuGetSourceRequest.ApplyCredential(request, credential);
                using HttpResponseMessage response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                try
                {
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException exception)
                    when (exception.StatusCode
                        == System.Net.HttpStatusCode.NotFound)
                {
                    throw new NuGetSourceResponseException(
                        "The Catalog Package Details leaf retained by the source was not found.",
                        exception);
                }

                return await NuGetMetadataReader.ReadResponseAsync(
                    response,
                    (json, cancellationToken) =>
                        NuGetCatalogPackageReceiptReader.ReadAsync(
                            json,
                            detailsEvent,
                            cancellationToken),
                    _options,
                    operation.RequestTimeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }
}

internal sealed record ParsedCatalogPackageReceipt(
    DateTimeOffset ReceivedAt,
    NuGetCatalogPackageReceiptBasis Basis);

internal static class NuGetCatalogPackageReceiptReader
{
    private const string TimestampFormat =
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK";

    private static JsonDocumentOptions DocumentOptions =>
        new()
        {
            AllowDuplicateProperties = false,
            MaxDepth = 64,
        };

    internal static async ValueTask<ParsedCatalogPackageReceipt> ReadAsync(
        Stream json,
        NuGetCatalogEvent detailsEvent,
        CancellationToken cancellationToken)
    {
        using JsonDocument document =
            await ParseDocumentAsync(
                json,
                cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid("The Catalog Package Details leaf must be an object.");
        RequirePackageDetailsType(root);

        string packageId = RequiredText(root, "id");
        string version = RequiredText(root, "version");
        PackageSourceCoordinate coordinate;
        try
        {
            coordinate = PackageSourceCoordinate.Create(packageId, version);
        }
        catch (ArgumentException exception)
        {
            throw Invalid(
                "The Catalog Package Details leaf contained an invalid package coordinate.",
                exception);
        }

        if (coordinate != detailsEvent.Coordinate)
        {
            throw Invalid(
                "The Catalog Package Details leaf did not match the retained package coordinate.");
        }

        if (!RequiredText(root, "catalog:commitId").Equals(
                detailsEvent.CommitId,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "The Catalog Package Details leaf did not match the retained commit ID.");
        }

        DateTimeOffset commitTimestamp =
            RequiredTimestamp(root, "catalog:commitTimeStamp");
        if (commitTimestamp != detailsEvent.CommitTimestamp)
        {
            throw Invalid(
                "The Catalog Package Details leaf did not match the retained commit timestamp.");
        }

        DateTimeOffset published = RequiredTimestamp(root, "published");
        if (root.TryGetProperty("created", out JsonElement created))
        {
            return new ParsedCatalogPackageReceipt(
                ReadTimestamp(created, "created"),
                NuGetCatalogPackageReceiptBasis.Created);
        }

        return new ParsedCatalogPackageReceipt(
            published,
            NuGetCatalogPackageReceiptBasis.PublishedFallback);
    }

    private static async ValueTask<JsonDocument> ParseDocumentAsync(
        Stream json,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(
                json,
                DocumentOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw Invalid(
                "The Catalog Package Details leaf was not valid JSON.",
                exception);
        }
    }

    private static void RequirePackageDetailsType(JsonElement root)
    {
        if (!root.TryGetProperty("@type", out JsonElement type))
        {
            throw Invalid(
                "The Catalog Package Details leaf must contain '@type'.");
        }

        bool found = false;
        if (type.ValueKind == JsonValueKind.String)
        {
            found = ReadText(type, "@type").Equals(
                "PackageDetails",
                StringComparison.Ordinal);
        }
        else if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in type.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw Invalid(
                        "The Catalog Package Details leaf had an invalid '@type' value.");
                }

                found |= ReadText(item, "@type").Equals(
                    "PackageDetails",
                    StringComparison.Ordinal);
            }
        }
        else
        {
            throw Invalid(
                "The Catalog Package Details leaf had an invalid '@type' value.");
        }

        if (!found)
        {
            throw Invalid(
                "The Catalog leaf was not a Package Details document.");
        }
    }

    private static string RequiredText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(
                $"The Catalog Package Details leaf must contain nonempty '{name}' text.");
        }

        string text = ReadText(value, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Invalid(
                $"The Catalog Package Details leaf must contain nonempty '{name}' text.");
        }

        return text;
    }

    private static string ReadText(JsonElement value, string name)
    {
        try
        {
            return value.GetString()
                ?? throw Invalid(
                    $"The Catalog Package Details leaf had an invalid '{name}' value.");
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid(
                $"The Catalog Package Details leaf contained invalid UTF-16 '{name}' text.",
                exception);
        }
    }

    private static DateTimeOffset RequiredTimestamp(
        JsonElement root,
        string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            throw Invalid(
                $"The Catalog Package Details leaf must contain '{name}'.");
        }

        return ReadTimestamp(value, name);
    }

    private static DateTimeOffset ReadTimestamp(
        JsonElement value,
        string name)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(
                $"The Catalog Package Details leaf had an invalid '{name}' timestamp.");
        }

        string text = ReadText(value, name);
        if (!HasExplicitOffset(text)
            || !DateTimeOffset.TryParseExact(
                text,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset parsed))
        {
            throw Invalid(
                $"The Catalog Package Details leaf had an invalid '{name}' timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    private static bool HasExplicitOffset(string value)
    {
        if (value.EndsWith('Z'))
            return true;
        if (value.Length < 6)
            return false;

        int offset = value.Length - 6;
        return value[offset] is '+' or '-'
            && char.IsAsciiDigit(value[offset + 1])
            && char.IsAsciiDigit(value[offset + 2])
            && value[offset + 3] == ':'
            && char.IsAsciiDigit(value[offset + 4])
            && char.IsAsciiDigit(value[offset + 5]);
    }

    private static NuGetSourceResponseException Invalid(string message) =>
        new(message);

    private static NuGetSourceResponseException Invalid(
        string message,
        Exception inner) =>
        new(message, inner);
}
