using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

public enum MemorySafetyRulesState
{
    Legacy,
    Updated,
    Unsupported,
    Malformed,
    Conflicting,
}

public enum MemorySafetyRulesObservationState
{
    Decoded,
    Malformed,
}

public sealed record MemorySafetyRulesObservation(
    int AttributeToken,
    MemorySafetyRulesObservationState State,
    int? Version,
    string? Detail);

public enum MemorySafetyMetadataFailureKind
{
    Malformed,
    BudgetExceeded,
}

public sealed record MemorySafetyMetadataFailure(
    MemorySafetyMetadataFailureKind Kind,
    string Detail);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemorySafetyRulesResult.Available), "available")]
[JsonDerivedType(typeof(MemorySafetyRulesResult.Unavailable), "unavailable")]
public abstract record MemorySafetyRulesResult(
    ImmutableArray<MemorySafetyRulesObservation> Observations)
{
    public sealed record Available(
        MemorySafetyRulesState State,
        ImmutableArray<MemorySafetyRulesObservation> Observations)
        : MemorySafetyRulesResult(Observations);

    public sealed record Unavailable(
        MemorySafetyMetadataFailure Failure,
        ImmutableArray<MemorySafetyRulesObservation> Observations)
        : MemorySafetyRulesResult(Observations);
}

[JsonConverter(typeof(JsonStringEnumConverter<MemorySafetyPointerEvidence>))]
public enum MemorySafetyPointerEvidence
{
    NotExamined,
    Absent,
    Present,
    Unavailable,
}

public enum MemorySafetyFixedBufferEvidence
{
    NotExamined,
    Absent,
    Present,
    Unavailable,
}

public enum RequiresUnsafeAttributeEvidenceState
{
    NotExamined,
    Read,
    Unavailable,
}

public readonly record struct RequiresUnsafeAttributeEvidence(
    RequiresUnsafeAttributeEvidenceState State,
    int ValidRowCount,
    bool HasMalformedRow)
{
    public bool HasValidRow => ValidRowCount > 0;

    public static RequiresUnsafeAttributeEvidence NotExamined =>
        new(RequiresUnsafeAttributeEvidenceState.NotExamined, 0, false);

    public static RequiresUnsafeAttributeEvidence None =>
        new(RequiresUnsafeAttributeEvidenceState.Read, 0, false);

    public static RequiresUnsafeAttributeEvidence Unavailable =>
        new(RequiresUnsafeAttributeEvidenceState.Unavailable, 0, false);
}

public sealed record MemorySafetyMemberContractEvidence(
    int MemberToken,
    MemorySafetyRulesState? RulesState,
    MemorySafetyPointerEvidence Pointer,
    MemorySafetyFixedBufferEvidence FixedBuffer,
    RequiresUnsafeAttributeEvidence DirectAttribute,
    RequiresUnsafeAttributeEvidence AssociatedAttribute,
    int? AssociatedMemberToken);

public enum MemorySafetyMemberContractFailureKind
{
    InvalidHandle,
    MetadataUnavailable,
    ConflictingRules,
    SignatureUnavailable,
    AttributeUnavailable,
    MalformedRequiresUnsafeAttribute,
    AmbiguousAssociation,
}

public sealed record MemorySafetyMemberContractFailure(
    MemorySafetyMemberContractFailureKind Kind,
    string Detail);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemorySafetyMemberContractResult.None), "none")]
[JsonDerivedType(typeof(MemorySafetyMemberContractResult.Implicit), "implicit")]
[JsonDerivedType(typeof(MemorySafetyMemberContractResult.Explicit), "explicit")]
[JsonDerivedType(typeof(MemorySafetyMemberContractResult.Unavailable), "unavailable")]
public abstract record MemorySafetyMemberContractResult(
    MemorySafetyMemberContractEvidence Evidence)
{
    public sealed record None(MemorySafetyMemberContractEvidence Evidence)
        : MemorySafetyMemberContractResult(Evidence);

    public sealed record Implicit(MemorySafetyMemberContractEvidence Evidence)
        : MemorySafetyMemberContractResult(Evidence);

    public sealed record Explicit(MemorySafetyMemberContractEvidence Evidence)
        : MemorySafetyMemberContractResult(Evidence);

    public sealed record Unavailable(
        MemorySafetyMemberContractEvidence Evidence,
        MemorySafetyMemberContractFailure Failure)
        : MemorySafetyMemberContractResult(Evidence);
}

/// <summary>
/// Derives memory-safety module rules and member caller contracts from one
/// metadata reader. Consumers retain ownership of body analysis, source
/// reconstruction, project policy, and presentation.
/// </summary>
/// <remarks>
/// <c>MemorySafetyMetadataIndex_RecognizesCompilerProducedModels</c>,
/// <c>MemorySafetyMetadataIndex_UsesVersionSpecificMemberContracts</c>, and
/// <c>AccessorFallsBackToAssociatedDefinitionCarrier</c> gate
/// the shared facts.
/// </remarks>
public sealed class MemorySafetyMetadataIndex
{
    const int UpdatedRulesVersion = 2;

    readonly MetadataReader _reader;
    readonly int _methodRowCount;
    readonly int _fieldRowCount;
    readonly int _propertyRowCount;
    readonly int _eventRowCount;
    readonly int _attributeRowBudget;
    readonly int _nameWorkBudget;
    readonly IReadOnlyDictionary<int, EntityHandle> _associatedContracts;
    readonly IReadOnlySet<int> _ambiguousAssociations;
    readonly bool _associationsIncomplete;

    MemorySafetyMetadataIndex(
        MetadataReader reader,
        int methodRowCount,
        int fieldRowCount,
        int propertyRowCount,
        int eventRowCount,
        int attributeRowBudget,
        int nameWorkBudget,
        MemorySafetyRulesResult rules,
        IReadOnlyDictionary<int, EntityHandle> associatedContracts,
        IReadOnlySet<int> ambiguousAssociations,
        MemorySafetyMetadataFailure? associationFailure,
        bool associationsIncomplete)
    {
        _reader = reader;
        _methodRowCount = methodRowCount;
        _fieldRowCount = fieldRowCount;
        _propertyRowCount = propertyRowCount;
        _eventRowCount = eventRowCount;
        _attributeRowBudget = attributeRowBudget;
        _nameWorkBudget = nameWorkBudget;
        Rules = rules;
        _associatedContracts = associatedContracts;
        _ambiguousAssociations = ambiguousAssociations;
        AssociationFailure = associationFailure;
        _associationsIncomplete = associationsIncomplete;
    }

    public MemorySafetyRulesResult Rules { get; }

    public MemorySafetyMetadataFailure? AssociationFailure { get; }

    public static MemorySafetyMetadataIndex Create(MetadataReader reader)
        => Create(
            reader,
            MetadataSafetyPolicy.MaxMemorySafetyAssociationRows,
            MetadataSafetyPolicy.MaxMemorySafetyAttributeRows,
            MetadataSafetyPolicy.MaxMemorySafetyNameWorkChars);

    internal static MemorySafetyMetadataIndex Create(
        MetadataReader reader,
        int associationRowBudget,
        int attributeRowBudget)
        => Create(
            reader,
            associationRowBudget,
            attributeRowBudget,
            MetadataSafetyPolicy.MaxMemorySafetyNameWorkChars);

    internal static MemorySafetyMetadataIndex Create(
        MetadataReader reader,
        int associationRowBudget,
        int attributeRowBudget,
        int nameWorkBudget)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            associationRowBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            attributeRowBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            nameWorkBudget);

        int methodRows;
        int fieldRows;
        int propertyRows;
        int eventRows;
        int methodSemanticsRows;
        MemorySafetyRulesResult rules;
        try
        {
            methodRows = reader.GetTableRowCount(TableIndex.MethodDef);
            fieldRows = reader.GetTableRowCount(TableIndex.Field);
            propertyRows = reader.GetTableRowCount(TableIndex.Property);
            eventRows = reader.GetTableRowCount(TableIndex.Event);
            methodSemanticsRows =
                reader.GetTableRowCount(TableIndex.MethodSemantics);
            if (!CustomAttributeParentsAreOrdered(reader))
            {
                return Failed(
                    reader,
                    attributeRowBudget,
                    nameWorkBudget,
                    MemorySafetyMetadataFailureKind.Malformed,
                    "The CustomAttribute table is not sorted by parent, so attribute owner lookups cannot observe every row.");
            }

            if (FindUnobservableProjection(reader) is { } projectionDefect)
            {
                return Failed(
                    reader,
                    attributeRowBudget,
                    nameWorkBudget,
                    MemorySafetyMetadataFailureKind.Malformed,
                    projectionDefect);
            }

            rules = ReadRules(
                reader,
                attributeRowBudget,
                nameWorkBudget);
        }
        catch (MetadataBudgetException)
        {
            return Failed(
                reader,
                attributeRowBudget,
                nameWorkBudget,
                MemorySafetyMetadataFailureKind.BudgetExceeded,
                "Memory-safety module metadata exceeded its scan budget.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            return Failed(
                reader,
                attributeRowBudget,
                nameWorkBudget,
                MemorySafetyMetadataFailureKind.Malformed,
                "Memory-safety module metadata could not be read.");
        }

        var associatedContracts = new Dictionary<int, EntityHandle>();
        var ambiguousAssociations = new HashSet<int>();
        MemorySafetyMetadataFailure? associationFailure = null;
        bool associationsIncomplete = false;
        if (rules is MemorySafetyRulesResult.Available)
        {
            try
            {
                BuildAssociations(
                    reader,
                    methodRows,
                    propertyRows,
                    eventRows,
                    methodSemanticsRows,
                    associationRowBudget,
                    associatedContracts,
                    ambiguousAssociations,
                    out bool hasMalformedRows,
                    out bool projectionIsIncomplete);
                if (projectionIsIncomplete)
                {
                    associationsIncomplete = true;
                    associationFailure = new(
                        MemorySafetyMetadataFailureKind.Malformed,
                        "The MethodSemantics table has rows that accessor projection does not observe, so accessor associations cannot be trusted.");
                }
                else if (hasMalformedRows)
                {
                    associationFailure = new(
                        MemorySafetyMetadataFailureKind.Malformed,
                        "One or more memory-safety accessor associations reference invalid MethodDefs.");
                }
            }
            catch (MetadataBudgetException)
            {
                associationsIncomplete = true;
                associationFailure = new(
                    MemorySafetyMetadataFailureKind.BudgetExceeded,
                    "Memory-safety accessor associations exceeded their scan budget.");
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or InvalidOperationException
                    or OverflowException)
            {
                associationsIncomplete = true;
                associationFailure = new(
                    MemorySafetyMetadataFailureKind.Malformed,
                    "Memory-safety accessor associations could not be read.");
            }
        }

        if (associationsIncomplete)
        {
            // A partial projection can also mis-associate the accessors it did
            // observe, so no association survives an incomplete scan. Direct
            // carriers stay decisive because they never depend on this map.
            associatedContracts.Clear();
            ambiguousAssociations.Clear();
        }

        return new(
            reader,
            methodRows,
            fieldRows,
            propertyRows,
            eventRows,
            attributeRowBudget,
            nameWorkBudget,
            rules,
            associatedContracts,
            ambiguousAssociations,
            associationFailure,
            associationsIncomplete);
    }

    public MemorySafetyMemberContractResult GetMemberContract(
        EntityHandle member)
    {
        if (!IsValidMemberHandle(member))
        {
            return Unavailable(
                EmptyEvidence(member, rulesState: null),
                MemorySafetyMemberContractFailureKind.InvalidHandle,
                "The member handle is nil, out of range, or not a supported definition handle.");
        }

        if (Rules is MemorySafetyRulesResult.Unavailable unavailableRules)
        {
            return Unavailable(
                EmptyEvidence(member, rulesState: null),
                MemorySafetyMemberContractFailureKind.MetadataUnavailable,
                unavailableRules.Failure.Detail);
        }

        var availableRules =
            (MemorySafetyRulesResult.Available)Rules;
        return availableRules.State switch
        {
            MemorySafetyRulesState.Updated =>
                GetUpdatedContract(member, availableRules.State),
            MemorySafetyRulesState.Conflicting =>
                GetConflictingContract(member, availableRules.State),
            _ => GetLegacyCompatibleContract(
                member,
                availableRules.State),
        };
    }

    MemorySafetyMemberContractResult GetLegacyCompatibleContract(
        EntityHandle member,
        MemorySafetyRulesState rulesState)
    {
        PointerReadResult pointer = ReadPointerEvidence(member);
        AttributeReadResult direct =
            ReadRequiresUnsafeAttributes(GetCustomAttributes(member));
        (AttributeReadResult associated, EntityHandle associatedMember) =
            ReadAssociatedAttributeEvidence(member);

        var evidence = new MemorySafetyMemberContractEvidence(
            MetadataTokens.GetToken(member),
            rulesState,
            pointer.Evidence,
            pointer.FixedBuffer,
            direct.Evidence,
            associated.Evidence,
            associatedMember.IsNil
                ? null
                : MetadataTokens.GetToken(associatedMember));

        if (pointer.Evidence == MemorySafetyPointerEvidence.Present)
        {
            // The fixed-buffer exemption only ever excludes a definite pointer
            // from propagation. It is not evidence about a signature that was
            // never decoded, so it must not stand in for one.
            return pointer.FixedBuffer
                == MemorySafetyFixedBufferEvidence.Present
                    ? new MemorySafetyMemberContractResult.None(evidence)
                    : new MemorySafetyMemberContractResult.Implicit(evidence);
        }

        if (pointer.Evidence == MemorySafetyPointerEvidence.Unavailable)
        {
            return Unavailable(
                evidence,
                MemorySafetyMemberContractFailureKind.SignatureUnavailable,
                "The member signature could not be decoded.");
        }

        return new MemorySafetyMemberContractResult.None(evidence);
    }

    MemorySafetyMemberContractResult GetUpdatedContract(
        EntityHandle member,
        MemorySafetyRulesState rulesState)
    {
        AttributeReadResult direct =
            ReadRequiresUnsafeAttributes(GetCustomAttributes(member));
        EntityHandle associated = default;
        AttributeReadResult associatedAttributes =
            AttributeReadResult.NotExamined;
        MemorySafetyMemberContractEvidence directEvidence =
            CreateAttributeEvidence(
                member,
                rulesState,
                direct,
                associatedAttributes,
                associated);
        if (direct.IsUnavailable)
        {
            return Unavailable(
                directEvidence,
                MemorySafetyMemberContractFailureKind.AttributeUnavailable,
                "RequiresUnsafeAttribute metadata could not be read.");
        }
        if (direct.Evidence.HasMalformedRow)
        {
            return Unavailable(
                directEvidence,
                MemorySafetyMemberContractFailureKind.MalformedRequiresUnsafeAttribute,
                "A RequiresUnsafeAttribute row has an unsupported constructor or value.");
        }
        if (direct.Evidence.HasValidRow)
            return new MemorySafetyMemberContractResult.Explicit(
                directEvidence);

        if (member.Kind == HandleKind.MethodDefinition)
        {
            int token = MetadataTokens.GetToken(member);
            if (_ambiguousAssociations.Contains(token))
            {
                return Unavailable(
                    CreateAttributeEvidence(
                        member,
                        rulesState,
                        direct,
                        associatedAttributes,
                        associated),
                    MemorySafetyMemberContractFailureKind.AmbiguousAssociation,
                    "The accessor is associated with more than one property or event.");
            }

            if (_associatedContracts.TryGetValue(token, out associated))
            {
                associatedAttributes = ReadRequiresUnsafeAttributes(
                    GetCustomAttributes(associated));
            }
            else if (_associationsIncomplete)
            {
                return Unavailable(
                    directEvidence,
                    MemorySafetyMemberContractFailureKind.MetadataUnavailable,
                    AssociationFailure?.Detail
                        ?? "Memory-safety accessor associations are unavailable.");
            }
        }

        MemorySafetyMemberContractEvidence evidence =
            CreateAttributeEvidence(
                member,
                rulesState,
                direct,
                associatedAttributes,
                associated);
        if (associatedAttributes.IsUnavailable)
        {
            return Unavailable(
                evidence,
                MemorySafetyMemberContractFailureKind.AttributeUnavailable,
                "RequiresUnsafeAttribute metadata could not be read.");
        }

        if (associatedAttributes.Evidence.HasMalformedRow)
        {
            return Unavailable(
                evidence,
                MemorySafetyMemberContractFailureKind.MalformedRequiresUnsafeAttribute,
                "A RequiresUnsafeAttribute row has an unsupported constructor or value.");
        }

        return associatedAttributes.Evidence.HasValidRow
            ? new MemorySafetyMemberContractResult.Explicit(evidence)
            : new MemorySafetyMemberContractResult.None(evidence);
    }

    MemorySafetyMemberContractResult GetConflictingContract(
        EntityHandle member,
        MemorySafetyRulesState rulesState)
    {
        PointerReadResult pointer = ReadPointerEvidence(member);
        AttributeReadResult direct =
            ReadRequiresUnsafeAttributes(GetCustomAttributes(member));
        (AttributeReadResult associatedAttributes, EntityHandle associated) =
            ReadAssociatedAttributeEvidence(member);

        var evidence = new MemorySafetyMemberContractEvidence(
            MetadataTokens.GetToken(member),
            rulesState,
            pointer.Evidence,
            pointer.FixedBuffer,
            direct.Evidence,
            associatedAttributes.Evidence,
            associated.IsNil
                ? null
                : MetadataTokens.GetToken(associated));
        return Unavailable(
            evidence,
            MemorySafetyMemberContractFailureKind.ConflictingRules,
            "Conflicting module markers do not establish one memory-safety model.");
    }

    MemorySafetyMemberContractEvidence CreateAttributeEvidence(
        EntityHandle member,
        MemorySafetyRulesState rulesState,
        AttributeReadResult direct,
        AttributeReadResult associated,
        EntityHandle associatedMember)
        => new(
            MetadataTokens.GetToken(member),
            rulesState,
            MemorySafetyPointerEvidence.NotExamined,
            MemorySafetyFixedBufferEvidence.NotExamined,
            direct.Evidence,
            associated.Evidence,
            associatedMember.IsNil
                ? null
                : MetadataTokens.GetToken(associatedMember));

    (AttributeReadResult Evidence, EntityHandle Associated)
        ReadAssociatedAttributeEvidence(EntityHandle member)
    {
        if (member.Kind != HandleKind.MethodDefinition)
            return (AttributeReadResult.NotExamined, default);

        int token = MetadataTokens.GetToken(member);
        if (_ambiguousAssociations.Contains(token))
            return (AttributeReadResult.Unavailable, default);
        if (_associatedContracts.TryGetValue(token, out EntityHandle associated))
        {
            return (
                ReadRequiresUnsafeAttributes(
                    GetCustomAttributes(associated)),
                associated);
        }

        return _associationsIncomplete
            ? (AttributeReadResult.Unavailable, default)
            : (AttributeReadResult.None, default);
    }

    PointerReadResult ReadPointerEvidence(EntityHandle member)
    {
        try
        {
            return new(
                PointerDetector.ReadMember(_reader, member),
                member.Kind == HandleKind.FieldDefinition
                    ? ReadFixedBufferEvidence(
                        _reader.GetFieldDefinition((FieldDefinitionHandle)member))
                    : MemorySafetyFixedBufferEvidence.NotExamined);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or ArgumentException)
        {
            return new(
                MemorySafetyPointerEvidence.Unavailable,
                MemorySafetyFixedBufferEvidence.Unavailable);
        }
    }

    MemorySafetyFixedBufferEvidence ReadFixedBufferEvidence(FieldDefinition field)
    {
        FixedBufferMetadataReadResult fixedBuffer;
        try
        {
            var nameBudget =
                new MetadataNameWorkBudget(_nameWorkBudget);
            fixedBuffer = FixedBufferMetadata.Read(
                _reader,
                field.GetCustomAttributes(),
                _attributeRowBudget,
                nameBudget.Observe);
        }
        catch (MetadataBudgetException)
        {
            fixedBuffer = new(
                FixedBufferMetadataReadState.Unavailable,
                Info: null);
        }
        return fixedBuffer.State switch
        {
            FixedBufferMetadataReadState.Present =>
                MemorySafetyFixedBufferEvidence.Present,
            FixedBufferMetadataReadState.Absent =>
                MemorySafetyFixedBufferEvidence.Absent,
            _ => MemorySafetyFixedBufferEvidence.Unavailable,
        };
    }

    AttributeReadResult ReadRequiresUnsafeAttributes(
        CustomAttributeHandleCollection attributes)
    {
        if (attributes.Count > _attributeRowBudget)
            return AttributeReadResult.Unavailable;

        var nameBudget = new MetadataNameWorkBudget(_nameWorkBudget);
        int validRows = 0;
        bool malformed = false;
        foreach (CustomAttributeHandle handle in attributes)
        {
            try
            {
                CustomAttribute attribute =
                    _reader.GetCustomAttribute(handle);
                string? name = AttributeReader.GetAttributeTypeName(
                    _reader,
                    attribute.Constructor,
                    nameBudget.Observe);
                if (name is not KnownAttributeNames.RequiresUnsafeAttribute
                    and not KnownAttributeNames.RequiresUnsafeAttributeCompilerServices)
                {
                    continue;
                }
                if (!AttributeReader.IsTopLevelAttributeType(
                        _reader,
                        attribute.Constructor,
                        name,
                        nameBudget.Observe))
                {
                    continue;
                }

                if (AttributeReader.HasExpectedMarkerConstructor(
                        _reader,
                        attribute.Constructor,
                        nameBudget.Observe)
                    && AttributeDecoder.TryDecode(
                        _reader,
                        attribute,
                        nameBudget.Observe) is
                        {
                            FixedArguments.Length: 0,
                            NamedArguments.Length: 0,
                        })
                {
                    validRows++;
                }
                else
                {
                    malformed = true;
                }
            }
            catch (MetadataBudgetException)
            {
                return AttributeReadResult.Unavailable;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or InvalidOperationException)
            {
                return AttributeReadResult.Unavailable;
            }
        }

        return new(
            new(
                RequiresUnsafeAttributeEvidenceState.Read,
                validRows,
                malformed),
            IsUnavailable: false);
    }

    CustomAttributeHandleCollection GetCustomAttributes(
        EntityHandle member)
        => member.Kind switch
        {
            HandleKind.MethodDefinition =>
                _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)member).GetCustomAttributes(),
            HandleKind.FieldDefinition =>
                _reader.GetFieldDefinition(
                    (FieldDefinitionHandle)member).GetCustomAttributes(),
            HandleKind.PropertyDefinition =>
                _reader.GetPropertyDefinition(
                    (PropertyDefinitionHandle)member).GetCustomAttributes(),
            HandleKind.EventDefinition =>
                _reader.GetEventDefinition(
                    (EventDefinitionHandle)member).GetCustomAttributes(),
            _ => default,
        };

    bool IsValidMemberHandle(EntityHandle member)
    {
        int row = MetadataTokens.GetRowNumber(member);
        return row > 0
            && member.Kind switch
            {
                HandleKind.MethodDefinition => row <= _methodRowCount,
                HandleKind.FieldDefinition => row <= _fieldRowCount,
                HandleKind.PropertyDefinition => row <= _propertyRowCount,
                HandleKind.EventDefinition => row <= _eventRowCount,
                _ => false,
            };
    }

    static MemorySafetyMemberContractEvidence EmptyEvidence(
        EntityHandle member,
        MemorySafetyRulesState? rulesState)
        => new(
            member.IsNil ? 0 : MetadataTokens.GetToken(member),
            rulesState,
            MemorySafetyPointerEvidence.NotExamined,
            MemorySafetyFixedBufferEvidence.NotExamined,
            RequiresUnsafeAttributeEvidence.NotExamined,
            RequiresUnsafeAttributeEvidence.NotExamined,
            AssociatedMemberToken: null);

    static MemorySafetyMemberContractResult.Unavailable Unavailable(
        MemorySafetyMemberContractEvidence evidence,
        MemorySafetyMemberContractFailureKind kind,
        string detail)
        => new(evidence, new(kind, detail));

    static MemorySafetyRulesResult ReadRules(
        MetadataReader reader,
        int attributeRowBudget,
        int nameWorkBudget)
    {
        CustomAttributeHandleCollection attributes =
            reader.GetModuleDefinition().GetCustomAttributes();
        if (attributes.Count > attributeRowBudget)
            throw new MetadataBudgetException();

        var observations =
            ImmutableArray.CreateBuilder<MemorySafetyRulesObservation>();
        var nameBudget = new MetadataNameWorkBudget(nameWorkBudget);
        bool malformed = false;
        try
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute =
                    reader.GetCustomAttribute(handle);
                string? name = AttributeReader.GetAttributeTypeName(
                    reader,
                    attribute.Constructor,
                    nameBudget.Observe);
                if (name != KnownAttributeNames.MemorySafetyRulesAttribute)
                    continue;
                if (!AttributeReader.IsTopLevelAttributeType(
                        reader,
                        attribute.Constructor,
                        name,
                        nameBudget.Observe))
                {
                    continue;
                }

                if (TryReadRulesVersion(
                        reader,
                        attribute,
                        nameBudget.Observe,
                        out int markerVersion))
                {
                    observations.Add(
                        new(
                            MetadataTokens.GetToken(handle),
                            MemorySafetyRulesObservationState.Decoded,
                            markerVersion,
                            Detail: null));
                }
                else
                {
                    malformed = true;
                    observations.Add(
                        new(
                            MetadataTokens.GetToken(handle),
                            MemorySafetyRulesObservationState.Malformed,
                            Version: null,
                            "The attribute is not exactly one Int32 fixed argument with no named arguments."));
                }
            }
        }
        catch (Exception ex) when (
            ex is MetadataBudgetException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException)
        {
            // R4: a refusal must not suppress markers already decoded from
            // earlier rows, so partial evidence travels with the failure.
            bool exhausted = ex is MetadataBudgetException;
            return new MemorySafetyRulesResult.Unavailable(
                new MemorySafetyMetadataFailure(
                    exhausted
                        ? MemorySafetyMetadataFailureKind.BudgetExceeded
                        : MemorySafetyMetadataFailureKind.Malformed,
                    exhausted
                        ? "Memory-safety module metadata exceeded its scan budget."
                        : "Memory-safety module metadata could not be read."),
                observations.ToImmutable());
        }

        ImmutableArray<MemorySafetyRulesObservation> evidence =
            observations.ToImmutable();
        if (evidence.IsEmpty)
        {
            return new MemorySafetyRulesResult.Available(
                MemorySafetyRulesState.Legacy,
                evidence);
        }

        if (malformed)
        {
            return new MemorySafetyRulesResult.Available(
                MemorySafetyRulesState.Malformed,
                evidence);
        }

        int normalizedVersion = evidence[0].Version!.Value;
        if (evidence.Any(observation =>
                observation.Version != normalizedVersion))
        {
            return new MemorySafetyRulesResult.Available(
                MemorySafetyRulesState.Conflicting,
                evidence);
        }

        return new MemorySafetyRulesResult.Available(
            normalizedVersion == UpdatedRulesVersion
                ? MemorySafetyRulesState.Updated
                : MemorySafetyRulesState.Unsupported,
            evidence);
    }

    static bool TryReadRulesVersion(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int> beforeMaterialize,
        out int version)
    {
        if (AttributeReader.HasExpectedInt32Constructor(
                reader,
                attribute.Constructor,
                beforeMaterialize)
            && AttributeDecoder.TryDecode(
                reader,
                attribute,
                beforeMaterialize) is
                {
                    FixedArguments:
                    [
                        {
                            Type: "int",
                            Value: int decoded,
                        },
                    ],
                    NamedArguments.Length: 0,
                })
        {
            version = decoded;
            return true;
        }

        version = default;
        return false;
    }

    /// <summary>
    /// Proves that the CustomAttribute table is physically sorted by its
    /// <c>HasCustomAttribute</c> parent coded index, as ECMA-335 II.22
    /// requires. SRM answers every owner-range lookup with a binary search
    /// whenever the tables stream claims the table is sorted, so an image that
    /// asserts that claim over unsorted rows can hide module markers and member
    /// carriers from <c>GetCustomAttributes</c> entirely. Rows are read once at
    /// construction and the index fails closed rather than reporting an
    /// attribute-derived contract it cannot observe completely.
    /// </summary>
    /// <summary>
    /// Proves that every projection this index reads through can observe every
    /// physical row it depends on, returning the defect when one cannot.
    /// </summary>
    /// <remarks>
    /// R2 requires any table an answer depends on to be proven whole before it
    /// is read. Attribute owner ranges are proven separately by
    /// <see cref="CustomAttributeParentsAreOrdered"/>; identity and accessor
    /// answers additionally depend on declaring-type resolution, which SRM
    /// answers with range and binary searches over NestedClass, the TypeDef
    /// method ranges, PropertyMap, and EventMap. Each search silently returns
    /// "not found" on a table whose physical order contradicts its sorted
    /// claim, which would read a nested carrier as top-level or an owned
    /// member as unowned. Every check below therefore pairs an enumeration
    /// that reads physical rows against the search that must agree with it, and
    /// accounts the reachable rows against the physical row count.
    /// </remarks>
    static string? FindUnobservableProjection(MetadataReader reader)
    {
        long rows = (long)reader.GetTableRowCount(TableIndex.TypeDef)
            + reader.GetTableRowCount(TableIndex.MethodDef)
            + reader.GetTableRowCount(TableIndex.NestedClass)
            + reader.GetTableRowCount(TableIndex.Property)
            + reader.GetTableRowCount(TableIndex.Event);
        if (rows > MetadataSafetyPolicy.MaxMemorySafetyProjectionIntegrityRows)
            throw new MetadataBudgetException();

        int reachableNested = 0;
        int reachableMethods = 0;
        int reachableProperties = 0;
        int reachableEvents = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);

            foreach (TypeDefinitionHandle nested in type.GetNestedTypes())
            {
                reachableNested++;
                if (reader.GetTypeDefinition(nested).GetDeclaringType()
                    != handle)
                {
                    return "The NestedClass table is not ordered by nested type, so declaring-type lookups cannot observe every row.";
                }
            }

            foreach (MethodDefinitionHandle method in type.GetMethods())
            {
                reachableMethods++;
                if (reader.GetMethodDefinition(method).GetDeclaringType()
                    != handle)
                {
                    return "The TypeDef method ranges are not ordered, so a method's declaring type cannot be resolved from every row.";
                }
            }

            foreach (PropertyDefinitionHandle property in type.GetProperties())
            {
                reachableProperties++;
                if (reader.GetPropertyDefinition(property).GetDeclaringType()
                    != handle)
                {
                    return "The PropertyMap table is not ordered by parent, so property owner lookups cannot observe every row.";
                }
            }

            foreach (EventDefinitionHandle @event in type.GetEvents())
            {
                reachableEvents++;
                if (reader.GetEventDefinition(@event).GetDeclaringType()
                    != handle)
                {
                    return "The EventMap table is not ordered by parent, so event owner lookups cannot observe every row.";
                }
            }
        }

        if (reachableNested != reader.GetTableRowCount(TableIndex.NestedClass))
            return "The NestedClass table has rows no declaring-type lookup can reach.";
        if (reachableMethods != reader.GetTableRowCount(TableIndex.MethodDef))
            return "The TypeDef method ranges leave MethodDef rows no declaring-type lookup can reach.";
        if (reachableProperties != reader.GetTableRowCount(TableIndex.Property))
            return "The PropertyMap table has Property rows no owner lookup can reach.";
        if (reachableEvents != reader.GetTableRowCount(TableIndex.Event))
            return "The EventMap table has Event rows no owner lookup can reach.";

        return null;
    }

    static bool CustomAttributeParentsAreOrdered(MetadataReader reader)
    {
        if (reader.GetTableRowCount(TableIndex.CustomAttribute)
            > MetadataSafetyPolicy.MaxMemorySafetyCustomAttributeOrderRows)
        {
            throw new MetadataBudgetException();
        }

        int previous = -1;
        foreach (CustomAttributeHandle handle in reader.CustomAttributes)
        {
            EntityHandle parent = reader.GetCustomAttribute(handle).Parent;
            int coded;
            try
            {
                coded = CodedIndex.HasCustomAttribute(parent);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (coded < previous)
                return false;
            previous = coded;
        }

        return true;
    }

    static void BuildAssociations(
        MetadataReader reader,
        int methodRowCount,
        int propertyRowCount,
        int eventRowCount,
        int methodSemanticsRowCount,
        int rowBudget,
        Dictionary<int, EntityHandle> associations,
        HashSet<int> ambiguous,
        out bool hasMalformedRows,
        out bool isIncomplete)
    {
        if (checked(
                propertyRowCount
                + eventRowCount
                + methodSemanticsRowCount) > rowBudget)
            throw new MetadataBudgetException();

        hasMalformedRows = false;
        int projectedRows = 0;
        foreach (PropertyDefinitionHandle propertyHandle
            in reader.PropertyDefinitions)
        {
            PropertyDefinition property =
                reader.GetPropertyDefinition(propertyHandle);
            TypeDefinitionHandle propertyOwner = property.GetDeclaringType();
            PropertyAccessors accessors = property.GetAccessors();
            AddAssociation(
                reader,
                accessors.Getter,
                propertyHandle,
                propertyOwner,
                AccessorRole.PropertyGetter,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            AddAssociation(
                reader,
                accessors.Setter,
                propertyHandle,
                propertyOwner,
                AccessorRole.PropertySetter,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            foreach (MethodDefinitionHandle other in accessors.Others)
            {
                AddAssociation(
                    reader,
                    other,
                    propertyHandle,
                    propertyOwner,
                    AccessorRole.Other,
                    methodRowCount,
                    associations,
                    ambiguous,
                    ref hasMalformedRows,
                    ref projectedRows);
            }
        }

        foreach (EventDefinitionHandle eventHandle
            in reader.EventDefinitions)
        {
            EventDefinition eventDefinition =
                reader.GetEventDefinition(eventHandle);
            TypeDefinitionHandle eventOwner =
                eventDefinition.GetDeclaringType();
            EventAccessors accessors = eventDefinition.GetAccessors();
            AddAssociation(
                reader,
                accessors.Adder,
                eventHandle,
                eventOwner,
                AccessorRole.EventAdder,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            AddAssociation(
                reader,
                accessors.Remover,
                eventHandle,
                eventOwner,
                AccessorRole.EventRemover,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            AddAssociation(
                reader,
                accessors.Raiser,
                eventHandle,
                eventOwner,
                AccessorRole.Other,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            foreach (MethodDefinitionHandle other in accessors.Others)
            {
                AddAssociation(
                    reader,
                    other,
                    eventHandle,
                    eventOwner,
                    AccessorRole.Other,
                    methodRowCount,
                    associations,
                    ambiguous,
                    ref hasMalformedRows,
                    ref projectedRows);
            }
        }

        // PropertyAccessors and EventAccessors expose one slot per semantic
        // role and SRM counts a single owner's rows in a ushort, so duplicate
        // rows, rows whose owner is unreachable, and a 65,536-row wrap all
        // vanish from the projection without any error. Only a row-for-row
        // accounting against the physical MethodSemantics table proves the
        // association map observed the whole table.
        isIncomplete = projectedRows != methodSemanticsRowCount;
    }

    /// <summary>
    /// The semantic role a MethodSemantics row assigns to an accessor, used to
    /// apply the ECMA-335 II.22.28 shape constraint for that role.
    /// </summary>
    enum AccessorRole
    {
        PropertyGetter,
        PropertySetter,
        EventAdder,
        EventRemover,
        Other,
    }

    /// <summary>
    /// Whether a projected accessor row is positively determined to violate the
    /// shape ECMA-335 II.22.28 requires for its role.
    /// </summary>
    /// <remarks>
    /// R3 forbids inheriting a contract through a relationship that does not
    /// satisfy its spec constraints. The checks below are limited to properties
    /// real compiler output always satisfies — accessors are emitted
    /// <c>specialname</c>, an adder or remover takes exactly one argument, and a
    /// setter takes exactly one more argument than its property's index — so a
    /// legitimate accessor is never dropped. An undecodable signature is a
    /// refusal, not a violation, and is left to the caller's evidence rather
    /// than treated as a negative answer (R4).
    ///
    /// This validates shape, not full signature-type identity. The unvalidated
    /// residue can only make a method over-report as requiring unsafe, which an
    /// assembly author gains nothing by forging, whereas rejecting a legitimate
    /// accessor would under-report and hide real unsafety.
    /// </remarks>
    static bool AccessorShapeIsInvalid(
        MetadataReader reader,
        MethodDefinitionHandle method,
        EntityHandle associated,
        AccessorRole role)
    {
        MethodDefinition definition = reader.GetMethodDefinition(method);
        if ((definition.Attributes & MethodAttributes.SpecialName) == 0)
            return true;

        if (role is AccessorRole.Other)
            return false;

        if (TryReadParameterCount(reader, definition.Signature)
            is not { } parameters)
        {
            return false;
        }

        return role switch
        {
            AccessorRole.EventAdder or AccessorRole.EventRemover =>
                parameters != 1,
            AccessorRole.PropertyGetter =>
                TryReadPropertyIndexCount(reader, associated)
                    is { } getterIndexes
                    && parameters != getterIndexes,
            AccessorRole.PropertySetter =>
                TryReadPropertyIndexCount(reader, associated)
                    is { } setterIndexes
                    && parameters != setterIndexes + 1,
            _ => false,
        };
    }

    /// <summary>
    /// Reads the index parameter count declared by an associated property's
    /// signature, or null when the association is not a property or the blob
    /// cannot be read as a property signature.
    /// </summary>
    static int? TryReadPropertyIndexCount(
        MetadataReader reader,
        EntityHandle associated)
    {
        if (associated.Kind is not HandleKind.PropertyDefinition)
            return null;

        try
        {
            PropertyDefinition property = reader.GetPropertyDefinition(
                (PropertyDefinitionHandle)associated);
            BlobReader blob = reader.GetBlobReader(property.Signature);
            SignatureHeader header = blob.ReadSignatureHeader();
            if (header.Kind is not SignatureKind.Property)
                return null;

            return blob.ReadCompressedInteger();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a method signature's declared parameter count, or null when the
    /// blob cannot be read as one.
    /// </summary>
    static int? TryReadParameterCount(
        MetadataReader reader,
        BlobHandle signature)
    {
        try
        {
            BlobReader blob = reader.GetBlobReader(signature);
            SignatureHeader header = blob.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method)
                return null;
            if (header.IsGeneric)
                _ = blob.ReadCompressedInteger();
            return blob.ReadCompressedInteger();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    static void AddAssociation(
        MetadataReader reader,
        MethodDefinitionHandle method,
        EntityHandle associated,
        TypeDefinitionHandle owner,
        AccessorRole role,
        int methodRowCount,
        Dictionary<int, EntityHandle> associations,
        HashSet<int> ambiguous,
        ref bool hasMalformedRows,
        ref int projectedRows)
    {
        if (method.IsNil)
            return;

        projectedRows++;
        int row = MetadataTokens.GetRowNumber(method);
        if (row <= 0 || row > methodRowCount)
        {
            hasMalformedRows = true;
            return;
        }

        // ECMA-335 II.22.28 requires an accessor and its associated property
        // or event to be declared by the same type, but SRM projects the row
        // without checking that. Carrying an attribute across types would let
        // an unrelated method inherit another type's declaration, so a row
        // that crosses types is rejected like any other invalid row.
        if (owner.IsNil
            || reader.GetMethodDefinition(method).GetDeclaringType() != owner)
        {
            hasMalformedRows = true;
            return;
        }

        // Same-type ownership proves the row names a member of the right type,
        // not that the named member can be this accessor. A row naming an
        // ordinary method still projects, so the relationship itself is
        // validated before any contract inherits through it (R3).
        if (AccessorShapeIsInvalid(reader, method, associated, role))
        {
            hasMalformedRows = true;
            return;
        }

        int token = MetadataTokens.GetToken(method);
        if (ambiguous.Contains(token))
            return;
        if (associations.TryGetValue(token, out EntityHandle existing)
            && existing != associated)
        {
            associations.Remove(token);
            ambiguous.Add(token);
            return;
        }

        associations[token] = associated;
    }

    static MemorySafetyMetadataIndex Failed(
        MetadataReader reader,
        int attributeRowBudget,
        int nameWorkBudget,
        MemorySafetyMetadataFailureKind kind,
        string detail)
    {
        int methodRows = 0;
        int fieldRows = 0;
        int propertyRows = 0;
        int eventRows = 0;
        try
        {
            methodRows = reader.GetTableRowCount(TableIndex.MethodDef);
            fieldRows = reader.GetTableRowCount(TableIndex.Field);
            propertyRows = reader.GetTableRowCount(TableIndex.Property);
            eventRows = reader.GetTableRowCount(TableIndex.Event);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
        }

        var failure = new MemorySafetyMetadataFailure(kind, detail);
        return new(
            reader,
            methodRows,
            fieldRows,
            propertyRows,
            eventRows,
            attributeRowBudget,
            nameWorkBudget,
            new MemorySafetyRulesResult.Unavailable(failure, []),
            new Dictionary<int, EntityHandle>(),
            new HashSet<int>(),
            associationFailure: null,
            associationsIncomplete: false);
    }

    readonly record struct PointerReadResult(
        MemorySafetyPointerEvidence Evidence,
        MemorySafetyFixedBufferEvidence FixedBuffer);

    readonly record struct AttributeReadResult(
        RequiresUnsafeAttributeEvidence Evidence,
        bool IsUnavailable)
    {
        public static AttributeReadResult NotExamined =>
            new(
                RequiresUnsafeAttributeEvidence.NotExamined,
                IsUnavailable: false);

        public static AttributeReadResult None =>
            new(
                RequiresUnsafeAttributeEvidence.None,
                IsUnavailable: false);

        public static AttributeReadResult Unavailable =>
            new(
                RequiresUnsafeAttributeEvidence.Unavailable,
                IsUnavailable: true);
    }

    sealed class MetadataNameWorkBudget(int remaining)
    {
        int _remaining = remaining;

        public void Observe(int characters)
        {
            if (characters < 0 || characters > _remaining)
                throw new MetadataBudgetException();
            _remaining -= characters;
        }
    }

    sealed class MetadataBudgetException : Exception;
}
