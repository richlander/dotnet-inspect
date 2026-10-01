using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Vocabulary;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace DotnetInspector.JsonSchema;

public readonly record struct JsonSchemaContractIdentity
{
    public JsonSchemaContractIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct JsonSchemaIdentity
{
    public JsonSchemaIdentity(string value)
    {
        ValidateDigest(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    internal static void ValidateDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 71
            || !value.StartsWith("sha256:", StringComparison.Ordinal)
            || value.AsSpan(7).IndexOfAnyExcept(
                "0123456789abcdef".AsSpan()) >= 0)
        {
            throw new ArgumentException(
                "An exact identity must be 'sha256:' followed by "
                + "64 lowercase hexadecimal digits.",
                parameterName);
        }
    }
}

public readonly record struct JsonSchemaDescriptorIdentity
{
    public JsonSchemaDescriptorIdentity(string value)
    {
        JsonSchemaIdentity.ValidateDigest(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record JsonSchemaVocabularyBinding(
    string SchemaLocation,
    VocabularyIdentity Vocabulary,
    VocabularyTermIdentity Term);

public sealed record JsonSchemaVocabularyDescriptor(
    int FormatVersion,
    JsonSchemaContractIdentity Contract,
    JsonWireDirection Direction,
    string Dialect,
    JsonSchemaIdentity SchemaIdentity,
    JsonSchemaDescriptorIdentity DescriptorIdentity,
    VocabularyCatalogIdentity VocabularyCatalog,
    VocabularySnapshotIdentity VocabularySnapshotIdentity,
    JsonElement Schema,
    ImmutableArray<JsonSchemaVocabularyBinding> Bindings);

public abstract record JsonSchemaContractRoot
{
    private JsonSchemaContractRoot()
    {
    }

    public sealed record Object(ApiType Type) : JsonSchemaContractRoot;

    public sealed record Positional(JsonPositionalRowContract Row)
        : JsonSchemaContractRoot;
}

public sealed class JsonPositionalRowContract
{
    public JsonPositionalRowContract(
        IEnumerable<JsonPositionalRowSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        Slots = [.. slots];
        if (Slots.Length == 0)
        {
            throw new ArgumentException(
                "A positional row must declare at least one slot.",
                nameof(slots));
        }
        if (Slots.Select(slot => slot.NodeIdentity)
            .Distinct(StringComparer.Ordinal)
            .Count() != Slots.Length)
        {
            throw new ArgumentException(
                "Positional-row node identities must be unique.",
                nameof(slots));
        }
    }

    public ImmutableArray<JsonPositionalRowSlot> Slots { get; }
}

public sealed class JsonPositionalRowSlot
{
    public JsonPositionalRowSlot(
        string nodeIdentity,
        ApiTypeShape shape,
        bool allowsNull,
        bool isDisplayable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeIdentity);
        NodeIdentity = nodeIdentity;
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        AllowsNull = allowsNull;
        IsDisplayable = isDisplayable;
    }

    public string NodeIdentity { get; }

    public ApiTypeShape Shape { get; }

    public bool AllowsNull { get; }

    public bool IsDisplayable { get; }
}

public abstract record JsonSchemaBindingTarget
{
    private JsonSchemaBindingTarget()
    {
    }

    public sealed record ObjectMember(
        ApiType DeclaringType,
        ApiMember Member)
        : JsonSchemaBindingTarget;

    public sealed record PositionalSlot(
        JsonPositionalRowContract Row,
        JsonPositionalRowSlot Slot)
        : JsonSchemaBindingTarget;
}

public sealed record JsonSchemaBindingDeclaration(
    JsonSchemaBindingTarget Target,
    VocabularyTermIdentity Term);

public enum JsonSchemaBindingCoverage
{
    DeclaredOnly,
    CompleteDisplaySlots,
}

public sealed class JsonSchemaContractDeclaration
{
    public JsonSchemaContractDeclaration(
        JsonSchemaContractIdentity identity,
        JsonWireDirection direction,
        JsonSchemaContractRoot root,
        IEnumerable<JsonSchemaBindingDeclaration>? bindings = null,
        JsonSchemaBindingCoverage bindingCoverage =
            JsonSchemaBindingCoverage.DeclaredOnly)
    {
        if (direction is not JsonWireDirection.Serialize
            and not JsonWireDirection.Deserialize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "A schema contract requires one wire direction.");
        }

        Identity = identity;
        Direction = direction;
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Bindings = [.. bindings ?? []];
        BindingCoverage = bindingCoverage;
    }

    public JsonSchemaContractIdentity Identity { get; }

    public JsonWireDirection Direction { get; }

    public JsonSchemaContractRoot Root { get; }

    public ImmutableArray<JsonSchemaBindingDeclaration> Bindings { get; }

    public JsonSchemaBindingCoverage BindingCoverage { get; }
}

public sealed class JsonSchemaVocabularyException(
    string location,
    string reason)
    : Exception($"{location}: {reason}.")
{
    public string Location { get; } = location;

    public string Reason { get; } = reason;
}
