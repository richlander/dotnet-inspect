using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly Dictionary<string, int> _tokenByKey = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<MethodIdentity>> _conversionsByKey =
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
            if (ApiMemberIdentity.IsConversionOperator(method.Name))
            {
                if (_conversionsByKey.TryGetValue(key, out var conversions))
                    conversions.Add(method);
                else
                    _conversionsByKey[key] = [method];
            }
            else
            {
                _tokenByKey.TryAdd(key, method.MetadataToken);
            }

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
            call.Callee.ParameterTypes);
        if (ApiMemberIdentity.IsConversionOperator(call.Callee.Name))
        {
            if (_conversionsByKey.TryGetValue(key, out var conversions))
            {
                foreach (MethodIdentity conversion in conversions)
                {
                    if (TypeRef.ExactSignatureEquals(
                            conversion.ReturnType,
                            call.Callee.OpenSignatureReturn))
                    {
                        return conversion.MetadataToken;
                    }
                }
            }
        }
        else if (_tokenByKey.TryGetValue(key, out int token))
        {
            return token;
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
            if (SignatureMatches(
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

    static bool SignatureMatches(
        MethodIdentity candidate,
        ImmutableArray<TypeRef> typeArguments,
        MemberRef callee)
    {
        if (candidate.GenericArity != callee.GenericArity
            || candidate.IsStatic == callee.HasThis
            || candidate.SignatureHeader != 0
                && callee.SignatureHeader != 0
                && candidate.SignatureHeader
                    != callee.SignatureHeader
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
