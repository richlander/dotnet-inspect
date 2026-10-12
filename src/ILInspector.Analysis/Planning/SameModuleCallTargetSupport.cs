using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// The typed target of one call operand, as
/// <c>docs/design/method-query-source.md#same-module-call-target-support</c>
/// defines it: a MethodDef of the inspected module, a member whose declaring
/// type belongs to another module, a runtime-provided array member, or a typed
/// unresolved outcome.
/// </summary>
public abstract record MethodCallTarget
{
    private protected MethodCallTarget()
    {
    }

    /// <summary>The call binds to <paramref name="Method"/>, declared in the inspected module.</summary>
    public sealed record CurrentModule(MethodDefinitionHandle Method) : MethodCallTarget;

    /// <summary>
    /// The callee's declaring type definition belongs to another module;
    /// <paramref name="Origin"/> is its exact decoded reference origin, never
    /// followed through type forwarding.
    /// </summary>
    public sealed record External(TypeReferenceOrigin Origin) : MethodCallTarget;

    /// <summary>The callee is a runtime-provided member of an array type.</summary>
    public sealed record RuntimeProvided : MethodCallTarget;

    /// <summary>No target is statically known; <paramref name="Reason"/> says why.</summary>
    public sealed record Unresolved(DirectCallTargetUnresolvedReason Reason) : MethodCallTarget;
}

/// <summary>
/// Metadata work that same-module call-target support performed: TypeDef rows
/// read to index the module's type definitions, MethodDef rows read as caller,
/// definition, or candidate probes, and the full length of the signature blob
/// of each enumerated step, as
/// <c>docs/design/method-query-source.md#same-module-call-target-support</c>
/// lists them.
/// </summary>
public sealed record SameModuleCallTargetWork(
    int TypeDefinitionRows,
    int MethodDefinitionRows,
    long SignatureBytes)
{
    public static SameModuleCallTargetWork Empty { get; } = new(0, 0, 0);

    public SameModuleCallTargetWork Since(SameModuleCallTargetWork before) =>
        new(
            TypeDefinitionRows - before.TypeDefinitionRows,
            MethodDefinitionRows - before.MethodDefinitionRows,
            SignatureBytes - before.SignatureBytes);

    public SameModuleCallTargetWork Plus(SameModuleCallTargetWork other) =>
        new(
            checked(TypeDefinitionRows + other.TypeDefinitionRows),
            checked(MethodDefinitionRows + other.MethodDefinitionRows),
            checked(SignatureBytes + other.SignatureBytes));
}

/// <summary>
/// Exact same-module call-target work in one source receipt, as
/// <c>docs/design/method-query-source.md#same-module-call-target-support</c>
/// defines it.
/// </summary>
public sealed record SameModuleCallTargetCoverage(
    bool Constructed,
    SameModuleCallTargetWork QueryWork,
    int Queries,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static SameModuleCallTargetCoverage Empty { get; } =
        new(
            Constructed: false,
            SameModuleCallTargetWork.Empty,
            Queries: 0,
            MethodDefinitionHandleCoverage.Empty);
}

/// <summary>
/// Same-module callee resolution over the subject's metadata. The resolution
/// is <see cref="SameModuleMethodResolver"/>'s; this source supplies the
/// candidate methods of one declaring type at a time, from a TypeDef index
/// built at the first query that resolves by signature.
/// </summary>
internal sealed class SameModuleCallTargetLookup : SameModuleMethodResolver
{
    readonly MetadataReader _reader;
    readonly Dictionary<(TypeRef Definition, string Name), SameModuleMethodSignature[]>
        _candidates = [];
    Dictionary<TypeRef, List<TypeDefinitionHandle>>? _typeDefinitions;
    MethodDefinitionHandle _callerHandle;
    SameModuleCallerScope _caller;
    GenericScope _callerGenericScope = GenericScope.Empty;
    int _typeDefinitionRows;
    int _methodDefinitionRows;
    long _signatureBytes;

    SameModuleCallTargetLookup(
        MetadataReader reader,
        SameImageSignatureComparer signatureComparer)
        : base(signatureComparer)
    {
        _reader = reader;
    }

    internal static SameModuleCallTargetLookup Create(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new(
            reader,
            new SameImageSignatureComparer(
                reader.IsAssembly
                    ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                    : null,
                reader.GetString(reader.GetModuleDefinition().Name)));
    }

    public SameModuleCallTargetWork RecordedWork =>
        new(_typeDefinitionRows, _methodDefinitionRows, _signatureBytes);

    /// <summary>
    /// The target of the call whose operand is <paramref name="operandToken"/>,
    /// in the body of <paramref name="caller"/>. Metadata that cannot be read
    /// throws, failing that body.
    /// </summary>
    public MethodCallTarget Resolve(
        MethodDefinitionHandle caller,
        CallKind kind,
        int operandToken)
    {
        if (kind == CallKind.CallIndirect)
            return new MethodCallTarget.Unresolved(
                DirectCallTargetUnresolvedReason.Indirect);

        SameModuleCallerScope scope = CallerScope(caller);
        EntityHandle operand = MetadataTokens.EntityHandle(operandToken);
        // A MethodSpec's target, not its instantiation, decides the call target.
        EntityHandle method = operand.Kind == HandleKind.MethodSpecification
            ? _reader.GetMethodSpecification(
                (MethodSpecificationHandle)operand).Method
            : operand;
        int definitionToken = MemberResolver.DefinitionToken(
            _reader,
            MetadataTokens.GetToken(method));
        if (ContainsToken(definitionToken))
        {
            // The definition probe: one MethodDef row, read for its declaring
            // type and, when the call site passes its checks, its generic
            // declaration.
            _methodDefinitionRows++;
            BlobHandle signature = SignatureOf(method);
            _signatureBytes += BlobLength(signature);
            return Project(
                ResolveDefinitionTarget(
                    scope,
                    definitionToken,
                    TypeRefDecoder.Instance.GetTypeFromDefinition(
                        _reader,
                        _reader.GetMethodDefinition(
                            (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                                definitionToken)).GetDeclaringType(),
                        0),
                    !SignatureBlobGuard.IsSafeToDecode(
                        _reader,
                        signature,
                        SignatureBlobGuard.Kind.Method)));
        }

        if (method.Kind == HandleKind.MemberReference)
        {
            MemberReference reference =
                _reader.GetMemberReference((MemberReferenceHandle)method);
            _signatureBytes += BlobLength(reference.Signature);
            if (reference.Parent.Kind == HandleKind.TypeSpecification)
            {
                _signatureBytes += BlobLength(
                    _reader.GetTypeSpecification(
                        (TypeSpecificationHandle)reference.Parent).Signature);
            }
        }

        MemberRef callee = MemberResolver.ResolveMethod(
            _reader,
            method,
            _callerGenericScope);
        return Project(
            ResolveTarget(
                kind,
                scope,
                definitionToken,
                callee));
    }

    BlobHandle SignatureOf(EntityHandle method) => method.Kind switch
    {
        HandleKind.MethodDefinition =>
            _reader.GetMethodDefinition((MethodDefinitionHandle)method).Signature,
        HandleKind.MemberReference =>
            _reader.GetMemberReference((MemberReferenceHandle)method).Signature,
        _ => throw new BadImageFormatException(
            $"A call operand's method is a {method.Kind}."),
    };

    static MethodCallTarget Project(SameModuleCallTarget target) =>
        target.Kind switch
        {
            SameModuleCallTargetKind.CurrentModule =>
                new MethodCallTarget.CurrentModule(
                    (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                        target.Token)),
            SameModuleCallTargetKind.External =>
                new MethodCallTarget.External(target.Origin!),
            SameModuleCallTargetKind.RuntimeProvided =>
                new MethodCallTarget.RuntimeProvided(),
            _ => new MethodCallTarget.Unresolved(target.Reason),
        };

    SameModuleCallerScope CallerScope(MethodDefinitionHandle caller)
    {
        if (caller == _callerHandle)
            return _caller;

        MethodDefinition method = _reader.GetMethodDefinition(caller);
        TypeDefinitionHandle typeHandle = method.GetDeclaringType();
        GenericParameterHandleCollection genericParameters =
            method.GetGenericParameters();
        _methodDefinitionRows++;
        _callerGenericScope = new GenericScope(
            MemberResolver.GenericParameterNames(
                _reader,
                _reader.GetTypeDefinition(typeHandle).GetGenericParameters()),
            MemberResolver.GenericParameterNames(_reader, genericParameters));
        _caller = new SameModuleCallerScope(
            TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0),
            genericParameters.Count,
            !HasExactGenericParameterDeclaration(method));
        _callerHandle = caller;
        return _caller;
    }

    public override bool ContainsToken(int token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        return handle.Kind == HandleKind.MethodDefinition
            && MetadataTokens.GetRowNumber(handle) is > 0 and int row
            && row <= _reader.MethodDefinitions.Count;
    }

    // Reached only through a token-bound query, whose definition probe
    // already counted this row.
    private protected override bool HasInvalidGenericDeclaration(int token)
    {
        return !HasExactGenericParameterDeclaration(
            _reader.GetMethodDefinition(
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(token)));
    }

    // The signature's generic parameter count against the method's declared
    // generic parameters, read from the signature header alone. An unreadable
    // header is an invalid declaration, as Library Body Analysis judges it.
    bool HasExactGenericParameterDeclaration(MethodDefinition method)
    {
        try
        {
            BlobReader signature = _reader.GetBlobReader(method.Signature);
            SignatureHeader header = signature.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method)
                return false;
            int count = header.IsGeneric ? signature.ReadCompressedInteger() : 0;
            return count >= 0
                && MemberResolver.HasExactGenericParameters(
                    _reader,
                    method.GetGenericParameters(),
                    count);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            return false;
        }
    }

    private protected override ReadOnlySpan<SameModuleMethodSignature> Candidates(
        TypeRef definition,
        string name)
    {
        if (_candidates.TryGetValue(
                (definition, name),
                out SameModuleMethodSignature[]? cached))
        {
            return cached;
        }

        List<SameModuleMethodSignature>? candidates = null;
        if (TypeDefinitions().TryGetValue(
                definition,
                out List<TypeDefinitionHandle>? types))
        {
            foreach (TypeDefinitionHandle typeHandle in types)
            {
                TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    _methodDefinitionRows++;
                    MethodDefinition method =
                        _reader.GetMethodDefinition(methodHandle);
                    if (!_reader.StringComparer.Equals(method.Name, name))
                        continue;
                    (candidates ??= []).Add(
                        Candidate(type, methodHandle, method));
                }
            }
        }

        SameModuleMethodSignature[] result =
            candidates is null ? [] : [.. candidates];
        _candidates[(definition, name)] = result;
        return result;
    }

    // The candidate facts Library Body Analysis records for a declared method.
    SameModuleMethodSignature Candidate(
        TypeDefinition type,
        MethodDefinitionHandle handle,
        MethodDefinition method)
    {
        GenericParameterHandleCollection genericParameters =
            method.GetGenericParameters();
        bool isStatic = (method.Attributes & MethodAttributes.Static) != 0;
        int token = MetadataTokens.GetToken(handle);
        _signatureBytes += BlobLength(method.Signature);
        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return new(
                token,
                [],
                TypeRef.Unsupported("method signature nesting depth exceeded"),
                genericParameters.Count,
                isStatic,
                SignatureHeader: 0,
                RequiredParameterCount: -1,
                !HasExactGenericParameterDeclaration(method));
        }

        MethodSignature<TypeRef> signature = method.DecodeSignature(
            TypeRefDecoder.Instance,
            new GenericScope(
                MemberResolver.GenericParameterNames(
                    _reader,
                    type.GetGenericParameters()),
                MemberResolver.GenericParameterNames(
                    _reader,
                    genericParameters)));
        return new(
            token,
            signature.ParameterTypes,
            signature.ReturnType,
            genericParameters.Count,
            isStatic,
            signature.Header.RawValue,
            signature.RequiredParameterCount,
            !MemberResolver.HasExactGenericParameters(
                _reader,
                genericParameters,
                signature.GenericParameterCount));
    }

    Dictionary<TypeRef, List<TypeDefinitionHandle>> TypeDefinitions()
    {
        if (_typeDefinitions is not null)
            return _typeDefinitions;

        var types = new Dictionary<TypeRef, List<TypeDefinitionHandle>>();
        foreach (TypeDefinitionHandle handle in _reader.TypeDefinitions)
        {
            _typeDefinitionRows++;
            TypeRef type = TypeRefDecoder.Instance.GetTypeFromDefinition(
                _reader,
                handle,
                0);
            if (types.TryGetValue(type, out List<TypeDefinitionHandle>? handles))
                handles.Add(handle);
            else
                types[type] = [handle];
        }
        return _typeDefinitions = types;
    }

    int BlobLength(BlobHandle blob) => _reader.GetBlobReader(blob).Length;
}

/// <summary>
/// Declared execution-scoped support that constructs its lookup at the first
/// query, so a declaring execution that never queries builds nothing.
/// </summary>
internal sealed class SameModuleCallTargetSupport(MetadataReader reader)
{
    SameModuleCallTargetLookup? _lookup;

    /// <summary>
    /// The lookup, constructed by this call when <paramref name="constructed"/>
    /// is true.
    /// </summary>
    public SameModuleCallTargetLookup Acquire(out bool constructed)
    {
        constructed = _lookup is null;
        return _lookup ??= SameModuleCallTargetLookup.Create(reader);
    }
}
