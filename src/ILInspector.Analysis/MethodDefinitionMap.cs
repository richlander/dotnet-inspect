using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly Dictionary<string, int> _tokenByKey = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<MethodIdentity>> _methodsByDeclaringTypeAndName = new(StringComparer.Ordinal);

    MethodDefinitionMap(ImmutableArray<MethodIdentity> methods)
    {
        foreach (var method in methods)
        {
            _methodTokens.Add(method.MetadataToken);
            _tokenByKey.TryAdd(Key(method.DeclaringType, method.Name, method.ParameterTypes), method.MetadataToken);

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
        if (_tokenByKey.TryGetValue(Key(call.Callee.DeclaringType, call.Callee.Name, call.Callee.ParameterTypes), out int token))
            return token;
        return ResolveConstructedGenericDeclaringType(call.Callee);
    }

    int ResolveConstructedGenericDeclaringType(MemberRef callee)
    {
        var declaring = callee.DeclaringType;
        if (declaring.Kind != TypeRefKind.GenericInstance || declaring.ElementType is not { } definition)
            return 0;
        if (!_methodsByDeclaringTypeAndName.TryGetValue(DeclaringTypeAndNameKey(definition, callee.Name), out var candidates))
            return 0;

        foreach (var candidate in candidates)
        {
            if (!definition.Equals(candidate.DeclaringType))
                continue;
            if (SignatureMatches(candidate, declaring.TypeArguments, callee.TypeArguments, callee.ParameterTypes, callee.ReturnType))
                return candidate.MetadataToken;
        }

        return 0;
    }

    static bool SignatureMatches(
        MethodIdentity candidate,
        ImmutableArray<TypeRef> typeArguments,
        ImmutableArray<TypeRef> methodArguments,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType)
    {
        if (candidate.ParameterTypes.Length != parameterTypes.Length)
            return false;
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            if (!candidate.ParameterTypes[i].Instantiate(typeArguments, methodArguments).Equals(parameterTypes[i]))
                return false;
        }
        return candidate.ReturnType.Instantiate(typeArguments, methodArguments).Equals(returnType);
    }

    static string DeclaringTypeAndNameKey(TypeRef declaringType, string name)
        => $"{declaringType.Assembly}|{declaringType.Namespace}|{declaringType.Name}|{name}";

    static string Key(TypeRef declaringType, string name, ImmutableArray<TypeRef> parameterTypes)
        => $"{declaringType.ToQualifiedDisplayString()}|{name}|{string.Join(",", parameterTypes.Select(type => type.ToQualifiedDisplayString()))}";
}
