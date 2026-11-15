#:project ../src/DotnetInspect.Web.Interop.Catalog/DotnetInspect.Web.Interop.Catalog.csproj
#:property EnablePreviewFeatures=true
#:package Markout

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Catalog;
using Markout;

// Invoke the production Browser activation export, including its complete
// Navigation wire response. Run each sample in a fresh NativeAOT process.
// The input is the exact encoded `w` packet from a shared Workspace URL.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: <probe> <packet-file>");
    return 2;
}

string packet = File.ReadAllText(args[0]).Trim();
Stopwatch watch = Stopwatch.StartNew();
#pragma warning disable CA1416 // Deliberate managed-host invocation of the Browser export.
string json = await CatalogExports.ActivateRetainedWorkspaceDefinition(
    "restoration-measurement", "Restoration measurement", "/", packet);
#pragma warning restore CA1416
using JsonDocument document = JsonDocument.Parse(json);
JsonElement result = document.RootElement;
if (result.GetProperty("status").GetString() != "activated")
{
    Console.Error.WriteLine(json);
    return 1;
}
JsonElement posting = result.GetProperty("posting");
JsonElement navigation = posting.GetProperty("navigation");
string session = navigation.GetProperty("authority").GetProperty("session").GetString()!;
// Session identity is deliberately fresh. Normalize only that owner-issued
// nonce; all descriptor, action, ordering, failure, and content fields remain.
string digest = Convert.ToHexStringLower(SHA256.HashData(
    Encoding.UTF8.GetBytes(json.Replace(session, "SESSION", StringComparison.Ordinal))));
JsonElement snapshot = navigation.GetProperty("snapshot");
watch.Stop();
var report = new RestorationMeasurements(
    [new(watch.Elapsed.TotalMilliseconds, Encoding.UTF8.GetByteCount(json),
        snapshot.GetProperty("types").GetArrayLength(),
        snapshot.GetProperty("members").GetArrayLength(), digest)]);
MarkoutSerializer.Serialize(report, Console.Out, new TableFormatter(),
    RestorationMeasurementContext.Default,
    new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv });
return 0;

public sealed record RestorationMeasurements(List<RestorationMeasurement> Results);

public sealed record RestorationMeasurement(
    double Ms, int JsonBytes, int Types, int Members, string Sha256);

[MarkoutContext(typeof(RestorationMeasurements))]
[MarkoutContext(typeof(RestorationMeasurement))]
public partial class RestorationMeasurementContext : MarkoutSerializerContext { }
