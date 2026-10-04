using DotnetInspector.Vocabulary;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace DotnetInspector.JsonSchema;

public sealed record DeclaredJsonSchemaVocabularyDescriptor(
    ApiType RootType,
    JsonSchemaVocabularyDescriptor Descriptor);

public static class DeclaredJsonSchemaVocabularyDescriptorBuilder
{
    public static IReadOnlyList<DeclaredJsonSchemaVocabularyDescriptor> Build(
        JsExportSurface surface,
        JsonWireDeclarationPlan declarationPlan,
        ApiAssemblyIdentity jsonContractIdentity,
        VocabularySnapshotReference vocabularySnapshot)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(declarationPlan);
        ArgumentNullException.ThrowIfNull(jsonContractIdentity);
        ArgumentNullException.ThrowIfNull(vocabularySnapshot);

        var results =
            new List<DeclaredJsonSchemaVocabularyDescriptor>();
        var identities = new HashSet<(
            JsonSchemaContractIdentity Contract,
            JsonWireDirection Direction)>();
        foreach ((ApiType type, JsonWireDirection admittedDirection)
            in surface.JsonSchemaRoots)
        {
            if (type.JsExportJsonSchemaDeclaration is not
                { } declaration)
            {
                continue;
            }

            JsonSchemaVocabularyDescriptor descriptor = BuildOne(
                surface,
                declarationPlan,
                jsonContractIdentity,
                vocabularySnapshot,
                type,
                declaration);
            if (descriptor.Direction != admittedDirection)
            {
                throw new JsonSchemaVocabularyException(
                    descriptor.Contract.Value,
                    "the admitted schema direction does not match its "
                        + "declaration");
            }
            if (!identities.Add((
                    descriptor.Contract,
                    descriptor.Direction)))
            {
                throw new JsonSchemaVocabularyException(
                    descriptor.Contract.Value,
                    "the contract and direction are declared more than once");
            }
            results.Add(new(type, descriptor));
        }

        return [
            .. results
                .OrderBy(result =>
                    result.Descriptor.Contract.Value,
                    StringComparer.Ordinal)
                .ThenBy(result =>
                    result.Descriptor.Direction),
        ];
    }

    static JsonSchemaVocabularyDescriptor BuildOne(
        JsExportSurface surface,
        JsonWireDeclarationPlan declarationPlan,
        ApiAssemblyIdentity jsonContractIdentity,
        VocabularySnapshotReference vocabularySnapshot,
        ApiType type,
        ApiJsExportJsonSchemaDeclaration declaration)
    {
        string location = type.FullName;
        if (declaration.AttributeAssembly is not
                { } attributeAssembly
            || !attributeAssembly.Equals(jsonContractIdentity))
        {
            throw new JsonSchemaVocabularyException(
                location,
                "JsExportJsonSchemaAttribute comes from an incompatible "
                    + "contract assembly");
        }
        string? unsupportedReason = declaration.UnsupportedReason;
        if (unsupportedReason is not null
            || declaration.ContractIdentity is not
                { } contractIdentityValue
            || declaration.Direction is not { } directionValue
            || declaration.VocabularyCatalog is not
                { } vocabularyCatalogValue
            || declaration.VocabularySnapshotIdentity is not
                { } vocabularySnapshotIdentityValue)
        {
            throw new JsonSchemaVocabularyException(
                location,
                "the JSON Schema declaration is malformed or unsupported"
                    + (unsupportedReason is null
                        ? ""
                        : $": {unsupportedReason}"));
        }

        JsonWireDirection direction = directionValue switch
        {
            "serialize" => JsonWireDirection.Serialize,
            "deserialize" => JsonWireDirection.Deserialize,
            _ => throw new JsonSchemaVocabularyException(
                location,
                "the JSON Schema direction must be 'serialize' or "
                    + "'deserialize'"),
        };
        if (!declarationPlan.Contains(type))
        {
            throw new JsonSchemaVocabularyException(
                location,
                "the declared root is absent from the shared wire "
                    + "declaration plan");
        }
        declarationPlan.Resolve(type, direction);

        JsonSchemaContractIdentity contractIdentity;
        VocabularyCatalogIdentity vocabularyCatalog;
        VocabularySnapshotIdentity vocabularySnapshotIdentity;
        try
        {
            contractIdentity = new(contractIdentityValue);
            vocabularyCatalog = new(vocabularyCatalogValue);
            vocabularySnapshotIdentity =
                new(vocabularySnapshotIdentityValue);
        }
        catch (ArgumentException exception)
        {
            throw new JsonSchemaVocabularyException(
                location,
                exception.Message);
        }
        if (vocabularyCatalog != vocabularySnapshot.Catalog)
        {
            throw new JsonSchemaVocabularyException(
                location,
                "the declared vocabulary catalog does not match the "
                    + "provided snapshot");
        }
        if (vocabularySnapshotIdentity != vocabularySnapshot.Identity)
        {
            throw new JsonSchemaVocabularyException(
                location,
                "the declared vocabulary snapshot identity does not match "
                    + "the provided snapshot");
        }

        var declaredSlots = new List<(
            ApiMember Member,
            ApiJsExportJsonSchemaSlotDeclaration Declaration)>();
        var participatingMembers = new HashSet<ApiMember>();
        foreach (ApiMember member in type.Members)
        {
            bool participates =
                JsonWireMemberRules.ParticipatesInWireContract(
                    member,
                    direction,
                    surface.AssemblyIdentity,
                    declarationPlan.DeclaredTypesByScopedIdentity);
            if (participates)
                participatingMembers.Add(member);
            if (member.JsExportJsonSchemaSlotDeclaration is
                { } slotDeclaration)
            {
                declaredSlots.Add((member, slotDeclaration));
            }
        }
        if (declaredSlots.Count == 0)
        {
            throw new JsonSchemaVocabularyException(
                location,
                "the positional contract declares no slots");
        }
        if (participatingMembers.Count != declaredSlots.Count
            || declaredSlots.Any(slot =>
                !participatingMembers.Contains(slot.Member)))
        {
            throw new JsonSchemaVocabularyException(
                location,
                "the positional declarations do not exactly cover the "
                    + "selected wire members");
        }

        var slots = new List<(
            int Order,
            JsonPositionalRowSlot Slot,
            VocabularyTermIdentity Term)>();
        foreach ((ApiMember member,
            ApiJsExportJsonSchemaSlotDeclaration slotDeclaration)
            in declaredSlots)
        {
            string memberLocation = $"{location}.{member.Name}";
            if (slotDeclaration.AttributeAssembly is not
                    { } slotAttributeAssembly
                || !slotAttributeAssembly.Equals(jsonContractIdentity))
            {
                throw new JsonSchemaVocabularyException(
                    memberLocation,
                    "JsExportJsonSchemaSlotAttribute comes from an "
                        + "incompatible contract assembly");
            }
            string? slotUnsupportedReason =
                slotDeclaration.UnsupportedReason;
            if (slotUnsupportedReason is not null
                || slotDeclaration.Order is not { } order
                || slotDeclaration.NodeIdentity is not
                    { } nodeIdentity
                || slotDeclaration.Vocabulary is not
                    { } vocabularyValue
                || slotDeclaration.Term is not { } termValue)
            {
                throw new JsonSchemaVocabularyException(
                    memberLocation,
                    "the JSON Schema slot declaration is malformed or "
                        + "unsupported"
                        + (slotUnsupportedReason is null
                            ? ""
                            : $": {slotUnsupportedReason}"));
            }
            if (order < 0)
            {
                throw new JsonSchemaVocabularyException(
                    memberLocation,
                    "the JSON Schema slot order must not be negative");
            }
            ApiTypeShape shape =
                member.SignatureModel?.ReturnTypeShape
                ?? throw new JsonSchemaVocabularyException(
                    memberLocation,
                    "the JSON Schema slot type shape is unavailable");
            VocabularyIdentity vocabulary;
            VocabularyTermIdentity term;
            try
            {
                vocabulary = new(
                    vocabularyCatalog,
                    vocabularyValue);
                term = new(vocabulary, termValue);
            }
            catch (ArgumentException exception)
            {
                throw new JsonSchemaVocabularyException(
                    memberLocation,
                    exception.Message);
            }
            bool allowsNull =
                JsonWireContractRules.CanValueBeNull(
                    shape,
                    surface.AssemblyIdentity,
                    declarationPlan.DeclaredTypesByScopedIdentity)
                == true;
            slots.Add((
                order,
                new(
                    nodeIdentity,
                    shape,
                    allowsNull,
                    slotDeclaration.Displayable),
                term));
        }

        slots.Sort((left, right) =>
            left.Order.CompareTo(right.Order));
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].Order != index)
            {
                throw new JsonSchemaVocabularyException(
                    location,
                    "the JSON Schema slot orders must be contiguous and "
                        + "zero-based");
            }
        }

        JsonPositionalRowContract row;
        try
        {
            row = new(slots.Select(slot => slot.Slot));
        }
        catch (ArgumentException exception)
        {
            throw new JsonSchemaVocabularyException(
                location,
                exception.Message);
        }
        var bindings =
            slots.Select(slot =>
                new JsonSchemaBindingDeclaration(
                    new JsonSchemaBindingTarget.PositionalSlot(
                        row,
                        slot.Slot),
                    slot.Term));
        var contract = new JsonSchemaContractDeclaration(
            contractIdentity,
            direction,
            new JsonSchemaContractRoot.Positional(row),
            bindings,
            JsonSchemaBindingCoverage.CompleteDisplaySlots);
        return JsonSchemaVocabularyDescriptorBuilder.Build(
            surface,
            declarationPlan,
            contract,
            vocabularySnapshot);
    }
}
