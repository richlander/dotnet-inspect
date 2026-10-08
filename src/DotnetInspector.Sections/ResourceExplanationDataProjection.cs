using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

/// <summary>
/// Lowers a validated explanation Document to compact resource data.
/// Host bindings supply usable navigation addresses; declarations stay in the catalog.
/// </summary>
public static class ResourceExplanationDataProjection
{
    public static JsonElement Create(
        ResourceExplanationDocument document,
        Func<ResourcePath, string> bindAddress)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bindAddress);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var projection = new Projection(document, bindAddress, writer);
            projection.WriteResource(document.Resources[0]);
        }
        using JsonDocument result = JsonDocument.Parse(stream.ToArray());
        return result.RootElement.Clone();
    }

    private sealed class Projection(
        ResourceExplanationDocument document,
        Func<ResourcePath, string> bindAddress,
        Utf8JsonWriter writer)
    {
        private readonly Dictionary<ExplanationFactIdentity, ExplanationFactDeclaration> _facts =
            document.Schemas.SelectMany(schema => schema.ResourceTypes)
                .SelectMany(type => type.Facts).ToDictionary(fact => fact.Identity);
        private readonly Dictionary<ExplanationFieldIdentity, ExplanationRecordFieldDeclaration> _fields =
            document.Schemas.SelectMany(schema => schema.DataShapes)
                .OfType<ExplanationDataShapeDeclaration.Record>()
                .SelectMany(shape => shape.Fields).ToDictionary(field => field.Identity);
        private readonly ILookup<ExplanationResourceKey, ResourceExplanationRelationship> _relationships =
            document.Relationships.ToLookup(relationship => relationship.Source);

        public void WriteResource(ResourceExplanationResource resource)
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            WriteKey(resource.Key);
            writer.WriteString("scope", resource.Scope.ToString());
            if (resource.Path is not null)
                writer.WriteString("path", resource.Path.Value);
            writer.WritePropertyName("facts");
            writer.WriteStartObject();
            foreach (ExplanationFactObservation fact in resource.Facts)
            {
                if (fact.State != ExplanationObservationState.Available)
                    continue;
                ExplanationFactDeclaration declaration = _facts[fact.Fact];
                writer.WritePropertyName(fact.Fact.Value);
                WriteValues(fact.Values, declaration.Cardinality);
            }
            writer.WriteEndObject();
            writer.WritePropertyName("fact_states");
            writer.WriteStartObject();
            foreach (ExplanationFactObservation fact in resource.Facts)
            {
                if (fact.State == ExplanationObservationState.Available)
                    continue;
                writer.WritePropertyName(fact.Fact.Value);
                WriteOutcome(fact.State, fact.OutcomeData);
            }
            writer.WriteEndObject();
            ResourceExplanationRelationship[] relationships = _relationships[resource.Key].ToArray();
            writer.WritePropertyName("relationships");
            writer.WriteStartObject();
            foreach (ResourceExplanationRelationship relationship in relationships)
            {
                writer.WritePropertyName(relationship.Relationship.Value);
                writer.WriteStartObject();
                writer.WriteString("state", relationship.State.ToString());
                writer.WriteString("completeness", relationship.TargetCompleteness.ToString());
                if (relationship.OutcomeData is not null)
                {
                    writer.WritePropertyName("outcome");
                    WriteValue(relationship.OutcomeData);
                }
                writer.WritePropertyName("targets");
                writer.WriteStartArray();
                foreach (ResourceExplanationRelationshipTarget target in relationship.Targets)
                {
                    writer.WriteStartObject();
                    WriteKey(target.Resource);
                    WriteAddresses(target.Addresses);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WritePropertyName("_links");
            writer.WriteStartObject();
            if (resource.Path is not null)
            {
                writer.WritePropertyName("self");
                WriteLink(resource.Path);
            }
            foreach (ResourceExplanationRelationship relationship in relationships)
            {
                ResourcePath[] paths = relationship.Targets.SelectMany(target =>
                    target.Addresses).Select(PathOf).OfType<ResourcePath>().ToArray();
                if (paths.Length == 0)
                    continue;
                writer.WritePropertyName(relationship.Relationship.Value);
                writer.WriteStartArray();
                foreach (ResourcePath path in paths)
                    WriteLink(path);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            WriteAddresses(resource.Addresses);
            if (resource.Key == document.Root)
            {
                writer.WritePropertyName("traversal");
                // The existing receipt is small and preserves all traversal bounds.
                JsonSerializer.Serialize(writer, document.Traversal,
                    ResourceExplanationJsonContext.Default.ResourceExplanationTraversalReceipt);
                if (document.Resources.Length > 1)
                {
                    writer.WritePropertyName("_embedded");
                    writer.WriteStartObject();
                    writer.WritePropertyName("resources");
                    writer.WriteStartArray();
                    foreach (ResourceExplanationResource expanded in document.Resources.Skip(1))
                        WriteResource(expanded);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject();
        }

        private void WriteKey(ExplanationResourceKey key)
        {
            writer.WritePropertyName("identity");
            writer.WriteStartObject();
            writer.WriteString("owner", key.Owner.Value);
            writer.WriteString("schema", key.ResourceType.Schema.Value);
            writer.WriteString("type", key.ResourceType.Value);
            writer.WritePropertyName("value");
            WriteValue(key.IdentityValue);
            writer.WriteEndObject();
        }

        private void WriteOutcome(ExplanationObservationState state, ExplanationValue? value)
        {
            writer.WriteStartObject();
            writer.WriteString("state", state.ToString());
            if (value is not null)
            {
                writer.WritePropertyName("outcome");
                WriteValue(value);
            }
            writer.WriteEndObject();
        }

        private void WriteAddresses(ImmutableArray<ExplanationPublicAddress> addresses)
        {
            writer.WritePropertyName("addresses");
            writer.WriteStartArray();
            foreach (ExplanationPublicAddress address in addresses)
            {
                writer.WriteStartObject();
                writer.WriteString("owner", address.Kind.Schema.Owner.Value);
                writer.WriteString("schema", address.Kind.Schema.Value);
                writer.WriteString("kind", address.Kind.Value);
                writer.WritePropertyName("value");
                WriteValue(address.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static ResourcePath? PathOf(ExplanationPublicAddress address) =>
            address.Kind == ResourceExplanationVocabulary.ResourcePathAddressKind
                && address.Value is ExplanationValue.Scalar { Value.Text: { } text }
                    ? new ResourcePath(text) : null;

        private void WriteLink(ResourcePath path)
        {
            string href = bindAddress(path);
            if (string.IsNullOrWhiteSpace(href))
                throw new InvalidOperationException("An explanation link requires a usable host address.");
            writer.WriteStartObject();
            writer.WriteString("href", href);
            writer.WriteEndObject();
        }

        private void WriteValues(ImmutableArray<ExplanationValue> values,
            ExplanationCardinality cardinality)
        {
            if (cardinality == ExplanationCardinality.OrderedMany)
            {
                writer.WriteStartArray();
                foreach (ExplanationValue value in values)
                    WriteValue(value);
                writer.WriteEndArray();
            }
            else if (values.IsEmpty)
                writer.WriteNullValue();
            else
                WriteValue(values[0]);
        }

        private void WriteValue(ExplanationValue value)
        {
            switch (value)
            {
                case ExplanationValue.Scalar scalar:
                    WriteScalar(scalar.Value);
                    break;
                case ExplanationValue.Record record:
                    writer.WriteStartObject();
                    foreach (ExplanationRecordFieldValue field in record.Fields)
                    {
                        ExplanationRecordFieldDeclaration declaration = _fields[field.Field];
                        writer.WritePropertyName(field.Field.Value);
                        WriteValues(field.Values, declaration.Cardinality);
                    }
                    writer.WriteEndObject();
                    break;
                case ExplanationValue.Choice choice:
                    writer.WriteStartObject();
                    writer.WriteString("case", choice.Case.Value);
                    if (choice.Value is not null)
                    {
                        writer.WritePropertyName("value");
                        WriteValue(choice.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case ExplanationValue.VocabularyTerm term:
                    // Term identity is qualified explicitly; vocabulary links never imply acceptance.
                    writer.WriteStartObject();
                    writer.WriteString("catalog", term.Identity.Vocabulary.Catalog.Value);
                    writer.WriteString("vocabulary", term.Identity.Vocabulary.Value);
                    writer.WriteString("term", term.Identity.Value);
                    writer.WriteEndObject();
                    break;
                default:
                    throw new InvalidOperationException("Unsupported explanation value.");
            }
        }

        private void WriteScalar(ExplanationScalarValue value)
        {
            switch (value.Kind)
            {
                case ExplanationScalarKind.Text: writer.WriteStringValue(value.Text); break;
                case ExplanationScalarKind.Boolean: writer.WriteBooleanValue(value.Boolean!.Value); break;
                case ExplanationScalarKind.Integer:
                    BigInteger integer = value.Integer!.Value;
                    // Canonical decimal strings preserve arbitrary precision across JavaScript hosts.
                    writer.WriteStringValue(integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case ExplanationScalarKind.Decimal:
                    writer.WriteStringValue(value.Decimal!.Value.ToString(CultureInfo.InvariantCulture)); break;
                case ExplanationScalarKind.BinaryFloatingPoint:
                    writer.WriteNumberValue(value.BinaryFloatingPoint!.Value); break;
                case ExplanationScalarKind.Octets:
                    writer.WriteBase64StringValue(value.Octets.AsSpan()); break;
                default: throw new InvalidOperationException("Unsupported explanation scalar.");
            }
        }
    }
}
