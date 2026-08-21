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

    [JSExport]
    public static string RenameWidget(string widgetJson, string newName)
    {
        WidgetDto widget = JsonSerializer.Deserialize(
            widgetJson, FixtureJsonContext.Default.WidgetDto)!;
        return JsonSerializer.Serialize(
            widget with { Name = newName }, FixtureJsonContext.Default.WidgetDto);
    }

    [JSExport]
    public static string GetWidgetOrOwner(bool wantOwner) =>
        wantOwner
            ? JsonSerializer.Serialize(
                new WidgetOwner("example"), FixtureJsonContext.Default.WidgetOwner)
            : JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null), FixtureJsonContext.Default.WidgetDto);

    [JSExport]
    public static string GetWidgetArray() =>
        JsonSerializer.Serialize(
            new WidgetDto[] { new("widget", 0, [], null) },
            FixtureJsonContext.Default.WidgetDtoArray);

    [JSExport]
    public static string GetWidgetSummary() =>
        JsonSerializer.Serialize(
            new WidgetSummary("widget", WidgetStatus.Published),
            FixtureJsonContext.Default.WidgetSummary);

    [JSExport]
    public static string GetWidgetPermissionSummary() =>
        JsonSerializer.Serialize(
            new WidgetPermissionSummary("widget", WidgetPermission.Read | WidgetPermission.Write),
            FixtureJsonContext.Default.WidgetPermissionSummary);

    [JSExport]
    public static string GetWidgetPrioritySummary() =>
        JsonSerializer.Serialize(
            new WidgetPrioritySummary("widget", WidgetPriority.High),
            FixtureJsonContext.Default.WidgetPrioritySummary);

    [JSExport]
    public static string GetWidgetAudit() =>
        JsonSerializer.Serialize(
            new WidgetAudit("widget", "system", string.Empty),
            FixtureJsonContext.Default.WidgetAudit);

    [JSExport]
    public static string QueryPackage(string packageId) => packageId;
}

public sealed record WidgetDto(string Name, int Count, int[] Tags, WidgetOwner? Owner);

public sealed record WidgetOwner(string DisplayName);

public sealed record WidgetCatalog(Dictionary<string, WidgetOwner> OwnersByKey);

[JsonConverter(typeof(JsonStringEnumConverter<WidgetStatus>))]
public enum WidgetStatus
{
    Draft,
    Published,
    Archived,
}

public sealed record WidgetSummary(string Name, WidgetStatus Status);

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<WidgetPermission>))]
public enum WidgetPermission
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed record WidgetPermissionSummary(string Name, WidgetPermission Permissions);

public enum WidgetPriority
{
    Low,
    Medium,
    High,
}

public sealed record WidgetPrioritySummary(string Name, WidgetPriority Priority);

public sealed record WidgetAudit(string Name)
{
    [JsonPropertyName("wire_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("")]
    public string EmptyWireName { get; init; } = "";

    [JsonPropertyName("display-name")]
    public string DisplayNameWithDash { get; init; } = "";

    [JsonIgnore]
    public string IgnoredAtWire { get; init; } = "";

    [JsonInclude]
    internal string LastEditedBy { get; init; } = "";

    public WidgetAudit(string name, string lastEditedBy, string displayNameWithDash) : this(name)
    {
        DisplayName = name;
        EmptyWireName = string.Empty;
        DisplayNameWithDash = displayNameWithDash;
        IgnoredAtWire = lastEditedBy;
        LastEditedBy = lastEditedBy;
    }
}

public sealed record InternalContextPascalWidget(string Name, int Count);
public sealed record InternalContextCamelWidget(string Name, int Count);

[SupportedOSPlatform("browser")]
public static partial class InternalContextFixtureExports
{
    [JSExport]
    public static string GetInternalContextWidget(string name) =>
        JsonSerializer.Serialize(
            new InternalContextPascalWidget(name, 1),
            InternalContextFixtureJsonContext.Default.InternalContextPascalWidget);

    [JSExport]
    public static string GetInternalContextCamelWidget(string name) =>
        JsonSerializer.Serialize(
            new InternalContextCamelWidget(name, 2),
            InternalContextCamelFixtureJsonContext.Default.InternalContextCamelWidget);
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

[JsonSerializable(typeof(InternalContextPascalWidget))]
internal sealed partial class InternalContextFixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(InternalContextCamelWidget))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InternalContextCamelFixtureJsonContext : JsonSerializerContext;

public sealed record NeedsUnmappedTypeFixture(Guid Unmapped);

[SupportedOSPlatform("browser")]
public static partial class NeedsUnmappedTypeFixtureExports
{
    [JSExport]
    public static string GetNeedsUnmappedType() =>
        JsonSerializer.Serialize(
            new NeedsUnmappedTypeFixture(Guid.Empty),
            NeedsUnmappedTypeFixtureJsonContext.Default.NeedsUnmappedTypeFixture);
}

[JsonSerializable(typeof(NeedsUnmappedTypeFixture))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class NeedsUnmappedTypeFixtureJsonContext : JsonSerializerContext;
