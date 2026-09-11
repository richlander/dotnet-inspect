using System.Runtime.InteropServices.JavaScript;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ILInspector.JsExportSurface.Fixtures;

/// <summary>
/// A small, purpose-built <c>[JSExport]</c> surface exercising the cases TypeScript facade generation needs to
/// handle: a plain record DTO, array and nullable properties, a nested record, an async
/// (<c>Task&lt;string&gt;</c>) export, and a non-generic <c>Task</c> export. Deliberately not a
/// real product surface — kept minimal and stable as a regression fixture.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class FixtureExports
{
    static JsonTypeInfo<WidgetDto> UnrelatedWidgetTypeInfo =>
        FixtureJsonContext.Default.WidgetDto;

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
    public static async Task<string>
        GetWidgetSerializedBeforeAwait(string name)
    {
        string payload = JsonSerializer.Serialize(
            new WidgetDto(name, 0, [], null),
            FixtureJsonContext.Default.WidgetDto);
        await Task.Yield();
        await Task.Yield();
        return payload;
    }

    [JSExport]
    public static async Task<string>
        GetWidgetConditionallySerializedBeforeAwait(string name)
    {
        if (name.Length > 3)
        {
            name = JsonSerializer.Serialize(
                new WidgetDto(name, 0, [], null),
                FixtureJsonContext.Default.WidgetDto);
            await Task.Yield();
        }
        return name;
    }

    [JSExport]
    public static async Task<string> GetStringArrayAsyncAfterAwait(string value) =>
        JsonSerializer.Serialize(
            await GetStringArrayAsync(value),
            FixtureJsonContext.Default.StringArray);

    [JSExport]
    public static async Task<string> GetWidgetOrRawAfterAwait(
        bool raw)
    {
        await Task.Yield();
        return raw
            ? "{}"
            : JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null),
                FixtureJsonContext.Default.WidgetDto);
    }

    [JSExport]
    public static async Task<string> GetWidgetFromIncompleteFlowAfterAwait(
        string? cached)
    {
        await Task.Yield();
        string result;
        if (cached is null)
        {
            result = JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null),
                FixtureJsonContext.Default.WidgetDto);
        }
        else
        {
            result = cached;
        }

        return result;
    }

    [JSExport]
    public static async Task<string> GetWidgetThroughLocalAsync()
    {
        await Task.Yield();
        return await SerializeLocalAsync();

        static async Task<string> SerializeLocalAsync()
        {
            await Task.Yield();
            return JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null),
                FixtureJsonContext.Default.WidgetDto);
        }
    }

    [JSExport]
    public static byte[] EchoBytes(byte[] value) => value;

    [JSExport]
    public static void ReportValue(
        [JSMarshalAs<JSType.Function<JSType.Number>>]
        Action<int> callback) =>
        callback(42);

    [JSExport]
    public static void ReportValueAgain(
        [JSMarshalAs<JSType.Function<JSType.Number>>]
        Action<int> callback) =>
        callback(43);

    [JSExport]
    public static void ReportNullableText(
        [JSMarshalAs<JSType.Function<JSType.String>>]
        Action<string?> callback) =>
        callback(null);

    [JSExport]
    public static bool TransformValue(
        [JSMarshalAs<JSType.Function<
            JSType.Number,
            JSType.String,
            JSType.Boolean>>]
        Func<int, string, bool> callback) =>
        callback(42, "answer");

    [JSExport]
    public static void ObserveValues(
        [JSMarshalAs<JSType.Function<
            JSType.Number,
            JSType.String,
            JSType.Boolean>>]
        Action<int, string, bool> callback) =>
        callback(42, "answer", true);

    [JSExport]
    public static string GetRegisteredString(string value) =>
        JsonSerializer.Serialize(value, FixtureJsonContext.Default.String);

    static async Task<string[]> GetStringArrayAsync(string value)
    {
        await Task.Yield();
        return [value];
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
    public static string GetWidgetOrRawOk(bool asJson) =>
        asJson
            ? JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null),
                FixtureJsonContext.Default.WidgetDto)
            : "ok";

    [JSExport]
    public static string GetWidgetOrCached(string? cached) =>
        cached ?? JsonSerializer.Serialize(
            new WidgetDto("widget", 0, [], null),
            FixtureJsonContext.Default.WidgetDto);

    [JSExport]
    public static string GetWidgetOrCachedViaLocal(string? cached)
    {
        string result;
        if (cached is null)
        {
            result = JsonSerializer.Serialize(
                new WidgetDto("widget", 0, [], null),
                FixtureJsonContext.Default.WidgetDto);
        }
        else
        {
            result = cached;
        }

        return result;
    }

    [JSExport]
    public static string GetWidgetFromEitherJsonBranch(bool first) =>
        first
            ? JsonSerializer.Serialize(
                new WidgetDto("first", 0, [], null),
                FixtureJsonContext.Default.WidgetDto)
            : JsonSerializer.Serialize(
                new WidgetDto("second", 0, [], null),
                FixtureJsonContext.Default.WidgetDto);

    [JSExport]
    public static string GetWidgetArray() =>
        JsonSerializer.Serialize(
            new WidgetDto[] { new("widget", 0, [], null) },
            FixtureJsonContext.Default.WidgetDtoArray);

    [JSExport]
    public static string SerializeWidgetSideEffect()
    {
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(
            stream,
            new WidgetDto("widget", 0, [], null),
            FixtureJsonContext.Default.WidgetDto);
        return "ok";
    }

    [JSExport]
    public static string IgnoreSerializedWidget()
    {
        JsonSerializer.Serialize(
            new WidgetDto("widget", 0, [], null),
            FixtureJsonContext.Default.WidgetDto);
        return "ok";
    }

    [JSExport]
    public static Task<string> SetUnrelatedAsyncBuilder()
    {
        var builder = AsyncTaskMethodBuilder<string>.Create();
        builder.SetResult(JsonSerializer.Serialize(
            new WidgetDto("widget", 0, [], null),
            FixtureJsonContext.Default.WidgetDto));
        return Task.FromResult("ok");
    }

    [JSExport]
    public static string RoundTripWidgetWithRuntimeTypeInfo(string widgetJson)
    {
        JsonTypeInfo<WidgetDto> typeInfo =
            (JsonTypeInfo<WidgetDto>)new DefaultJsonTypeInfoResolver()
                .GetTypeInfo(
                    typeof(WidgetDto),
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    });
        WidgetDto widget = JsonSerializer.Deserialize(
            widgetJson,
            typeInfo)!;
        return JsonSerializer.Serialize(widget, typeInfo);
    }

    [JSExport]
    public static string RoundTripWidgetWithUnrelatedTypeInfo(string widgetJson)
    {
        WidgetDto widget = JsonSerializer.Deserialize(
            widgetJson,
            UnrelatedWidgetTypeInfo)!;
        return JsonSerializer.Serialize(
            widget,
            UnrelatedWidgetTypeInfo);
    }

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
    public static string GetCustomNamedGenerated() =>
        JsonSerializer.Serialize(
            new CustomNamedDto("generated"),
            CustomNamedJsonContext.Default.RegisteredCustomNamed);

    [JSExport]
    public static string GetCustomNamedHandwritten() =>
        JsonSerializer.Serialize(
            new CustomNamedDto("handwritten"),
            CustomNamedJsonContext.Default.Evil);

    [JSExport]
    public static string QueryPackage(string packageId) => packageId;
}

public sealed record WidgetDto(
    string Name,
    int Count,
    int[] Tags,
    WidgetOwner? Owner)
{
    public int this[int index] => Tags[index];
}

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

public sealed record ByteEnvelopeDto(byte[] Content, BytePayloadDto Payload);

public sealed record BytePayloadDto(byte[] Content);

public sealed record CustomNamedDto(string DisplayName);

public sealed record InternalContextPascalWidget(string Name, int Count);
public sealed record InternalContextCamelWidget(string Name, int Count);
public sealed record ConflictingPolicyWidget(string DisplayName);

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
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(WidgetCatalog))]
[JsonSerializable(typeof(WidgetSummary))]
[JsonSerializable(typeof(WidgetPermissionSummary))]
[JsonSerializable(typeof(WidgetPrioritySummary))]
[JsonSerializable(typeof(WidgetAudit))]
[JsonSerializable(typeof(ByteEnvelopeDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class FixtureJsonContext : JsonSerializerContext;

[JsonSerializable(
    typeof(CustomNamedDto),
    TypeInfoPropertyName = "RegisteredCustomNamed")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class CustomNamedJsonContext : JsonSerializerContext;

public sealed partial class CustomNamedJsonContext
{
    public JsonTypeInfo<CustomNamedDto> Evil =>
        (JsonTypeInfo<CustomNamedDto>)new DefaultJsonTypeInfoResolver()
            .GetTypeInfo(
                typeof(CustomNamedDto),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                });
}

[JsonSerializable(typeof(InternalContextPascalWidget))]
internal sealed partial class InternalContextFixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(InternalContextCamelWidget))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InternalContextCamelFixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ConflictingPolicyWidget))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class ConflictingCamelFixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ConflictingPolicyWidget))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public sealed partial class ConflictingSnakeFixtureJsonContext : JsonSerializerContext;

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

/// <summary>
/// Exports that reach their DTOs in exactly one wire direction, so that
/// <c>[JsonIgnore(Condition = WhenReading)]</c> and <c>Condition = WhenWriting</c>
/// can be observed as directional rather than as total exclusion. Compiled and
/// source-generator-backed on purpose: the decoded condition has to be the one
/// the real compiler and STJ generator emit.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class DirectionalFixtureExports
{
    [JSExport]
    public static string GetDirectionalOutput(string name) =>
        JsonSerializer.Serialize(
            new DirectionalOutputDto(name),
            DirectionalFixtureJsonContext.Default.DirectionalOutputDto);

    [JSExport]
    public static string SetDirectionalInput(string payloadJson)
    {
        DirectionalInputDto payload = JsonSerializer.Deserialize(
            payloadJson,
            DirectionalFixtureJsonContext.Default.DirectionalInputDto)!;
        return payload.Name;
    }

    [JSExport]
    public static int SetDirectionalSharedInput(string payloadJson) =>
        JsonSerializer.Deserialize(
            payloadJson,
            DirectionalFixtureJsonContext.Default
                .DirectionalSharedInputDto)!
            .Value;

    [JSExport]
    public static int SetDirectionalAccessorInput(string payloadJson) =>
        JsonSerializer.Deserialize(
            payloadJson,
            DirectionalFixtureJsonContext.Default
                .DirectionalAccessorInputDto)!
            .Id;

    [JSExport]
    public static string RoundTripDirectional(string payloadJson)
    {
        DirectionalRoundTripDto payload = JsonSerializer.Deserialize(
            payloadJson,
            DirectionalFixtureJsonContext.Default.DirectionalRoundTripDto)!;
        return JsonSerializer.Serialize(
            payload,
            DirectionalFixtureJsonContext.Default.DirectionalRoundTripDto);
    }
}

/// <summary>Reached only through a <c>WhenReading</c>-ignored member.</summary>
public sealed record DirectionalNote(string Text);

public sealed record DirectionalOutputDto(string Name)
{
    /// <summary>Written but never read: survives the serialize declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public DirectionalNote? ServerNote { get; init; }

    /// <summary>Read but never written: absent from the serialize declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string ClientSecret { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public DirectionalSharedInputDto? InputOnlyChild { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public DirectionalInactiveInputDto? InactiveInput { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string AlwaysPresent { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DefaultHidden { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NullHidden { get; init; }

    [JsonIgnore]
    public string NeverOnWire { get; init; } = "";
}

public sealed record DirectionalInputDto(string Name)
{
    /// <summary>Read but never written: survives the deserialize declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string ClientSecret { get; init; } = "";

    /// <summary>Written but never read: absent from the deserialize declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public string ServerNote { get; init; } = "";
}

public sealed class DirectionalSharedInputDto
{
    public int Value { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string Secret { get; set; } = "";
}

public sealed class DirectionalInactiveInputDto
{
    public int Value { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string Secret { get; set; } = "";
}

public sealed class DirectionalAccessorInputDto
{
    string writeOnly = "";

    public int Id { get; set; }
    public string PrivateGetter { private get; set; } = "";
    public string PrivateSetter { get; private set; } = "";
    public string WriteOnly { set => writeOnly = value; }

    public string ReadPrivateGetter() => PrivateGetter;
    public string ReadWriteOnly() => writeOnly;
}

/// <summary>Reached in both directions, so its split member has no single shape.</summary>
public sealed record DirectionalRoundTripDto(string Name)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public string ServerNote { get; init; } = "";
}

[JsonSerializable(typeof(DirectionalOutputDto))]
[JsonSerializable(typeof(DirectionalInputDto))]
[JsonSerializable(typeof(DirectionalSharedInputDto))]
[JsonSerializable(typeof(DirectionalAccessorInputDto))]
[JsonSerializable(typeof(DirectionalRoundTripDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class DirectionalFixtureJsonContext : JsonSerializerContext;

public sealed record ClosedGenericRootDto(string Name);

[SupportedOSPlatform("browser")]
public static partial class ClosedGenericRootFixtureExports
{
    [JSExport]
    public static string GetClosedGenericRoot() =>
        JsonSerializer.Serialize(
            new Dictionary<string, ClosedGenericRootDto>
            {
                ["first"] = new("value"),
            },
            ClosedGenericRootFixtureJsonContext.Default
                .DictionaryStringClosedGenericRootDto);
}

[JsonSerializable(typeof(Dictionary<string, ClosedGenericRootDto>))]
public sealed partial class ClosedGenericRootFixtureJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class PrimitiveRootFixtureExports
{
    [JSExport]
    public static string GetRegisteredInt() =>
        JsonSerializer.Serialize(
            42,
            PrimitiveRootFixtureJsonContext.Default.Int32);

    [JSExport]
    public static string GetRegisteredIntArray() =>
        JsonSerializer.Serialize(
            new[] { 1, 2 },
            PrimitiveRootFixtureJsonContext.Default.Int32Array);

    [JSExport]
    public static string GetRegisteredByteArray() =>
        JsonSerializer.Serialize(
            new byte[] { 0, 1 },
            PrimitiveRootFixtureJsonContext.Default.ByteArray);

    [JSExport]
    public static string GetRegisteredDecimal() =>
        JsonSerializer.Serialize(
            1.5m,
            PrimitiveRootFixtureJsonContext.Default.Decimal);

    [JSExport]
    public static string GetRegisteredDecimalArray() =>
        JsonSerializer.Serialize(
            new[] { 1.5m, 2.5m },
            PrimitiveRootFixtureJsonContext.Default.DecimalArray);

    [JSExport]
    public static string ReadRegisteredInt(string payload) =>
        JsonSerializer.Deserialize(
            payload,
            PrimitiveRootFixtureJsonContext.Default.Int32)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
}

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(decimal[]))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
public sealed partial class PrimitiveRootFixtureJsonContext : JsonSerializerContext;

public sealed record ContextSerializationOnlyDto(string Name)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public string ServerNote { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string ClientSecret { get; init; } = "";
}

public sealed record MetadataOverrideDto(string Name);

[SupportedOSPlatform("browser")]
public static partial class SourceGenerationModeFixtureExports
{
    [JSExport]
    public static string GetContextSerializationOnly(string name) =>
        JsonSerializer.Serialize(
            new ContextSerializationOnlyDto(name)
            {
                ServerNote = "server",
                ClientSecret = "client",
            },
            SourceGenerationModeFixtureJsonContext.Default
                .ContextSerializationOnlyDto);

    [JSExport]
    public static string SetContextSerializationOnly(string payload) =>
        JsonSerializer.Deserialize(
            payload,
            SourceGenerationModeFixtureJsonContext.Default
                .ContextSerializationOnlyDto)!
            .Name;

    [JSExport]
    public static string SetMetadataOverride(string payload) =>
        JsonSerializer.Deserialize(
            payload,
            SourceGenerationModeFixtureJsonContext.Default
                .MetadataOverrideDto)!
            .Name;
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(ContextSerializationOnlyDto))]
[JsonSerializable(
    typeof(MetadataOverrideDto),
    GenerationMode = JsonSourceGenerationMode.Metadata)]
public sealed partial class SourceGenerationModeFixtureJsonContext
    : JsonSerializerContext;
