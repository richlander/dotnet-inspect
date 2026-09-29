using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis;

internal sealed partial class LibraryMethodAnalysisRunner
{
    internal BodyTypeUseMethodFact AnalyzeBodyTypeUses(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodBodyBlock body,
        StateMachineRelationshipResult stateMachineByImplementation,
        int maximumInstructions,
        int maximumOccurrences,
        CancellationToken cancellationToken)
    {
        int methodToken = MetadataTokens.GetToken(methodHandle);
        try
        {
            byte[] il = body.GetILBytes() ?? [];
            if (!InstructionDecoder.TryDecodeBounded(
                    il,
                    maximumInstructions,
                    cancellationToken,
                    out ImmutableArray<DecodedInstruction> instructions,
                    out int decodedInstructionCount))
            {
                return BodyTypeUseMethodFact.CreateLimited(
                    typeHandle,
                    methodToken,
                    checked((long)decodedInstructionCount + 1),
                    maximumInstructions);
            }

            GenericScope scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            MethodIdentity physicalMethod =
                _infrastructure.CreateMethodIdentity(
                    typeHandle,
                    methodHandle,
                    methodDefinition,
                    scope);
            DeclaredOwnerResolution resolution;
            MethodIdentity? logicalMethod;
            if (stateMachineByImplementation
                is StateMachineRelationshipResult.Rejected)
            {
                logicalMethod = null;
                resolution = DeclaredOwnerResolution.Rejected;
            }
            else
            {
                MethodDefinitionHandle ownerHandle = methodHandle;
                MethodDefinition ownerDefinition = methodDefinition;
                TypeDefinitionHandle ownerTypeHandle = typeHandle;
                TypeDefinition ownerType = typeDefinition;
                MethodIdentity ownerMethod = physicalMethod;
                if (stateMachineByImplementation
                    is StateMachineRelationshipResult.Resolved stateMachine)
                {
                    ownerHandle =
                        stateMachine.Relationship.Kickoff.Handle;
                    ownerDefinition =
                        _infrastructure.Reader.GetMethodDefinition(
                            ownerHandle);
                    ownerTypeHandle =
                        ownerDefinition.GetDeclaringType();
                    ownerType =
                        _infrastructure.Reader.GetTypeDefinition(
                            ownerTypeHandle);
                    GenericScope ownerScope =
                        _infrastructure.CreateScope(
                            ownerType,
                            ownerDefinition);
                    ownerMethod =
                        _infrastructure.CreateMethodIdentity(
                            ownerTypeHandle,
                            ownerHandle,
                            ownerDefinition,
                            ownerScope);
                }

                bool ownerSourceGenerated =
                    _infrastructure.IsSourceGeneratedTypeOrEnclosing(
                        ownerTypeHandle);
                bool ownerCompilerGenerated =
                    _infrastructure.HasCompilerGeneratedAttribute(
                        ownerDefinition.GetCustomAttributes())
                    || _infrastructure.HasCompilerGeneratedAttribute(
                        ownerType.GetCustomAttributes());
                try
                {
                    resolution =
                        _infrastructure.ResolveUltimateDeclaredMethod(
                            ownerHandle,
                            ownerDefinition,
                            ownerMethod,
                            ownerSourceGenerated,
                            maximumInstructions,
                            cancellationToken,
                            out _,
                            out AuthenticatedSourceOwner? ultimateOwner);
                    logicalMethod = resolution switch
                    {
                        DeclaredOwnerResolution.None
                            when !ownerCompilerGenerated => ownerMethod,
                        DeclaredOwnerResolution.Resolved =>
                            ultimateOwner!.Value.Method,
                        _ => null,
                    };
                }
                catch (
                    AttributionBodyInstructionLimitExceededException exception)
                {
                    return BodyTypeUseMethodFact.CreateLimited(
                        typeHandle,
                        methodToken,
                        exception.AttemptedCharge,
                        exception.Limit,
                        detail:
                            "Logical-owner attribution probe "
                            + $"0x{exception.MethodToken:X8} exceeded its "
                            + "Analysis body-use instruction limit.");
                }
            }

            var rows = ImmutableArray.CreateBuilder<BodyTypeUseOccurrence>();
            var diagnostics =
                ImmutableArray.CreateBuilder<AnalysisLibraryBodyUseDiagnostic>();
            IMethodCallResolver resolver =
                _infrastructure.CreateCallResolver(
                    scope,
                    physicalMethod);
            int operandsConsidered = 0;
            int operandsExamined = 0;
            int operandsUnavailable = 0;
            foreach (DecodedInstruction instruction
                in instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsTypedOperand(instruction))
                    continue;

                operandsConsidered++;
                try
                {
                    if (!TryClassify(
                            instruction,
                            out AnalysisLibraryBodyUseOperandKind kind))
                    {
                        continue;
                    }

                    int token = checked((int)instruction.OperandValue);
                    ImmutableArray<TypeRef> roots =
                        ResolveOperandTypes(
                            resolver,
                            instruction,
                            kind,
                            token);
                    string? unavailable = roots.IsDefaultOrEmpty
                        ? "no typed root"
                        : roots
                            .Select(UnavailableTypeReason)
                            .FirstOrDefault(
                                static reason => reason is not null);
                    if (unavailable is not null)
                    {
                        operandsUnavailable++;
                        diagnostics.Add(
                            new(
                                AnalysisLibraryBodyUseDiagnosticKind
                                    .UnresolvedOperand,
                                methodToken,
                                instruction.Offset,
                                $"The {kind} operand 0x{token:X8} could not "
                                    + $"be resolved ({unavailable})."));
                        continue;
                    }

                    var operandRows =
                        ImmutableArray.CreateBuilder<
                            BodyTypeUseOccurrence>();
                    int ordinal = 0;
                    foreach (TypeRef root in roots)
                    {
                        CollectLocalDefinitions(
                            root,
                            kind,
                            token,
                            instruction.Offset,
                            methodToken,
                            logicalMethod,
                            operandRows,
                            ref ordinal);
                    }
                    long attempted = checked(
                        (long)rows.Count + operandRows.Count);
                    if (attempted > maximumOccurrences)
                    {
                        return BodyTypeUseMethodFact.CreateLimited(
                            typeHandle,
                            methodToken,
                            attempted,
                            maximumOccurrences,
                            operandsConsidered,
                            operandsExamined,
                            operandsUnavailable);
                    }
                    rows.AddRange(operandRows);
                    operandsExamined++;
                }
                catch (Exception exception)
                    when (IsRecoverableMethodFailure(exception))
                {
                    operandsUnavailable++;
                    diagnostics.Add(
                        new(
                            AnalysisLibraryBodyUseDiagnosticKind
                                .UnresolvedOperand,
                            methodToken,
                            instruction.Offset,
                            ProducerFailure.Describe(exception)));
                }
            }

            if (logicalMethod is null)
            {
                diagnostics.Add(
                    new(
                        AnalysisLibraryBodyUseDiagnosticKind
                            .UnavailableLogicalOwner,
                        methodToken,
                        null,
                        resolution == DeclaredOwnerResolution.Rejected
                            ? "Generated-body ownership evidence was rejected."
                            : "The physical body has no authenticated logical owner."));
            }

            TypeDefinitionHandle physicalType =
                methodDefinition.GetDeclaringType();
            return new(
                physicalType,
                methodToken,
                logicalMethod is null
                    ? AnalysisLibraryBodyUseFidelity.PhysicalOnly
                    : AnalysisLibraryBodyUseFidelity.LogicalOwner,
                rows.ToImmutable(),
                diagnostics.ToImmutable(),
                operandsConsidered,
                operandsExamined,
                operandsUnavailable,
                Limited: false,
                AttemptedCharge: null,
                Limit: null);
        }
        catch (Exception exception)
            when (IsRecoverableMethodFailure(exception))
        {
            return BodyTypeUseMethodFact.Unavailable(
                typeHandle,
                methodToken,
                ProducerFailure.Describe(exception));
        }
    }

    static bool IsTypedOperand(DecodedInstruction instruction) =>
        instruction.Operand is OperandKind.InlineMethod
            or OperandKind.InlineField
            or OperandKind.InlineType
            or OperandKind.InlineTok;

    void CollectLocalDefinitions(
        TypeRef type,
        AnalysisLibraryBodyUseOperandKind kind,
        int operandToken,
        int ilOffset,
        int methodToken,
        MethodIdentity? logicalMethod,
        ImmutableArray<BodyTypeUseOccurrence>.Builder rows,
        ref int ordinal)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (logicalMethod is not null
                    && _infrastructure.TryResolveLocalTypeDefinition(
                        logicalMethod.DeclaringType,
                        out TypeDefinitionHandle source)
                    && AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                        _infrastructure.Reader,
                        _infrastructure.Reader.GetTypeDefinition(source))
                    && _infrastructure.TryResolveLocalTypeDefinition(
                        type,
                        out TypeDefinitionHandle target)
                    && AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                        _infrastructure.Reader,
                        _infrastructure.Reader.GetTypeDefinition(target)))
                {
                    rows.Add(
                        new(
                            source,
                            target,
                            methodToken,
                            kind,
                            operandToken,
                            ilOffset,
                            ordinal));
                }
                ordinal++;
                break;
            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition)
                {
                    CollectLocalDefinitions(
                        definition,
                        kind,
                        operandToken,
                        ilOffset,
                        methodToken,
                        logicalMethod,
                        rows,
                        ref ordinal);
                }
                foreach (TypeRef argument in type.TypeArguments)
                {
                    CollectLocalDefinitions(
                        argument,
                        kind,
                        operandToken,
                        ilOffset,
                        methodToken,
                        logicalMethod,
                        rows,
                        ref ordinal);
                }
                break;
            case TypeRefKind.SzArray
                or TypeRefKind.Array
                or TypeRefKind.ByRef
                or TypeRefKind.Pointer
                or TypeRefKind.Pinned:
                if (type.ElementType is { } element)
                {
                    CollectLocalDefinitions(
                        element,
                        kind,
                        operandToken,
                        ilOffset,
                        methodToken,
                        logicalMethod,
                        rows,
                        ref ordinal);
                }
                break;
            case TypeRefKind.Unsupported
                when type.ModifierType is { } modifier
                    && type.UnmodifiedType is { } unmodified:
                if (type.IsRequiredModifier)
                {
                    CollectLocalDefinitions(
                        modifier,
                        kind,
                        operandToken,
                        ilOffset,
                        methodToken,
                        logicalMethod,
                        rows,
                        ref ordinal);
                }
                CollectLocalDefinitions(
                    unmodified,
                    kind,
                    operandToken,
                    ilOffset,
                    methodToken,
                    logicalMethod,
                    rows,
                    ref ordinal);
                break;
            case TypeRefKind.Unsupported
                when type.FunctionPointerSignature is { } signature:
                CollectLocalDefinitions(
                    signature.ReturnType,
                    kind,
                    operandToken,
                    ilOffset,
                    methodToken,
                    logicalMethod,
                    rows,
                    ref ordinal);
                foreach (TypeRef parameter
                    in signature.ParameterTypes)
                {
                    CollectLocalDefinitions(
                        parameter,
                        kind,
                        operandToken,
                        ilOffset,
                        methodToken,
                        logicalMethod,
                        rows,
                        ref ordinal);
                }
                break;
        }
    }

    string? UnavailableTypeReason(TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                return _infrastructure
                        .CanCanonicalizeCurrentModuleReference(type)
                    && !_infrastructure.TryResolveLocalTypeDefinition(
                        type,
                        out _)
                        ? "current-image Type definition is unavailable"
                        : null;
            case TypeRefKind.GenericInstance:
                if (type.ElementType is null)
                    return "generic instance definition is unavailable";
                string? unavailable =
                    UnavailableTypeReason(type.ElementType);
                if (unavailable is not null)
                    return unavailable;
                foreach (TypeRef argument in type.TypeArguments)
                {
                    unavailable = UnavailableTypeReason(argument);
                    if (unavailable is not null)
                        return unavailable;
                }
                return null;
            case TypeRefKind.SzArray
                or TypeRefKind.Array
                or TypeRefKind.ByRef
                or TypeRefKind.Pointer
                or TypeRefKind.Pinned:
                return type.ElementType is { } element
                    ? UnavailableTypeReason(element)
                    : "element type is unavailable";
            case TypeRefKind.Unsupported
                when type.ModifierType is { } modifier
                    && type.UnmodifiedType is { } unmodified:
                if (type.IsRequiredModifier
                    && UnavailableTypeReason(modifier)
                        is { } modifierUnavailable)
                {
                    return modifierUnavailable;
                }
                return UnavailableTypeReason(unmodified);
            case TypeRefKind.Unsupported
                when type.FunctionPointerSignature is { } signature:
                string? signatureUnavailable =
                    UnavailableTypeReason(signature.ReturnType);
                if (signatureUnavailable is not null)
                    return signatureUnavailable;
                foreach (TypeRef parameter in signature.ParameterTypes)
                {
                    signatureUnavailable =
                        UnavailableTypeReason(parameter);
                    if (signatureUnavailable is not null)
                        return signatureUnavailable;
                }
                return null;
            case TypeRefKind.Unsupported:
                return string.IsNullOrWhiteSpace(type.UnsupportedReason)
                    ? "unsupported Type shape"
                    : type.UnsupportedReason;
            default:
                return null;
        }
    }

    static ImmutableArray<TypeRef> ResolveOperandTypes(
        IMethodCallResolver resolver,
        DecodedInstruction instruction,
        AnalysisLibraryBodyUseOperandKind kind,
        int token)
    {
        if (instruction.Operand == OperandKind.InlineMethod
            || kind is AnalysisLibraryBodyUseOperandKind.MethodToken
                or AnalysisLibraryBodyUseOperandKind.GenericMethodInstantiation)
        {
            MemberRef member = resolver.ResolveMember(token);
            var types = ImmutableArray.CreateBuilder<TypeRef>(
                1 + member.TypeArguments.Length);
            types.Add(member.DeclaringType);
            types.AddRange(member.TypeArguments);
            return types.MoveToImmutable();
        }
        if (instruction.Operand == OperandKind.InlineField
            || kind == AnalysisLibraryBodyUseOperandKind.FieldToken)
        {
            (TypeRef? declaringType, _) =
                resolver.ResolveFieldOwner(token);
            return declaringType is null ? [] : [declaringType];
        }
        return [resolver.ResolveType(token)];
    }

    bool TryClassify(
        DecodedInstruction instruction,
        out AnalysisLibraryBodyUseOperandKind kind)
    {
        kind = instruction.Operand == OperandKind.InlineMethod
                && IsMethodSpecification(instruction.OperandValue)
            ? AnalysisLibraryBodyUseOperandKind.GenericMethodInstantiation
            : instruction.OpCode switch
        {
            ILOpCode.Newobj =>
                AnalysisLibraryBodyUseOperandKind.Constructor,
            ILOpCode.Call or ILOpCode.Callvirt =>
                AnalysisLibraryBodyUseOperandKind.Call,
            ILOpCode.Ldftn or ILOpCode.Ldvirtftn =>
                AnalysisLibraryBodyUseOperandKind.MethodReference,
            ILOpCode.Newarr =>
                AnalysisLibraryBodyUseOperandKind.Array,
            ILOpCode.Castclass =>
                AnalysisLibraryBodyUseOperandKind.Cast,
            ILOpCode.Isinst =>
                AnalysisLibraryBodyUseOperandKind.TypeTest,
            ILOpCode.Box =>
                AnalysisLibraryBodyUseOperandKind.Box,
            ILOpCode.Unbox or ILOpCode.Unbox_any =>
                AnalysisLibraryBodyUseOperandKind.Unbox,
            ILOpCode.Constrained =>
                AnalysisLibraryBodyUseOperandKind.Constrained,
            ILOpCode.Ldtoken =>
                TokenKind(instruction.OperandValue),
            _ when instruction.Operand == OperandKind.InlineMethod =>
                AnalysisLibraryBodyUseOperandKind.MethodReference,
            _ when instruction.Operand == OperandKind.InlineField =>
                AnalysisLibraryBodyUseOperandKind.Field,
            _ when instruction.Operand == OperandKind.InlineType =>
                AnalysisLibraryBodyUseOperandKind.Type,
            _ => default,
        };
        return instruction.Operand
            is OperandKind.InlineMethod
                or OperandKind.InlineField
                or OperandKind.InlineType
                or OperandKind.InlineTok;
    }

    AnalysisLibraryBodyUseOperandKind TokenKind(long value)
    {
        EntityHandle handle =
            MetadataTokens.EntityHandle(checked((int)value));
        return handle.Kind switch
        {
            HandleKind.MethodDefinition
                or HandleKind.MethodSpecification =>
                    AnalysisLibraryBodyUseOperandKind.MethodToken,
            HandleKind.FieldDefinition =>
                AnalysisLibraryBodyUseOperandKind.FieldToken,
            HandleKind.MemberReference =>
                _infrastructure.Reader
                    .GetMemberReference((MemberReferenceHandle)handle)
                    .GetKind()
                    == MemberReferenceKind.Field
                        ? AnalysisLibraryBodyUseOperandKind.FieldToken
                        : AnalysisLibraryBodyUseOperandKind.MethodToken,
            _ => AnalysisLibraryBodyUseOperandKind.TypeToken,
        };
    }

    static bool IsMethodSpecification(long value) =>
        MetadataTokens.EntityHandle(checked((int)value)).Kind
            == HandleKind.MethodSpecification;
}

internal sealed record BodyTypeUseOccurrence(
    TypeDefinitionHandle Source,
    TypeDefinitionHandle Target,
    int PhysicalMethodToken,
    AnalysisLibraryBodyUseOperandKind OperandKind,
    int OperandToken,
    int IlOffset,
    int OccurrenceOrdinal);

internal sealed record BodyTypeUseMethodFact(
    TypeDefinitionHandle PhysicalType,
    int PhysicalMethodToken,
    AnalysisLibraryBodyUseFidelity Fidelity,
    ImmutableArray<BodyTypeUseOccurrence> Occurrences,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics,
    int OperandsConsidered,
    int OperandsExamined,
    int OperandsUnavailable,
    bool Limited,
    long? AttemptedCharge,
    long? Limit)
{
    internal static BodyTypeUseMethodFact Unavailable(
        TypeDefinitionHandle type,
        int methodToken,
        string detail) =>
        new(
            type,
            methodToken,
            AnalysisLibraryBodyUseFidelity.PhysicalOnly,
            [],
            [new(
                AnalysisLibraryBodyUseDiagnosticKind.MalformedBody,
                methodToken,
                null,
                detail)],
            0,
            0,
            0,
            Limited: false,
            AttemptedCharge: null,
            Limit: null);

    internal static BodyTypeUseMethodFact CreateLimited(
        TypeDefinitionHandle type,
        int methodToken,
        long attempted,
        long limit,
        int operandsConsidered = 0,
        int operandsExamined = 0,
        int operandsUnavailable = 0,
        string? detail = null) =>
        new(
            type,
            methodToken,
            AnalysisLibraryBodyUseFidelity.PhysicalOnly,
            [],
            [new(
                AnalysisLibraryBodyUseDiagnosticKind.Limit,
                methodToken,
                null,
                detail
                    ?? "The method body exceeded its Analysis body-use limit.",
                limit,
                attempted)],
            operandsConsidered,
            operandsExamined,
            operandsUnavailable,
            Limited: true,
            AttemptedCharge: attempted,
            Limit: limit);
}
