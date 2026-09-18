using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly HashSet<int> _invalidMethodTokens = [];
    readonly Dictionary<(TypeRef DeclaringType, string Name), List<MethodIdentity>>
        _methodsByDeclaringTypeAndName = [];
    readonly SameImageSignatureComparer _signatureComparer;

    MethodDefinitionMap(
        ImmutableArray<MethodIdentity> methods,
        string? currentModuleName)
    {
        AssemblyReferenceIdentity? assembly = null;
        foreach (MethodIdentity method in methods)
        {
            if (assembly is null
                && Definition(method.DeclaringType).Resolution?.Origin
                    is TypeReferenceOrigin.CurrentAssembly
                    { Assembly: { } currentAssembly })
            {
                assembly = currentAssembly;
            }

            _methodTokens.Add(method.MetadataToken);
            if (method.HasInvalidGenericParameterDeclaration)
                _invalidMethodTokens.Add(method.MetadataToken);

            var key = (method.DeclaringType, method.Name);
            if (_methodsByDeclaringTypeAndName.TryGetValue(
                    key,
                    out List<MethodIdentity>? candidates))
            {
                candidates.Add(method);
            }
            else
            {
                _methodsByDeclaringTypeAndName[key] = [method];
            }
        }
        _signatureComparer = new(assembly, currentModuleName);
    }

    public static MethodDefinitionMap Create(
        ImmutableArray<MethodIdentity> methods,
        string? currentModuleName = null)
        => new(methods, currentModuleName);

    public bool ContainsToken(int token)
        => _methodTokens.Contains(token);

    public bool CouldResolveToCurrentModule(DirectCall call)
        => call.Callee.Kind != MemberKind.Unsupported
            && _signatureComparer.CanResolveToCurrentModule(call.Callee.DeclaringType);

    public int Resolve(DirectCall call)
    {
        MethodIdentity scope = call.EvidenceMethod;
        if (scope.HasInvalidGenericParameterDeclaration
            || !TryGetDeclaringTypeParameterCount(
                scope.DeclaringType,
                out int callerTypeParameterCount)
            || SignatureTypeFacts.IsMalformed(
                call.Callee.DeclaringType,
                callerTypeParameterCount,
                scope.GenericArity))
        {
            return 0;
        }
        return ResolveCore(
            call.CalleeDefinitionToken,
            call.Callee);
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
            callee);
    }

    int ResolveCore(
        int calleeDefinitionToken,
        MemberRef callee)
    {
        if (_methodTokens.Contains(
                calleeDefinitionToken))
        {
            return _invalidMethodTokens.Contains(
                    calleeDefinitionToken)
                ? 0
                : calleeDefinitionToken;
        }
        if (callee.Kind == MemberKind.Unsupported
            || !_signatureComparer.CanResolveToCurrentModule(
                callee.DeclaringType))
        {
            return 0;
        }

        return ResolveBySignature(callee);
    }

    int ResolveBySignature(MemberRef callee)
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
            return 0;
        }
        if (!_methodsByDeclaringTypeAndName.TryGetValue(
                (definition, callee.Name),
                out List<MethodIdentity>? candidates))
        {
            return 0;
        }

        int resolvedToken = 0;
        foreach (MethodIdentity candidate in candidates)
        {
            if (!SignatureMatches(
                    candidate,
                    declaringTypeParameterCount,
                    callee))
            {
                continue;
            }
            if (resolvedToken != 0)
                return 0;
            resolvedToken = candidate.MetadataToken;
        }

        return resolvedToken;
    }

    bool SignatureMatches(
        MethodIdentity candidate,
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

    static TypeRef Definition(TypeRef type)
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
