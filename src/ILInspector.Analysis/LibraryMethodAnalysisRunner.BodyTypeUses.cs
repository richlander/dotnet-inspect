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
        int maximumInstructions,
        int maximumOccurrences)
    {
        int methodToken = MetadataTokens.GetToken(methodHandle);
        try
        {
            GenericScope scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            MethodIdentity physicalMethod =
                _infrastructure.CreateMethodIdentity(
                    typeHandle,
                    methodHandle,
                    methodDefinition,
                    scope);
            bool sourceGenerated =
                _infrastructure.IsSourceGeneratedTypeOrEnclosing(
                    typeHandle);
            DeclaredOwnerResolution resolution =
                _infrastructure.ResolveUltimateDeclaredMethod(
                    methodHandle,
                    methodDefinition,
                    physicalMethod,
                    sourceGenerated,
                    out _,
                    out AuthenticatedSourceOwner? ultimateOwner);
            bool compilerGenerated =
                _infrastructure.HasCompilerGeneratedAttribute(
                    methodDefinition.GetCustomAttributes())
                || _infrastructure.HasCompilerGeneratedAttribute(
                    typeDefinition.GetCustomAttributes());
            MethodIdentity? logicalMethod = resolution switch
            {
                DeclaredOwnerResolution.None when !compilerGenerated =>
                    physicalMethod,
                DeclaredOwnerResolution.Resolved => ultimateOwner!.Value.Method,
                _ => null,
            };

            byte[] il = body.GetILBytes() ?? [];
            MethodInstructions decoded = DecodeBody(
                il,
                body.ExceptionRegions);
            if (decoded.Instructions.Length > maximumInstructions)
            {
                return BodyTypeUseMethodFact.CreateLimited(
                    typeHandle,
                    methodToken,
                    decoded.Instructions.Length,
                    maximumInstructions);
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
                in decoded.Instructions)
            {
                if (!TryClassify(
                        instruction,
                        out AnalysisLibraryBodyUseOperandKind kind))
                {
                    continue;
                }

                operandsConsidered++;
                int token = checked((int)instruction.OperandValue);
                ImmutableArray<TypeRef> roots;
                try
                {
                    roots = ResolveOperandTypes(
                        resolver,
                        instruction,
                        kind,
                        token);
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
                    continue;
                }

                if (roots.IsDefaultOrEmpty
                    || roots.Any(IsUnavailableType))
                {
                    operandsUnavailable++;
                    string unsupported =
                        roots.FirstOrDefault(
                            static type =>
                                type.Kind
                                    == TypeRefKind.Unsupported)
                            ?.UnsupportedReason
                        ?? "no typed root";
                    diagnostics.Add(
                        new(
                            AnalysisLibraryBodyUseDiagnosticKind
                                .UnresolvedOperand,
                            methodToken,
                            instruction.Offset,
                            $"The {kind} operand 0x{token:X8} could not "
                                + $"be resolved ({unsupported})."));
                    continue;
                }

                operandsExamined++;
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
                        rows,
                        ref ordinal);
                    if (rows.Count > maximumOccurrences)
                    {
                        return BodyTypeUseMethodFact.CreateLimited(
                            typeHandle,
                            methodToken,
                            rows.Count,
                            maximumOccurrences,
                            operandsConsidered,
                            operandsExamined,
                            operandsUnavailable);
                    }
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
                    && _infrastructure.TryResolveLocalTypeDefinition(
                        type,
                        out TypeDefinitionHandle target))
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

    static bool IsUnavailableType(TypeRef type) =>
        type.Kind == TypeRefKind.Unsupported
        && type.FunctionPointerSignature is null
        && (type.ModifierType is null
            || type.UnmodifiedType is null);

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
        int operandsUnavailable = 0) =>
        new(
            type,
            methodToken,
            AnalysisLibraryBodyUseFidelity.PhysicalOnly,
            [],
            [new(
                AnalysisLibraryBodyUseDiagnosticKind.Limit,
                methodToken,
                null,
                "The method body exceeded its Analysis body-use limit.",
                limit,
                attempted)],
            operandsConsidered,
            operandsExamined,
            operandsUnavailable,
            Limited: true,
            AttemptedCharge: attempted,
            Limit: limit);
}
