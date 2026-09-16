using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly Dictionary<string, List<MethodIdentity>> _methodsByKey =
        new(StringComparer.Ordinal);
    readonly Dictionary<string, List<MethodIdentity>> _methodsByDeclaringTypeAndName = new(StringComparer.Ordinal);

    MethodDefinitionMap(ImmutableArray<MethodIdentity> methods)
    {
        foreach (var method in methods)
        {
            _methodTokens.Add(method.MetadataToken);
            string key = Key(
                method.DeclaringType,
                method.Name,
                method.GenericArity,
                method.ParameterTypes);
            if (_methodsByKey.TryGetValue(key, out var methodsByKey))
                methodsByKey.Add(method);
            else
                _methodsByKey[key] = [method];

            var groupKey = DeclaringTypeAndNameKey(method.DeclaringType, method.Name);
            if (_methodsByDeclaringTypeAndName.TryGetValue(groupKey, out var list))
                list.Add(method);
            else
                _methodsByDeclaringTypeAndName[groupKey] = [method];
        }
    }

    public static MethodDefinitionMap Create(ImmutableArray<MethodIdentity> methods) => new(methods);

    public bool ContainsToken(int token) => _methodTokens.Contains(token);

    public int Resolve(DirectCall call)
    {
        if (_methodTokens.Contains(call.CalleeDefinitionToken))
            return call.CalleeDefinitionToken;
        if (call.Callee.Kind == MemberKind.Unsupported)
            return 0;
        string key = Key(
            call.Callee.DeclaringType,
            call.Callee.Name,
            call.Callee.GenericArity,
            call.Callee.OpenSignatureParameters);
        if (_methodsByKey.TryGetValue(key, out var candidates))
        {
            int resolvedToken = 0;
            foreach (MethodIdentity candidate in candidates)
            {
                if (!LocalSignatureMatches(candidate, call.Callee))
                    continue;
                if (resolvedToken != 0)
                    return 0;
                resolvedToken = candidate.MetadataToken;
            }
            if (resolvedToken != 0)
                return resolvedToken;
        }
        return ResolveConstructedGenericDeclaringType(call.Callee);
    }

    int ResolveConstructedGenericDeclaringType(MemberRef callee)
    {
        var declaring = callee.DeclaringType;
        if (declaring.Kind != TypeRefKind.GenericInstance || declaring.ElementType is not { } definition)
            return 0;
        if (!_methodsByDeclaringTypeAndName.TryGetValue(DeclaringTypeAndNameKey(definition, callee.Name), out var candidates))
            return 0;

        int resolvedToken = 0;
        foreach (var candidate in candidates)
        {
            if (!definition.Equals(candidate.DeclaringType))
                continue;
            if (ConstructedSignatureMatches(
                    candidate,
                    declaring.TypeArguments,
                    callee))
            {
                if (resolvedToken != 0)
                    return 0;
                resolvedToken = candidate.MetadataToken;
            }
        }

        return resolvedToken;
    }

    static bool LocalSignatureMatches(
        MethodIdentity candidate,
        MemberRef callee)
    {
        if (!MethodShapeMatches(candidate, callee)
            || candidate.ParameterTypes.Length
                != callee.OpenSignatureParameters.Length)
        {
            return false;
        }

        for (int i = 0;
            i < callee.OpenSignatureParameters.Length;
            i++)
        {
            if (!TypeRef.ExactSignatureEquals(
                    candidate.ParameterTypes[i],
                    callee.OpenSignatureParameters[i]))
            {
                return false;
            }
        }
        return TypeRef.ExactSignatureEquals(
            candidate.ReturnType,
            callee.OpenSignatureReturn);
    }

    static bool ConstructedSignatureMatches(
        MethodIdentity candidate,
        ImmutableArray<TypeRef> typeArguments,
        MemberRef callee)
    {
        if (!MethodShapeMatches(candidate, callee)
            || candidate.ParameterTypes.Length
                != callee.ParameterTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < callee.ParameterTypes.Length; i++)
        {
            if (!TypeRef.ExactSignatureEquals(
                    candidate.ParameterTypes[i].Instantiate(
                        typeArguments,
                        callee.TypeArguments),
                    callee.ParameterTypes[i]))
            {
                return false;
            }
        }
        TypeRef candidateReturn = candidate.ReturnType.Instantiate(
            typeArguments,
            callee.TypeArguments);
        return TypeRef.ExactSignatureEquals(
            candidateReturn,
            callee.ReturnType);
    }

    static bool MethodShapeMatches(
        MethodIdentity candidate,
        MemberRef callee)
        => candidate.GenericArity == callee.GenericArity
            && candidate.IsStatic != callee.HasThis
            && candidate.SignatureHeader
                == callee.SignatureHeader
            && candidate.RequiredParameterCount
                == callee.RequiredParameterCount;

    static string DeclaringTypeAndNameKey(TypeRef declaringType, string name)
        => $"{declaringType.Assembly}|{declaringType.Namespace}|{declaringType.Name}|{name}";

    static string Key(
        TypeRef declaringType,
        string name,
        int genericArity,
        ImmutableArray<TypeRef> parameterTypes) =>
        $"{GenericMemberIdentity.KeyFragment(declaringType)}|{name}|{genericArity}|"
        + string.Join(
            ",",
            parameterTypes.Select(
                GenericMemberIdentity.KeyFragment));
}
