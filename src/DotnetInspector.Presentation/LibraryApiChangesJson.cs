using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Sections;

namespace DotnetInspector.Presentation;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters =
    [
        typeof(InertStringJsonConverter),
        typeof(LibraryApiDiffEndpointIssueJsonConverter),
        typeof(LibraryApiTypeTableOutcomeJsonConverter),
        typeof(LibraryApiChangesTableOutcomeJsonConverter),
        typeof(LibraryApiTypeDocumentOutcomeJsonConverter),
        typeof(LibraryApiChangesDocumentOutcomeJsonConverter),
    ])]
[JsonSerializable(typeof(LibraryApiChangesTable<LibraryApiTypeChangeRow>), TypeInfoPropertyName = "TypeTable")]
[JsonSerializable(typeof(LibraryApiChangesTable<LibraryApiChangeRow>), TypeInfoPropertyName = "ChangesTable")]
[JsonSerializable(typeof(LibraryApiChangesDocument<LibraryApiTypeChangeRow>), TypeInfoPropertyName = "TypeDocument")]
[JsonSerializable(typeof(LibraryApiChangesDocument<LibraryApiChangeRow>), TypeInfoPropertyName = "ChangesDocument")]
[JsonSerializable(typeof(LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiTypeChangeRow>>), TypeInfoPropertyName = "TypeTableOutcome")]
[JsonSerializable(typeof(LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>), TypeInfoPropertyName = "ChangesTableOutcome")]
[JsonSerializable(typeof(LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiTypeChangeRow>>), TypeInfoPropertyName = "TypeDocumentOutcome")]
[JsonSerializable(typeof(LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiChangeRow>>), TypeInfoPropertyName = "ChangesDocumentOutcome")]
public sealed partial class LibraryApiChangesJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters =
    [
        typeof(InertStringJsonConverter),
        typeof(LibraryApiDiffEndpointIssueJsonConverter),
        typeof(LibraryApiTypeInventoryDocumentOutcomeJsonConverter),
    ])]
[JsonSerializable(typeof(LibraryApiChangesDocument<LibraryApiTypeInventoryRow>), TypeInfoPropertyName = "TypeInventoryDocument")]
[JsonSerializable(typeof(LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiTypeInventoryRow>>), TypeInfoPropertyName = "TypeInventoryDocumentOutcome")]
public sealed partial class LibraryApiTypeInventoryJsonContext : JsonSerializerContext;

public abstract class LibraryApiChangesOutcomeJsonConverter<T>
    : JsonConverter<LibraryApiChangesOutcome<T>>
{
    protected abstract JsonTypeInfo<T> ContentInfo { get; }
    protected abstract string ContentProperty { get; }

    public override LibraryApiChangesOutcome<T> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument parsed = JsonDocument.ParseValue(ref reader);
        JsonElement root = parsed.RootElement;
        if (root.GetProperty("outcome").GetString() == "available")
            return new LibraryApiChangesOutcome<T>.Available(
                root.GetProperty(ContentProperty).Deserialize(ContentInfo)
                    ?? throw new JsonException("The Library API representation is missing."));
        return root.Deserialize(LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome) switch
        {
            LibraryApiDiffOutcome.Unavailable unavailable =>
                new LibraryApiChangesOutcome<T>.Unavailable(unavailable),
            LibraryApiDiffOutcome.Rejected rejected =>
                new LibraryApiChangesOutcome<T>.Rejected(rejected),
            _ => throw new JsonException("Unknown Library API representation outcome."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer, LibraryApiChangesOutcome<T> value, JsonSerializerOptions options)
    {
        if (value is LibraryApiChangesOutcome<T>.Available available)
        {
            writer.WriteStartObject();
            writer.WriteString("outcome", "available");
            writer.WritePropertyName(ContentProperty);
            JsonSerializer.Serialize(writer, available.Content, ContentInfo);
            writer.WriteEndObject();
            return;
        }
        LibraryApiDiffOutcome failure = value switch
        {
            LibraryApiChangesOutcome<T>.Unavailable unavailable => unavailable.Failure,
            LibraryApiChangesOutcome<T>.Rejected rejected => rejected.Failure,
            _ => throw new InvalidOperationException("Unknown Library API representation outcome."),
        };
        JsonSerializer.Serialize(writer, failure, LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);
    }
}

public sealed class LibraryApiTypeTableOutcomeJsonConverter
    : LibraryApiChangesOutcomeJsonConverter<LibraryApiChangesTable<LibraryApiTypeChangeRow>>
{
    protected override JsonTypeInfo<LibraryApiChangesTable<LibraryApiTypeChangeRow>> ContentInfo
        => LibraryApiChangesJsonContext.Default.TypeTable;
    protected override string ContentProperty => "table";
}

public sealed class LibraryApiChangesTableOutcomeJsonConverter
    : LibraryApiChangesOutcomeJsonConverter<LibraryApiChangesTable<LibraryApiChangeRow>>
{
    protected override JsonTypeInfo<LibraryApiChangesTable<LibraryApiChangeRow>> ContentInfo
        => LibraryApiChangesJsonContext.Default.ChangesTable;
    protected override string ContentProperty => "table";
}

public sealed class LibraryApiTypeDocumentOutcomeJsonConverter
    : LibraryApiChangesOutcomeJsonConverter<LibraryApiChangesDocument<LibraryApiTypeChangeRow>>
{
    protected override JsonTypeInfo<LibraryApiChangesDocument<LibraryApiTypeChangeRow>> ContentInfo
        => LibraryApiChangesJsonContext.Default.TypeDocument;
    protected override string ContentProperty => "document";
}

public sealed class LibraryApiChangesDocumentOutcomeJsonConverter
    : LibraryApiChangesOutcomeJsonConverter<LibraryApiChangesDocument<LibraryApiChangeRow>>
{
    protected override JsonTypeInfo<LibraryApiChangesDocument<LibraryApiChangeRow>> ContentInfo
        => LibraryApiChangesJsonContext.Default.ChangesDocument;
    protected override string ContentProperty => "document";
}

public sealed class LibraryApiTypeInventoryDocumentOutcomeJsonConverter
    : LibraryApiChangesOutcomeJsonConverter<LibraryApiChangesDocument<LibraryApiTypeInventoryRow>>
{
    protected override JsonTypeInfo<LibraryApiChangesDocument<LibraryApiTypeInventoryRow>> ContentInfo
        => LibraryApiTypeInventoryJsonContext.Default.TypeInventoryDocument;
    protected override string ContentProperty => "document";
}
