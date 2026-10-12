using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>The signature facts of one current-module method that callee matching compares.</summary>
internal readonly record struct SameModuleMethodSignature(
    int Token,
    ImmutableArray<TypeRef> ParameterTypes,
    TypeRef ReturnType,
    int GenericArity,
    bool IsStatic,
    byte SignatureHeader,
    int RequiredParameterCount,
    bool HasInvalidGenericParameterDeclaration)
{
    public static SameModuleMethodSignature From(MethodIdentity method) =>
        new(
            method.MetadataToken,
            method.ParameterTypes,
            method.ReturnType,
            method.GenericArity,
            method.IsStatic,
            method.SignatureHeader,
            method.RequiredParameterCount,
            method.HasInvalidGenericParameterDeclaration);
}

/// <summary>The generic scope of the physical body that contains a call.</summary>
internal readonly record struct SameModuleCallerScope(
    TypeRef DeclaringType,
    int GenericArity,
    bool HasInvalidGenericParameterDeclaration)
{
    public static SameModuleCallerScope From(MethodIdentity method) =>
        new(
            method.DeclaringType,
            method.GenericArity,
            method.HasInvalidGenericParameterDeclaration);
}

/// <summary>The kind of a <see cref="SameModuleCallTarget"/>.</summary>
internal enum SameModuleCallTargetKind
{
    CurrentModule,
    External,
    RuntimeProvided,
    Unresolved,
}

/// <summary>
/// One call's target before a consumer projects it: a current-module MethodDef
/// token, an external declaring-type origin, a runtime-provided array member,
/// or a typed unresolved reason.
/// </summary>
internal readonly record struct SameModuleCallTarget(
    SameModuleCallTargetKind Kind,
    int Token,
    TypeReferenceOrigin? Origin,
    DirectCallTargetUnresolvedReason Reason)
{
    public static SameModuleCallTarget CurrentModule(int token) =>
        new(SameModuleCallTargetKind.CurrentModule, token, null, default);

    public static SameModuleCallTarget External(TypeReferenceOrigin origin) =>
        new(SameModuleCallTargetKind.External, 0, origin, default);

    public static SameModuleCallTarget RuntimeProvided { get; } =
        new(SameModuleCallTargetKind.RuntimeProvided, 0, null, default);

    public static SameModuleCallTarget Unresolved(
        DirectCallTargetUnresolvedReason reason) =>
        new(SameModuleCallTargetKind.Unresolved, 0, null, reason);
}

/// <summary>
/// The single owner of typed same-module callee resolution
/// (<c>docs/design/library-body-analysis-service.md#direct-call-target-resolution</c>).
/// A call binds by definition token and then by signature against the
/// current-module methods its source supplies. Sources differ only in how
/// they supply those methods: eagerly from a declared-method list, or lazily,
/// one declaring type at a time, from metadata.
/// </summary>
internal abstract class SameModuleMethodResolver
{
    readonly SameImageSignatureComparer _signatureComparer;

    private protected SameModuleMethodResolver(
        SameImageSignatureComparer signatureComparer)
    {
        _signatureComparer = signatureComparer;
    }

    /// <summary>Whether <paramref name="token"/> is a current-module method this source supplies.</summary>
    public abstract bool ContainsToken(int token);

    /// <summary>Whether a supplied method has an invalid generic parameter declaration.</summary>
    private protected abstract bool HasInvalidGenericDeclaration(int token);

    /// <summary>
    /// The supplied methods whose declaring type equals <paramref name="definition"/>
    /// and whose name is <paramref name="name"/>, in declaration order.
    /// </summary>
    private protected abstract ReadOnlySpan<SameModuleMethodSignature> Candidates(
        TypeRef definition,
        string name);

    public bool CanResolveToCurrentModule(TypeRef declaringType)
        => _signatureComparer.CanResolveToCurrentModule(declaringType);

    /// <summary>
    /// The target of one call: <paramref name="callee"/> decoded from the
    /// operand whose definition token is <paramref name="calleeDefinitionToken"/>,
    /// in the body described by <paramref name="caller"/>.
    /// </summary>
    public SameModuleCallTarget ResolveTarget(
        CallKind kind,
        SameModuleCallerScope caller,
        int calleeDefinitionToken,
        MemberRef callee)
    {
        if (kind == CallKind.CallIndirect)
            return SameModuleCallTarget.Unresolved(
                DirectCallTargetUnresolvedReason.Indirect);

        TypeRef declaring = callee.DeclaringType;
        if (!ContainsToken(calleeDefinitionToken)
            && callee.Kind != MemberKind.Unsupported)
        {
            if (declaring.Kind is TypeRefKind.Array or TypeRefKind.SzArray)
                return SameModuleCallTarget.RuntimeProvided;
            TypeRef definition = Definition(declaring);
            if (!CanResolveToCurrentModule(declaring)
                && definition.Resolution?.Origin is { } origin)
            {
                return SameModuleCallTarget.External(origin);
            }
        }

        MethodResolution resolution = ResolveTyped(
            caller,
            calleeDefinitionToken,
            callee);
        return resolution.Token != 0
            ? SameModuleCallTarget.CurrentModule(resolution.Token)
            : SameModuleCallTarget.Unresolved(resolution.Reason);
    }

    /// <summary>
    /// The target of a call whose operand peels to <paramref name="definitionToken"/>,
    /// a method this source supplies, without decoding the callee signature:
    /// such a call binds by token, so it needs only the callee's declaring
    /// type and whether its signature is past the safe decoding limits. It is
    /// the outcome <see cref="ResolveTarget"/> returns for the decoded callee.
    /// </summary>
    public SameModuleCallTarget ResolveDefinitionTarget(
        SameModuleCallerScope caller,
        int definitionToken,
        TypeRef calleeDeclaringType,
        bool unsupportedSignature)
    {
        if (!ContainsToken(definitionToken))
            throw new ArgumentOutOfRangeException(nameof(definitionToken));
        MethodResolution resolution =
            CheckCallSite(caller, calleeDeclaringType, unsupportedSignature)
            ?? ResolveSupplied(definitionToken);
        return resolution.Token != 0
            ? SameModuleCallTarget.CurrentModule(resolution.Token)
            : SameModuleCallTarget.Unresolved(resolution.Reason);
    }

    /// <summary>
    /// Resolves a call to a current-module definition, retaining why an
    /// unresolved call failed.
    /// </summary>
    public MethodResolution ResolveTyped(
        SameModuleCallerScope caller,
        int calleeDefinitionToken,
        MemberRef callee)
        => CheckCallSite(
                caller,
                callee.DeclaringType,
                callee.Kind == MemberKind.Unsupported)
            ?? ResolveCore(
                calleeDefinitionToken,
                callee);

    static MethodResolution? CheckCallSite(
        SameModuleCallerScope caller,
        TypeRef calleeDeclaringType,
        bool unsupportedSignature)
    {
        if (caller.HasInvalidGenericParameterDeclaration)
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.InvalidGenericDeclaration);
        // An undecodable callee (for example one past the signature budget) is
        // unsupported, not malformed; its declaring type would otherwise fail
        // the malformed-shape check below. The token is 0 either way.
        if (unsupportedSignature)
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.UnsupportedSignature);
        if (!TryGetDeclaringTypeParameterCount(
                caller.DeclaringType,
                out int callerTypeParameterCount)
            || SignatureTypeFacts.IsMalformed(
                calleeDeclaringType,
                callerTypeParameterCount,
                caller.GenericArity))
        {
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.MalformedSignature);
        }
        return null;
    }

    public int Resolve(
        int calleeDefinitionToken,
        MemberRef callee)
    {
        if (SignatureTypeFacts.IsMalformed(
                callee.DeclaringType,
                typeParameterCount: 0,
                methodParameterCount: 0))
        {
            return 0;
        }
        return ResolveCore(
            calleeDefinitionToken,
            callee).Token;
    }

    MethodResolution ResolveCore(
        int calleeDefinitionToken,
        MemberRef callee)
    {
        if (ContainsToken(calleeDefinitionToken))
            return ResolveSupplied(calleeDefinitionToken);
        if (callee.Kind == MemberKind.Unsupported)
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.UnsupportedSignature);
        if (!_signatureComparer.CanResolveToCurrentModule(
                callee.DeclaringType))
        {
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.Unmatched);
        }

        return ResolveBySignature(callee);
    }

    MethodResolution ResolveSupplied(int token)
        => HasInvalidGenericDeclaration(token)
            ? MethodResolution.Fail(DirectCallTargetUnresolvedReason.InvalidGenericDeclaration)
            : MethodResolution.Resolved(token);

    MethodResolution ResolveBySignature(MemberRef callee)
    {
        TypeRef declaring = callee.DeclaringType;
        TypeRef definition = Definition(declaring);
        ImmutableArray<TypeRef> typeArguments =
            declaring.Kind == TypeRefKind.GenericInstance
                ? declaring.TypeArguments
                : [];
        if (!TryGetDeclaringTypeParameterCount(
                definition,
                out int declaringTypeParameterCount)
            || (declaring.Kind == TypeRefKind.GenericInstance
                && typeArguments.Length
                    != declaringTypeParameterCount))
        {
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.MalformedSignature);
        }
        ReadOnlySpan<SameModuleMethodSignature> candidates =
            Candidates(definition, callee.Name);
        if (candidates.IsEmpty)
            return MethodResolution.Fail(DirectCallTargetUnresolvedReason.Unmatched);

        int resolvedToken = 0;
        foreach (SameModuleMethodSignature candidate in candidates)
        {
            if (!SignatureMatches(
                    candidate,
                    declaringTypeParameterCount,
                    callee))
            {
                continue;
            }
            if (resolvedToken != 0)
                return MethodResolution.Fail(DirectCallTargetUnresolvedReason.Ambiguous);
            resolvedToken = candidate.Token;
        }

        return resolvedToken != 0
            ? MethodResolution.Resolved(resolvedToken)
            : MethodResolution.Fail(DirectCallTargetUnresolvedReason.Unmatched);
    }

    bool SignatureMatches(
        in SameModuleMethodSignature candidate,
        int declaringTypeParameterCount,
        MemberRef callee)
        => !candidate.HasInvalidGenericParameterDeclaration
            && SignatureMatches(
                candidate.ParameterTypes,
                candidate.ReturnType,
                declaringTypeParameterCount,
                candidate.GenericArity,
                candidate.IsStatic,
                candidate.SignatureHeader,
                candidate.RequiredParameterCount,
                callee.OpenSignatureParameters,
                callee.OpenSignatureReturn,
                callee.GenericArity,
                callee.HasThis,
                callee.SignatureHeader,
                callee.RequiredParameterCount,
                _signatureComparer.Matches);

    internal static bool SignatureMatches(
        ImmutableArray<TypeRef> candidateParameterTypes,
        TypeRef candidateReturnType,
        int candidateTypeParameterCount,
        int candidateGenericArity,
        bool candidateIsStatic,
        byte candidateSignatureHeader,
        int candidateRequiredParameterCount,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount,
        Func<TypeRef, TypeRef, bool> typeMatches)
    {
        if (candidateGenericArity != genericArity
            || candidateIsStatic == hasThis
            || (candidateSignatureHeader & 0x4F)
                != (signatureHeader & 0x4F)
            || ((signatureHeader & 0x0F) == 0x05
                && candidateRequiredParameterCount
                    != requiredParameterCount))
        {
            return false;
        }
        if (SignatureTypeFacts.IsMalformed(
                candidateReturnType,
                candidateTypeParameterCount,
                candidateGenericArity)
            || candidateParameterTypes.Any(
                parameter =>
                    SignatureTypeFacts.IsMalformed(
                        parameter,
                        candidateTypeParameterCount,
                        candidateGenericArity)))
        {
            return false;
        }

        bool isVarArg =
            (signatureHeader & 0x0F) == 0x05;
        int comparedParameterCount = isVarArg
            ? requiredParameterCount
            : parameterTypes.Length;
        if (comparedParameterCount < 0
            || candidateParameterTypes.Length
                != comparedParameterCount
            || parameterTypes.Length
                < comparedParameterCount)
        {
            return false;
        }

        for (int i = 0; i < comparedParameterCount; i++)
        {
            if (!typeMatches(
                    candidateParameterTypes[i],
                    parameterTypes[i]))
            {
                return false;
            }
        }

        return typeMatches(
            candidateReturnType,
            returnType);
    }

    private protected static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    internal static bool TryGetDeclaringTypeParameterCount(
        TypeRef type,
        out int count)
    {
        count = 0;
        TypeRef definition = Definition(type);
        ImmutableArray<string> segments =
            definition.Resolution?.Type.Segments
                ?? [];
        if (segments.IsDefaultOrEmpty)
        {
            return AddArity(
                definition.Name,
                plusIsBoundary: true,
                ref count);
        }

        foreach (string segment in segments)
        {
            if (!AddArity(
                    segment,
                    plusIsBoundary: false,
                    ref count))
            {
                return false;
            }
        }
        return true;

        static bool AddArity(
            string name,
            bool plusIsBoundary,
            ref int count)
        {
            foreach (MetadataNameComponent component
                in MetadataNameArity.EnumerateComponents(
                    name,
                    dotIsBoundary: false,
                    plusIsBoundary))
            {
                if (component.Arity > int.MaxValue - count)
                    return false;
                count += component.Arity;
            }
            return true;
        }
    }
}

/// <summary>A current-module definition token, or why no definition was bound.</summary>
internal readonly record struct MethodResolution(
    int Token,
    DirectCallTargetUnresolvedReason Reason)
{
    public static MethodResolution Resolved(int token) => new(token, default);

    public static MethodResolution Fail(DirectCallTargetUnresolvedReason reason) => new(0, reason);
}
