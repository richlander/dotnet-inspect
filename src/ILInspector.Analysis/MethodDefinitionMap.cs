using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly Dictionary<string, List<MethodIdentity>>
        _methodsByDeclaringTypeAndName =
            new(StringComparer.Ordinal);
    readonly AssemblyReferenceIdentity? _currentAssembly;

    MethodDefinitionMap(
        ImmutableArray<MethodIdentity> methods)
    {
        foreach (MethodIdentity method in methods)
        {
            if (_currentAssembly is null
                && Definition(method.DeclaringType).Resolution?.Origin
                    is TypeReferenceOrigin.CurrentAssembly
                    { Assembly: { } currentAssembly })
            {
                _currentAssembly = currentAssembly;
            }

            _methodTokens.Add(method.MetadataToken);
            string key = DeclaringTypeAndNameKey(
                method.DeclaringType,
                method.Name);
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
    }

    public static MethodDefinitionMap Create(
        ImmutableArray<MethodIdentity> methods)
        => new(methods);

    public bool ContainsToken(int token)
        => _methodTokens.Contains(token);

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
        if (callee.Kind == MemberKind.Unsupported
            || !CanResolveToCurrentModule(
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
        if (!_methodsByDeclaringTypeAndName.TryGetValue(
                DeclaringTypeAndNameKey(
                    definition,
                    callee.Name),
                out List<MethodIdentity>? candidates))
        {
            return 0;
        }

        int resolvedToken = 0;
        foreach (MethodIdentity candidate in candidates)
        {
            if (!definition.Equals(candidate.DeclaringType)
                || !SignatureMatches(
                    candidate,
                    typeArguments,
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

    bool CanResolveToCurrentModule(TypeRef type)
    {
        TypeReferenceOrigin? origin =
            Definition(type).Resolution?.Origin;
        return origin switch
        {
            null => true,
            TypeReferenceOrigin.CurrentAssembly => true,
            TypeReferenceOrigin.AssemblyReference reference =>
                _currentAssembly is { } current
                && reference.Assembly.IsEquivalentTo(current),
            _ => false,
        };
    }

    bool SignatureMatches(
        MethodIdentity candidate,
        ImmutableArray<TypeRef> typeArguments,
        MemberRef callee)
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
            callee.TypeArguments,
            callee.ParameterTypes,
            callee.ReturnType,
            callee.GenericArity,
            callee.HasThis,
            callee.SignatureHeader,
            callee.RequiredParameterCount,
            LocalSignatureTypeMatches);

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
        => SignatureMatches(
            candidateParameterTypes,
            candidateReturnType,
            candidateTypeParameterCount,
            candidateGenericArity,
            candidateIsStatic,
            candidateSignatureHeader,
            candidateRequiredParameterCount,
            typeArguments,
            methodArguments,
            parameterTypes,
            returnType,
            genericArity,
            hasThis,
            signatureHeader,
            requiredParameterCount,
            TypeRef.ExactSignatureEquals);

    static bool SignatureMatches(
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
                    candidateParameterTypes[i].Instantiate(
                        typeArguments,
                        methodArguments),
                    parameterTypes[i]))
            {
                return false;
            }
        }

        return typeMatches(
            candidateReturnType.Instantiate(
                typeArguments,
                methodArguments),
            returnType);
    }

    bool LocalSignatureTypeMatches(
        TypeRef left,
        TypeRef right)
    {
        if (!TypeRef.ExactSignatureEquals(left, right))
            return false;

        var pending =
            new Stack<(TypeRef Left, TypeRef Right)>();
        var visited =
            new HashSet<(TypeRef Left, TypeRef Right)>(
                TypeRefPairReferenceComparer.Instance);
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            (TypeRef currentLeft, TypeRef currentRight) =
                pending.Pop();
            if (!visited.Add((currentLeft, currentRight)))
                continue;

            if (!SignatureOriginsMatch(
                    currentLeft,
                    currentRight))
            {
                return false;
            }

            if (currentLeft.ElementType is not null)
            {
                pending.Push((
                    currentLeft.ElementType,
                    currentRight.ElementType!));
            }
            if (currentLeft.ModifierType is not null)
            {
                pending.Push((
                    currentLeft.ModifierType,
                    currentRight.ModifierType!));
            }
            if (currentLeft.UnmodifiedType is not null)
            {
                pending.Push((
                    currentLeft.UnmodifiedType,
                    currentRight.UnmodifiedType!));
            }
            for (int i = 0;
                i < currentLeft.TypeArguments.Length;
                i++)
            {
                pending.Push((
                    currentLeft.TypeArguments[i],
                    currentRight.TypeArguments[i]));
            }
            if (currentLeft.FunctionPointerSignature
                    is { } leftSignature)
            {
                MethodSignature<TypeRef> rightSignature =
                    currentRight.FunctionPointerSignature!.Value;
                pending.Push((
                    leftSignature.ReturnType,
                    rightSignature.ReturnType));
                for (int i = 0;
                    i < leftSignature.ParameterTypes.Length;
                    i++)
                {
                    pending.Push((
                        leftSignature.ParameterTypes[i],
                        rightSignature.ParameterTypes[i]));
                }
            }
        }

        return true;
    }

    bool SignatureOriginsMatch(
        TypeRef left,
        TypeRef right)
    {
        TypeRef leftDefinition = Definition(left);
        TypeRef rightDefinition = Definition(right);
        if (leftDefinition.Assembly == TypeRef.CoreLibrary
            && rightDefinition.Assembly == TypeRef.CoreLibrary)
        {
            return leftDefinition.TrustedFrameworkAssembly
                && rightDefinition.TrustedFrameworkAssembly;
        }

        TypeReferenceOrigin? leftOrigin =
            leftDefinition.Resolution?.Origin;
        TypeReferenceOrigin? rightOrigin =
            rightDefinition.Resolution?.Origin;
        if (leftOrigin is null || rightOrigin is null)
            return true;

        return (leftOrigin, rightOrigin) switch
        {
            (TypeReferenceOrigin.CurrentAssembly,
                TypeReferenceOrigin.CurrentAssembly) => true,
            (TypeReferenceOrigin.CurrentAssembly,
                TypeReferenceOrigin.AssemblyReference reference) =>
                IsCurrentAssembly(reference.Assembly),
            (TypeReferenceOrigin.AssemblyReference reference,
                TypeReferenceOrigin.CurrentAssembly) =>
                IsCurrentAssembly(reference.Assembly),
            (TypeReferenceOrigin.AssemblyReference leftReference,
                TypeReferenceOrigin.AssemblyReference rightReference) =>
                leftReference.Assembly.IsEquivalentTo(
                    rightReference.Assembly),
            (TypeReferenceOrigin.IntrinsicCoreLibrary,
                TypeReferenceOrigin.IntrinsicCoreLibrary) => true,
            (TypeReferenceOrigin.ModuleReference leftModule,
                TypeReferenceOrigin.ModuleReference rightModule) =>
                leftModule.ModuleName == rightModule.ModuleName,
            _ => false,
        };
    }

    bool IsCurrentAssembly(
        AssemblyReferenceIdentity assembly)
        => _currentAssembly is { } current
            && assembly.IsEquivalentTo(current);

    static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    static string DeclaringTypeAndNameKey(
        TypeRef declaringType,
        string name)
        => $"{declaringType.Assembly}|"
            + $"{declaringType.Namespace}|"
            + $"{declaringType.Name}|{name}";

    sealed class TypeRefPairReferenceComparer
        : IEqualityComparer<(TypeRef Left, TypeRef Right)>
    {
        internal static TypeRefPairReferenceComparer Instance
            { get; } = new();

        public bool Equals(
            (TypeRef Left, TypeRef Right) x,
            (TypeRef Left, TypeRef Right) y)
            => ReferenceEquals(x.Left, y.Left)
                && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode(
            (TypeRef Left, TypeRef Right) pair)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(pair.Left),
                RuntimeHelpers.GetHashCode(pair.Right));
    }
}
