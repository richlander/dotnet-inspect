#:project ../tests/DotnetInspector.PackageManifestCorpus/DotnetInspector.PackageManifestCorpus.csproj

using System.Net.Http.Headers;
using System.Text;
using DotnetInspector.PackageManifestCorpus;
using DotnetInspector.Queries;

bool refresh = args switch
{
    [] => false,
    ["--refresh"] => true,
    _ => throw new ArgumentException(
        "Usage: dotnet run eng/verify-package-manifest-corpus.cs -- [--refresh]"),
};

string catalogPath = Path.GetFullPath(
    Path.Combine("eng", "package-manifest-corpus.json"));
if (!File.Exists(catalogPath))
{
    throw new FileNotFoundException(
        "Run the package-manifest corpus verifier from the repository root.",
        catalogPath);
}

PackageManifestCorpusCatalog catalog;
await using (FileStream stream = File.OpenRead(catalogPath))
    catalog = PackageManifestCorpusVerifier.LoadCatalog(stream);

using var client = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60),
};
client.DefaultRequestHeaders.UserAgent.Add(
    new ProductInfoHeaderValue(
        "dotnet-inspect-manifest-corpus",
        "1.0"));

var updatedEntries =
    new List<PackageManifestCorpusEntry>(catalog.Packages.Count);
foreach (PackageManifestCorpusEntry entry in catalog.Packages)
{
    Uri manifestUri = ManifestUri(entry);
    using HttpResponseMessage response = await client.GetAsync(
        manifestUri,
        HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();
    byte[] manifestBytes = await ReadBoundedAsync(
        response.Content,
        PackageManifestFactsQuery.MaxManifestBytes);
    PackageManifestCorpusObservation observation =
        PackageManifestCorpusVerifier.Verify(
            entry,
            manifestBytes,
            verifyHash: !refresh);
    updatedEntries.Add(
        refresh
            ? entry with { Sha256 = observation.Sha256 }
            : entry);
    Console.WriteLine(
        $"PASS {entry.Id}@{entry.Version} "
        + $"sha256={observation.Sha256} "
        + $"coverage={string.Join(',', observation.Coverage)}");
}

if (refresh)
{
    var updatedCatalog = catalog with
    {
        Packages = updatedEntries,
    };
    await File.WriteAllTextAsync(
        catalogPath,
        PackageManifestCorpusVerifier.SerializeCatalog(updatedCatalog),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"Refreshed {catalog.Packages.Count} corpus entries.");
}
else
{
    Console.WriteLine($"Verified {catalog.Packages.Count} corpus entries.");
}

static Uri ManifestUri(PackageManifestCorpusEntry entry)
{
    string packageId = entry.Id.ToLowerInvariant();
    string version = entry.Version.ToLowerInvariant();
    return new Uri(
        "https://api.nuget.org/v3-flatcontainer/"
        + $"{Uri.EscapeDataString(packageId)}/"
        + $"{Uri.EscapeDataString(version)}/"
        + $"{Uri.EscapeDataString(packageId)}.nuspec");
}

static async Task<byte[]> ReadBoundedAsync(
    HttpContent content,
    int maximumBytes)
{
    if (content.Headers.ContentLength is > 0
        && content.Headers.ContentLength > maximumBytes)
    {
        throw new InvalidDataException(
            "A package-manifest corpus response exceeds the configured byte limit.");
    }

    await using Stream source = await content.ReadAsStreamAsync();
    using var destination = new MemoryStream(
        capacity: Math.Min(
            maximumBytes,
            (int)(content.Headers.ContentLength ?? 0)));
    byte[] buffer = new byte[81920];
    while (true)
    {
        int read = await source.ReadAsync(buffer);
        if (read == 0)
            return destination.ToArray();
        if (destination.Length + read > maximumBytes)
        {
            throw new InvalidDataException(
                "A package-manifest corpus response exceeds the configured byte limit.");
        }

        destination.Write(buffer, 0, read);
    }
}
