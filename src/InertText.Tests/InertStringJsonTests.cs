using System.Text.Json;
using System.Text.Json.Serialization;
using InertText.Json;

namespace InertText.Tests;

public sealed class InertStringJsonTests
{
    [Fact]
    public void Serialize_WritesTheEncodedValueAsAJsonString()
    {
        var value = new InertString(TextPolicy.Field, "line\u202Egpj");

        string json = JsonSerializer.Serialize(value);

        Assert.Equal("\"line\\\\u202Egpj\"", json);
    }

    [Fact]
    public void Converter_IsAttachedToTheCurrencyType()
    {
        JsonConverterAttribute? attribute =
            typeof(InertString).GetCustomAttributes(
                    typeof(JsonConverterAttribute),
                    inherit: false)
                .Cast<JsonConverterAttribute>()
                .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(InertStringJsonConverter), attribute.ConverterType);
    }

    [Fact]
    public void Deserialize_RejectsAStringWithoutAnExplicitPolicy()
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => JsonSerializer.Deserialize<InertString>("\"contained\""));

        Assert.Contains(
            "explicit text policy",
            error.ToString(),
            StringComparison.Ordinal);
    }
}
