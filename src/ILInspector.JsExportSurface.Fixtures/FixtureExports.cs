using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.Fixtures;

/// <summary>
/// A small, purpose-built <c>[JSExport]</c> surface exercising the cases <c>tsbindgen</c> needs to
/// handle: a plain record DTO, array and nullable properties, a nested record, an async
/// (<c>Task&lt;string&gt;</c>) export, and a non-generic <c>Task</c> export. Deliberately not a
/// real product surface — kept minimal and stable as a regression fixture.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class FixtureExports
{
    [JSExport]
    public static string GetWidget(string name, int count) =>
        JsonSerializer.Serialize(
            new WidgetDto(name, count, [1, 2, 3], null),
            FixtureJsonContext.Default.WidgetDto);

    [JSExport]
    public static async Task<string> GetWidgetAsync(string name)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new WidgetDto(name, 0, [], new WidgetOwner("example")),
            FixtureJsonContext.Default.WidgetDto);
    }

    [JSExport]
    public static async Task Ping()
    {
        await Task.Yield();
    }

    // Exercises the Deserialize side of JsonWireContractResolver: a single JSON-string parameter
    // carrying a DTO the erased [JSExport] signature (string -> string) cannot reveal.
    [JSExport]
    public static string RenameWidget(string widgetJson, string newName)
    {
        WidgetDto widget = JsonSerializer.Deserialize(
            widgetJson, FixtureJsonContext.Default.WidgetDto)!;
        return JsonSerializer.Serialize(
            widget with { Name = newName }, FixtureJsonContext.Default.WidgetDto);
    }

    // Exercises the return-position ambiguity guard: two distinct DTOs are Serialize<T>'d for the
    // return value on different branches. JsonWireContractResolver has no branch/reachability
    // evidence to decide which one actually flows to the caller, so ReturnWireType must stay
    // unset rather than arbitrarily pick whichever call site DirectCalls happens to enumerate
    // first.
    [JSExport]
    public static string GetWidgetOrOwner(bool wantOwner) =>
        wantOwner
            ? JsonSerializer.Serialize(
                new WidgetOwner("example"), FixtureJsonContext.Default.WidgetOwner)
            : JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null), FixtureJsonContext.Default.WidgetDto);

    // Exercises container-shaped DTO resolution: the Serialize<T> type argument is WidgetDto[],
    // not WidgetDto itself. TypeRef.Name is empty for non-Definition kinds (GenericInstance,
    // SzArray, Array), so resolution must read ToDisplayString() to recover "WidgetDto[]" rather
    // than silently collapsing to an empty/unknown type.
    [JSExport]
    public static string GetWidgetArray() =>
        JsonSerializer.Serialize(
            new WidgetDto[] { new("widget", 0, [], null) },
            FixtureJsonContext.Default.WidgetDtoArray);

    // Exercises enum discovery: WidgetSummary's Status property references WidgetStatus, an enum
    // reachable only transitively (never independently registered on FixtureJsonContext).
    [JSExport]
    public static string GetWidgetSummary() =>
        JsonSerializer.Serialize(
            new WidgetSummary("widget", WidgetStatus.Published),
            FixtureJsonContext.Default.WidgetSummary);

    // Exercises [Flags]-enum wire fidelity: STJ's string converter serializes a combination as a
    // comma-joined list of names (e.g. "Read, Write"), not a single declared name, so a closed
    // union is wrong here even though the converter is present.
    [JSExport]
    public static string GetWidgetPermissionSummary() =>
        JsonSerializer.Serialize(
            new WidgetPermissionSummary("widget", WidgetPermission.Read | WidgetPermission.Write),
            FixtureJsonContext.Default.WidgetPermissionSummary);

    // Exercises the absence of JsonStringEnumConverter: without it, STJ serializes the enum by its
    // numeric underlying value, so the wire shape must be `number`, not a string-literal union.
    [JSExport]
    public static string GetWidgetPrioritySummary() =>
        JsonSerializer.Serialize(
            new WidgetPrioritySummary("widget", WidgetPriority.High),
            FixtureJsonContext.Default.WidgetPrioritySummary);

    // Exercises [JsonInclude]: a non-public property explicitly opted into the wire contract must
    // still be emitted, even though it looks (via non-null Accessibility) like the same shape as a
    // record's compiler-synthesized EqualityContract getter.
    [JSExport]
    public static string GetWidgetAudit() =>
        JsonSerializer.Serialize(
            new WidgetAudit("widget", "system"),
            FixtureJsonContext.Default.WidgetAudit);
}

public sealed record WidgetDto(string Name, int Count, int[] Tags, WidgetOwner? Owner);

public sealed record WidgetOwner(string DisplayName);

// Exercises multi-argument generic extraction (a JSON-map shape whose value type is a locally
// declared record, not the first type argument) — see JsExportSurfaceBuilder's
// ExtractCandidateTypeNames, which must walk every top-level generic argument, not just the first.
public sealed record WidgetCatalog(Dictionary<string, WidgetOwner> OwnersByKey);

// Exercises enum discovery: a nested-record-referenced enum backed by JsonStringEnumConverter
// (STJ serializes it as its member name, a string) must be routed to JsExportSurface.Enums and
// rendered as a TS string-literal union, not silently treated as a record with zero properties.
[JsonConverter(typeof(JsonStringEnumConverter<WidgetStatus>))]
public enum WidgetStatus
{
    Draft,
    Published,
    Archived,
}

public sealed record WidgetSummary(string Name, WidgetStatus Status);

// A [Flags] enum backed by JsonStringEnumConverter: STJ serializes a combination as a
// comma-joined string of member names, which is not representable as a single-member union.
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<WidgetPermission>))]
public enum WidgetPermission
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed record WidgetPermissionSummary(string Name, WidgetPermission Permissions);

// Deliberately has no JsonConverter: STJ serializes this by its numeric underlying value.
public enum WidgetPriority
{
    Low,
    Medium,
    High,
}

public sealed record WidgetPrioritySummary(string Name, WidgetPriority Priority);

// Exercises [JsonInclude] on a non-public property: it must still be emitted as wire-contract
// shape, distinct from a record's compiler-synthesized (and always non-public) EqualityContract.
public sealed record WidgetAudit(string Name)
{
    [JsonInclude]
    internal string LastEditedBy { get; init; } = "";

    public WidgetAudit(string name, string lastEditedBy) : this(name)
    {
        LastEditedBy = lastEditedBy;
    }
}


[JsonSerializable(typeof(WidgetDto))]
[JsonSerializable(typeof(WidgetDto[]))]
[JsonSerializable(typeof(WidgetCatalog))]
[JsonSerializable(typeof(WidgetSummary))]
[JsonSerializable(typeof(WidgetPermissionSummary))]
[JsonSerializable(typeof(WidgetPrioritySummary))]
[JsonSerializable(typeof(WidgetAudit))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class FixtureJsonContext : JsonSerializerContext;
