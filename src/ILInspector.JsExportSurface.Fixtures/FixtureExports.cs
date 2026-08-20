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
}

public sealed record WidgetDto(string Name, int Count, int[] Tags, WidgetOwner? Owner);

public sealed record WidgetOwner(string DisplayName);

// Exercises multi-argument generic extraction (a JSON-map shape whose value type is a locally
// declared record, not the first type argument) — see JsExportSurfaceBuilder's
// ExtractCandidateTypeNames, which must walk every top-level generic argument, not just the first.
public sealed record WidgetCatalog(Dictionary<string, WidgetOwner> OwnersByKey);

[JsonSerializable(typeof(WidgetDto))]
[JsonSerializable(typeof(WidgetDto[]))]
[JsonSerializable(typeof(WidgetCatalog))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class FixtureJsonContext : JsonSerializerContext;
