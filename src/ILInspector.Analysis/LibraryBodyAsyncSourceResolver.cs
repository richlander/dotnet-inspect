using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Owns acquisition-scoped async source-to-state-machine mapping and scoped
/// evidence expansion for primary-image analysis.
/// </summary>
internal sealed class LibraryBodyAsyncSourceResolver
{
    readonly MetadataReader _reader;
    readonly AssemblyReferenceIdentity _assemblyIdentity;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver;
    readonly Func<TypeDefinitionHandle, bool>
        _isSourceGeneratedTypeOrEnclosing;
    readonly Func<
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>>
        _localTypeDefinitions;
    readonly Func<EntityHandle, TypeRef> _typeFromEntity;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _asyncStateMachineSourceMethods;
    IReadOnlySet<int>? _classicAsyncSourceMethodTokens;
    IReadOnlySet<MetadataTypeDefinitionName>?
        _ambiguousAsyncStateMachineTypes;

    internal LibraryBodyAsyncSourceResolver(
        MetadataReader reader,
        AssemblyReferenceIdentity assemblyIdentity,
        LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
        Func<TypeDefinitionHandle, bool>
            isSourceGeneratedTypeOrEnclosing,
        Func<
            IReadOnlyDictionary<
                MetadataTypeDefinitionName,
                TypeDefinitionHandle>>
            localTypeDefinitions,
        Func<EntityHandle, TypeRef> typeFromEntity)
    {
        _reader = reader;
        _assemblyIdentity = assemblyIdentity;
        _primaryMetadataResolver = primaryMetadataResolver;
        _isSourceGeneratedTypeOrEnclosing =
            isSourceGeneratedTypeOrEnclosing;
        _localTypeDefinitions = localTypeDefinitions;
        _typeFromEntity = typeFromEntity;
    }

    internal LibraryBodyAnalysisPlan ExpandEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        Dictionary<int, TypeRef>?
            typeScopeEvidenceSources =
                plan.TypeScopeEvidenceSources is null
                    ? null
                    : new Dictionary<int, TypeRef>(
                        plan.TypeScopeEvidenceSources);
        if (plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence)
            && (bodyScope is not null
                || plan.TypeScope is not null))
        {
            bool mapRequired =
                plan.TypeScope is not null
                || bodyScope is not null
                    && ScopeMayRequireStateMachineBody(
                        bodyScope);
            if (mapRequired)
            {
                var expandedScope =
                    bodyScope is null
                        ? new HashSet<int>()
                        : new HashSet<int>(bodyScope);
                typeScopeEvidenceSources ??= [];
                foreach ((
                    int moveNextToken,
                    MethodIdentity source)
                    in AsyncStateMachineSourceMethods())
                {
                    typeScopeEvidenceSources[moveNextToken] =
                        source.DeclaringType;
                    if (bodyScope?.Contains(
                            source.MetadataToken)
                        == true)
                    {
                        expandedScope.Add(moveNextToken);
                    }
                }
                if (bodyScope is not null)
                    bodyScope = expandedScope;
            }
        }
        return plan with
        {
            MethodScope = bodyScope,
            TypeScopeEvidenceSources =
                typeScopeEvidenceSources,
        };
    }

    internal bool ScopeMayRequireStateMachineBody(
        IReadOnlySet<int> bodyScope)
    {
        foreach (int token in bodyScope)
        {
            EntityHandle handle =
                MetadataTokens.EntityHandle(token);
            if (handle.Kind
                != HandleKind.MethodDefinition)
            {
                continue;
            }
            try
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)handle);
                if (MethodClassificationScanner
                        .ClassifyAsyncMethod(
                            _reader,
                            method)
                    == MethodClassification.RuntimeAsync)
                {
                    continue;
                }

                AsyncStateMachineAttributeInfo attribute =
                    AsyncStateMachineAttribute(
                        method.GetCustomAttributes());
                if (attribute.SerializedType is { } serializedType
                    && StateMachineTypeDefinitionName(
                        serializedType) is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                continue;
            }
        }
        return false;
    }

    internal MethodIdentity? ResolveSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated)
    {
        MethodClassification? classification =
            MethodClassificationScanner.ClassifyAsyncMethod(
                _reader,
                methodDefinition);
        if (classification
            == MethodClassification.RuntimeAsync)
        {
            if (!HasAnalyzableIlBody(methodDefinition))
            {
                throw new BadImageFormatException(
                    "The async source method does not have an analyzable managed IL body.");
            }
            return !typeSourceGenerated
                && !_primaryMetadataResolver.HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                && !_primaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
                        methodDefinition.GetCustomAttributes())
                && !IsBlazorRenderMethod(physicalMethod)
                    ? physicalMethod
                    : null;
        }

        AsyncStateMachineAttributeInfo stateMachineAttribute =
            AsyncStateMachineAttribute(
                methodDefinition.GetCustomAttributes());
        if (stateMachineAttribute.Rejected)
        {
            throw new BadImageFormatException(
                "The async state-machine attribute is malformed or ambiguous.");
        }
        if (stateMachineAttribute.Ignored)
            return null;

        if (methodDefinition.RelativeVirtualAddress == 0
            && (stateMachineAttribute.Present
                    && classification
                        == MethodClassification.StateMachineAsync))
        {
            throw new BadImageFormatException(
                "The async source method does not have an executable body.");
        }

        if (stateMachineAttribute.Present
            && classification
                == MethodClassification.StateMachineAsync)
        {
            if (typeSourceGenerated
                || _primaryMetadataResolver.HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                || _primaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
                        methodDefinition.GetCustomAttributes())
                || IsBlazorRenderMethod(physicalMethod))
            {
                return null;
            }

            _ = AsyncStateMachineSourceMethods();
            if (_classicAsyncSourceMethodTokens!.Contains(
                    physicalMethod.MetadataToken))
            {
                return null;
            }
            throw new BadImageFormatException(
                "The classic async source does not map to a unique valid state-machine body.");
        }

        if (physicalMethod.DeclaringType.Resolution?.Type
                is not { } stateMachineType)
        {
            return null;
        }
        EntityHandle physicalHandle =
            MetadataTokens.EntityHandle(
                physicalMethod.MetadataToken);
        if (physicalHandle.Kind
                != HandleKind.MethodDefinition
            || !ImplementsAsyncStateMachine(
                _reader.GetTypeDefinition(
                    methodDefinition.GetDeclaringType()))
            || !IsMoveNextBody(
                (MethodDefinitionHandle)physicalHandle))
        {
            return null;
        }

        IReadOnlyDictionary<
            int,
            MethodIdentity> sources =
                AsyncStateMachineSourceMethods();
        if (sources.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? source))
        {
            return source;
        }
        if (_ambiguousAsyncStateMachineTypes?.Contains(
                stateMachineType) == true)
        {
            throw new BadImageFormatException(
                "Multiple async source methods name this state-machine type.");
        }
        return null;
    }

    internal void Prewarm()
    {
        _ = AsyncStateMachineSourceMethods();
        _ = _localTypeDefinitions();
    }

    IReadOnlyDictionary<
        int,
        MethodIdentity> AsyncStateMachineSourceMethods()
    {
        if (_asyncStateMachineSourceMethods is not null)
            return _asyncStateMachineSourceMethods;

        var methodsByType = new Dictionary<
            MetadataTypeDefinitionName,
            MethodIdentity>();
        var ambiguous = new HashSet<MetadataTypeDefinitionName>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition;
            try
            {
                typeDefinition =
                    _reader.GetTypeDefinition(typeHandle);
                if (_isSourceGeneratedTypeOrEnclosing(typeHandle))
                    continue;
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                continue;
            }

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                try
                {
                    var methodDefinition =
                        _reader.GetMethodDefinition(methodHandle);
                    if (_primaryMetadataResolver
                            .HasGeneratedCodeAttribute(
                                methodDefinition.GetCustomAttributes())
                        || _primaryMetadataResolver
                            .HasCompilerGeneratedAttribute(
                                methodDefinition.GetCustomAttributes()))
                    {
                        continue;
                    }

                    AsyncStateMachineAttributeInfo attribute =
                        AsyncStateMachineAttribute(
                            methodDefinition.GetCustomAttributes());
                    if (attribute.Rejected
                        || MethodClassificationScanner
                            .ClassifyAsyncMethod(
                                _reader,
                                methodDefinition)
                            == MethodClassification.RuntimeAsync
                        || methodDefinition.RelativeVirtualAddress
                            == 0
                        || attribute.SerializedType is not
                            { } serializedType
                        || StateMachineTypeDefinitionName(serializedType)
                            is not { } stateMachineType
                        || ambiguous.Contains(stateMachineType))
                    {
                        continue;
                    }

                    var scope =
                        _primaryMetadataResolver.CreateScope(
                            typeDefinition,
                            methodDefinition);
                    MethodIdentity method =
                        _primaryMetadataResolver.CreateMethodIdentity(
                            typeHandle,
                            methodHandle,
                            methodDefinition,
                            scope);
                    if (IsBlazorRenderMethod(method))
                        continue;

                    if (!methodsByType.TryAdd(
                            stateMachineType,
                            method))
                    {
                        methodsByType.Remove(stateMachineType);
                        ambiguous.Add(stateMachineType);
                    }
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    // The normal per-method pass retains the malformed method's
                    // diagnostic; source-map prewarming must not abort the index.
                }
            }
        }

        var methods = new Dictionary<
            int,
            MethodIdentity>();
        foreach ((
            MetadataTypeDefinitionName stateMachineType,
            MethodIdentity source) in methodsByType)
        {
            try
            {
                if (!_localTypeDefinitions().TryGetValue(
                        stateMachineType,
                        out TypeDefinitionHandle typeHandle)
                    || typeHandle.IsNil
                    || !TryGetAsyncStateMachineMoveNext(
                        typeHandle,
                        out MethodDefinitionHandle moveNext))
                {
                    ambiguous.Add(stateMachineType);
                    continue;
                }

                if (!methods.TryAdd(
                        MetadataTokens.GetToken(moveNext),
                        source))
                {
                    ambiguous.Add(stateMachineType);
                    methods.Remove(
                        MetadataTokens.GetToken(moveNext));
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                ambiguous.Add(stateMachineType);
            }
        }

        _ambiguousAsyncStateMachineTypes = ambiguous;
        _classicAsyncSourceMethodTokens =
            new HashSet<int>(
                methods.Values.Select(
                    source => source.MetadataToken));
        _asyncStateMachineSourceMethods = methods;
        return methods;
    }

    bool TryGetAsyncStateMachineMoveNext(
        TypeDefinitionHandle typeHandle,
        out MethodDefinitionHandle moveNext)
    {
        moveNext = default;
        var type = _reader.GetTypeDefinition(typeHandle);
        if (!ImplementsAsyncStateMachine(type))
            return false;

        foreach (var handle
            in type.GetMethodImplementations())
        {
            var implementation =
                _reader.GetMethodImplementation(handle);
            MemberRef declaration =
                MemberResolver.ResolveMethod(
                    _reader,
                    implementation.MethodDeclaration,
                    GenericScope.Empty);
            if (!IsAsyncStateMachineMoveNextDeclaration(
                    declaration))
            {
                continue;
            }
            if (!moveNext.IsNil
                || implementation.MethodBody.Kind
                    != HandleKind.MethodDefinition)
            {
                return false;
            }

            var body = (MethodDefinitionHandle)
                implementation.MethodBody;
            MethodDefinition bodyDefinition =
                _reader.GetMethodDefinition(body);
            if (bodyDefinition.GetDeclaringType()
                    != typeHandle
                || !HasAnalyzableIlBody(bodyDefinition)
                || !IsMoveNextBody(body))
            {
                return false;
            }
            moveNext = body;
        }
        if (!moveNext.IsNil)
            return true;

        foreach (var handle in type.GetMethods())
        {
            if (!HasAnalyzableIlBody(
                    _reader.GetMethodDefinition(handle))
                || !IsMoveNextBody(handle)
                || !_reader.StringComparer.Equals(
                    _reader.GetMethodDefinition(handle).Name,
                    "MoveNext"))
            {
                continue;
            }
            if (!moveNext.IsNil)
                return false;
            moveNext = handle;
        }
        return !moveNext.IsNil;
    }

    static bool HasAnalyzableIlBody(
        MethodDefinition method)
        => method.RelativeVirtualAddress != 0
            && (method.Attributes
                    & MethodAttributes.PinvokeImpl) == 0
            && (method.ImplAttributes
                    & (MethodImplAttributes.CodeTypeMask
                        | MethodImplAttributes.ManagedMask
                        | MethodImplAttributes.InternalCall))
                == MethodImplAttributes.IL;

    bool ImplementsAsyncStateMachine(
        TypeDefinition type)
    {
        foreach (var handle
            in type.GetInterfaceImplementations())
        {
            TypeRef interfaceType = _typeFromEntity(
                _reader.GetInterfaceImplementation(
                    handle).Interface);
            if (FrameworkIdentity.IsKnownFrameworkType(
                    DefinitionType(interfaceType),
                    "System.Threading.Tasks",
                    "System.Runtime.CompilerServices",
                    "IAsyncStateMachine"))
            {
                return true;
            }
        }
        return false;
    }

    bool IsMoveNextBody(
        MethodDefinitionHandle handle)
    {
        MemberRef method = MemberResolver.ResolveMethod(
            _reader,
            handle,
            GenericScope.Empty);
        return method.HasThis
            && method.GenericArity == 0
            && method.ParameterTypes.Length == 0
            && method.SignatureHeader == 0x20
            && method.RequiredParameterCount == 0
            && FrameworkIdentity.IsCoreLibraryType(
                method.ReturnType,
                "System",
                "Void");
    }

    static bool IsAsyncStateMachineMoveNextDeclaration(
        MemberRef declaration)
        => declaration.Name == "MoveNext"
            && declaration.HasThis
            && declaration.GenericArity == 0
            && declaration.ParameterTypes.Length == 0
            && declaration.SignatureHeader == 0x20
            && declaration.RequiredParameterCount == 0
            && FrameworkIdentity.IsKnownFrameworkType(
                DefinitionType(
                    declaration.DeclaringType),
                "System.Threading.Tasks",
                "System.Runtime.CompilerServices",
                "IAsyncStateMachine")
            && FrameworkIdentity.IsCoreLibraryType(
                declaration.ReturnType,
                "System",
                "Void");

    AsyncStateMachineAttributeInfo AsyncStateMachineAttribute(
        CustomAttributeHandleCollection attributes)
    {
        bool sawAttribute = false;
        string? serializedType = null;
        foreach (var handle in attributes)
        {
            var attribute = _reader.GetCustomAttribute(handle);
            string? name = AttributeDecoder.GetAttributeTypeName(
                _reader,
                attribute.Constructor);
            if (name is not (
                    KnownAttributeNames.AsyncStateMachineAttribute
                    or KnownAttributeNames.AsyncIteratorStateMachineAttribute))
            {
                continue;
            }
            if (!TryGetTrustedAsyncStateMachineAttribute(
                    _reader,
                    attribute.Constructor,
                    name,
                    out MemberRef constructor))
            {
                continue;
            }
            if (!HasAsyncStateMachineConstructorShape(
                    constructor))
            {
                return new(
                    Present: true,
                    Rejected: true,
                    Ignored: false,
                    SerializedType: null);
            }

            if (sawAttribute)
            {
                return new(
                    Present: true,
                    Rejected: true,
                    Ignored: false,
                    SerializedType: null);
            }
            sawAttribute = true;

            if (TryReadSerializedStateMachineType(
                    attribute,
                    out string? typeName))
            {
                if (IsCurrentAssemblyStateMachineType(typeName))
                    serializedType = typeName;
                continue;
            }

            return new(
                Present: true,
                Rejected: true,
                Ignored: false,
                SerializedType: null);
        }
        return new(
            Present: sawAttribute,
            Rejected: false,
            Ignored: sawAttribute
                && serializedType is null,
            serializedType);
    }

    internal static bool IsTrustedAsyncStateMachineAttribute(
        MetadataReader reader,
        EntityHandle constructor,
        string attributeName)
        => TryGetTrustedAsyncStateMachineAttribute(
                reader,
                constructor,
                attributeName,
                out MemberRef member)
            && HasAsyncStateMachineConstructorShape(member);

    static bool TryGetTrustedAsyncStateMachineAttribute(
        MetadataReader reader,
        EntityHandle constructor,
        string attributeName,
        out MemberRef member)
    {
        member = MemberResolver.ResolveMethod(
            reader,
            constructor,
            GenericScope.Empty);
        int separator = attributeName.LastIndexOf('.');
        string ns = separator < 0
            ? ""
            : attributeName[..separator];
        string name = separator < 0
            ? attributeName
            : attributeName[(separator + 1)..];
        return FrameworkIdentity.IsCoreLibraryType(
                DefinitionType(member.DeclaringType),
                ns,
                name);
    }

    static bool HasAsyncStateMachineConstructorShape(
        MemberRef member)
        => member.Name == ".ctor"
            && member.Kind == MemberKind.Constructor
            && member.HasThis
            && member.GenericArity == 0
            && member.SignatureHeader == 0x20
            && member.RequiredParameterCount == 1
            && member.ParameterTypes.Length == 1
            && FrameworkIdentity.IsCoreLibraryType(
                member.ParameterTypes[0],
                "System",
                "Type")
            && FrameworkIdentity.IsCoreLibraryType(
                member.ReturnType,
                "System",
                "Void");

    bool TryReadSerializedStateMachineType(
        CustomAttribute attribute,
        [NotNullWhen(true)] out string? serializedType)
    {
        serializedType = null;
        try
        {
            BlobReader value = _reader.GetBlobReader(
                attribute.Value);
            if (value.ReadUInt16() != 0x0001)
                return false;
            serializedType = value.ReadSerializedString();
            return serializedType is not null
                && value.ReadUInt16() == 0
                && value.RemainingBytes == 0;
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            serializedType = null;
            return false;
        }
    }

    bool IsCurrentAssemblyStateMachineType(
        string serializedType)
    {
        int separator = serializedType.IndexOf(',');
        if (separator < 0)
            return true;

        string[] assemblyParts =
            serializedType[(separator + 1)..].Split(',');
        if (assemblyParts.Length == 0
            || !string.Equals(
                assemblyParts[0].Trim(),
                _assemblyIdentity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string part in assemblyParts.Skip(1))
        {
            int equals = part.IndexOf('=');
            if (equals < 0)
                return false;
            string key = part[..equals].Trim();
            string value = part[(equals + 1)..].Trim();
            if (key.Equals(
                    "Version",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!Version.TryParse(
                        value,
                        out Version? version)
                    || version != _assemblyIdentity.Version)
                {
                    return false;
                }
            }
            else if (key.Equals(
                    "Culture",
                    StringComparison.OrdinalIgnoreCase))
            {
                string? culture = value.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                        ? null
                        : value;
                if (!string.Equals(
                        culture,
                        _assemblyIdentity.Culture,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (key.Equals(
                    "PublicKeyToken",
                    StringComparison.OrdinalIgnoreCase))
            {
                string? token = value.Equals(
                    "null",
                    StringComparison.OrdinalIgnoreCase)
                        ? null
                        : value;
                if (!string.Equals(
                        token,
                        _assemblyIdentity.PublicKeyToken,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        return true;
    }

    static MetadataTypeDefinitionName?
        StateMachineTypeDefinitionName(string serializedName)
    {
        int assemblySeparator = serializedName.IndexOf(',');
        ReadOnlySpan<char> typeName = (
            assemblySeparator < 0
                ? serializedName.AsSpan()
                : serializedName.AsSpan(0, assemblySeparator)).Trim();
        if (typeName.IsEmpty || typeName.IndexOf('[') >= 0)
            return null;
        int nestedSeparator = typeName.IndexOf('+');
        int rootEnd = nestedSeparator < 0
            ? typeName.Length
            : nestedSeparator;
        int namespaceEnd = typeName[..rootEnd].LastIndexOf('.');
        string ns = namespaceEnd < 0
            ? ""
            : typeName[..namespaceEnd].ToString();
        string segments = typeName[(namespaceEnd + 1)..].ToString();
        return MetadataTypeDefinitionName.Create(
            ns,
            [.. segments.Split('+')])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : null;
    }

    static bool IsBlazorRenderMethod(MethodIdentity method) =>
        LibraryMethodAnalysisRunner.IsBlazorRenderMethod(method);

    static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    static bool IsRecoverableMethodFailure(Exception exception) =>
        LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(
            exception);

    readonly record struct AsyncStateMachineAttributeInfo(
        bool Present,
        bool Rejected,
        bool Ignored,
        string? SerializedType);
}
