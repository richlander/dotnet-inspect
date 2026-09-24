using System.Text.Json.Serialization;

namespace AttributeEnumFixtures;

public enum Wide : long
{
    Negative = -5_000_000_001L,
    Positive = 6_000_000_002L,
}

public enum Narrow : byte
{
    Value = 201,
}

public static class ProducerTruth
{
    public const string WideName = "AttributeEnumFixtures." + nameof(Wide);
    public const string NarrowName = "AttributeEnumFixtures." + nameof(Narrow);
    public const long Negative = (long)Wide.Negative;
    public const long Positive = (long)Wide.Positive;
    public const byte NarrowValue = (byte)Narrow.Value;
}

public sealed record JsonOptionsPayload(string? Name);

[JsonSerializable(typeof(JsonOptionsPayload))]
public partial class AbsentJsonOptionsContext : JsonSerializerContext;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(JsonOptionsPayload))]
public partial class DefaultJsonOptionsContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(JsonOptionsPayload))]
public partial class ExplicitNeverJsonOptionsContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonOptionsPayload))]
public partial class WhenWritingNullJsonOptionsContext : JsonSerializerContext;
