using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>One resolved call site in a physical MethodDef body.</summary>
public sealed record MethodCallSite(
    int ILOffset,
    int ReturnAddress,
    int OperandToken,
    CallKind Kind,
    MemberRef Callee,
    MethodCallTarget Target);

/// <summary>IL-ordered resolved calls from one physical MethodDef body.</summary>
public sealed record MethodCallSites(
    ImmutableArray<MethodCallSite> Sites);

/// <summary>One method body's decoded local-signature facts.</summary>
public sealed record MethodLocalSignature(
    ImmutableArray<TypeRef> Types,
    int DeclaredCount,
    string? IncompleteReason);

/// <summary>The resolved identity and current-module status of a field operand.</summary>
public abstract record MethodFieldOperand
{
    private protected MethodFieldOperand()
    {
    }

    public sealed record Resolved(
        TypeRef DeclaringType,
        string Name,
        TypeRef FieldType,
        int? CurrentModuleDefinitionToken,
        bool HasRva)
        : MethodFieldOperand;

    public sealed record Unresolved(string Reason) : MethodFieldOperand;
}

/// <summary>Metadata decode work performed by Method call support.</summary>
public sealed record MethodCallMetadataWork(
    int CallSites,
    int MethodOperands,
    int FieldOperands,
    int LocalSignatures,
    int GenericParameterRows,
    long SignatureBytes)
{
    public static MethodCallMetadataWork Empty { get; } =
        new(0, 0, 0, 0, 0, 0);

    public MethodCallMetadataWork Since(MethodCallMetadataWork before) =>
        new(
            CallSites - before.CallSites,
            MethodOperands - before.MethodOperands,
            FieldOperands - before.FieldOperands,
            LocalSignatures - before.LocalSignatures,
            GenericParameterRows - before.GenericParameterRows,
            SignatureBytes - before.SignatureBytes);

    public MethodCallMetadataWork Plus(MethodCallMetadataWork other) =>
        new(
            checked(CallSites + other.CallSites),
            checked(MethodOperands + other.MethodOperands),
            checked(FieldOperands + other.FieldOperands),
            checked(LocalSignatures + other.LocalSignatures),
            checked(GenericParameterRows + other.GenericParameterRows),
            checked(SignatureBytes + other.SignatureBytes));
}

/// <summary>Exact Method call-support work in one source receipt.</summary>
public sealed record MethodCallMetadataCoverage(
    bool Constructed,
    MethodCallMetadataWork Work,
    int CallReads,
    int LocalSignatureReads,
    int FieldOperandReads,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static MethodCallMetadataCoverage Empty { get; } =
        new(
            Constructed: false,
            MethodCallMetadataWork.Empty,
            CallReads: 0,
            LocalSignatureReads: 0,
            FieldOperandReads: 0,
            MethodDefinitionHandleCoverage.Empty);
}

internal readonly record struct MethodCallOperandSite(
    int ILOffset,
    int ReturnAddress,
    int OperandToken,
    CallKind Kind,
    MemberRef Callee);

/// <summary>
/// Execution-scoped operand and signature resolution for Method call
/// producers. The lookup owns metadata decoding; callers own interpretation.
/// </summary>
internal sealed class MethodCallMetadataLookup(MetadataReader reader)
{
    int _callSites;
    int _methodOperands;
    int _fieldOperands;
    int _localSignatures;
    int _genericParameterRows;
    long _signatureBytes;

    public MethodCallMetadataWork RecordedWork =>
        new(
            _callSites,
            _methodOperands,
            _fieldOperands,
            _localSignatures,
            _genericParameterRows,
            _signatureBytes);

    public ImmutableArray<MethodCallOperandSite> ReadCalls(
        TypeDefinition type,
        MethodDefinition method,
        MethodInstructions instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        if (!instructions.IsComplete)
        {
            throw new BadImageFormatException(
                instructions.Blocks.IncompleteReason
                    ?? "Method instruction decoding was incomplete.");
        }

        GenericScope scope = Scope(type, method);
        var sites = ImmutableArray.CreateBuilder<MethodCallOperandSite>();
        foreach (DecodedInstruction instruction
            in instructions.Instructions)
        {
            if (CallKindOf(instruction.OpCode) is not { } kind)
                continue;

            int operand = MethodInstructionFacts.OperandInt32(instruction);
            EntityHandle handle = MetadataTokens.EntityHandle(operand);
            MemberRef callee;
            if (kind == CallKind.CallIndirect)
            {
                if (handle.Kind != HandleKind.StandaloneSignature)
                {
                    throw new BadImageFormatException(
                        $"A calli operand is a {handle.Kind}.");
                }
                callee = ResolveIndirectCall(
                    (StandaloneSignatureHandle)handle,
                    scope);
            }
            else
            {
                if (handle.Kind is not (
                        HandleKind.MethodDefinition
                        or HandleKind.MemberReference
                        or HandleKind.MethodSpecification))
                {
                    throw new BadImageFormatException(
                        $"A call operand is a {handle.Kind}.");
                }
                RecordMethodOperand(handle);
                ValidateMethodOperand(handle);
                callee = MemberResolver.ResolveMethod(
                    reader,
                    handle,
                    scope);
            }

            _callSites = checked(_callSites + 1);
            sites.Add(
                new(
                    instruction.Offset,
                    instruction.NextOffset,
                    operand,
                    kind,
                    callee));
        }

        return sites.ToImmutable();
    }

    public MethodLocalSignature ReadLocalSignature(
        TypeDefinition type,
        MethodDefinition method,
        MethodBodyBlock body)
    {
        GenericScope scope = Scope(type, method);
        _localSignatures = checked(_localSignatures + 1);
        return MethodCallMetadataReader.DecodeLocalSignature(
            reader,
            body,
            scope,
            RecordSignature);
    }

    public MethodFieldOperand ResolveField(
        TypeDefinition type,
        MethodDefinition method,
        int operandToken)
    {
        GenericScope scope = Scope(type, method);
        EntityHandle handle = MetadataTokens.EntityHandle(operandToken);
        _fieldOperands = checked(_fieldOperands + 1);
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
            {
                FieldDefinition field =
                    reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                RecordSignature(field.Signature);
                TypeRef declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    field.GetDeclaringType(),
                    0);
                TypeRef fieldType = SignatureBlobGuard.IsSafeToDecode(
                        reader,
                        field.Signature,
                        SignatureBlobGuard.Kind.Field)
                    ? field.DecodeSignature(TypeRefDecoder.Instance, scope)
                    : TypeRef.Unsupported(
                        "field signature nesting depth exceeded");
                return new MethodFieldOperand.Resolved(
                    declaring,
                    reader.GetString(field.Name),
                    fieldType,
                    operandToken,
                    (field.Attributes & FieldAttributes.HasFieldRVA) != 0);
            }
            case HandleKind.MemberReference:
            {
                MemberReference field =
                    reader.GetMemberReference((MemberReferenceHandle)handle);
                if (field.GetKind() != MemberReferenceKind.Field)
                {
                    return new MethodFieldOperand.Unresolved(
                        "The member reference is not a field.");
                }
                RecordSignature(field.Signature);
                TypeRef declaring = MemberResolver.ResolveParentType(
                    reader,
                    field.Parent,
                    scope);
                TypeRef fieldType = SignatureBlobGuard.IsSafeToDecode(
                        reader,
                        field.Signature,
                        SignatureBlobGuard.Kind.Field)
                    ? field.DecodeFieldSignature(
                        TypeRefDecoder.Instance,
                        GenericScope.Empty)
                        .Instantiate(
                            declaring.Kind
                                == TypeRefKind.GenericInstance
                                    ? declaring.TypeArguments
                                    : [],
                            [])
                    : TypeRef.Unsupported(
                        "field-reference signature nesting depth exceeded");
                return new MethodFieldOperand.Resolved(
                    declaring,
                    reader.GetString(field.Name),
                    fieldType,
                    CurrentModuleDefinitionToken: null,
                    HasRva: false);
            }
            default:
                return new MethodFieldOperand.Unresolved(
                    $"The field operand is a {handle.Kind}.");
        }
    }

    GenericScope Scope(
        TypeDefinition type,
        MethodDefinition method)
    {
        _genericParameterRows = checked(
            _genericParameterRows
                + type.GetGenericParameters().Count
                + method.GetGenericParameters().Count);
        return MethodSemanticIdentityReader.CreateScope(
            reader,
            type,
            method);
    }

    MemberRef ResolveIndirectCall(
        StandaloneSignatureHandle handle,
        GenericScope scope)
    {
        StandaloneSignature standalone =
            reader.GetStandaloneSignature(handle);
        RecordSignature(standalone.Signature);
        switch (SignatureBlobGuard.CheckShape(
            reader,
            standalone.Signature,
            SignatureBlobGuard.Kind.StandaloneMethod))
        {
            case SignatureBlobGuard.ShapeCheck.DepthExceeded:
            case SignatureBlobGuard.ShapeCheck.NodesExceeded:
                return MemberRef.Unsupported(
                    "calli signature decode bounds exceeded");
            case SignatureBlobGuard.ShapeCheck.Malformed:
                throw new BadImageFormatException(
                    "A calli signature is malformed.");
            case SignatureBlobGuard.ShapeCheck.Safe:
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown calli signature validation result.");
        }
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                standalone.Signature,
                SignatureBlobGuard.Kind.StandaloneMethod))
        {
            throw new BadImageFormatException(
                "A calli signature is malformed.");
        }

        MethodSignature<TypeRef> signature =
            standalone.DecodeMethodSignature(
                TypeRefDecoder.Instance,
                scope);
        return new MemberRef(
            TypeRef.Unsupported("function pointer"),
            "calli",
            signature.ParameterTypes,
            signature.ReturnType,
            MemberKind.FunctionPointer)
        {
            HasThis = signature.Header.IsInstance,
            SignatureHeader = signature.Header.RawValue,
            RequiredParameterCount = signature.RequiredParameterCount,
            GenericArity = signature.GenericParameterCount,
            OpenParameterTypes = signature.ParameterTypes,
            OpenReturnType = signature.ReturnType,
        };
    }

    void ValidateMethodOperand(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                ValidateMethodSignature(
                    reader.GetMethodDefinition(
                            (MethodDefinitionHandle)handle)
                        .Signature,
                    "method-definition");
                break;
            case HandleKind.MemberReference:
                ValidateMethodSignature(
                    reader.GetMemberReference(
                            (MemberReferenceHandle)handle)
                        .Signature,
                    "member-reference");
                break;
            case HandleKind.MethodSpecification:
            {
                MethodSpecification specification =
                    reader.GetMethodSpecification(
                        (MethodSpecificationHandle)handle);
                ValidateSignature(
                    specification.Signature,
                    SignatureBlobGuard.Kind.MethodSpecification,
                    "method-specification");
                ValidateMethodOperand(specification.Method);
                break;
            }
        }
    }

    void ValidateMethodSignature(
        BlobHandle signature,
        string operandKind) =>
        ValidateSignature(
            signature,
            SignatureBlobGuard.Kind.Method,
            operandKind);

    void ValidateSignature(
        BlobHandle signature,
        SignatureBlobGuard.Kind kind,
        string operandKind)
    {
        switch (SignatureBlobGuard.CheckShape(
            reader,
            signature,
            kind))
        {
            case SignatureBlobGuard.ShapeCheck.DepthExceeded:
            case SignatureBlobGuard.ShapeCheck.NodesExceeded:
                return;
            case SignatureBlobGuard.ShapeCheck.Malformed:
                throw new BadImageFormatException(
                    $"A call operand {operandKind} signature is malformed.");
            case SignatureBlobGuard.ShapeCheck.Safe:
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown call operand signature validation result.");
        }
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                signature,
                kind))
        {
            throw new BadImageFormatException(
                $"A call operand {operandKind} signature is malformed.");
        }
    }

    void RecordMethodOperand(EntityHandle handle)
    {
        _methodOperands = checked(_methodOperands + 1);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                RecordSignature(
                    reader.GetMethodDefinition(
                            (MethodDefinitionHandle)handle)
                        .Signature);
                break;
            case HandleKind.MemberReference:
            {
                MemberReference member =
                    reader.GetMemberReference(
                        (MemberReferenceHandle)handle);
                RecordSignature(member.Signature);
                if (member.Parent.Kind
                    == HandleKind.TypeSpecification)
                {
                    RecordSignature(
                        reader.GetTypeSpecification(
                                (TypeSpecificationHandle)member.Parent)
                            .Signature);
                }
                break;
            }
            case HandleKind.MethodSpecification:
            {
                MethodSpecification specification =
                    reader.GetMethodSpecification(
                        (MethodSpecificationHandle)handle);
                RecordSignature(specification.Signature);
                RecordMethodOperand(specification.Method);
                break;
            }
        }
    }

    void RecordSignature(BlobHandle signature)
    {
        _signatureBytes = checked(
            _signatureBytes
                + reader.GetBlobReader(signature).Length);
    }

    static CallKind? CallKindOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Call => CallKind.Call,
        ILOpCode.Callvirt => CallKind.CallVirtual,
        ILOpCode.Newobj => CallKind.NewObject,
        ILOpCode.Ldftn => CallKind.LoadFunction,
        ILOpCode.Ldvirtftn => CallKind.LoadVirtualFunction,
        ILOpCode.Calli => CallKind.CallIndirect,
        _ => null,
    };
}

internal sealed class MethodCallMetadataSupport(MetadataReader reader)
{
    MethodCallMetadataLookup? _lookup;

    public MethodCallMetadataLookup Acquire(out bool constructed)
    {
        constructed = _lookup is null;
        return _lookup ??= new MethodCallMetadataLookup(reader);
    }
}

internal static class MethodCallMetadataReader
{
    public static MethodLocalSignature DecodeLocalSignature(
        MetadataReader reader,
        MethodBodyBlock body,
        GenericScope scope,
        Action<BlobHandle>? recordSignature = null)
    {
        if (body.LocalSignature.IsNil)
            return new([], 0, null);

        StandaloneSignature signature =
            reader.GetStandaloneSignature(body.LocalSignature);
        recordSignature?.Invoke(signature.Signature);
        BlobReader blob = reader.GetBlobReader(signature.Signature);
        SignatureHeader header = blob.ReadSignatureHeader();
        if (header.Kind != SignatureKind.LocalVariables
            || header.RawValue != 0x07)
        {
            throw new BadImageFormatException(
                "A method body local signature does not have the local-variable signature kind.");
        }
        int declaredCount = blob.ReadCompressedInteger();
        if (declaredCount < 0)
        {
            throw new BadImageFormatException(
                "A method body local signature has an invalid local count.");
        }
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.LocalVariables))
        {
            return new(
                [],
                declaredCount,
                "The local signature exceeds the guarded decode policy.");
        }
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.LocalVariables))
        {
            return new(
                [],
                declaredCount,
                "The local signature is incomplete or contains trailing data.");
        }
        return new(
            signature.DecodeLocalSignature(
                TypeRefDecoder.Instance,
                scope),
            declaredCount,
            null);
    }
}
