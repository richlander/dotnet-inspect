using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Source;

public sealed record BrowserMemberBodyDiffRequest(
    string PackageId, string BeforeVersion, string AfterVersion, string Framework,
    string RuntimeIdentifier, string CompileAssetId, string Generation,
    string? InventoryId = null, string? MemberId = null);

public sealed record BrowserMemberBodyDiffResult(
    string Kind, BrowserMemberBodyDiffInventory? Inventory,
    BrowserMemberBodyDiffDocument? Document, string? Detail,
    InspectionEnvelope<JsonElement>? Inspection = null,
    BrowserMemberBodyDiffMissingCounterpart? MissingCounterpart = null);

public sealed record BrowserMemberBodyDiffMissingCounterpart(
    BrowserMemberBodyDiffEndpoint Endpoint,
    string Version,
    string RuntimeIdentifier,
    string[] AvailableRuntimeIdentifiers);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserMemberBodyDiffEndpoint>))]
public enum BrowserMemberBodyDiffEndpoint
{
    Before,
    After,
}

public sealed record BrowserMemberBodyDiffInventory(
    string Id, bool IsComplete, BrowserMemberBodyCoverage[] Coverage,
    BrowserMemberBodyType[] Types, BrowserMemberBodyMember[] Destinations);

public sealed record BrowserMemberBodyCoverage(
    string Mechanism, int Evaluated, int Exact, int Changed,
    int Unavailable, int Incomplete, int Failed);

public sealed record BrowserMemberBodyType(
    string Identity, string Display, bool CanNavigate,
    BrowserMemberBodyMember[] Members);

public sealed record BrowserMemberBodyMember(
    string Id, string Display, string Outcome, string[] Mechanisms,
    string? Fingerprint, string? Selector, int? MethodToken, string? IdentityFailure, string? TypeIdentity);

public sealed record BrowserMemberBodyDiffDocument(
    string Subject, BrowserMemberBodyMedium[] Media,
    string BeforeOutcome, string AfterOutcome, string? BeforeDetail, string? AfterDetail);

public sealed record BrowserMemberBodyMedium(
    string Medium, BrowserSourceDiff? Diff, string? Limit, string? BeforeText = null, string? AfterText = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserMemberBodyDiffRequest))]
[JsonSerializable(typeof(BrowserMemberBodyDiffResult))]
internal sealed partial class BrowserMemberBodyDiffJsonContext : JsonSerializerContext;
