using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class MethodDefinitionMap
{
    readonly HashSet<int> _methodTokens = [];
    readonly Dictionary<string, List<MethodIdentity>> _methodsByKey =
        new(StringComparer.Ordinal);
    readonly Dictionary<string, List<MethodIdentity>> _methodsByDeclaringTypeAndName = new(StringComparer.Ordinal);
    readonly AssemblyReferenceIdentity? _currentAssembly;

    MethodDefinitionMap(ImmutableArray<MethodIdentity> methods)
    {
        foreach (var method in methods)
        {
            if (_currentAssembly is null
                && Definition(method.DeclaringType).Resolution?.Origin
                    is TypeReferenceOrigin.CurrentAssembly
                    { Assembly: { } currentAssembly })
            {
                _currentAssembly = currentAssembly;
            }

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

    public bool CouldResolveToCurrentModule(DirectCall call)
        => call.Callee.Kind != MemberKind.Unsupported
            && CanResolveToCurrentModule(call.Callee.DeclaringType);

    public int Resolve(DirectCall call)
    {
        if (_methodTokens.Contains(call.CalleeDefinitionToken))
            return call.CalleeDefinitionToken;
        if (call.Callee.Kind == MemberKind.Unsupported)
            return 0;
        if (!CanResolveToCurrentModule(
                call.Callee.DeclaringType))
        {
            return 0;
        }
        ImmutableArray<TypeRef> requiredParameters =
            call.Callee.RequiredParameterPrefix(
                call.Callee.OpenSignatureParameters);
        string key = Key(
            call.Callee.DeclaringType,
            call.Callee.Name,
            call.Callee.GenericArity,
            requiredParameters);
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

    static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    bool LocalSignatureMatches(
        MethodIdentity candidate,
        MemberRef callee)
    {
        ImmutableArray<TypeRef> requiredParameters =
            callee.RequiredParameterPrefix(
                callee.OpenSignatureParameters);
        if (!MethodShapeMatches(candidate, callee)
            || candidate.ParameterTypes.Length
                != requiredParameters.Length)
        {
            return false;
        }

        for (int i = 0; i < requiredParameters.Length; i++)
        {
            if (!LocalSignatureTypeMatches(
                    candidate.ParameterTypes[i],
                    requiredParameters[i]))
            {
                return false;
            }
        }
        return LocalSignatureTypeMatches(
            candidate.ReturnType,
            callee.OpenSignatureReturn);
    }

    bool ConstructedSignatureMatches(
        MethodIdentity candidate,
        ImmutableArray<TypeRef> typeArguments,
        MemberRef callee)
    {
        ImmutableArray<TypeRef> requiredParameters =
            callee.RequiredParameterPrefix(
                callee.ParameterTypes);
        if (!MethodShapeMatches(candidate, callee)
            || candidate.ParameterTypes.Length
                != requiredParameters.Length)
        {
            return false;
        }

        for (int i = 0; i < requiredParameters.Length; i++)
        {
            if (!LocalSignatureTypeMatches(
                    candidate.ParameterTypes[i].Instantiate(
                        typeArguments,
                        callee.TypeArguments),
                    requiredParameters[i]))
            {
                return false;
            }
        }
        TypeRef candidateReturn = candidate.ReturnType.Instantiate(
            typeArguments,
            callee.TypeArguments);
        return LocalSignatureTypeMatches(
            candidateReturn,
            callee.ReturnType);
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
        AssemblyReferenceIdentity assembly) =>
        _currentAssembly is { } current
        && assembly.IsEquivalentTo(current);

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
