using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Instructions;

/// <summary>
/// An SRM-backed <see cref="IStackTypeResolver"/>: resolves local/argument types from the method
/// and local signatures, and field/element/call/newobj effects from metadata tokens. Stays inside
/// the substrate boundary — it reads the one <see cref="MetadataReader"/> it was built from and
/// never loads inspected assemblies. Cross-assembly type references coarsen to object-reference;
/// generic parameters coarsen to <see cref="StackType.Unknown"/>. This is what lets the typed
/// stack answer "what is the receiver type at this call?" for the demonstration.
/// </summary>
public sealed class MetadataStackTypeResolver : IStackTypeResolver
{
    readonly MetadataReader _reader;
    readonly StackTypeSignatureProvider _provider;
    readonly ImmutableArray<StackType> _arguments;
    readonly ImmutableArray<StackType> _locals;

    /// <summary>True when the owning method has a non-void return (so <c>ret</c> pops one value).</summary>
    public bool MethodReturnsValue { get; }

    MetadataStackTypeResolver(
        MetadataReader reader,
        StackTypeSignatureProvider provider,
        ImmutableArray<StackType> arguments,
        ImmutableArray<StackType> locals,
        bool methodReturnsValue)
    {
        _reader = reader;
        _provider = provider;
        _arguments = arguments;
        _locals = locals;
        MethodReturnsValue = methodReturnsValue;
    }

    public static MetadataStackTypeResolver Create(MetadataReader reader, MethodDefinitionHandle method, MethodBodyBlock body)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(body);
        var provider = new StackTypeSignatureProvider(reader);
        var definition = reader.GetMethodDefinition(method);
        if (!GuardedProviderDecode.TryMethod(reader, definition, provider, null, out var signature))
            throw new BadImageFormatException("Method signature exceeds the safe structural depth.");

        var arguments = ImmutableArray.CreateBuilder<StackType>();
        if (signature.Header.IsInstance && !signature.Header.HasExplicitThis)
            arguments.Add(provider.Classify(definition.GetDeclaringType()) == StackType.ValueType
                ? StackType.ManagedPointer
                : StackType.ObjectReference);
        foreach (var parameter in signature.ParameterTypes)
            arguments.Add(parameter.Stack);

        var locals = ImmutableArray<StackType>.Empty;
        if (!body.LocalSignature.IsNil)
        {
            var localSig = GuardedProviderDecode.LocalVariables(
                reader,
                reader.GetStandaloneSignature(body.LocalSignature),
                provider,
                null);
            locals = [.. localSig.Select(t => t.Stack)];
        }

        return new MetadataStackTypeResolver(reader, provider, arguments.ToImmutable(), locals, !signature.ReturnType.IsVoid);
    }

    public StackType Argument(int index) => index >= 0 && index < _arguments.Length ? _arguments[index] : StackType.Unknown;

    public StackType Local(int index) => index >= 0 && index < _locals.Length ? _locals[index] : StackType.Unknown;

    public StackType Field(int fieldToken)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(fieldToken);
            return handle.Kind switch
            {
                HandleKind.FieldDefinition => GuardedProviderDecode.Field(
                    _reader,
                    _reader.GetFieldDefinition((FieldDefinitionHandle)handle),
                    _provider,
                    null,
                    SigType.Of(StackType.Unknown)).Stack,
                HandleKind.MemberReference => GuardedProviderDecode.MemberRefField(
                    _reader,
                    _reader.GetMemberReference((MemberReferenceHandle)handle),
                    _provider,
                    null,
                    SigType.Of(StackType.Unknown)).Stack,
                _ => StackType.Unknown,
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return StackType.Unknown; // fail closed: malformed/out-of-range token coarsens to Unknown
        }
    }

    public StackType TypeOfToken(int typeToken)
    {
        try { return _provider.Classify(MetadataTokens.EntityHandle(typeToken)); }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException) { return StackType.Unknown; }
    }

    public bool TryResolveCall(int methodToken, bool isNewObj, out int popCount, out bool pushes, out StackType pushType)
    {
        popCount = -1;
        pushes = false;
        pushType = StackType.Unknown;

        try
        {
            if (!TryMethodInfo(MetadataTokens.EntityHandle(methodToken), out int paramCount, out bool hasThis, out var returnType, out var declaringType))
                return false;

            if (isNewObj)
            {
                popCount = paramCount;
                pushes = true;
                pushType = _provider.Classify(declaringType);
                return true;
            }

            popCount = paramCount + (hasThis ? 1 : 0);
            pushes = !returnType.IsVoid;
            pushType = returnType.Stack;
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            popCount = -1;
            pushes = false;
            pushType = StackType.Unknown;
            return false; // fail closed on malformed/out-of-range token
        }
    }

    public bool TryResolveCalli(int signatureToken, out int popCount, out bool pushes, out StackType pushType)
    {
        popCount = -1;
        pushes = false;
        pushType = StackType.Unknown;
        try
        {
            var handle = MetadataTokens.EntityHandle(signatureToken);
            if (handle.Kind != HandleKind.StandaloneSignature)
                return false;
            if (!GuardedProviderDecode.TryStandaloneMethod(
                _reader,
                _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle),
                _provider,
                null,
                out var signature))
            {
                return false;
            }
            popCount = signature.ParameterTypes.Length + (signature.Header.IsInstance && !signature.Header.HasExplicitThis ? 1 : 0);
            pushes = !signature.ReturnType.IsVoid;
            pushType = signature.ReturnType.Stack;
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            popCount = -1;
            pushes = false;
            pushType = StackType.Unknown;
            return false;
        }
    }

    bool TryMethodInfo(EntityHandle handle, out int paramCount, out bool hasThis, out SigType returnType, out EntityHandle declaringType)
    {
        paramCount = 0;
        hasThis = false;
        returnType = SigType.Void;
        declaringType = default;

        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var definition = _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                if (!GuardedProviderDecode.TryMethod(_reader, definition, _provider, null, out var signature))
                    return false;
                paramCount = signature.ParameterTypes.Length;
                hasThis = signature.Header.IsInstance && !signature.Header.HasExplicitThis;
                returnType = signature.ReturnType;
                declaringType = definition.GetDeclaringType();
                return true;
            }
            case HandleKind.MemberReference:
            {
                var member = _reader.GetMemberReference((MemberReferenceHandle)handle);
                if (!GuardedProviderDecode.TryMemberRefMethod(_reader, member, _provider, null, out var signature))
                    return false;
                paramCount = signature.ParameterTypes.Length;
                hasThis = signature.Header.IsInstance && !signature.Header.HasExplicitThis;
                returnType = signature.ReturnType;
                declaringType = member.Parent;
                return true;
            }
            case HandleKind.MethodSpecification:
            {
                var spec = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return TryMethodInfo(spec.Method, out paramCount, out hasThis, out returnType, out declaringType);
            }
            default:
                return false;
        }
    }
}
