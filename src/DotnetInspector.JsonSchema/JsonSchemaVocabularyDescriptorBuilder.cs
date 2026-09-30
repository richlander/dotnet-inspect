using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspector.Vocabulary;
using ILInspector.Analysis;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace DotnetInspector.JsonSchema;

public static class JsonSchemaVocabularyDescriptorBuilder
{
    public const int FormatVersion = 1;
    public const string Dialect =
        "https://json-schema.org/draft/2020-12/schema";

    public static JsonSchemaVocabularyDescriptor Build(
        JsExportSurface surface,
        JsonWireDeclarationPlan declarationPlan,
        JsonSchemaContractDeclaration declaration,
        VocabularySnapshot vocabularySnapshot,
        VocabularySnapshotIdentity expectedVocabularySnapshotIdentity)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(declarationPlan);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(vocabularySnapshot);
        if (vocabularySnapshot.Identity
            != expectedVocabularySnapshotIdentity)
        {
            throw new JsonSchemaVocabularyException(
                declaration.Identity.Value,
                $"expected vocabulary snapshot "
                    + $"'{expectedVocabularySnapshotIdentity}' but received "
                    + $"'{vocabularySnapshot.Identity}'");
        }

        var lowering = new SchemaLowering(
            surface,
            declarationPlan,
            declaration.Direction);
        JsonObject schema = lowering.Lower(declaration.Root);
        byte[] schemaProjection =
            JsonSchemaCanonicalizer.Write(
                schema,
                omitRootId: true);
        var schemaIdentity = new JsonSchemaIdentity(
            JsonSchemaCanonicalizer.Digest(schemaProjection));
        schema["$id"] =
            $"urn:dotnet-inspect:json-schema:{schemaIdentity.Value}";

        ImmutableArray<JsonSchemaVocabularyBinding> bindings =
            BuildBindings(
                declaration,
                vocabularySnapshot,
                lowering);
        byte[] canonicalSchema = JsonSchemaCanonicalizer.Write(schema);
        JsonElement schemaElement =
            JsonDocument.Parse(canonicalSchema).RootElement.Clone();

        JsonObject descriptorProjection = DescriptorProjection(
            declaration,
            vocabularySnapshot,
            schemaIdentity,
            schema,
            bindings);
        var descriptorIdentity =
            new JsonSchemaDescriptorIdentity(
                JsonSchemaCanonicalizer.Digest(
                    JsonSchemaCanonicalizer.Write(
                        descriptorProjection)));

        return new(
            FormatVersion,
            declaration.Identity,
            declaration.Direction,
            Dialect,
            schemaIdentity,
            descriptorIdentity,
            vocabularySnapshot.Catalog,
            vocabularySnapshot.Identity,
            schemaElement,
            bindings);
    }

    public static byte[] SerializeCanonical(
        JsonSchemaVocabularyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        JsonNode schema = JsonNode.Parse(
            descriptor.Schema.GetRawText())
            ?? throw new JsonSchemaVocabularyException(
                descriptor.Contract.Value,
                "the descriptor schema could not be parsed");
        JsonObject projection = DescriptorProjection(
            descriptor,
            schema);
        return JsonSchemaCanonicalizer.Write(projection);
    }

    static ImmutableArray<JsonSchemaVocabularyBinding> BuildBindings(
        JsonSchemaContractDeclaration declaration,
        VocabularySnapshot vocabularySnapshot,
        SchemaLowering lowering)
    {
        var bindings = new List<(
            int Order,
            JsonSchemaVocabularyBinding Binding)>();
        var boundLocations = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonSchemaBindingDeclaration binding
            in declaration.Bindings)
        {
            VocabularyTerm term;
            try
            {
                term = vocabularySnapshot.GetTerm(binding.Term);
            }
            catch (KeyNotFoundException exception)
            {
                throw new JsonSchemaVocabularyException(
                    declaration.Identity.Value,
                    exception.Message);
            }

            if (term.Identity.Vocabulary.Catalog
                != vocabularySnapshot.Catalog)
            {
                throw new JsonSchemaVocabularyException(
                    declaration.Identity.Value,
                    $"term '{term.Identity}' belongs to another "
                        + "vocabulary catalog");
            }

            foreach ((string location, int order)
                in lowering.ResolveBindings(binding.Target))
            {
                if (!boundLocations.Add(location))
                {
                    throw new JsonSchemaVocabularyException(
                        location,
                        "multiple vocabulary terms target one schema location");
                }
                if (!SchemaLowering.PointerResolves(
                        lowering.Schema,
                        location))
                {
                    throw new JsonSchemaVocabularyException(
                        location,
                        "the resolved schema location does not exist");
                }

                bindings.Add((
                    order,
                    new(
                        location,
                        term.Identity.Vocabulary,
                        term.Identity)));
            }
        }

        if (declaration.BindingCoverage
            == JsonSchemaBindingCoverage.CompleteDisplaySlots)
        {
            if (declaration.Root
                is not JsonSchemaContractRoot.Positional positional)
            {
                throw new JsonSchemaVocabularyException(
                    declaration.Identity.Value,
                    "complete display-slot coverage requires a positional row");
            }

            foreach (JsonPositionalRowSlot slot
                in positional.Row.Slots.Where(slot =>
                    slot.IsDisplayable))
            {
                var target = new JsonSchemaBindingTarget
                    .PositionalSlot(positional.Row, slot);
                (string location, _) =
                    lowering.ResolveBinding(target);
                if (!boundLocations.Contains(location))
                {
                    throw new JsonSchemaVocabularyException(
                        location,
                        "a displayable positional slot has no vocabulary binding");
                }
            }
        }

        return [
            .. bindings
                .OrderBy(binding => binding.Order)
                .Select(binding => binding.Binding),
        ];
    }

    static JsonObject DescriptorProjection(
        JsonSchemaContractDeclaration declaration,
        VocabularySnapshot snapshot,
        JsonSchemaIdentity schemaIdentity,
        JsonNode schema,
        ImmutableArray<JsonSchemaVocabularyBinding> bindings) =>
        new()
        {
            ["formatVersion"] = FormatVersion,
            ["contract"] = declaration.Identity.Value,
            ["direction"] = DirectionName(declaration.Direction),
            ["dialect"] = Dialect,
            ["schemaIdentity"] = schemaIdentity.Value,
            ["vocabularyCatalog"] = snapshot.Catalog.Value,
            ["vocabularySnapshotIdentity"] = snapshot.Identity.Value,
            ["schema"] = schema.DeepClone(),
            ["bindings"] = new JsonArray(
                [.. bindings.Select(BindingNode)]),
        };

    static JsonObject DescriptorProjection(
        JsonSchemaVocabularyDescriptor descriptor,
        JsonNode schema) =>
        new()
        {
            ["formatVersion"] = descriptor.FormatVersion,
            ["contract"] = descriptor.Contract.Value,
            ["direction"] = DirectionName(descriptor.Direction),
            ["dialect"] = descriptor.Dialect,
            ["schemaIdentity"] = descriptor.SchemaIdentity.Value,
            ["descriptorIdentity"] =
                descriptor.DescriptorIdentity.Value,
            ["vocabularyCatalog"] =
                descriptor.VocabularyCatalog.Value,
            ["vocabularySnapshotIdentity"] =
                descriptor.VocabularySnapshotIdentity.Value,
            ["schema"] = schema,
            ["bindings"] = new JsonArray(
                [.. descriptor.Bindings.Select(BindingNode)]),
        };

    static JsonObject BindingNode(
        JsonSchemaVocabularyBinding binding) =>
        new()
        {
            ["schemaLocation"] = binding.SchemaLocation,
            ["vocabulary"] = binding.Vocabulary.Value,
            ["term"] = binding.Term.Value,
        };

    static string DirectionName(JsonWireDirection direction) =>
        direction switch
        {
            JsonWireDirection.Serialize => "serialize",
            JsonWireDirection.Deserialize => "deserialize",
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };

    sealed class SchemaLowering
    {
        readonly JsExportSurface _surface;
        readonly JsonWireDeclarationPlan _plan;
        readonly JsonWireDirection _direction;
        readonly JsonObject _definitions = [];
        readonly Dictionary<string, string> _definitionNames =
            new(StringComparer.Ordinal);
        readonly Dictionary<
            JsonSchemaBindingTarget,
            List<(string Location, int Order)>> _bindingLocations = [];
        readonly Dictionary<MetadataTypeDefinitionName, ApiType>
            _typesByDefinitionName;
        int _locationOrder;

        public SchemaLowering(
            JsExportSurface surface,
            JsonWireDeclarationPlan plan,
            JsonWireDirection direction)
        {
            _surface = surface;
            _plan = plan;
            _direction = direction;
            _typesByDefinitionName = plan.Types
                .Where(type => type.DefinitionName is not null)
                .GroupBy(type => type.DefinitionName!)
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.Single());
        }

        public JsonObject Schema { get; private set; } = [];

        public JsonObject Lower(JsonSchemaContractRoot root)
        {
            JsonNode rootSchema = root switch
            {
                JsonSchemaContractRoot.Object value =>
                    LowerObjectRoot(value.Type),
                JsonSchemaContractRoot.Positional value =>
                    LowerPositionalRoot(value.Row),
                _ => throw new JsonSchemaVocabularyException(
                    "schema root",
                    "unsupported contract-root kind"),
            };

            Schema = new()
            {
                ["$schema"] = Dialect,
            };
            if (rootSchema is JsonObject rootObject)
            {
                foreach ((string name, JsonNode? value)
                    in rootObject)
                {
                    Schema[name] = value?.DeepClone();
                }
            }
            else
            {
                Schema["allOf"] = new JsonArray(rootSchema);
            }
            if (_definitions.Count > 0)
                Schema["$defs"] = _definitions;
            return Schema;
        }

        public IReadOnlyList<(string Location, int Order)> ResolveBindings(
            JsonSchemaBindingTarget target)
        {
            if (!_bindingLocations.TryGetValue(
                    target,
                    out List<(string Location, int Order)>? locations)
                || locations.Count == 0)
            {
                throw new JsonSchemaVocabularyException(
                    "vocabulary binding",
                    "the target is absent in the selected schema direction");
            }
            return locations;
        }

        public (string Location, int Order) ResolveBinding(
            JsonSchemaBindingTarget target)
        {
            IReadOnlyList<(string Location, int Order)> locations =
                ResolveBindings(target);
            if (locations.Count != 1)
            {
                throw new JsonSchemaVocabularyException(
                    "vocabulary binding",
                    "the target resolves to more than one schema location");
            }
            return locations[0];
        }

        JsonNode LowerObjectRoot(ApiType type)
        {
            JsonWireDeclarationIdentity declaration =
                ResolveDeclaration(type);
            return Reference(
                EnsureDefinition(
                    declaration,
                    []));
        }

        JsonNode LowerPositionalRoot(JsonPositionalRowContract row)
        {
            var prefixItems = new JsonArray();
            for (int index = 0; index < row.Slots.Length; index++)
            {
                JsonPositionalRowSlot slot = row.Slots[index];
                prefixItems.Add(
                    LowerShape(
                        slot.Shape,
                        [],
                        allowNull: slot.AllowsNull,
                        converterControlledString: false));
                string location = $"/prefixItems/{index}";
                AddBindingLocation(
                    new JsonSchemaBindingTarget
                        .PositionalSlot(row, slot),
                    location);
            }

            return new JsonObject
            {
                ["type"] = "array",
                ["prefixItems"] = prefixItems,
                ["items"] = false,
                ["minItems"] = row.Slots.Length,
                ["maxItems"] = row.Slots.Length,
            };
        }

        string EnsureDefinition(
            JsonWireDeclarationIdentity declaration,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            string key = DefinitionKey(
                declaration,
                typeArguments);
            if (_definitionNames.TryGetValue(
                    key,
                    out string? existing))
            {
                return existing;
            }

            string name = AllocateDefinitionName(
                declaration,
                typeArguments);
            _definitionNames.Add(key, name);
            _definitions[name] = new JsonObject();
            JsExportPolymorphicUnion? polymorphicRoot =
                FindPolymorphicRoot(declaration.Type);
            (JsExportPolymorphicUnion Union,
                JsExportPolymorphicCase Case)? polymorphicCase =
                    FindPolymorphicCase(declaration.Type);
            JsonNode definition = declaration.Type.Kind switch
            {
                "enum" => LowerEnum(declaration.Type),
                _ when polymorphicRoot is not null =>
                    LowerPolymorphicUnion(
                        polymorphicRoot,
                        typeArguments),
                _ when polymorphicCase is { } value =>
                    LowerPolymorphicCase(
                        value.Union,
                        value.Case,
                        typeArguments,
                        name),
                _ when _surface.Unions.Any(union =>
                    ReferenceEquals(
                        union.Definition,
                        declaration.Type)) =>
                    LowerUnion(
                        _surface.Unions.Single(union =>
                            ReferenceEquals(
                                union.Definition,
                                declaration.Type)),
                        typeArguments),
                _ => LowerRecord(
                    declaration,
                    typeArguments,
                    name),
            };
            _definitions[name] = definition;
            return name;
        }

        JsExportPolymorphicUnion? FindPolymorphicRoot(ApiType type)
        {
            JsExportPolymorphicUnion? found = null;
            foreach (JsExportPolymorphicUnion union
                in _surface.PolymorphicUnions)
            {
                if (!ReferenceEquals(union.Definition, type))
                    continue;
                if (found is not null)
                {
                    throw new JsonSchemaVocabularyException(
                        type.FullName,
                        "multiple polymorphic roots name one type");
                }
                found = union;
            }
            return found;
        }

        (JsExportPolymorphicUnion Union,
            JsExportPolymorphicCase Case)? FindPolymorphicCase(
                ApiType type)
        {
            (JsExportPolymorphicUnion Union,
                JsExportPolymorphicCase Case)? found = null;
            foreach (JsExportPolymorphicUnion union
                in _surface.PolymorphicUnions)
            {
                foreach (JsExportPolymorphicCase @case
                    in union.Cases)
                {
                    if (!ReferenceEquals(@case.Definition, type))
                        continue;
                    if (found is not null)
                    {
                        throw new JsonSchemaVocabularyException(
                            type.FullName,
                            "multiple polymorphic cases name one type");
                    }
                    found = (union, @case);
                }
            }
            return found;
        }

        JsonNode LowerPolymorphicUnion(
            JsExportPolymorphicUnion union,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            if (union.UnsupportedReason is not null
                || string.IsNullOrEmpty(
                    union.TypeDiscriminatorPropertyName)
                || union.Cases.Count == 0)
            {
                throw new JsonSchemaVocabularyException(
                    union.Definition.FullName,
                    union.UnsupportedReason
                        ?? "polymorphic discriminator evidence is incomplete");
            }

            var alternatives = new JsonArray();
            foreach (JsExportPolymorphicCase @case in union.Cases)
            {
                JsonWireDeclarationIdentity declaration =
                    ResolveDeclaration(@case.Definition);
                JsonNode reference =
                    Reference(
                        EnsureDefinition(
                            declaration,
                            typeArguments));
                alternatives.Add(reference);
            }
            return new JsonObject { ["anyOf"] = alternatives };
        }

        JsonNode LowerPolymorphicCase(
            JsExportPolymorphicUnion union,
            JsExportPolymorphicCase @case,
            ImmutableArray<ApiTypeShape> typeArguments,
            string definitionName)
        {
            ApiType root = union.Definition;
            ApiType caseType = @case.Definition;
            if (JsonWireContractRules.HasUnsupportedJsonConverter(root)
                || JsonWireContractRules.HasUnsupportedJsonConverter(
                    caseType))
            {
                throw Unsupported(
                    caseType,
                    "custom JsonConverter is unsupported");
            }
            if (root.HasUnsupportedJsonWireAttributes
                || caseType.HasUnsupportedJsonWireAttributes)
            {
                throw Unsupported(
                    caseType,
                    "wire-shaping attributes are unsupported");
            }

            IReadOnlyList<JsonWirePolymorphicMember> members;
            try
            {
                members =
                    JsonWireContractRules.GetPolymorphicCaseMembers(
                        _surface,
                        union,
                        @case,
                        _direction,
                        _plan.DeclaredTypesByScopedIdentity);
            }
            catch (UnsupportedJsExportSurfaceException exception)
            {
                throw new JsonSchemaVocabularyException(
                    exception.Location,
                    exception.Reason);
            }

            if (_direction == JsonWireDirection.Deserialize
                && members.Any(item =>
                    JsonWireMemberRules.RequiresConstructorBindingEvidence(
                        caseType,
                        item.Member,
                        _surface.AssemblyIdentity,
                        _plan.DeclaredTypesByScopedIdentity)))
            {
                throw Unsupported(
                    caseType,
                    "constructor-binding evidence is incomplete");
            }

            string discriminatorPropertyName =
                union.TypeDiscriminatorPropertyName!;
            var properties = new JsonObject
            {
                [discriminatorPropertyName] = new JsonObject
                {
                    ["type"] = "string",
                    ["const"] = @case.TypeDiscriminator,
                },
            };
            var required = new List<string>
            {
                discriminatorPropertyName,
            };
            foreach (JsonWirePolymorphicMember item in members)
            {
                ApiMember member = item.Member;
                JsonWireMemberPresence presence =
                    JsonWireMemberRules.GetPresence(
                        _surface,
                        caseType,
                        member,
                        _direction,
                        _surface.AssemblyIdentity,
                        _plan.DeclaredTypesByScopedIdentity);
                if (presence == JsonWireMemberPresence.Absent)
                    continue;
                if (presence == JsonWireMemberPresence.Unsupported)
                {
                    throw new JsonSchemaVocabularyException(
                        $"{caseType.FullName}.{member.Name}",
                        "member presence is unsupported");
                }
                if (member.SignatureModel?.ReturnTypeShape
                    is not { } shape)
                {
                    throw new JsonSchemaVocabularyException(
                        $"{caseType.FullName}.{member.Name}",
                        "exact member type shape is unavailable");
                }
                if (member.JsonConverterAttributeCount > 0
                    && !JsonWireContractRules
                        .HasApprovedInertStringConverter(member))
                {
                    throw new JsonSchemaVocabularyException(
                        $"{caseType.FullName}.{member.Name}",
                        "custom JsonConverter is unsupported");
                }

                properties[item.PropertyName] =
                    LowerShape(
                        shape,
                        typeArguments,
                        allowNull:
                            presence
                                != JsonWireMemberPresence.Conditional,
                        converterControlledString:
                            member.JsonConverterAttributeCount == 1);
                if ((_direction == JsonWireDirection.Serialize
                        && presence == JsonWireMemberPresence.Present)
                    || (_direction == JsonWireDirection.Deserialize
                        && member.SignatureModel.IsRequired))
                {
                    required.Add(item.PropertyName);
                }

                AddBindingLocation(
                    new JsonSchemaBindingTarget.ObjectMember(
                        item.DeclaringType,
                        member),
                    $"/$defs/{EscapePointer(definitionName)}"
                        + $"/properties/"
                        + EscapePointer(item.PropertyName));
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(
                    [.. required
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .Select(value =>
                            JsonValue.Create(value))]),
                ["additionalProperties"] =
                    _direction == JsonWireDirection.Deserialize,
            };
        }

        JsonNode LowerRecord(
            JsonWireDeclarationIdentity declaration,
            ImmutableArray<ApiTypeShape> typeArguments,
            string definitionName)
        {
            ApiType type = declaration.Type;
            if (JsonWireMemberRules.GetContextDefaultIgnoreCondition(
                    _surface,
                    type)
                == JsonWireContextDefaultIgnoreCondition.Unsupported)
            {
                throw Unsupported(
                    type,
                    "serializer-context ignore options are unsupported");
            }
            if (JsonWireContractRules.HasUnsupportedJsonConverter(type))
            {
                throw Unsupported(
                    type,
                    "custom JsonConverter is unsupported");
            }
            if (JsonWireContractRules.HasUnsupportedRecordWireShape(
                    type,
                    _surface.AssemblyIdentity,
                    _plan.DeclaredTypesByScopedIdentity))
            {
                throw Unsupported(
                    type,
                    "wire-shaping attributes or inheritance are unsupported");
            }
            if (_direction == JsonWireDirection.Deserialize
                && type.Members.Any(member =>
                    JsonWireMemberRules
                        .RequiresConstructorBindingEvidence(
                            type,
                            member,
                            _surface.AssemblyIdentity,
                            _plan.DeclaredTypesByScopedIdentity)))
            {
                throw Unsupported(
                    type,
                    "constructor-binding evidence is incomplete");
            }

            var properties = new JsonObject();
            var required = new List<string>();
            var resolvedNames = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (ApiMember member in type.Members)
            {
                JsonWireMemberPresence presence =
                    JsonWireMemberRules.GetPresence(
                        _surface,
                        type,
                        member,
                        _direction,
                        _surface.AssemblyIdentity,
                        _plan.DeclaredTypesByScopedIdentity);
                if (presence == JsonWireMemberPresence.Absent)
                    continue;
                if (presence == JsonWireMemberPresence.Unsupported)
                {
                    throw new JsonSchemaVocabularyException(
                        $"{type.FullName}.{member.Name}",
                        "member presence is unsupported");
                }
                if (member.SignatureModel?.ReturnTypeShape
                    is not { } shape)
                {
                    throw new JsonSchemaVocabularyException(
                        $"{type.FullName}.{member.Name}",
                        "exact member type shape is unavailable");
                }
                if (member.JsonConverterAttributeCount > 0
                    && !JsonWireContractRules
                        .HasApprovedInertStringConverter(member))
                {
                    throw new JsonSchemaVocabularyException(
                        $"{type.FullName}.{member.Name}",
                        "custom JsonConverter is unsupported");
                }

                string propertyName =
                    JsonWireContractRules.ResolvePropertyName(
                        type,
                        member);
                if (!resolvedNames.Add(propertyName))
                {
                    throw new JsonSchemaVocabularyException(
                        $"{type.FullName}.{member.Name}",
                        "multiple members resolve to one JSON property name");
                }

                properties[propertyName] =
                    LowerShape(
                        shape,
                        typeArguments,
                        allowNull:
                            presence
                                != JsonWireMemberPresence.Conditional,
                        converterControlledString:
                            member.JsonConverterAttributeCount == 1);
                if ((_direction == JsonWireDirection.Serialize
                        && presence == JsonWireMemberPresence.Present)
                    || (_direction == JsonWireDirection.Deserialize
                        && member.SignatureModel.IsRequired))
                {
                    required.Add(propertyName);
                }

                AddBindingLocation(
                    new JsonSchemaBindingTarget.ObjectMember(
                        type,
                        member),
                    $"/$defs/{EscapePointer(definitionName)}"
                        + $"/properties/{EscapePointer(propertyName)}");
            }

            var result = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] =
                    _direction == JsonWireDirection.Deserialize,
            };
            if (required.Count > 0)
            {
                result["required"] = new JsonArray(
                    [.. required
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .Select(value =>
                            JsonValue.Create(value))]);
            }
            return result;
        }

        JsonNode LowerEnum(ApiType type)
        {
            if (JsonWireContractRules.HasUnsupportedJsonConverter(type))
            {
                throw Unsupported(
                    type,
                    "custom JsonConverter is unsupported");
            }
            if (type.HasUnsupportedJsonWireAttributes)
            {
                throw Unsupported(
                    type,
                    "wire-shaping attributes are unsupported");
            }
            if (!JsonWireContractRules.UsesStringEnumConverter(type))
                return new JsonObject { ["type"] = "integer" };
            if (type.HasMalformedFlagsAttribute
                || type.FlagsAttributeCount > 1)
            {
                throw Unsupported(
                    type,
                    "[Flags] metadata is unsupported");
            }
            if (type.IsFlagsEnum)
            {
                return new JsonObject
                {
                    ["type"] = new JsonArray("string", "integer"),
                };
            }

            string[] names =
            [
                .. type.Members
                    .Where(member =>
                        member.Kind == "field"
                        && member.IsConst)
                    .Select(member =>
                        member.JsonStringEnumMemberName
                        ?? member.Name),
            ];
            if (names.Length == 0)
            {
                throw Unsupported(
                    type,
                    "string-converted enums require declared members");
            }
            return new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray(
                            [.. names.Select(name =>
                                JsonValue.Create(name))]),
                    },
                    new JsonObject { ["type"] = "integer" }),
            };
        }

        JsonNode LowerUnion(
            JsExportUnion union,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            string? reason = _direction
                == JsonWireDirection.Deserialize
                    ? union.DeserializationUnsupportedReason
                    : union.SerializationUnsupportedReason;
            if (reason is not null
                || union.IncludesNull is null
                || union.CaseTypes.Count == 0)
            {
                throw new JsonSchemaVocabularyException(
                    union.Definition.FullName,
                    reason
                        ?? "union case or null evidence is unavailable");
            }

            var alternatives = new JsonArray();
            foreach (TypeRef caseType in union.CaseTypes)
            {
                alternatives.Add(
                    LowerTypeRef(
                        caseType,
                        typeArguments));
            }
            if (union.IncludesNull == true)
            {
                JsonNode nullSchema =
                    new JsonObject { ["type"] = "null" };
                alternatives.Add(nullSchema);
            }
            return new JsonObject { ["anyOf"] = alternatives };
        }

        JsonNode LowerShape(
            ApiTypeShape shape,
            ImmutableArray<ApiTypeShape> typeArguments,
            bool allowNull,
            bool converterControlledString)
        {
            if (converterControlledString)
                return new JsonObject { ["type"] = "string" };
            if (shape.Kind == ApiTypeShapeKind.GenericParameter)
            {
                if (shape.IsMethodGenericParameter
                    || shape.GenericParameterIndex < 0
                    || shape.GenericParameterIndex
                        >= typeArguments.Length)
                {
                    throw new JsonSchemaVocabularyException(
                        "generic parameter",
                        "closed type-argument evidence is unavailable");
                }
                return LowerShape(
                    typeArguments[shape.GenericParameterIndex],
                    [],
                    allowNull,
                    converterControlledString: false);
            }

            bool nullable = false;
            ApiTypeShape effective = shape;
            if (shape is
                {
                    Kind: ApiTypeShapeKind.GenericInstance,
                    Definition.FullName:
                        "System.Nullable`1",
                    TypeArguments: [var nullableArgument],
                })
            {
                nullable = true;
                effective = nullableArgument;
            }
            else if (allowNull)
            {
                nullable =
                    JsonWireContractRules.CanValueBeNull(
                        shape,
                        _surface.AssemblyIdentity,
                        _plan.DeclaredTypesByScopedIdentity)
                    ?? throw new JsonSchemaVocabularyException(
                        "JSON value",
                        "null capability could not be authenticated");
            }

            JsonNode value = LowerNonNullShape(
                effective,
                typeArguments);
            return nullable && allowNull
                ? AddNull(value)
                : value;
        }

        JsonNode LowerNonNullShape(
            ApiTypeShape shape,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            switch (shape.Kind)
            {
                case ApiTypeShapeKind.Primitive:
                    return PrimitiveSchema(shape.Primitive);
                case ApiTypeShapeKind.GenericParameter:
                    if (shape.IsMethodGenericParameter
                        || shape.GenericParameterIndex < 0
                        || shape.GenericParameterIndex
                            >= typeArguments.Length)
                    {
                        throw new JsonSchemaVocabularyException(
                            "generic parameter",
                            "closed type-argument evidence is unavailable");
                    }
                    return LowerShape(
                        typeArguments[shape.GenericParameterIndex],
                        [],
                        allowNull: true,
                        converterControlledString: false);
                case ApiTypeShapeKind.SzArray:
                    if (shape.ElementType is null)
                    {
                        throw new JsonSchemaVocabularyException(
                            "array",
                            "element type is unavailable");
                    }
                    if (shape.ElementType is
                        {
                            Kind: ApiTypeShapeKind.Primitive,
                            Primitive: ApiPrimitiveType.Byte,
                        })
                    {
                        return new JsonObject
                        {
                            ["type"] = "string",
                            ["contentEncoding"] = "base64",
                        };
                    }
                    return new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = LowerShape(
                            shape.ElementType,
                            typeArguments,
                            allowNull: true,
                            converterControlledString: false),
                    };
                case ApiTypeShapeKind.Array:
                    throw new JsonSchemaVocabularyException(
                        "array",
                        "multi-dimensional arrays are unsupported");
                case ApiTypeShapeKind.Named:
                case ApiTypeShapeKind.GenericInstance:
                    return LowerNamedShape(
                        shape,
                        typeArguments);
                default:
                    throw new JsonSchemaVocabularyException(
                        "JSON value",
                        $"unsupported type-shape kind '{shape.Kind}'");
            }
        }

        JsonNode LowerNamedShape(
            ApiTypeShape shape,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            ApiTypeReferenceIdentity definition =
                shape.Definition
                ?? throw new JsonSchemaVocabularyException(
                    "named type",
                    "definition identity is unavailable");
            string fullName = definition.FullName;
            if (fullName == "System.String")
                return new JsonObject { ["type"] = "string" };
            if (fullName == "System.Object"
                || fullName == "System.Text.Json.JsonElement")
            {
                return JsonValue.Create(true)!;
            }
            if (fullName is "System.Guid")
            {
                return new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                };
            }
            if (fullName is "System.DateTime"
                or "System.DateTimeOffset")
            {
                return new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "date-time",
                };
            }
            if (fullName is "System.Version"
                or JsonWireContractRules.InertStringFullName)
            {
                return new JsonObject { ["type"] = "string" };
            }
            if (fullName == "System.Nullable`1"
                && shape.TypeArguments is [var nullable])
            {
                return AddNull(
                    LowerNonNullShape(
                        nullable,
                        typeArguments));
            }
            if (IsDictionary(fullName))
            {
                if (shape.TypeArguments
                    is not [var key, var value]
                    || !IsStringShape(key))
                {
                    throw new JsonSchemaVocabularyException(
                        fullName,
                        "only string-keyed dictionaries are supported");
                }
                return new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = LowerShape(
                        value,
                        typeArguments,
                        allowNull: true,
                        converterControlledString: false),
                };
            }
            if (IsCollection(fullName))
            {
                if (shape.TypeArguments is not [var element])
                {
                    throw new JsonSchemaVocabularyException(
                        fullName,
                        "collection element type is unavailable");
                }
                return new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = LowerShape(
                        element,
                        typeArguments,
                        allowNull: true,
                        converterControlledString: false),
                };
            }

            if (!_plan.DeclaredTypesByScopedIdentity.TryGetValue(
                    definition,
                    out ApiType? localType))
            {
                throw new JsonSchemaVocabularyException(
                    fullName,
                    "no authenticated JSON wire mapping exists");
            }
            JsonWireDeclarationIdentity declaration =
                ResolveDeclaration(localType);
            return Reference(
                EnsureDefinition(
                    declaration,
                    shape.Kind
                        == ApiTypeShapeKind.GenericInstance
                            ? shape.TypeArguments
                            : []));
        }

        JsonNode LowerTypeRef(
            TypeRef type,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            if (type.Kind == TypeRefKind.GenericParameter)
            {
                if (type.GenericParameterIndex < 0
                    || type.GenericParameterIndex
                        >= typeArguments.Length)
                {
                    throw new JsonSchemaVocabularyException(
                        "union generic parameter",
                        "closed type-argument evidence is unavailable");
                }
                return LowerShape(
                    typeArguments[type.GenericParameterIndex],
                    [],
                    allowNull: true,
                    converterControlledString: false);
            }
            if (type.Kind == TypeRefKind.SzArray
                && type.ElementType is { } element)
            {
                return new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = LowerTypeRef(
                        element,
                        typeArguments),
                };
            }
            if (type.Kind == TypeRefKind.GenericInstance
                && type.ElementType is { } genericDefinition)
            {
                string fullName = TypeFullName(genericDefinition);
                if (fullName is "System.Nullable`1"
                    && type.TypeArguments is [var nullable])
                {
                    return AddNull(
                        LowerTypeRef(
                            nullable,
                            typeArguments));
                }
                if (IsCollection(fullName)
                    && type.TypeArguments is [var collectionElement])
                {
                    return new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = LowerTypeRef(
                            collectionElement,
                            typeArguments),
                    };
                }
                if (IsDictionary(fullName)
                    && type.TypeArguments is [var key, var value]
                    && TypeFullName(key) == "System.String")
                {
                    return new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] =
                            LowerTypeRef(
                                value,
                                typeArguments),
                    };
                }
                if (genericDefinition.Resolution?.Type
                    is { } definitionName
                    && _typesByDefinitionName.TryGetValue(
                        definitionName,
                        out ApiType? localGeneric))
                {
                    JsonWireDeclarationIdentity declaration =
                        ResolveDeclaration(localGeneric);
                    return Reference(
                        EnsureDefinition(
                            declaration,
                            [.. type.TypeArguments.Select(
                                TypeShapeFromTypeRef)]));
                }
            }
            if (type.Kind == TypeRefKind.Definition)
            {
                string fullName = TypeFullName(type);
                if (fullName == "System.String")
                    return new JsonObject { ["type"] = "string" };
                if (fullName == "System.Boolean")
                    return new JsonObject { ["type"] = "boolean" };
                if (fullName.StartsWith(
                    "System.Int",
                    StringComparison.Ordinal)
                    || fullName.StartsWith(
                        "System.UInt",
                        StringComparison.Ordinal)
                    || fullName is "System.Byte"
                        or "System.SByte")
                {
                    return new JsonObject { ["type"] = "integer" };
                }
                if (type.Resolution?.Type is { } definitionName
                    && _typesByDefinitionName.TryGetValue(
                        definitionName,
                        out ApiType? local))
                {
                    return Reference(
                        EnsureDefinition(
                            ResolveDeclaration(local),
                            []));
                }
            }

            throw new JsonSchemaVocabularyException(
                TypeFullName(type),
                "union case type is unsupported");
        }

        JsonWireDeclarationIdentity ResolveDeclaration(ApiType type)
        {
            if (!_plan.Contains(type))
            {
                throw new JsonSchemaVocabularyException(
                    type.FullName,
                    "the type is not in the authenticated declaration plan");
            }
            JsonWireDeclarationIdentity declaration =
                _plan.Resolve(type, _direction);
            if (declaration.Direction
                is not JsonWireDirection.Both
                && (declaration.Direction & _direction)
                    == JsonWireDirection.None)
            {
                throw new JsonSchemaVocabularyException(
                    type.FullName,
                    $"the '{_direction}' direction is not authenticated");
            }
            return declaration;
        }

        string AllocateDefinitionName(
            JsonWireDeclarationIdentity declaration,
            ImmutableArray<ApiTypeShape> typeArguments)
        {
            var name = new StringBuilder();
            foreach (char value in declaration.Type.Name)
            {
                name.Append(char.IsAsciiLetterOrDigit(value)
                    || value == '_'
                        ? value
                        : '_');
            }
            if (declaration.IsSplit)
            {
                name.Append(
                    declaration.Direction
                        == JsonWireDirection.Deserialize
                            ? "Input"
                            : "Output");
            }
            if (typeArguments.Length > 0)
            {
                string arguments = string.Join(
                    "|",
                    typeArguments.Select(TypeShapeKey));
                string digest =
                    JsonSchemaCanonicalizer.Digest(
                        Encoding.UTF8.GetBytes(arguments));
                name.Append('_').Append(digest.AsSpan(7, 8));
            }

            string candidate = name.ToString();
            int suffix = 2;
            while (_definitions.ContainsKey(candidate))
                candidate = $"{name}_{suffix++}";
            return candidate;
        }

        static string DefinitionKey(
            JsonWireDeclarationIdentity declaration,
            ImmutableArray<ApiTypeShape> typeArguments) =>
            $"{declaration.Type.FullName}|{declaration.Direction}|"
            + string.Join("|", typeArguments.Select(TypeShapeKey));

        static string TypeShapeKey(ApiTypeShape shape)
        {
            var value = new StringBuilder();
            Write(shape);
            return value.ToString();

            void Write(ApiTypeShape current)
            {
                value.Append((int)current.Kind).Append(':')
                    .Append(current.Primitive).Append(':')
                    .Append(current.Definition?.Assembly.Name).Append(':')
                    .Append(current.Definition?.FullName).Append(':')
                    .Append(current.GenericParameterIndex).Append('[');
                if (current.ElementType is { } element)
                    Write(element);
                foreach (ApiTypeShape argument
                    in current.TypeArguments)
                {
                    value.Append(',');
                    Write(argument);
                }
                value.Append(']');
            }
        }

        ApiTypeShape TypeShapeFromTypeRef(TypeRef type)
        {
            if (type.Kind == TypeRefKind.SzArray
                && type.ElementType is { } element)
            {
                return ApiTypeShape.SzArray(
                    TypeShapeFromTypeRef(element));
            }
            if (type.Kind == TypeRefKind.GenericParameter)
            {
                return ApiTypeShape.GenericParameter(
                    type.GenericParameterIndex,
                    isMethodParameter: false);
            }
            TypeRef definition = type.Kind == TypeRefKind.GenericInstance
                ? type.ElementType
                    ?? throw new JsonSchemaVocabularyException(
                        "union generic instance",
                        "definition type is unavailable")
                : type;
            if (TryGetPrimitive(
                    definition,
                    out ApiPrimitiveType primitive))
            {
                return ApiTypeShape.PrimitiveType(primitive);
            }
            ApiAssemblyIdentity assembly =
                GetAssemblyIdentity(definition)
                ?? throw new JsonSchemaVocabularyException(
                    TypeFullName(definition),
                    "exact union assembly identity is unavailable");
            MetadataTypeDefinitionName definitionName =
                definition.Resolution?.Type
                ?? throw new JsonSchemaVocabularyException(
                    TypeFullName(definition),
                    "exact union type identity is unavailable");
            var identity = new ApiTypeReferenceIdentity(
                assembly,
                definitionName.ToMetadataFullName(),
                definitionName);
            bool? isValueType = IsValueType(definition);
            return type.Kind == TypeRefKind.GenericInstance
                ? ApiTypeShape.GenericInstance(
                    identity,
                    [.. type.TypeArguments.Select(
                        TypeShapeFromTypeRef)],
                    isValueType)
                : ApiTypeShape.Named(
                    identity,
                    isValueType);
        }

        ApiAssemblyIdentity? GetAssemblyIdentity(TypeRef type)
        {
            AssemblyReferenceIdentity? identity =
                type.Resolution?.Origin switch
                {
                    TypeReferenceOrigin.AssemblyReference reference =>
                        reference.Assembly,
                    TypeReferenceOrigin.CurrentAssembly current =>
                        current.Assembly,
                    _ => null,
                };
            if (identity is not null)
            {
                return new(
                    identity.Name,
                    identity.Version,
                    identity.Culture,
                    identity.PublicKeyToken);
            }
            return type.Resolution?.Origin
                    is TypeReferenceOrigin.IntrinsicCoreLibrary
                ? new("corelib", null, null, null)
                : _surface.AssemblyIdentity;
        }

        bool? IsValueType(TypeRef type)
        {
            if (type.Resolution?.Type is { } definitionName
                && _typesByDefinitionName.TryGetValue(
                    definitionName,
                    out ApiType? local))
            {
                return local.Kind is "struct" or "enum";
            }
            return type.RawTypeKind switch
            {
                0x11 => true,
                0x12 => false,
                _ => null,
            };
        }

        static bool TryGetPrimitive(
            TypeRef type,
            out ApiPrimitiveType primitive) =>
            Enum.TryParse(
                type.Name,
                ignoreCase: false,
                out primitive)
            && type.Namespace == "System";

        static string TypeFullName(TypeRef type)
        {
            TypeRef definition = type.Kind == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
            return definition.Resolution?.Type?.ToMetadataFullName()
                ?? (definition.Namespace.Length == 0
                    ? definition.Name
                    : $"{definition.Namespace}.{definition.Name}");
        }

        void AddBindingLocation(
            JsonSchemaBindingTarget target,
            string location)
        {
            if (!_bindingLocations.TryGetValue(
                    target,
                    out List<(string Location, int Order)>? locations))
            {
                locations = [];
                _bindingLocations.Add(target, locations);
            }
            locations.Add((location, _locationOrder++));
        }

        static JsonNode PrimitiveSchema(
            ApiPrimitiveType? primitive) =>
            primitive switch
            {
                ApiPrimitiveType.Boolean =>
                    new JsonObject { ["type"] = "boolean" },
                ApiPrimitiveType.Char =>
                    new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                        ["maxLength"] = 1,
                    },
                ApiPrimitiveType.SByte =>
                    IntegerSchema(sbyte.MinValue, sbyte.MaxValue),
                ApiPrimitiveType.Byte =>
                    IntegerSchema(byte.MinValue, byte.MaxValue),
                ApiPrimitiveType.Int16 =>
                    IntegerSchema(short.MinValue, short.MaxValue),
                ApiPrimitiveType.UInt16 =>
                    IntegerSchema(ushort.MinValue, ushort.MaxValue),
                ApiPrimitiveType.Int32 =>
                    IntegerSchema(int.MinValue, int.MaxValue),
                ApiPrimitiveType.UInt32 =>
                    IntegerSchema(uint.MinValue, uint.MaxValue),
                ApiPrimitiveType.Int64 =>
                    IntegerSchema(long.MinValue, long.MaxValue),
                ApiPrimitiveType.UInt64 =>
                    new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = JsonValue.Create(ulong.MinValue),
                        ["maximum"] = JsonValue.Create(ulong.MaxValue),
                    },
                ApiPrimitiveType.IntPtr
                    or ApiPrimitiveType.UIntPtr =>
                    throw new JsonSchemaVocabularyException(
                        "primitive value",
                        "platform-sized integer schema is unsupported"),
                ApiPrimitiveType.Single
                    or ApiPrimitiveType.Double
                    or ApiPrimitiveType.Decimal =>
                    new JsonObject { ["type"] = "number" },
                ApiPrimitiveType.String =>
                    new JsonObject { ["type"] = "string" },
                ApiPrimitiveType.Object => JsonValue.Create(true)!,
                _ => throw new JsonSchemaVocabularyException(
                    "primitive value",
                    $"primitive '{primitive}' has no JSON wire mapping"),
            };

        static JsonObject IntegerSchema(
            long minimum,
            long maximum) =>
            new()
            {
                ["type"] = "integer",
                ["minimum"] = JsonValue.Create(minimum),
                ["maximum"] = JsonValue.Create(maximum),
            };

        static JsonNode AddNull(JsonNode value) =>
            new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    value,
                    new JsonObject { ["type"] = "null" }),
            };

        static JsonObject Reference(string definitionName) =>
            new()
            {
                ["$ref"] =
                    $"#/$defs/{EscapePointer(definitionName)}",
            };

        static bool IsStringShape(ApiTypeShape shape) =>
            shape is
            {
                Kind: ApiTypeShapeKind.Primitive,
                Primitive: ApiPrimitiveType.String,
            }
            || shape.Definition?.FullName == "System.String";

        static bool IsDictionary(string fullName) =>
            fullName is
                "System.Collections.Generic.Dictionary`2"
                or "System.Collections.Generic.IReadOnlyDictionary`2";

        static bool IsCollection(string fullName) =>
            fullName is
                "System.Collections.Generic.ICollection`1"
                or "System.Collections.Generic.IEnumerable`1"
                or "System.Collections.Generic.IReadOnlyCollection`1"
                or "System.Collections.Generic.IReadOnlyList`1"
                or "System.Collections.Generic.List`1"
                or "System.Collections.Immutable.ImmutableArray`1";

        static JsonSchemaVocabularyException Unsupported(
            ApiType type,
            string reason) =>
            new(type.FullName, reason);

        static string EscapePointer(string value) =>
            value.Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);

        internal static bool PointerResolves(
            JsonNode root,
            string pointer)
        {
            if (pointer.Length == 0)
                return true;
            if (pointer[0] != '/')
                return false;

            JsonNode? current = root;
            foreach (string encoded in pointer[1..].Split('/'))
            {
                string segment = encoded
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                current = current switch
                {
                    JsonObject value when value.TryGetPropertyValue(
                        segment,
                        out JsonNode? child) => child,
                    JsonArray value when int.TryParse(
                        segment,
                        out int index)
                        && index >= 0
                        && index < value.Count => value[index],
                    _ => null,
                };
                if (current is null)
                    return false;
            }
            return true;
        }
    }
}
