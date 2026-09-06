#:project ../src/DotnetInspector.Core/DotnetInspector.Core.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Core;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args is ["--self-test"])
{
    await ProbeTests.RunAsync();
    return 0;
}
if (args.Length != 5 || args[0] is not ("events" or "snapshots")
    || !DateTimeOffset.TryParse(args[1], out var from)
    || !DateTimeOffset.TryParse(args[2], out var through)
    || from.Offset != TimeSpan.Zero || through.Offset != TimeSpan.Zero
    || through <= from || through - from > TimeSpan.FromDays(1)
    || !int.TryParse(args[3], out int take) || take is < 1 or > 100_000
    || !int.TryParse(args[4], out int trials) || trials is < 1 or > 10)
{
    Console.Error.WriteLine("Usage: dotnet run tools/CatalogChangeBenchmark.cs -c Release --"
        + " <events|snapshots> <exclusive-start-UTC> <inclusive-end-UTC>"
        + " <take:1..100000> <trials:1..10> (window at most 24h)");
    return 1;
}

for (int trial = 1; trial <= trials; trial++)
{
    using var handler = new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        Credentials = null,
        UseCookies = false,
        UseProxy = false,
    };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-inspect-catalog-experiment");
    foreach (string cache in new[] { "cold-client", "warm-connection" })
    {
        var probe = new CatalogProbe(client);
        Summary result = await probe.RunAsync(from, through, take, args[0] == "snapshots",
            row => Console.WriteLine(JsonSerializer.Serialize(row, ProbeJson.Default.ChangeRow)));
        result = result with { Trial = trial, Cache = cache };
        Console.WriteLine(JsonSerializer.Serialize(result, ProbeJson.Default.Summary));
        if (result.Completion == "failed")
            return 1;
    }
}
return 0;

sealed class CatalogProbe(HttpClient client, int maxPages = 128, int maxRequests = 512)
{
    public const string Service = "https://api.nuget.org/v3/index.json";
    private readonly Dictionary<string, Cost> _costs = new(StringComparer.Ordinal);
    private long _bytes;
    private int _requests;

    public async Task<Summary> RunAsync(
        DateTimeOffset from, DateTimeOffset through, int take, bool snapshots,
        Action<ChangeRow> observe)
    {
        var result = new Summary
        {
            FromExclusive = from, ThroughInclusive = through,
            Take = take, Mode = snapshots ? "snapshots" : "events",
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var projection = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var coordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long started = Stopwatch.GetTimestamp();
        try
        {
            using var service = await GetAsync(Service, "service", deadline.Token);
            string catalogUrl = service.RootElement.GetProperty("resources").EnumerateArray()
                .Where(resource => Text(resource, "@type") == "Catalog/3.0.0")
                .Select(resource => Text(resource, "@id")).First();
            using var index = await GetAsync(catalogUrl, "index", deadline.Token);
            result.ObservedHorizon = Stamp(index.RootElement, "commitTimeStamp");
            if (through > result.ObservedHorizon)
                throw new InvalidDataException("Requested horizon has not been observed in the Catalog.");
            var pages = Items(index.RootElement)
                .Select(page => new Page(Text(page, "@id"), Stamp(page, "commitTimeStamp")))
                .OrderBy(page => page.End).ThenBy(page => page.Url, StringComparer.Ordinal).ToArray();
            result.IndexPages = pages.Length;
            DateTimeOffset previousEnd = DateTimeOffset.MinValue;
            foreach (Page page in pages)
            {
                // A page's maximum is not its minimum. Include the page crossing the upper
                // boundary, including equal-timestamp neighboring pages, then filter its items.
                if (previousEnd > through)
                    break;
                previousEnd = page.End;
                if (page.End <= from)
                    continue;
                if (result.Pages == maxPages)
                    throw new InvalidDataException("Catalog page budget exhausted.");
                using var document = await GetAsync(page.Url, "page", deadline.Token);
                result.Pages++;
                var entries = Items(document.RootElement)
                    .Select(item => new Entry(
                        Text(item, "@id"), Text(item, "nuget:id"), Text(item, "nuget:version"),
                        Text(item, "@type"), Text(item, "commitId"), Stamp(item, "commitTimeStamp")))
                    .OrderBy(item => item.Commit).ThenBy(item => item.Url, StringComparer.Ordinal)
                    .ToArray();
                result.PageItems += entries.Length;
                foreach (Entry entry in entries.Where(item => item.Commit > from && item.Commit <= through))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (entry.Kind is not ("nuget:PackageDetails" or "nuget:PackageDelete"))
                        throw new InvalidDataException("Unknown Catalog event type.");
                    bool? listed = null;
                    DateTimeOffset? published = null;
                    if (snapshots)
                    {
                        using var leaf = await GetAsync(entry.Url, "leaf", deadline.Token);
                        JsonElement root = leaf.RootElement;
                        if (!string.Equals(Text(root, "id"), entry.Id, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(Text(root, "version"), entry.Version, StringComparison.OrdinalIgnoreCase)
                            || Text(root, "catalog:commitId") != entry.CommitId
                            || Stamp(root, "catalog:commitTimeStamp") != entry.Commit
                            || !HasType(root.GetProperty("@type"), entry.Kind["nuget:".Length..]))
                            throw new InvalidDataException("Catalog leaf does not match its page event.");
                        published = Stamp(root, "published");
                        if (entry.Kind == "nuget:PackageDetails" && root.TryGetProperty("listed", out var value))
                            listed = value.GetBoolean();
                    }
                    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var row = new ChangeRow(entry.Id, entry.Version, entry.Kind,
                        entry.CommitId, entry.Commit, entry.Url, snapshots, listed, published, elapsed);
                    // This is the probe-consumer boundary, not stdout flush or either product UI.
                    observe(row);
                    result.Returned++;
                    result.FirstMs ??= elapsed;
                    result.LastMs = elapsed;
                    if (entry.Kind == "nuget:PackageDelete") result.Deletes++;
                    else result.Details++;
                    if (listed == false) result.UnlistedSnapshots++;
                    if (snapshots && entry.Kind == "nuget:PackageDetails" && listed is null)
                        result.UnknownListedSnapshots++;
                    coordinates.Add($"{entry.Id}@{entry.Version}");
                    projection.AppendData(Encoding.UTF8.GetBytes(
                        $"{entry.Url}\n{entry.Id}\n{entry.Version}\n{entry.Kind}\n"
                        + $"{entry.CommitId}\n{entry.Commit:O}\n{listed}\n{published:O}\n"));
                    if (result.Returned == take)
                    {
                        result.NthMs = elapsed;
                        result.Completion = "result-limit";
                        break;
                    }
                }
                if (result.Completion == "result-limit")
                    break;
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (result.Completion != "result-limit")
                result.Completion = "window-exhausted";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException
            or OperationCanceledException)
        {
            result.Completion = "failed";
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
        }
        result.UniqueCoordinates = coordinates.Count;
        result.ProjectionSha256 = Convert.ToHexString(projection.GetHashAndReset());
        result.Costs = _costs;
        result.TerminalMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return result;
    }

    private async Task<JsonDocument> GetAsync(string url, string stage, CancellationToken token)
    {
        // This research probe targets only nuget.org, not arbitrary configured feeds.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "api.nuget.org" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("The probe only requests HTTPS api.nuget.org URLs.");
        if (_requests == maxRequests)
            throw new InvalidDataException("Catalog request budget exhausted.");
        if (!_costs.TryGetValue(stage, out var cost))
            _costs.Add(stage, cost = new());
        _requests++;
        cost.Requests++;
        long started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using Stream body = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, token)) != 0)
            {
                cost.DecodedBytes += read;
                _bytes += read;
                if (buffer.Length + read > 16 * 1024 * 1024 || _bytes > 64 * 1024 * 1024)
                    throw new InvalidDataException("Catalog decoded-body budget exhausted.");
                buffer.Write(chunk, 0, read);
            }
            return HardenedJson.Parse(buffer.ToArray());
        }
        finally
        {
            cost.AcquisitionMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
    }

    private static JsonElement.ArrayEnumerator Items(JsonElement root)
    {
        var items = root.GetProperty("items");
        if (root.GetProperty("count").GetInt32() != items.GetArrayLength())
            throw new InvalidDataException("Catalog count differs from its items.");
        return items.EnumerateArray();
    }

    private static string Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString() is { Length: > 0 } text
            ? text : throw new InvalidDataException($"Missing text: {name}");

    private static DateTimeOffset Stamp(JsonElement value, string name) =>
        value.GetProperty(name).GetDateTimeOffset();

    private static bool HasType(JsonElement value, string type) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Any(item => item.GetString() == type)
            : value.GetString() == type;

    private sealed record Page(string Url, DateTimeOffset End);
    private sealed record Entry(
        string Url, string Id, string Version, string Kind, string CommitId, DateTimeOffset Commit);
}

sealed record ChangeRow(
    string Id, string Version, string Kind, string CommitId, DateTimeOffset Commit,
    string Leaf, bool Enriched, bool? Listed, DateTimeOffset? Published, double ElapsedMs)
{
    public string Record => "event";
}

sealed record Summary
{
    public string Record => "summary";
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Os { get; init; } = RuntimeInformation.OSDescription;
    public string Framework { get; init; } = RuntimeInformation.FrameworkDescription;
    public string Mode { get; init; } = "";
    public string Cache { get; init; } = "";
    public int Trial { get; init; }
    public DateTimeOffset FromExclusive { get; init; }
    public DateTimeOffset ThroughInclusive { get; init; }
    public DateTimeOffset? ObservedHorizon { get; set; }
    public int Take { get; init; }
    public int IndexPages { get; set; }
    public int Pages { get; set; }
    public int PageItems { get; set; }
    public int Returned { get; set; }
    public int UniqueCoordinates { get; set; }
    public int Details { get; set; }
    public int Deletes { get; set; }
    public int UnlistedSnapshots { get; set; }
    public int UnknownListedSnapshots { get; set; }
    public double? FirstMs { get; set; }
    public double? NthMs { get; set; }
    public double? LastMs { get; set; }
    public double TerminalMs { get; set; }
    public string Completion { get; set; } = "";
    public string? Error { get; set; }
    public string ProjectionSha256 { get; set; } = "";
    public Dictionary<string, Cost> Costs { get; set; } = [];
}

sealed class Cost
{
    public int Requests { get; set; }
    public long DecodedBytes { get; set; }
    public double AcquisitionMs { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChangeRow))]
[JsonSerializable(typeof(Summary))]
partial class ProbeJson : JsonSerializerContext;

static class ProbeTests
{
    public static async Task RunAsync()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        DateTimeOffset end = start.AddHours(1);
        const string prefix = "https://api.nuget.org/";
        const string service = """{"resources":[{"@type":"Catalog/3.0.0","@id":"https://api.nuget.org/catalog"}]}""";
        string[] entries =
        [
            Entry("lower", start, "nuget:PackageDetails"),
            Entry("details", start.AddMinutes(30), "nuget:PackageDetails"),
            Entry("delete", end, "nuget:PackageDelete"),
            Entry("later", end.AddMinutes(1), "nuget:PackageDetails"),
        ];
        var documents = new Dictionary<string, string>
        {
            [CatalogProbe.Service] = service,
            [prefix + "catalog"] = $$"""
                {"commitTimeStamp":"{{end.AddHours(1):O}}","count":3,"items":[
                {"@id":"{{prefix}}unused","commitTimeStamp":"{{end.AddHours(1):O}}"},
                {"@id":"{{prefix}}upper","commitTimeStamp":"{{end.AddMinutes(1):O}}"},
                {"@id":"{{prefix}}lower","commitTimeStamp":"{{start.AddMinutes(30):O}}"}]}
                """,
            [prefix + "lower"] = $$"""{"count":2,"items":[{{entries[1]}},{{entries[0]}}]}""",
            [prefix + "upper"] = $$"""{"count":2,"items":[{{entries[3]}},{{entries[2]}}]}""",
            [prefix + "details"] = $$"""
                {"@type":["PackageDetails","catalog:Permalink"],"id":"Example","version":"1.0.0",
                "catalog:commitId":"details","catalog:commitTimeStamp":"{{start.AddMinutes(30):O}}",
                "published":"1900-01-01T00:00:00Z","listed":false}
                """,
            [prefix + "delete"] = $$"""
                {"@type":"PackageDelete","id":"Example","version":"1.0.0",
                "catalog:commitId":"delete","catalog:commitTimeStamp":"{{end:O}}",
                "published":"1900-01-01T00:00:00Z"}
                """,
        };
        async Task<(Summary Result, List<ChangeRow> Rows)> Run(
            bool snapshots = false, int take = 10, int pages = 128, int requests = 512,
            DateTimeOffset? lower = null, DateTimeOffset? upper = null)
        {
            using var client = new HttpClient(new FixtureHandler(documents));
            var rows = new List<ChangeRow>();
            var result = await new CatalogProbe(client, pages, requests)
                .RunAsync(lower ?? start, upper ?? end, take, snapshots, rows.Add);
            return (result, rows);
        }
        var all = await Run();
        Require(all.Result is { Completion: "window-exhausted", Returned: 2, Details: 1,
            Deletes: 1, Pages: 2, NthMs: null, UniqueCoordinates: 1 }
            && all.Rows[0].Commit < all.Rows[1].Commit, "exclusive lower/inclusive upper, crossing page, repeated coordinate");
        var limit = await Run(take: 1);
        Require(limit.Result is { Completion: "result-limit", Returned: 1, Pages: 1 }
            && limit.Result.NthMs == limit.Result.FirstMs, "first result is not window exhaustion");
        var enriched = await Run(snapshots: true);
        Require(enriched.Result is { Completion: "window-exhausted", UnlistedSnapshots: 1 }
            && enriched.Rows[1].Listed is null && enriched.Rows[0].Published!.Value.Year == 1900,
            "unlisted snapshot and separate deletion, not publication inference");
        var empty = await Run(lower: start.AddMinutes(31), upper: end.AddMinutes(-1));
        Require(empty.Result is { Completion: "window-exhausted", Returned: 0,
            FirstMs: null, NthMs: null, LastMs: null }, "empty window");
        var pageBudget = await Run(pages: 1);
        Require(pageBudget.Result is { Completion: "failed", Returned: 1, NthMs: null }, "page budget after partial output");
        var requestBudget = await Run(requests: 1);
        Require(requestBudget.Result is { Completion: "failed", Returned: 0 }, "request budget");
        var horizon = await Run(upper: end.AddHours(2));
        Require(horizon.Result is { Completion: "failed", Pages: 0 }, "unobserved horizon");
        string originalIndex = documents[prefix + "catalog"];
        string originalLower = documents[prefix + "lower"];
        string tiedEntry = Entry("tie", end, "nuget:PackageDetails")
            .Replace("\"nuget:id\":\"Example\"", "\"nuget:id\":\"Other\"")
            .Replace("\"commitId\":\"tie\"", "\"commitId\":\"delete\"");
        documents[prefix + "catalog"] = originalIndex.Replace(
            $"\"commitTimeStamp\":\"{start.AddMinutes(30):O}\"", $"\"commitTimeStamp\":\"{end:O}\"");
        documents[prefix + "lower"] = $$"""{"count":3,"items":[{{tiedEntry}},{{entries[1]}},{{entries[0]}}]}""";
        var tied = await Run();
        Require(tied.Result is { Completion: "window-exhausted", Returned: 3, Deletes: 1 },
            "upper-bound commit ties across pages");
        documents[prefix + "catalog"] = originalIndex;
        documents[prefix + "lower"] = originalLower;
        documents[prefix + "details"] = new string(' ', 16 * 1024 * 1024 + 1);
        var oversized = await Run(snapshots: true);
        Require(oversized.Result is { Completion: "failed", Returned: 0 }, "decoded response budget");
        documents.Remove(prefix + "upper");
        var missingPage = await Run();
        Require(missingPage.Result is { Completion: "failed", Returned: 1 }, "selected page failure");
        documents[prefix + "details"] = """{"id":"Other"}""";
        var wrongIdentity = await Run(snapshots: true);
        Require(wrongIdentity.Result is { Completion: "failed", Returned: 0 }, "leaf identity binding");
        documents[prefix + "catalog"] = """{"count":0,"count":1,"items":[]}""";
        var malformed = await Run();
        Require(malformed.Result is { Completion: "failed", Returned: 0 }, "duplicate JSON properties");
        Console.WriteLine("Catalog probe: 12 offline boundary/failure cases passed.");
    }

    private static string Entry(string name, DateTimeOffset stamp, string type) => $$"""
        {"@id":"https://api.nuget.org/{{name}}","@type":"{{type}}","nuget:id":"Example",
        "nuget:version":"1.0.0","commitId":"{{name}}","commitTimeStamp":"{{stamp:O}}"}
        """;

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + description);
    }

    private sealed class FixtureHandler(Dictionary<string, string> documents) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            bool found = documents.TryGetValue(request.RequestUri!.AbsoluteUri, out string? json);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(json ?? ""),
            });
        }
    }
}
