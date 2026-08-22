using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.Tests;

internal sealed record ControlPropertyNameFixture
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("line\nbreak\r\t\u0001")]
    public string Value { get; init; } = "";
}

internal sealed class ControlFieldPropertyNameFixture
{
    [JsonInclude]
    [JsonPropertyName("field\nbreak\r\t\u0001")]
    public string Value = "";
}

internal sealed class BackingFieldControlPropertyNameFixture
{
    [field: JsonPropertyName("backing\nbreak\r\t\u0001")]
    public string Value { get; set; } = "";
}

internal sealed class SafeBackingFieldPropertyNameFixture
{
    [field: JsonPropertyName("not_the_property_name")]
    public string Value { get; set; } = "";
}

internal sealed class FilteredEventControlPropertyNameFixture
{
    [field: JsonPropertyName("event\nbreak\r\t\u0001")]
    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class JsonIncludedFieldRootFixture
{
    [JsonInclude]
    public JsonIncludedFieldNestedFixture Child = new();
}

internal sealed class JsonIncludedFieldNestedFixture
{
    public string Value { get; set; } = "";
}

internal sealed class GetterAccessibilityFixture
{
    private string _setterOnly = "";

    public string SetterOnlyAtWire { private get; set; } = "";

    [JsonInclude]
    public string IncludedPrivateGetter { private get; set; } = "";

    [JsonInclude]
    public string IncludedInternalGetter { internal get; set; } = "";

    public string PublicGetter { get; private set; } = "";

    [JsonInclude]
    public string NoGetter
    {
        set => _setterOnly = value;
    }
}

#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
internal sealed class SourceGeneratedJsonIncludeAccessibilityFixture
{
    [JsonInclude]
    public string IncludedPrivateGetter { private get; set; } = "";

    [JsonInclude]
    public string IncludedInternalGetter { internal get; set; } = "";

    [JsonInclude]
    internal string IncludedInternalField = "internal-field";

    [JsonInclude]
    private string IncludedPrivateField = "private-field";
}
#pragma warning restore SYSLIB1038
#pragma warning restore CS0414

internal sealed class JsonIgnoreNeverFixture
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Included { get; set; } = "";

    [JsonIgnore]
    public string Excluded { get; set; } = "";
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(string))]
internal sealed partial class AdditionalOptionsJsonContext
    : JsonSerializerContext;

internal sealed class MemberJsonConverterFixture
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NamedEnumFixture Value { get; set; }

    [JsonIgnore]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NamedEnumFixture Ignored { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum NamedEnumFixture
{
    [JsonStringEnumMemberName("wire \"value\"\n\u2028")]
    Value,

    [JsonStringEnumMemberName("duplicate")]
    FirstDuplicate,

    [JsonStringEnumMemberName("duplicate")]
    SecondDuplicate,
}

internal enum OtherEnumFixture
{
    Value,
}

[JsonConverter(typeof(JsonStringEnumConverter<OtherEnumFixture>))]
internal enum MismatchedStringEnumConverterFixture
{
    Value,
}

internal enum ControlPropertyNameEnumFixture
{
    [JsonPropertyName("enum\nbreak\r\t\u0001")]
    Value,
}

[JsonSerializable(typeof(ControlFieldPropertyNameFixture))]
[JsonSerializable(typeof(SourceGeneratedJsonIncludeAccessibilityFixture))]
internal sealed partial class ControlPropertyNameFixtureJsonContext : JsonSerializerContext;
