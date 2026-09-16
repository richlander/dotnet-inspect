using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

#pragma warning disable CS0414
internal sealed class SourceGeneratedJsonIncludeHiddenTypeFixture
{
    public string Public { get; set; } = "public";

    [JsonInclude]
    private HiddenValue HiddenProperty { get; set; } = HiddenValue.Value;

    [JsonInclude]
    private HiddenValue HiddenField = HiddenValue.Value;

    enum HiddenValue
    {
        Value,
    }

    public int Read() => (int)HiddenField + (int)HiddenProperty;
}
#pragma warning restore CS0414

internal sealed class ConverterControlledAccessibleEnumFixture
{
    [JsonConverter(
        typeof(
            JsonStringEnumConverter<
                ConverterControlledAccessibleEnum>))]
    public ConverterControlledAccessibleEnum ConvertedField
        { get; set; } =
        ConverterControlledAccessibleEnum.One;
}

public enum ConverterControlledAccessibleEnum
{
    One,
    Two,
}

#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
internal sealed partial class NestedContextJsonIncludeHiddenTypeFixture
{
    [JsonInclude]
    private HiddenValue HiddenField = HiddenValue.Value;

    private enum HiddenValue
    {
        Value,
    }

    public int Read() => (int)HiddenField;

    public static string SerializeValue() =>
        JsonSerializer.Serialize(
            new NestedContextJsonIncludeHiddenTypeFixture(),
            NestedContextJsonContext.Default
                .NestedContextJsonIncludeHiddenTypeFixture);

    [JsonSerializable(
        typeof(NestedContextJsonIncludeHiddenTypeFixture))]
    private sealed partial class NestedContextJsonContext
        : JsonSerializerContext;
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
[JsonSerializable(typeof(SourceGeneratedJsonIncludeHiddenTypeFixture))]
internal sealed partial class ControlPropertyNameFixtureJsonContext : JsonSerializerContext;

internal sealed partial class ControlPropertyNameFixtureJsonContext
{
    public JsonTypeInfo<HandwrittenContextPropertyFixture> Handwritten =>
        throw new NotSupportedException();
}

internal sealed class HandwrittenContextPropertyFixture
{
    public string Value { get; set; } = "";
}

internal abstract class InheritedWireBaseFixture
{
    public string Id { get; set; } = "";
}

internal sealed class InheritedWireDerivedFixture
    : InheritedWireBaseFixture
{
    public int Count { get; set; }
}

internal sealed class NumberHandlingWireFixture
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public int Count { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
internal sealed class TypeNumberHandlingWireFixture
{
    public int Count { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(PolymorphicWireDerivedFixture), "derived")]
internal abstract class PolymorphicWireFixture
{
    public string Name { get; set; } = "";
}

internal sealed class PolymorphicWireDerivedFixture
    : PolymorphicWireFixture
{
    public bool Enabled { get; set; }
}

internal sealed class ExtensionDataWireFixture
{
    public string Id { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}
