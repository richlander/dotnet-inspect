using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
public sealed partial class MethodBodySource : IOperandNameResolver
{
    readonly PEReader _peReader;
    readonly MetadataReader _reader;
    readonly MetadataOperandNameResolver _resolver;
    readonly Action _ensureAlive;

    internal MethodBodySource(PEReader peReader, Action ensureAlive)
        : this(
            peReader,
            MetadataFormatAdmission.GetMetadataReader(peReader),
            ensureAlive)
    {
    }

    internal MethodBodySource(
        PEReader peReader,
        MetadataReader reader,
        Action ensureAlive)
    {
        _peReader = peReader;
        _reader = reader;
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

    /// <summary>
    /// Methods declared on the same TypeDef as one MethodDef and sharing its
    /// exact name, including that method, in metadata order and regardless
    /// of accessibility.
    /// </summary>
    public IReadOnlyList<MethodBodyMember> EnumerateSameNameMethods(
        int methodDefinitionToken)
    {
        _ensureAlive();
        EntityHandle handle =
            MetadataTokens.EntityHandle(methodDefinitionToken);
        if (handle.Kind != HandleKind.MethodDefinition
            || MetadataTokens.GetRowNumber(handle) is var row
                && (row < 1 || row > _reader.MethodDefinitions.Count))
        {
            throw new ArgumentOutOfRangeException(
                nameof(methodDefinitionToken),
                $"Token 0x{methodDefinitionToken:X8} is not a MethodDef in "
                    + "this image.");
        }

        var selected =
            _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
        var type = _reader.GetTypeDefinition(selected.GetDeclaringType());
        string typeName = _reader.GetFullTypeName(type);
        string name = _reader.GetString(selected.Name);
        List<MethodBodyMember> methods = [];
        foreach (var methodHandle in type.GetMethods())
        {
            var method = _reader.GetMethodDefinition(methodHandle);
            if (!_reader.StringComparer.Equals(method.Name, name))
                continue;
            methods.Add(new MethodBodyMember(
                MetadataTokens.GetToken(methodHandle),
                typeName,
                name,
                method.RelativeVirtualAddress != 0));
        }
        return methods;
    }

    public bool TryRead(
        int methodToken,
        out MethodBodyData? body,
        out string? error)
    {
        body = null;
        error = null;

        MethodBodyReadResult result = Read(methodToken);
        if (result is MethodBodyReadResult.Available available)
        {
            body = available.Body;
            return true;
        }

        error = result switch
        {
            MethodBodyReadResult.NoBody =>
                $"Method token 0x{methodToken:X} has no IL body.",
            MethodBodyReadResult.Unavailable
            {
                Reason: MethodBodyUnavailableReason.NotMethodDefinitionToken
            } => $"Token 0x{methodToken:X} is not a MethodDef token.",
            _ => $"Could not decode IL for token 0x{methodToken:X}.",
        };
        return false;
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

    public MethodBodySelection? ResolveUniqueMethod(
        string typeName,
        string methodName,
        bool publicOnly)
    {
        _ensureAlive();
        var typeHandle = FindType(typeName);
        if (typeHandle.IsNil)
            return null;

        MethodDefinitionHandle? methodHandle =
            FindUniqueMethod(typeHandle, methodName, publicOnly);
        return methodHandle is { } handle ? CreateSelection(handle) : null;
    }

    public MethodBodySelection? ResolveAccessorMethod(
        string typeName,
        string memberName,
        int accessorIndex,
        bool publicOnly)
    {
        _ensureAlive();
        var typeHandle = FindType(typeName);
        if (typeHandle.IsNil)
            return null;

        MethodDefinitionHandle? methodHandle =
            FindAccessorMethod(
                typeHandle,
                memberName,
                accessorIndex,
                publicOnly);
        return methodHandle is { } handle ? CreateSelection(handle) : null;
    }

    /// <summary>
    /// Extracts the declarations of one Type without decoding any other Type.
    /// Members are exactly those a complete declaration walk yields for that
    /// Type at the same scope; receiver-contextual extension members declared
    /// on other Types are not projected (see <see cref="DeclaresExtensionMethod"/>).
    /// </summary>
    /// <returns>The Type, or null when no Type has that full name or the
    /// scope excludes it.</returns>
    public ApiType? ExtractDeclaredType(string typeName, bool includeAll)
    {
        _ensureAlive();
        TypeDefinitionHandle typeHandle = FindType(typeName);
        if (typeHandle.IsNil)
            return null;

        int token = MetadataTokens.GetToken(typeHandle);
        ApiSurface surface = ApiSurfaceExtractor.ExtractDeclarations(
            _peReader,
            includeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.Public,
            handle => handle == typeHandle);
        return surface.Types.FirstOrDefault(
            type => type.MetadataToken == token);
    }

    /// <summary>
    /// Reports whether any static method in this image whose name matches
    /// <paramref name="methodName"/> under <see cref="TypeMatcher.MatchesMemberName"/>
    /// carries <c>[Extension]</c>. A complete API
    /// surface projects such a method onto its receiver Type, so a caller that
    /// selects members from <see cref="ExtractDeclaredType"/> must use the
    /// complete surface when this returns true.
    /// </summary>
    public bool DeclaresExtensionMethod(string methodName)
    {
        _ensureAlive();
        foreach (MethodDefinitionHandle handle in _reader.MethodDefinitions)
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.Static) == 0
                || !TypeMatcher.MatchesMemberName(
                    _reader.GetString(method.Name),
                    methodName))
            {
                continue;
            }
            if (AttributeReader.HasExtensionAttribute(
                    _reader,
                    method.GetCustomAttributes()))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the TypeDef token of the Type whose full metadata name is
    /// <paramref name="typeName"/> when <see cref="TypeMatcher.Matches"/>
    /// admits no other Type in the image for that name; otherwise null.
    /// </summary>
    /// <remarks>
    /// Surface Type lookup takes the first <see cref="TypeMatcher.Lookup"/>
    /// match in surface order, which a case-variant or dotted-suffix name such
    /// as <c>A.Outer.Widget</c> can win over <c>Outer.Widget</c>. A unique
    /// candidate is the Type that lookup selects whenever it is in scope.
    /// Nested exported Types carry no declaring-Type name here, so any
    /// exported Type with the same simple name also rejects the token.
    /// </remarks>
    public int? FindUniqueLookupTypeToken(string typeName)
    {
        _ensureAlive();
        if (TypeMatcher.IsTypeGlobPattern(typeName))
            return null;

        TypeDefinitionHandle selected = default;
        foreach (var handle in _reader.TypeDefinitions)
        {
            string name = _reader.GetFullTypeName(_reader.GetTypeDefinition(handle));
            if (selected.IsNil && name == typeName)
                selected = handle;
            else if (TypeMatcher.Matches(name, typeName))
                return null;
        }

        if (selected.IsNil)
            return null;

        string simpleName = TypeMatcher.GetBaseName(
            TypeMatcher.GetSimpleName(typeName));
        foreach (var handle in _reader.ExportedTypes)
        {
            var exported = _reader.GetExportedType(handle);
            if (TypeMatcher.GetBaseName(_reader.GetString(exported.Name))
                    .Equals(simpleName, StringComparison.OrdinalIgnoreCase)
                || TypeMatcher.Matches(_reader.GetFullTypeName(exported), typeName))
                return null;
        }

        return MetadataTokens.GetToken(selected);
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

    MethodDefinitionHandle? FindUniqueMethod(
        TypeDefinitionHandle typeHandle,
        string methodName,
        bool publicOnly)
    {
        MethodDefinitionHandle match = default;
        foreach (var handle in _reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            var method = _reader.GetMethodDefinition(handle);
            if (!TypeMatcher.MatchesMemberName(
                    _reader.GetString(method.Name),
                    methodName))
                continue;
            if (publicOnly
                && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            {
                continue;
            }
            if (!match.IsNil)
                return null;

            match = handle;
        }
        var type = _reader.GetTypeDefinition(typeHandle);
        foreach (var handle in type.GetProperties())
        {
            var property = _reader.GetPropertyDefinition(handle);
            if (TypeMatcher.MatchesMemberName(
                    _reader.GetString(property.Name),
                    methodName))
            {
                return null;
            }
        }
        foreach (var handle in type.GetEvents())
        {
            var @event = _reader.GetEventDefinition(handle);
            if (TypeMatcher.MatchesMemberName(
                    _reader.GetString(@event.Name),
                    methodName))
            {
                return null;
            }
        }
        foreach (var handle in type.GetFields())
        {
            var field = _reader.GetFieldDefinition(handle);
            if (TypeMatcher.MatchesMemberName(
                    _reader.GetString(field.Name),
                    methodName))
            {
                return null;
            }
        }
        return match.IsNil ? null : match;
    }

    MethodDefinitionHandle? FindAccessorMethod(
        TypeDefinitionHandle typeHandle,
        string memberName,
        int accessorIndex,
        bool publicOnly)
    {
        if (accessorIndex < 0)
            return null;

        MethodDefinitionHandle match = default;
        bool found = false;
        var type = _reader.GetTypeDefinition(typeHandle);
        foreach (var handle in type.GetMethods())
        {
            var method = _reader.GetMethodDefinition(handle);
            if (TypeMatcher.MatchesMemberName(
                    _reader.GetString(method.Name),
                    memberName))
            {
                return null;
            }
        }
        foreach (var handle in type.GetFields())
        {
            var field = _reader.GetFieldDefinition(handle);
            if (TypeMatcher.MatchesMemberName(
                    _reader.GetString(field.Name),
                    memberName))
            {
                return null;
            }
        }
        foreach (var handle in type.GetProperties())
        {
            var property = _reader.GetPropertyDefinition(handle);
            if (!TypeMatcher.MatchesMemberName(
                    _reader.GetString(property.Name),
                    memberName))
                continue;
            if (found)
                return null;

            found = true;
            PropertyAccessors accessors = property.GetAccessors();
            match = SelectPresentAccessor(
                accessors.Getter,
                accessors.Setter,
                accessorIndex);
        }
        foreach (var handle in type.GetEvents())
        {
            var @event = _reader.GetEventDefinition(handle);
            if (!TypeMatcher.MatchesMemberName(
                    _reader.GetString(@event.Name),
                    memberName))
                continue;
            if (found)
                return null;

            found = true;
            EventAccessors accessors = @event.GetAccessors();
            match = SelectPresentAccessor(
                accessors.Adder,
                accessors.Remover,
                accessorIndex);
        }

        if (match.IsNil)
            return null;
        var selectedMethod = _reader.GetMethodDefinition(match);
        return !publicOnly
            || (selectedMethod.Attributes & MethodAttributes.MemberAccessMask)
                == MethodAttributes.Public
                ? match
                : null;
    }

    static MethodDefinitionHandle SelectPresentAccessor(
        MethodDefinitionHandle first,
        MethodDefinitionHandle second,
        int accessorIndex)
    {
        if (!first.IsNil)
        {
            if (accessorIndex == 0)
                return first;
            accessorIndex--;
        }
        return accessorIndex == 0 ? second : default;
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
