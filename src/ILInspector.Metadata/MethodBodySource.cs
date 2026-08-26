using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Metadata identity for one method operand.
/// </summary>
public sealed record MethodOperandIdentity(
    string DeclaringType,
    string Name);

/// <summary>
/// A method available from an opened metadata session.
/// </summary>
public sealed record MethodBodyMember(
    int MetadataToken,
    string DeclaringType,
    string Name,
    bool HasBody);

/// <summary>
/// Materialized metadata facts for one selected method.
/// </summary>
public sealed record MethodBodySelection(
    int MetadataToken,
    IReadOnlyList<string>? GenericParameterNames,
    bool HasBody,
    IReadOnlyList<(string Name, string? Value)> Attributes,
    MethodClassification? AsyncClassification,
    bool HasAsyncStateMachineAttribute);

/// <summary>
/// Session-bound access to method bodies and operand names without exposing
/// PE or metadata readers. Returned body data is copied and may outlive the
/// owning session; resolver operations require the owner to remain alive.
/// </summary>
public sealed class MethodBodySource : IOperandNameResolver
{
    readonly PEReader _peReader;
    readonly MetadataReader _reader;
    readonly MetadataOperandNameResolver _resolver;
    readonly Action _ensureAlive;

    internal MethodBodySource(PEReader peReader, Action ensureAlive)
    {
        _peReader = peReader;
        _reader = peReader.GetMetadataReader();
        _resolver = new MetadataOperandNameResolver(_reader);
        _ensureAlive = ensureAlive;
    }

    public ILSyntax Syntax => _resolver.Syntax;

    public IReadOnlyList<MethodBodyMember> EnumerateMethods()
    {
        _ensureAlive();
        List<MethodBodyMember> methods = [];
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(typeHandle);
            string typeName = _reader.GetFullTypeName(type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = _reader.GetMethodDefinition(methodHandle);
                methods.Add(new MethodBodyMember(
                    MetadataTokens.GetToken(methodHandle),
                    typeName,
                    _reader.GetString(method.Name),
                    method.RelativeVirtualAddress != 0));
            }
        }
        return methods;
    }

    public bool TryRead(
        int methodToken,
        out MethodBodyData? body,
        out string? error)
    {
        _ensureAlive();
        body = null;
        error = null;

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return false;
        }

        try
        {
            var method = _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return false;
            }

            var methodBody = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            body = new MethodBodyData(
                (methodBody.GetILBytes() ?? []).ToImmutableArray(),
                methodBody.ExceptionRegions.ToImmutableArray());
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            error = $"Could not decode IL for token 0x{methodToken:X}.";
            return false;
        }
    }

    public MethodBodySelection? ResolveMethod(
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly,
        int? preferredToken = null)
    {
        _ensureAlive();
        var typeHandle = FindType(typeName);
        if (typeHandle.IsNil)
            return null;

        var methodHandle = ValidateMethod(typeHandle, methodName, preferredToken)
            ?? FindMethod(typeHandle, methodName, overloadIndex, publicOnly);
        return methodHandle is { } handle ? CreateSelection(handle) : null;
    }

    /// <summary>
    /// Resolves canonical member identities in one metadata scan per declaring
    /// type. Missing and ambiguous identities are omitted.
    /// </summary>
    public IReadOnlyDictionary<string, MethodBodySelection> ResolveMethods(
        IEnumerable<MemberAnchor> anchors)
    {
        _ensureAlive();
        ArgumentNullException.ThrowIfNull(anchors);

        var resolved = new Dictionary<string, MethodBodySelection>(
            StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        var requestedByType = anchors
            .DistinctBy(anchor => anchor.CanonicalSignature)
            .GroupBy(anchor => anchor.TypeFullName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(anchor => anchor.CanonicalSignature)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            string typeName = ApiMemberIdentity.CreateTypeAnchorName(
                _reader,
                typeHandle);
            if (!requestedByType.TryGetValue(
                    typeName,
                    out HashSet<string>? requested))
            {
                continue;
            }

            foreach (var handle in _reader.GetTypeDefinition(typeHandle).GetMethods())
            {
                var method = _reader.GetMethodDefinition(handle);
                string canonicalSignature = ApiMemberIdentity.CreateMethodAnchor(
                        _reader,
                        typeHandle,
                        method)
                    .CanonicalSignature;
                if (!requested.Contains(canonicalSignature))
                    continue;

                if (!resolved.TryAdd(
                        canonicalSignature,
                        CreateSelection(handle)))
                {
                    resolved.Remove(canonicalSignature);
                    ambiguous.Add(canonicalSignature);
                }
            }
        }

        foreach (string canonicalSignature in ambiguous)
            resolved.Remove(canonicalSignature);
        return resolved;
    }

    /// <summary>
    /// Resolves source MethodDef tokens to methods with the same exact
    /// acquisition-address structural identity in a distinct metadata source.
    /// Use the nominal-identity overload when the sources may encode a
    /// corresponding type through different metadata scopes. Missing and
    /// ambiguous identities are omitted.
    /// </summary>
    public IReadOnlyDictionary<int, MethodBodySelection> ResolveCorrespondingMethods(
        IEnumerable<int> sourceMethodTokens,
        MethodBodySource target) =>
        ResolveCorrespondingMethodsCore(
            sourceMethodTokens,
            target,
            sourceNominalTypeIdentity: null,
            targetNominalTypeIdentity: null);

    /// <summary>
    /// Resolves source MethodDefs using caller-supplied identities for every
    /// named signature type. The callbacks must return equal values only for
    /// type definitions proven to correspond.
    /// </summary>
    public IReadOnlyDictionary<int, MethodBodySelection> ResolveCorrespondingMethods(
        IEnumerable<int> sourceMethodTokens,
        MethodBodySource target,
        Func<MetadataNamedTypeReference, string>
            sourceNominalTypeIdentity,
        Func<MetadataNamedTypeReference, string>
            targetNominalTypeIdentity)
    {
        ArgumentNullException.ThrowIfNull(sourceNominalTypeIdentity);
        ArgumentNullException.ThrowIfNull(targetNominalTypeIdentity);
        return ResolveCorrespondingMethodsCore(
            sourceMethodTokens,
            target,
            sourceNominalTypeIdentity,
            targetNominalTypeIdentity);
    }

    IReadOnlyDictionary<int, MethodBodySelection>
        ResolveCorrespondingMethodsCore(
            IEnumerable<int> sourceMethodTokens,
            MethodBodySource target,
            Func<MetadataNamedTypeReference, string>?
                sourceNominalTypeIdentity,
            Func<MetadataNamedTypeReference, string>?
                targetNominalTypeIdentity)
    {
        _ensureAlive();
        ArgumentNullException.ThrowIfNull(sourceMethodTokens);
        ArgumentNullException.ThrowIfNull(target);
        target._ensureAlive();

        if (target._reader.MethodDefinitions.Count
            > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
        {
            throw new BadImageFormatException(
                "The target method table exceeds the correspondence safety limit.");
        }
        if (_reader.MethodDefinitions.Count
            > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
        {
            throw new BadImageFormatException(
                "The source method table exceeds the correspondence safety limit.");
        }

        StructuralSignatureWorkBudget? sourceWorkBudget =
            sourceNominalTypeIdentity is null
                ? null
                : new StructuralSignatureWorkBudget();
        var sourceSignatures = sourceNominalTypeIdentity is null
            ? new StructuralSignatureBuilder(_reader)
            : new StructuralSignatureBuilder(
                _reader,
                sourceWorkBudget!,
                new NominalTypeIdentityAdapter(
                    sourceNominalTypeIdentity));
        var sourceFilterSignatures =
            sourceNominalTypeIdentity is null
                ? sourceSignatures
                : new StructuralSignatureBuilder(
                    _reader,
                    sourceWorkBudget!,
                    new NominalTypeIdentityAdapter(
                        MetadataNameIdentity));
        var requested = new Dictionary<StructuralMethodKey, List<int>>();
        var requestedFilterTypes = new HashSet<StructuralTypeKey>();
        foreach (int sourceToken in sourceMethodTokens.Distinct())
        {
            EntityHandle entity = MetadataTokens.EntityHandle(sourceToken);
            if (entity.Kind != HandleKind.MethodDefinition)
                continue;

            MethodDefinitionHandle sourceHandle =
                (MethodDefinitionHandle)entity;
            MethodDefinition sourceMethod;
            try
            {
                sourceMethod = _reader.GetMethodDefinition(sourceHandle);
            }
            catch (Exception ex) when (ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentOutOfRangeException)
            {
                continue;
            }

            StructuralMethodKey key =
                sourceSignatures.BuildMethodKey(sourceMethod);
            requestedFilterTypes.Add(
                sourceFilterSignatures.BuildTypeKey(
                    sourceMethod.GetDeclaringType()));
            if (!requested.TryGetValue(key, out List<int>? tokens))
            {
                tokens = [];
                requested.Add(key, tokens);
            }
            tokens.Add(sourceToken);
        }

        var sourceMatchCounts = requested.Keys
            .ToDictionary(key => key, _ => 0);
        var sourceDeclaringTypes =
            new Dictionary<TypeDefinitionHandle, bool>();
        foreach (MethodDefinitionHandle sourceHandle
            in _reader.MethodDefinitions)
        {
            MethodDefinition sourceMethod =
                _reader.GetMethodDefinition(sourceHandle);
            TypeDefinitionHandle declaringType =
                sourceMethod.GetDeclaringType();
            if (!sourceDeclaringTypes.TryGetValue(
                    declaringType,
                    out bool declaringTypeMatches))
            {
                declaringTypeMatches = requestedFilterTypes.Contains(
                    sourceFilterSignatures.BuildTypeKey(
                        declaringType));
                sourceDeclaringTypes.Add(
                    declaringType,
                    declaringTypeMatches);
            }
            if (!declaringTypeMatches)
                continue;

            StructuralMethodKey key =
                sourceSignatures.BuildMethodKey(sourceMethod);
            if (sourceMatchCounts.TryGetValue(key, out int count))
                sourceMatchCounts[key] = checked(count + 1);
        }

        var resolved = new Dictionary<int, MethodBodySelection>();
        var ambiguous = requested
            .Where(entry =>
                entry.Value.Count != 1
                || sourceMatchCounts[entry.Key] != 1)
            .SelectMany(entry => entry.Value)
            .ToHashSet();
        StructuralSignatureWorkBudget? targetWorkBudget =
            targetNominalTypeIdentity is null
                ? null
                : new StructuralSignatureWorkBudget();
        var targetSignatures = targetNominalTypeIdentity is null
            ? new StructuralSignatureBuilder(target._reader)
            : new StructuralSignatureBuilder(
                target._reader,
                targetWorkBudget!,
                new NominalTypeIdentityAdapter(
                    targetNominalTypeIdentity));
        var targetFilterSignatures =
            targetNominalTypeIdentity is null
                ? targetSignatures
                : new StructuralSignatureBuilder(
                    target._reader,
                    targetWorkBudget!,
                    new NominalTypeIdentityAdapter(
                        MetadataNameIdentity));
        var matchingDeclaringTypes =
            new Dictionary<TypeDefinitionHandle, bool>();
        foreach (MethodDefinitionHandle targetHandle
            in target._reader.MethodDefinitions)
        {
            MethodDefinition targetMethod =
                target._reader.GetMethodDefinition(targetHandle);
            TypeDefinitionHandle declaringType =
                targetMethod.GetDeclaringType();
            if (!matchingDeclaringTypes.TryGetValue(
                    declaringType,
                    out bool declaringTypeMatches))
            {
                declaringTypeMatches = requestedFilterTypes.Contains(
                    targetFilterSignatures.BuildTypeKey(
                        declaringType));
                matchingDeclaringTypes.Add(
                    declaringType,
                    declaringTypeMatches);
            }
            if (!declaringTypeMatches)
                continue;

            StructuralMethodKey key =
                targetSignatures.BuildMethodKey(targetMethod);
            if (!requested.TryGetValue(key, out List<int>? sourceTokens))
                continue;
            if (sourceTokens.Count != 1
                || sourceMatchCounts[key] != 1)
                continue;

            MethodBodySelection selection =
                target.CreateSelection(targetHandle);
            foreach (int sourceToken in sourceTokens)
            {
                if (!resolved.TryAdd(sourceToken, selection))
                {
                    resolved.Remove(sourceToken);
                    ambiguous.Add(sourceToken);
                }
            }
        }

        foreach (int sourceToken in ambiguous)
            resolved.Remove(sourceToken);
        return resolved;
    }

    static string MetadataNameIdentity(
        MetadataNamedTypeReference reference) =>
        reference.Type.ToEscapedFullName();

    sealed class NominalTypeIdentityAdapter(
        Func<MetadataNamedTypeReference, string> identity)
        : IStructuralNominalTypeIdentityProvider
    {
        public string GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle) =>
            Resolve(
                MetadataNamedTypeSignatureDecoder.DecodeTypeDefinition(
                    reader,
                    handle));

        public string GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle) =>
            Resolve(
                MetadataNamedTypeSignatureDecoder.DecodeTypeReference(
                    reader,
                    handle));

        string Resolve(MetadataNamedTypeReference? reference)
        {
            if (reference is null)
            {
                throw new BadImageFormatException(
                    "The nominal signature type could not be decoded.");
            }

            string resolved;
            try
            {
                resolved = identity(reference);
            }
            catch (StructuralNominalTypeResolutionException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                throw new StructuralNominalTypeResolutionException(
                    "The nominal signature type identity could not be "
                    + "resolved.",
                    ex);
            }
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new StructuralNominalTypeResolutionException(
                    "The nominal signature type identity is empty.");
            }
            return resolved;
        }
    }

    /// <summary>
    /// Returns the metadata-owned canonical identity of a MethodDef token in
    /// this source. The token is interpreted only within this acquisition.
    /// </summary>
    public MemberAnchor? ResolveMethodAnchor(int methodToken)
    {
        _ensureAlive();
        var entity = MetadataTokens.EntityHandle(methodToken);
        if (entity.Kind != HandleKind.MethodDefinition)
            return null;

        try
        {
            var handle = (MethodDefinitionHandle)entity;
            var method = _reader.GetMethodDefinition(handle);
            return ApiMemberIdentity.CreateMethodAnchor(
                    _reader,
                    method.GetDeclaringType(),
                    method);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public bool ContainsType(string typeName)
    {
        _ensureAlive();
        return !FindType(typeName).IsNil;
    }

    public string? ResolveUserString(int token)
    {
        _ensureAlive();
        try
        {
            return _reader.GetUserString(MetadataTokens.UserStringHandle(token));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    public MethodOperandIdentity? ResolveMethodIdentity(int token)
    {
        _ensureAlive();
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
                handle = _reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;

            return handle.Kind switch
            {
                HandleKind.MethodDefinition => ResolveMethodDefinitionIdentity(
                    (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => ResolveMemberReferenceIdentity(
                    (MemberReferenceHandle)handle),
                _ => null
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException)
        {
            return null;
        }
    }

    public string ResolveType(int token)
    {
        _ensureAlive();
        return _resolver.ResolveType(token);
    }

    public string ResolveMethod(int token)
    {
        _ensureAlive();
        return _resolver.ResolveMethod(token);
    }

    public string ResolveField(int token)
    {
        _ensureAlive();
        return _resolver.ResolveField(token);
    }

    public string ResolveString(int token)
    {
        _ensureAlive();
        return _resolver.ResolveString(token);
    }

    public string ResolveToken(int token)
    {
        _ensureAlive();
        return _resolver.ResolveToken(token);
    }

    MethodOperandIdentity ResolveMethodDefinitionIdentity(MethodDefinitionHandle handle)
    {
        var method = _reader.GetMethodDefinition(handle);
        var type = _reader.GetTypeDefinition(method.GetDeclaringType());
        return new MethodOperandIdentity(
            _reader.GetFullTypeName(type),
            _reader.GetString(method.Name));
    }

    TypeDefinitionHandle FindType(string typeName)
    {
        foreach (var handle in _reader.TypeDefinitions)
        {
            if (_reader.GetFullTypeName(_reader.GetTypeDefinition(handle)) == typeName)
                return handle;
        }
        return default;
    }

    MethodDefinitionHandle? ValidateMethod(
        TypeDefinitionHandle typeHandle,
        string methodName,
        int? token)
    {
        if (token is not { } value)
            return null;

        var entity = MetadataTokens.EntityHandle(value);
        if (entity.Kind != HandleKind.MethodDefinition)
            return null;

        var handle = (MethodDefinitionHandle)entity;
        try
        {
            var method = _reader.GetMethodDefinition(handle);
            return method.GetDeclaringType() == typeHandle
                && _reader.GetString(method.Name) == methodName
                    ? handle
                    : null;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    MethodDefinitionHandle? FindMethod(
        TypeDefinitionHandle typeHandle,
        string methodName,
        int overloadIndex,
        bool publicOnly)
    {
        int seen = 0;
        foreach (var handle in _reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            var method = _reader.GetMethodDefinition(handle);
            if (_reader.GetString(method.Name) != methodName)
                continue;
            if (publicOnly
                && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            {
                continue;
            }
            if (seen++ == overloadIndex)
                return handle;
        }
        return null;
    }

    MethodBodySelection CreateSelection(MethodDefinitionHandle handle)
    {
        var method = _reader.GetMethodDefinition(handle);
        var genericHandles = method.GetGenericParameters();
        IReadOnlyList<string>? genericNames = genericHandles.Count == 0
            ? null
            : genericHandles.Select(_reader.GetGenericParameterName).ToList();
        var classification = MethodClassificationScanner.ClassifyAsyncMethod(_reader, method);
        bool hasAsyncStateMachineAttribute = AttributeReader.HasAttribute(
            _reader,
            method.GetCustomAttributes(),
            KnownAttributeNames.AsyncStateMachineAttribute);
        return new MethodBodySelection(
            MetadataTokens.GetToken(handle),
            genericNames,
            method.RelativeVirtualAddress != 0,
            AttributeReader.GetMethodAttributes(_reader, handle),
            classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync
                ? classification
                : null,
            hasAsyncStateMachineAttribute);
    }

    MethodOperandIdentity? ResolveMemberReferenceIdentity(MemberReferenceHandle handle)
    {
        var member = _reader.GetMemberReference(handle);
        if (member.GetKind() != MemberReferenceKind.Method)
            return null;

        string? declaringType = member.Parent.Kind switch
        {
            HandleKind.TypeDefinition => _reader.GetFullTypeName(
                _reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent)),
            HandleKind.TypeReference => _reader.GetFullTypeName(
                _reader.GetTypeReference((TypeReferenceHandle)member.Parent)),
            _ => null
        };
        return declaringType is null
            ? null
            : new MethodOperandIdentity(declaringType, _reader.GetString(member.Name));
    }
}
