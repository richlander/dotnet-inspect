using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.TypeScriptFixtures;

public union WidgetSelection(WidgetDto, string);

public union FlagSelection(bool?, WidgetDto);

public union OutcomeSelection(WidgetSelection, bool);

public union KindSelection(WidgetKind, string);

// Reference entries inside union-case collections stay conservatively
// nullable: signature-only case facts cannot retain nested NRT annotations,
// and a producer can write null into either shape.
public union CollectionSelection(
    WidgetDto[],
    IReadOnlyDictionary<string, WidgetDto?>,
    int);

public union Boxed<TValue>(TValue, string);

// The int alternative keeps the closed byte[] use unambiguous for the read
// classifier while its wire form stays a Base64 JSON string.
public union Wrapped<TValue>(TValue, int);

public enum WidgetKind
{
    Basic,
    Deluxe,
}

public sealed record SelectionEnvelope(
    WidgetSelection Result,
    WidgetSelection[] Items,
    IReadOnlyDictionary<string, WidgetSelection> ByName,
    OutcomeSelection Outcome,
    KindSelection Kind,
    WidgetKind DeclaredKind,
    Boxed<int> Count,
    Boxed<WidgetDto> Widget,
    Boxed<WidgetDto[]> Group,
    Wrapped<byte[]> Blob);

[JsonSerializable(typeof(WidgetSelection))]
[JsonSerializable(typeof(FlagSelection))]
[JsonSerializable(typeof(OutcomeSelection))]
[JsonSerializable(typeof(KindSelection))]
[JsonSerializable(typeof(CollectionSelection))]
[JsonSerializable(typeof(Boxed<int>))]
[JsonSerializable(typeof(Boxed<WidgetDto>))]
[JsonSerializable(typeof(Wrapped<byte[]>))]
[JsonSerializable(typeof(SelectionEnvelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class UnionFixtureJsonContext : JsonSerializerContext;
