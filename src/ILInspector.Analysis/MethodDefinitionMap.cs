using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly Dictionary<string, List<MethodIdentity>> _methodsByDeclaringTypeAndName = new(StringComparer.Ordinal);

    MethodDefinitionMap(ImmutableArray<MethodIdentity> methods)
    {
        foreach (var method in methods)
        {
            _methodTokens.Add(method.MetadataToken);
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
        => Resolve(
            call.CalleeDefinitionToken,
            call.Callee);

    public int Resolve(
        int calleeDefinitionToken,
        MemberRef callee)
    {
        if (_methodTokens.Contains(calleeDefinitionToken))
            return calleeDefinitionToken;
        if (callee.Kind == MemberKind.Unsupported)
            return 0;
        return ResolveBySignature(callee);
    }

    int ResolveBySignature(MemberRef callee)
    {
        var declaring = callee.DeclaringType;
        TypeRef definition =
            declaring.Kind == TypeRefKind.GenericInstance
                ? declaring.ElementType!
                : declaring;
        ImmutableArray<TypeRef> typeArguments =
            declaring.Kind == TypeRefKind.GenericInstance
                ? declaring.TypeArguments
                : [];
        if (!_methodsByDeclaringTypeAndName.TryGetValue(DeclaringTypeAndNameKey(definition, callee.Name), out var candidates))
            return 0;

        int resolvedToken = 0;
        foreach (var candidate in candidates)
        {
            if (!definition.Equals(candidate.DeclaringType))
                continue;
            if (SignatureMatches(
                    candidate,
                    typeArguments,
                    callee.TypeArguments,
                    callee.ParameterTypes,
                    callee.ReturnType,
                    callee.GenericArity,
                    callee.HasThis,
                    callee.SignatureHeader,
                    callee.RequiredParameterCount))
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
        ImmutableArray<TypeRef> methodArguments,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount)
        => SignatureMatches(
            candidate.ParameterTypes,
            candidate.ReturnType,
            typeArguments.IsDefaultOrEmpty
                ? int.MaxValue
                : typeArguments.Length,
            candidate.GenericArity,
            candidate.IsStatic,
            candidate.SignatureHeader,
            candidate.RequiredParameterCount,
            typeArguments,
            methodArguments,
            parameterTypes,
            returnType,
            genericArity,
            hasThis,
            signatureHeader,
            requiredParameterCount);

    internal static bool SignatureMatches(
        ImmutableArray<TypeRef> candidateParameterTypes,
        TypeRef candidateReturnType,
        int candidateTypeParameterCount,
        int candidateGenericArity,
        bool candidateIsStatic,
        byte candidateSignatureHeader,
        int candidateRequiredParameterCount,
        ImmutableArray<TypeRef> typeArguments,
        ImmutableArray<TypeRef> methodArguments,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount)
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
            if (!TypeRef.ExactSignatureEquals(
                    candidateParameterTypes[i].Instantiate(
                        typeArguments,
                        methodArguments),
                    parameterTypes[i]))
            {
                return false;
            }
        }
        TypeRef candidateReturn = candidateReturnType.Instantiate(
            typeArguments,
            methodArguments);
        return TypeRef.ExactSignatureEquals(
            candidateReturn,
            returnType);
    }

    static string DeclaringTypeAndNameKey(TypeRef declaringType, string name)
        => $"{declaringType.Assembly}|{declaringType.Namespace}|{declaringType.Name}|{name}";

}
