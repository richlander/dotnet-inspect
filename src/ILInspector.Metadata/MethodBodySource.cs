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
