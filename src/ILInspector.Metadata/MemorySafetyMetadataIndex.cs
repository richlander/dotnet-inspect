using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

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

        if (pointer.FixedBuffer
            == MemorySafetyFixedBufferEvidence.Present)
            return new MemorySafetyMemberContractResult.None(evidence);

        if (pointer.Evidence == MemorySafetyPointerEvidence.Unavailable)
        {
            return Unavailable(
                evidence,
                MemorySafetyMemberContractFailureKind.SignatureUnavailable,
                "The member signature could not be decoded.");
        }

        return pointer.Evidence == MemorySafetyPointerEvidence.Present
            ? new MemorySafetyMemberContractResult.Implicit(evidence)
            : new MemorySafetyMemberContractResult.None(evidence);
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
            return member.Kind switch
            {
                HandleKind.MethodDefinition =>
                    ReadMethodPointer(
                        _reader.GetMethodDefinition(
                            (MethodDefinitionHandle)member)),
                HandleKind.FieldDefinition =>
                    ReadFieldPointer(
                        _reader.GetFieldDefinition(
                            (FieldDefinitionHandle)member)),
                HandleKind.PropertyDefinition =>
                    ReadPropertyPointer(
                        _reader.GetPropertyDefinition(
                            (PropertyDefinitionHandle)member)),
                HandleKind.EventDefinition =>
                    ReadEventPointer(
                        _reader.GetEventDefinition(
                            (EventDefinitionHandle)member)),
                _ => new(
                    MemorySafetyPointerEvidence.Unavailable,
                    MemorySafetyFixedBufferEvidence.Unavailable),
            };
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

    PointerReadResult ReadMethodPointer(MethodDefinition method)
    {
        GuardedProviderDecode.DecodeResult<
            MethodSignature<PointerDetection>> decoded =
            GuardedProviderDecode.MethodResult(
                _reader,
                method,
                PointerDetector.Instance,
                (object?)null,
                PointerDetection.Degraded);
        PointerDetection detection = PointerDetection.Combine(
            decoded.Value.ReturnType,
            decoded.Value.ParameterTypes);
        return FromPointerDetection(
            detection,
            decoded.IsDegraded,
            MemorySafetyFixedBufferEvidence.NotExamined);
    }

    PointerReadResult ReadFieldPointer(FieldDefinition field)
    {
        GuardedProviderDecode.DecodeResult<PointerDetection> decoded =
            GuardedProviderDecode.FieldResult(
                _reader,
                field,
                PointerDetector.Instance,
                (object?)null,
                PointerDetection.Degraded);
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
        return FromPointerDetection(
            decoded.Value,
            decoded.IsDegraded,
            fixedBuffer.State switch
            {
                FixedBufferMetadataReadState.Present =>
                    MemorySafetyFixedBufferEvidence.Present,
                FixedBufferMetadataReadState.Absent =>
                    MemorySafetyFixedBufferEvidence.Absent,
                _ => MemorySafetyFixedBufferEvidence.Unavailable,
            });
    }

    PointerReadResult ReadPropertyPointer(PropertyDefinition property)
    {
        GuardedProviderDecode.DecodeResult<
            MethodSignature<PointerDetection>> decoded =
            GuardedProviderDecode.PropertyResult(
                _reader,
                property,
                PointerDetector.Instance,
                (object?)null,
                PointerDetection.Degraded);
        PointerDetection detection = PointerDetection.Combine(
            decoded.Value.ReturnType,
            decoded.Value.ParameterTypes);
        return FromPointerDetection(
            detection,
            decoded.IsDegraded,
            MemorySafetyFixedBufferEvidence.NotExamined);
    }

    PointerReadResult ReadEventPointer(EventDefinition @event)
    {
        PointerDetection detection = @event.Type.Kind switch
        {
            HandleKind.TypeDefinition
                or HandleKind.TypeReference => default,
            HandleKind.TypeSpecification =>
                GuardedProviderDecode.TypeSpec(
                    _reader,
                    (TypeSpecificationHandle)@event.Type,
                    PointerDetector.Instance,
                    (object?)null,
                    PointerDetection.Degraded),
            _ => PointerDetection.Degraded,
        };
        return FromPointerDetection(
            detection,
            degradedByGuard: false,
            MemorySafetyFixedBufferEvidence.NotExamined);
    }

    static PointerReadResult FromPointerDetection(
        PointerDetection detection,
        bool degradedByGuard,
        MemorySafetyFixedBufferEvidence fixedBuffer)
    {
        MemorySafetyPointerEvidence evidence =
            detection.HasPointer
                ? MemorySafetyPointerEvidence.Present
                : degradedByGuard || detection.IsDegraded
                    ? MemorySafetyPointerEvidence.Unavailable
                    : MemorySafetyPointerEvidence.Absent;
        return new(evidence, fixedBuffer);
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
            PropertyAccessors accessors =
                reader.GetPropertyDefinition(propertyHandle).GetAccessors();
            AddAssociation(
                accessors.Getter,
                propertyHandle,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            AddAssociation(
                accessors.Setter,
                propertyHandle,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            foreach (MethodDefinitionHandle other in accessors.Others)
            {
                AddAssociation(
                    other,
                    propertyHandle,
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
            EventAccessors accessors =
                reader.GetEventDefinition(eventHandle).GetAccessors();
            AddAssociation(
                accessors.Adder,
                eventHandle,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            AddAssociation(
                accessors.Remover,
                eventHandle,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            AddAssociation(
                accessors.Raiser,
                eventHandle,
                methodRowCount,
                associations,
                ambiguous,
                ref hasMalformedRows,
                ref projectedRows);
            foreach (MethodDefinitionHandle other in accessors.Others)
            {
                AddAssociation(
                    other,
                    eventHandle,
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

    static void AddAssociation(
        MethodDefinitionHandle method,
        EntityHandle associated,
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
