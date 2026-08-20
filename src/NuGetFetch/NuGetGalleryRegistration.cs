using System.Text.Json;

namespace NuGetFetch;

internal sealed record NuGetGalleryRegistrationIndex(
    IReadOnlyList<NuGetGalleryRegistrationPage> Pages);

internal sealed record NuGetGalleryRegistrationPage(
    string? ExternalId,
    IReadOnlyList<NuGetGalleryRegistrationLeaf>? Items);

internal sealed record NuGetGalleryRegistrationLeaf(
    string Version,
    PackageListingState ListingState);

internal static class NuGetGalleryRegistration
{
    public static async ValueTask<NuGetGalleryRegistrationIndex>
        DeserializeIndexAsync(
            Stream json,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            json,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement pages = GetRequiredArray(
            document.RootElement,
            "items",
            "registration index");
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
                        ParseItems(inlineItems)));
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

    public static async ValueTask<IReadOnlyList<NuGetGalleryRegistrationLeaf>>
        DeserializePageAsync(
            Stream json,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            json,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseItems(
            GetRequiredArray(
                document.RootElement,
                "items",
                "registration page"));
    }

    private static IReadOnlyList<NuGetGalleryRegistrationLeaf> ParseItems(
        JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array)
            throw Invalid("Registration items must be an array.");

        var result =
            new List<NuGetGalleryRegistrationLeaf>(items.GetArrayLength());
        foreach (JsonElement item in items.EnumerateArray())
        {
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

            result.Add(
                new NuGetGalleryRegistrationLeaf(
                    normalizedVersion,
                    listingState));
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
