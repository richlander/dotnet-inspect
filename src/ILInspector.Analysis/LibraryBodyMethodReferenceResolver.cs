using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Owns assembly-scoped method-reference identity, resolution, caching, and
/// bounded signature work for primary-image analysis.
/// </summary>
internal sealed class LibraryBodyMethodReferenceResolver
{
    readonly MetadataReader _reader;
    readonly Action<MethodDefinitionHandle, int>?
        _methodReferenceResolved;
    // These assembly-owned caches are gated by
    // OptimizationOpportunities_DuplicateMemberRefsResolveStructuralIdentityOnce and
    // OptimizationOpportunities_SharedMemberRefDecodesOnceAcrossOwnerBodies.
    readonly ConcurrentDictionary<BlobHandle, Lazy<SignatureIdentity>>
        _methodReferenceSignatures = new();
    readonly ConcurrentDictionary<SignatureIdentity, SignatureIdentity>
        _canonicalMethodReferenceSignatures =
            new(SignatureIdentityComparer.Instance);
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<MemberRef>>
        _resolvedMethodDefinitions = new();
    readonly ConcurrentDictionary<
        MemberReferenceMetadataKey,
        Lazy<MemberRef>>
        _resolvedMemberReferences = new();
    readonly ConcurrentDictionary<
        MethodSpecificationResolutionKey,
        Lazy<MemberRef>>
        _resolvedMethodSpecifications = new();
    readonly ConcurrentDictionary<
        MemberReferenceParentKey,
        Lazy<TypeRef>>
        _memberReferenceDeclaringTypes = new();
    readonly ConcurrentDictionary<
        GenericScope,
        GenericScopeIdentity>
        _genericScopeIdentities = new();
    long _methodReferenceSignatureWork;
    long _methodReferenceDecodeWork;

    internal LibraryBodyMethodReferenceResolver(
        MetadataReader reader,
        Action<MethodDefinitionHandle, int>? methodReferenceResolved)
    {
        _reader = reader;
        _methodReferenceResolved = methodReferenceResolved;
    }

    internal MethodReferenceKey CreateIdentity(
        string name,
        TypeRef declaringType,
        BlobHandle signature) =>
        new(
            name,
            new ScopeAwareTypeIdentity(declaringType),
            Signature(signature));

    internal MethodReferenceKey ResolveIdentity(
        MemberReferenceHandle handle,
        GenericScope scope,
        MethodDefinitionHandle caller)
    {
        MemberReferenceMetadataKey referenceIdentity =
            MemberReferenceIdentity(handle, scope);
        MemberRef target = ResolveMethod(handle, scope, caller);
        TypeRef targetDefinition =
            target.DeclaringType.Kind == TypeRefKind.GenericInstance
                ? target.DeclaringType.ElementType!
                : target.DeclaringType;
        return new(
            target.Name,
            new ScopeAwareTypeIdentity(targetDefinition),
            referenceIdentity.Signature);
    }

    internal MemberRef ResolveMethod(
        EntityHandle handle,
        GenericScope scope,
        MethodDefinitionHandle caller)
    {
        return handle.Kind switch
        {
            HandleKind.MethodDefinition =>
                _resolvedMethodDefinitions.GetOrAdd(
                    (MethodDefinitionHandle)handle,
                    method => new Lazy<MemberRef>(
                        () => MemberResolver.ResolveMethod(
                            _reader,
                            method,
                            scope),
                        LazyThreadSafetyMode.ExecutionAndPublication)).Value,
            HandleKind.MemberReference =>
                ResolveMemberReference(
                    (MemberReferenceHandle)handle,
                    scope,
                    caller),
            HandleKind.MethodSpecification =>
                ResolveMethodSpecification(
                    (MethodSpecificationHandle)handle,
                    scope,
                    caller),
            _ => MemberRef.Unsupported(
                $"callee handle kind {handle.Kind}"),
        };
    }

    internal static bool SameMethodReferenceDeclaringType(
        TypeRef left,
        TypeRef right) =>
        ScopeAwareTypeEquals(left, right);

    internal readonly record struct MethodReferenceKey(
        string Name,
        ScopeAwareTypeIdentity DeclaringType,
        SignatureIdentity Signature);

    internal sealed class MethodReferenceKeyComparer
        : IEqualityComparer<MethodReferenceKey>
    {
        internal static MethodReferenceKeyComparer Instance { get; } =
            new();

        public bool Equals(MethodReferenceKey x, MethodReferenceKey y)
            => x.Name == y.Name
                && x.DeclaringType.Equals(y.DeclaringType)
                && SignatureIdentityEquals(x.Signature, y.Signature);

        public int GetHashCode(MethodReferenceKey obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Name),
                obj.DeclaringType,
                obj.Signature.HashCode);
    }

    internal sealed record SignatureIdentity(
        byte[] Bytes,
        int HashCode);

    internal sealed class ScopeAwareTypeIdentity
        : IEquatable<ScopeAwareTypeIdentity>
    {
        readonly int _hashCode;

        internal ScopeAwareTypeIdentity(TypeRef type)
        {
            Type = type;
            _hashCode = ScopeAwareTypeHashCode(type);
        }

        internal TypeRef Type { get; }

        public bool Equals(ScopeAwareTypeIdentity? other) =>
            other is not null
            && ScopeAwareTypeEquals(Type, other.Type);

        public override bool Equals(object? obj) =>
            Equals(obj as ScopeAwareTypeIdentity);

        public override int GetHashCode() => _hashCode;
    }

    sealed class GenericScopeIdentity : IEquatable<GenericScopeIdentity>
    {
        readonly int _hashCode;

        internal GenericScopeIdentity(GenericScope scope)
        {
            TypeParameters = scope.TypeParameters;
            MethodParameters = scope.MethodParameters;
            var hash = new HashCode();
            foreach (string parameter in TypeParameters)
                hash.Add(parameter, StringComparer.Ordinal);
            hash.Add(TypeParameters.Length);
            foreach (string parameter in MethodParameters)
                hash.Add(parameter, StringComparer.Ordinal);
            hash.Add(MethodParameters.Length);
            _hashCode = hash.ToHashCode();
        }

        internal ImmutableArray<string> TypeParameters { get; }
        internal ImmutableArray<string> MethodParameters { get; }

        public bool Equals(GenericScopeIdentity? other) =>
            ReferenceEquals(this, other)
            || other is not null
            && _hashCode == other._hashCode
            && TypeParameters.SequenceEqual(
                other.TypeParameters,
                StringComparer.Ordinal)
            && MethodParameters.SequenceEqual(
                other.MethodParameters,
                StringComparer.Ordinal);

        public override bool Equals(object? obj) =>
            Equals(obj as GenericScopeIdentity);

        public override int GetHashCode() => _hashCode;
    }

    readonly record struct MemberReferenceMetadataKey(
        ScopeAwareTypeIdentity DeclaringType,
        string Name,
        SignatureIdentity Signature,
        GenericScopeIdentity Scope);

    readonly record struct MemberReferenceParentKey(
        EntityHandle Parent,
        GenericScopeIdentity Scope);

    readonly record struct MethodTargetIdentity(
        int MethodDefinitionToken,
        MemberReferenceMetadataKey? MemberReference);

    readonly record struct MethodSpecificationResolutionKey(
        MethodTargetIdentity Target,
        SignatureIdentity Signature,
        GenericScopeIdentity Scope);

    sealed class SignatureIdentityComparer
        : IEqualityComparer<SignatureIdentity>
    {
        internal static SignatureIdentityComparer Instance { get; } =
            new();

        public bool Equals(
            SignatureIdentity? x,
            SignatureIdentity? y) =>
            x is not null
            && y is not null
            && SignatureIdentityEquals(x, y);

        public int GetHashCode(SignatureIdentity obj) =>
            obj.HashCode;
    }

    static bool ScopeAwareTypeEquals(TypeRef left, TypeRef right)
    {
        if (!left.Equals(right)
            || !Equals(left.Resolution, right.Resolution))
        {
            return false;
        }
        if (left.ElementType is { } leftElement)
        {
            if (right.ElementType is not { } rightElement
                || !ScopeAwareTypeEquals(leftElement, rightElement))
            {
                return false;
            }
        }
        for (int i = 0; i < left.TypeArguments.Length; i++)
        {
            if (!ScopeAwareTypeEquals(
                    left.TypeArguments[i],
                    right.TypeArguments[i]))
            {
                return false;
            }
        }
        return true;
    }

    static int ScopeAwareTypeHashCode(TypeRef type)
    {
        var hash = new HashCode();
        hash.Add(type);
        hash.Add(type.Resolution);
        if (type.ElementType is { } element)
            hash.Add(ScopeAwareTypeHashCode(element));
        foreach (TypeRef argument in type.TypeArguments)
            hash.Add(ScopeAwareTypeHashCode(argument));
        return hash.ToHashCode();
    }

    static bool SignatureIdentityEquals(
        SignatureIdentity x,
        SignatureIdentity y) =>
        ReferenceEquals(x, y)
        || (x.HashCode == y.HashCode
            && x.Bytes.AsSpan().SequenceEqual(y.Bytes));

    SignatureIdentity Signature(BlobHandle handle)
        => _methodReferenceSignatures.GetOrAdd(
            handle,
            blob => new Lazy<SignatureIdentity>(
                () => BuildSignature(blob),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    SignatureIdentity BuildSignature(BlobHandle handle)
    {
        int length = _reader.GetBlobReader(handle).Length;
        ReserveMethodReferenceSignatureWork(Math.Max(length, 1));
        byte[] bytes = _reader.GetBlobBytes(handle);
        var hash = new HashCode();
        foreach (byte value in bytes)
            hash.Add(value);
        var identity = new SignatureIdentity(
            bytes,
            hash.ToHashCode());
        return _canonicalMethodReferenceSignatures.GetOrAdd(
            identity,
            identity);
    }

    MemberReferenceMetadataKey MemberReferenceIdentity(
        MemberReferenceHandle handle,
        GenericScope scope)
    {
        MemberReference reference =
            _reader.GetMemberReference(handle);
        TypeRef declaringType =
            ResolveMemberReferenceDeclaringType(
                reference.Parent,
                scope);
        return new(
            new ScopeAwareTypeIdentity(declaringType),
            _reader.GetString(reference.Name),
            Signature(reference.Signature),
            MemberReferenceScope(
                reference.Parent,
                scope));
    }

    TypeRef ResolveMemberReferenceDeclaringType(
        EntityHandle parent,
        GenericScope scope)
        => _memberReferenceDeclaringTypes.GetOrAdd(
            new(
                parent,
                MemberReferenceScope(
                    parent,
                    scope)),
            key => new Lazy<TypeRef>(
                () => key.Parent.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        TypeRefDecoder.Instance.GetTypeFromDefinition(
                            _reader,
                            (TypeDefinitionHandle)key.Parent,
                            0),
                    HandleKind.TypeReference =>
                        TypeRefDecoder.Instance.GetTypeFromReference(
                            _reader,
                            (TypeReferenceHandle)key.Parent,
                            0),
                    HandleKind.TypeSpecification =>
                        TypeRefDecoder.Instance.GetTypeFromSpecification(
                            _reader,
                            scope,
                            (TypeSpecificationHandle)key.Parent,
                            0),
                    _ => TypeRef.Unsupported(
                        $"member parent kind {key.Parent.Kind}"),
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    GenericScopeIdentity MemberReferenceScope(
        EntityHandle parent,
        GenericScope scope) =>
        ScopeIdentity(
            parent.Kind == HandleKind.TypeSpecification
                ? scope
                : GenericScope.Empty);

    GenericScopeIdentity ScopeIdentity(GenericScope scope) =>
        _genericScopeIdentities.GetOrAdd(
            scope,
            static value => new(value));

    MemberRef ResolveMemberReference(
        MemberReferenceHandle handle,
        GenericScope scope,
        MethodDefinitionHandle caller)
    {
        MemberReferenceMetadataKey identity =
            MemberReferenceIdentity(handle, scope);
        return _resolvedMemberReferences.GetOrAdd(
            identity,
            _ => new Lazy<MemberRef>(
                () =>
                {
                    ReserveMethodReferenceDecodeWork(
                        identity.Signature.Bytes.Length);
                    _methodReferenceResolved?.Invoke(
                        caller,
                        MetadataTokens.GetToken(handle));
                    return MemberResolver.ResolveMethod(
                        _reader,
                        handle,
                        scope);
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    MemberRef ResolveMethodSpecification(
        MethodSpecificationHandle handle,
        GenericScope scope,
        MethodDefinitionHandle caller)
    {
        MethodSpecification specification =
            _reader.GetMethodSpecification(handle);
        MemberRef target = ResolveMethod(
            specification.Method,
            scope,
            caller);
        MethodTargetIdentity targetIdentity =
            specification.Method.Kind switch
            {
                HandleKind.MethodDefinition => new(
                    MetadataTokens.GetToken(specification.Method),
                    null),
                HandleKind.MemberReference => new(
                    0,
                    MemberReferenceIdentity(
                        (MemberReferenceHandle)specification.Method,
                        scope)),
                _ => throw new BadImageFormatException(
                    "The MethodSpec target is not a method definition or reference."),
            };
        var key = new MethodSpecificationResolutionKey(
            targetIdentity,
            Signature(specification.Signature),
            ScopeIdentity(scope));
        return _resolvedMethodSpecifications.GetOrAdd(
            key,
            _ => new Lazy<MemberRef>(
                () =>
                {
                    ReserveMethodReferenceDecodeWork(
                        key.Signature.Bytes.Length);
                    return DecodeMethodSpecification(
                        specification,
                        target,
                        scope);
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    MemberRef DecodeMethodSpecification(
        MethodSpecification specification,
        MemberRef target,
        GenericScope scope)
    {
        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                specification.Signature,
                SignatureBlobGuard.Kind.MethodSpecification))
        {
            throw new BadImageFormatException(
                "The MethodSpec signature exceeds its structural limits.");
        }

        ImmutableArray<TypeRef> arguments =
            specification.DecodeSignature(
                TypeRefDecoder.Instance,
                scope);
        if (target.Kind == MemberKind.Unsupported
            || target.GenericArity == 0
            || arguments.Length != target.GenericArity
            || arguments.Any(argument =>
                ContainsMalformedMethodSpecificationType(
                    argument,
                    scope)))
        {
            throw new BadImageFormatException(
                "The MethodSpec signature is invalid for its target and caller scope.");
        }

        return target with
        {
            TypeArguments = arguments,
            ReturnType = target.ReturnType.Instantiate(
                [],
                arguments),
            ParameterTypes =
            [
                .. target.ParameterTypes.Select(
                    parameter => parameter.Instantiate(
                        [],
                        arguments)),
            ],
        };
    }

    void ReserveMethodReferenceSignatureWork(int charge)
        => ReserveMethodReferenceWork(
            ref _methodReferenceSignatureWork,
            charge,
            "Method-reference signature identity work exceeds the assembly budget.");

    void ReserveMethodReferenceDecodeWork(int charge)
        => ReserveMethodReferenceWork(
            ref _methodReferenceDecodeWork,
            Math.Max(charge, 1),
            "Method-reference decoding work exceeds the assembly budget.");

    static void ReserveMethodReferenceWork(
        ref long work,
        int charge,
        string failure)
    {
        while (true)
        {
            long current = Volatile.Read(
                ref work);
            if (current < 0
                || charge
                    > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars
                        - current)
            {
                Interlocked.Exchange(
                    ref work,
                    -1);
                throw new BadImageFormatException(
                    failure);
            }
            if (Interlocked.CompareExchange(
                    ref work,
                    current + charge,
                    current)
                == current)
            {
                return;
            }
        }
    }

    static bool ContainsMalformedMethodSpecificationType(
        TypeRef type,
        GenericScope scope)
    {
        if (type.Kind == TypeRefKind.GenericParameter
            && (type.GenericParameterIndex < 0
                || type.GenericParameterIndex
                    >= scope.TypeParameters.Length)
            || type.Kind == TypeRefKind.MethodGenericParameter
                && (type.GenericParameterIndex < 0
                    || type.GenericParameterIndex
                        >= scope.MethodParameters.Length))
        {
            return true;
        }
        if (type.Kind == TypeRefKind.Unsupported)
        {
            if (type.UnmodifiedType is { } unmodified)
            {
                return ContainsMalformedMethodSpecificationType(
                        unmodified,
                        scope)
                    || (type.ModifierType is { } modifier
                        && ContainsMalformedMethodSpecificationType(
                            modifier,
                            scope));
            }
            if (type.FunctionPointerSignature is { } function)
            {
                return ContainsMalformedMethodSpecificationType(
                        function.ReturnType,
                        scope)
                    || function.ParameterTypes.Any(
                        parameter =>
                            ContainsMalformedMethodSpecificationType(
                                parameter,
                                scope));
            }
            return true;
        }
        if (type.ElementType is { } element
            && ContainsMalformedMethodSpecificationType(
                element,
                scope))
        {
            return true;
        }
        return type.TypeArguments.Any(
            argument => ContainsMalformedMethodSpecificationType(
                argument,
                scope));
    }
}
