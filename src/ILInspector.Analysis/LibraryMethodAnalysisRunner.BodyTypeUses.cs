using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis;

internal sealed partial class LibraryMethodAnalysisRunner
{
    BodyUseOwnerAttribution? _bodyUseOwners;

    internal BodyTypeUseMethodFact AnalyzeBodyTypeUses(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodBodyBlock body,
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
            // The body's own signature is decoded as the full method identity
            // decodes it, so a malformed one still fails the body visibly.
            _infrastructure.MethodSignature(methodDefinition.Signature)
                .ThrowIfFailed();
            _bodyUseOwners ??= new(_infrastructure.Reader);
            BodyUseOwner owner = _bodyUseOwners.Attribute(methodHandle);
            TypeDefinitionHandle? source =
                owner.Status == BodyUseOwnerStatus.Logical
                    && AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                        _infrastructure.Reader,
                        _infrastructure.Reader.GetTypeDefinition(owner.Source))
                    ? owner.Source
                    : null;

            var rows = ImmutableArray.CreateBuilder<BodyTypeUseOccurrence>();
            var diagnostics =
                ImmutableArray.CreateBuilder<AnalysisLibraryBodyUseDiagnostic>();
            IMethodCallResolver resolver =
                _infrastructure.CreateCallResolver(
                    scope,
                    methodHandle);
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
                int committed = rows.Count;
                try
                {
                    if (!TryClassify(
                            instruction,
                            out AnalysisLibraryBodyUseOperandKind kind))
                    {
                        continue;
                    }

                    int token = checked((int)instruction.OperandValue);
                    BodyTypeUseOperandBinding binding =
                        BindOperand(
                            resolver,
                            scope,
                            OperandPathOf(instruction, kind),
                            token);
                    if (binding.Unavailable is { } unavailable)
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

                    // The operand's rows commit atomically: they are appended
                    // in place and truncated away on any rejection.
                    if (source is { } logicalSource)
                    {
                        foreach (BodyTypeUseOperandTarget target
                            in binding.Targets)
                        {
                            rows.Add(
                                new(
                                    logicalSource,
                                    target.Target,
                                    methodToken,
                                    kind,
                                    token,
                                    instruction.Offset,
                                    target.Ordinal));
                        }
                    }
                    long attempted = rows.Count;
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
                    operandsExamined++;
                }
                catch (Exception exception)
                    when (IsRecoverableMethodFailure(exception))
                {
                    rows.Count = committed;
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

            if (owner.Status != BodyUseOwnerStatus.Logical)
            {
                diagnostics.Add(
                    new(
                        AnalysisLibraryBodyUseDiagnosticKind
                            .UnavailableLogicalOwner,
                        methodToken,
                        null,
                        owner.Status == BodyUseOwnerStatus.Rejected
                            ? "Generated-body ownership evidence was rejected."
                            : "The physical body has no authenticated logical owner."));
            }

            TypeDefinitionHandle physicalType =
                methodDefinition.GetDeclaringType();
            return new(
                physicalType,
                methodToken,
                owner.Status == BodyUseOwnerStatus.Logical
                    ? AnalysisLibraryBodyUseFidelity.LogicalOwner
                    : AnalysisLibraryBodyUseFidelity.PhysicalOnly,
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

    void CollectLocalTargets(
        TypeRef type,
        ImmutableArray<BodyTypeUseOperandTarget>.Builder targets,
        ref int ordinal)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (_infrastructure.TryResolveLocalTypeDefinition(
                        type,
                        out TypeDefinitionHandle target)
                    && AnalysisLibraryBodyUseProducer.IsTypeInPopulation(
                        _infrastructure.Reader,
                        _infrastructure.Reader.GetTypeDefinition(target)))
                {
                    targets.Add(new(ordinal, target));
                }
                ordinal++;
                break;
            case TypeRefKind.GenericInstance:
                if (type.ElementType is { } definition)
                {
                    CollectLocalTargets(
                        definition,
                        targets,
                        ref ordinal);
                }
                foreach (TypeRef argument in type.TypeArguments)
                {
                    CollectLocalTargets(
                        argument,
                        targets,
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
                    CollectLocalTargets(
                        element,
                        targets,
                        ref ordinal);
                }
                break;
            case TypeRefKind.Unsupported
                when type.ModifierType is { } modifier
                    && type.UnmodifiedType is { } unmodified:
                if (type.IsRequiredModifier)
                {
                    CollectLocalTargets(
                        modifier,
                        targets,
                        ref ordinal);
                }
                CollectLocalTargets(
                    unmodified,
                    targets,
                    ref ordinal);
                break;
            case TypeRefKind.Unsupported
                when type.FunctionPointerSignature is { } signature:
                CollectLocalTargets(
                    signature.ReturnType,
                    targets,
                    ref ordinal);
                foreach (TypeRef parameter
                    in signature.ParameterTypes)
                {
                    CollectLocalTargets(
                        parameter,
                        targets,
                        ref ordinal);
                }
                break;
        }
    }

    string? FirstUnavailableTypeReason(ImmutableArray<TypeRef> roots)
    {
        foreach (TypeRef root in roots)
        {
            if (UnavailableTypeReason(root) is { } reason)
                return reason;
        }
        return null;
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

    // Operand resolution and same-image binding depend only on the token,
    // its resolution path, and, for a MethodSpec, the caller's generic
    // arities, which validate its instantiation. Otherwise the caller's
    // generic scope only names generic parameters, which never bind or make
    // an operand unavailable. Each distinct operand is therefore resolved and
    // bound once per execution.
    Dictionary<BodyTypeUseOperandKey, BodyTypeUseOperandBinding>?
        _bodyUseOperandBindings;

    BodyTypeUseOperandBinding BindOperand(
        IMethodCallResolver resolver,
        GenericScope scope,
        BodyTypeUseOperandPath path,
        int token)
    {
        _bodyUseOperandBindings ??= [];
        var key = (token & unchecked((int)0xFF000000)) == 0x2B000000
            ? new BodyTypeUseOperandKey(
                token,
                path,
                scope.TypeParameters.Length,
                scope.MethodParameters.Length)
            : new BodyTypeUseOperandKey(token, path, 0, 0);
        if (_bodyUseOperandBindings.TryGetValue(
                key,
                out BodyTypeUseOperandBinding? binding))
        {
            if (binding.Failure is { } failure)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Throw(failure);
            }
            return binding;
        }

        // A recoverable failure is retained too, so a malformed operand fails
        // every use without repeating its resolution.
        try
        {
            ImmutableArray<TypeRef> roots =
                ResolveOperandTypes(resolver, scope, path, token);
            string? unavailable = roots.IsDefaultOrEmpty
                ? "no typed root"
                : FirstUnavailableTypeReason(roots);
            if (unavailable is not null)
            {
                binding = new(unavailable, []);
            }
            else
            {
                var targets =
                    ImmutableArray.CreateBuilder<BodyTypeUseOperandTarget>();
                int ordinal = 0;
                foreach (TypeRef root in roots)
                    CollectLocalTargets(root, targets, ref ordinal);
                binding = new(null, targets.DrainToImmutable());
            }
        }
        catch (Exception exception)
            when (IsRecoverableMethodFailure(exception))
        {
            _bodyUseOperandBindings.Add(key, new(null, [], exception));
            throw;
        }
        _bodyUseOperandBindings.Add(key, binding);
        return binding;
    }

    static BodyTypeUseOperandPath OperandPathOf(
        DecodedInstruction instruction,
        AnalysisLibraryBodyUseOperandKind kind) =>
        instruction.Operand == OperandKind.InlineMethod
            || kind is AnalysisLibraryBodyUseOperandKind.MethodToken
                or AnalysisLibraryBodyUseOperandKind.GenericMethodInstantiation
            ? BodyTypeUseOperandPath.Member
            : instruction.Operand == OperandKind.InlineField
                || kind == AnalysisLibraryBodyUseOperandKind.FieldToken
                ? BodyTypeUseOperandPath.Field
                : BodyTypeUseOperandPath.Type;

    // A method operand contributes its declaring type and instantiation
    // only, so its parameter and return types are never decoded.
    ImmutableArray<TypeRef> ResolveOperandTypes(
        IMethodCallResolver resolver,
        GenericScope scope,
        BodyTypeUseOperandPath path,
        int token)
    {
        switch (path)
        {
            case BodyTypeUseOperandPath.Member:
                (TypeRef declaringType, ImmutableArray<TypeRef> arguments) =
                    _infrastructure.ResolveMethodOwner(token, scope);
                return [declaringType, .. arguments];
            case BodyTypeUseOperandPath.Field:
                (TypeRef? fieldOwner, _) =
                    resolver.ResolveFieldOwner(token);
                return fieldOwner is null ? [] : [fieldOwner];
            default:
                return [resolver.ResolveType(token)];
        }
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

internal enum BodyTypeUseOperandPath : byte
{
    Member,
    Field,
    Type,
}

internal readonly record struct BodyTypeUseOperandKey(
    int Token,
    BodyTypeUseOperandPath Path,
    int TypeArity,
    int MethodArity);

internal readonly record struct BodyTypeUseOperandTarget(
    int Ordinal,
    TypeDefinitionHandle Target);

internal sealed record BodyTypeUseOperandBinding(
    string? Unavailable,
    ImmutableArray<BodyTypeUseOperandTarget> Targets,
    Exception? Failure = null);

internal readonly record struct BodyTypeUseOccurrence(
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
