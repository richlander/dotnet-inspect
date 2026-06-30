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
        var signature = definition.DecodeSignature(provider, null);

        var arguments = ImmutableArray.CreateBuilder<StackType>();
        if (signature.Header.IsInstance)
            arguments.Add(provider.Classify(definition.GetDeclaringType()) == StackType.ValueType
                ? StackType.ManagedPointer
                : StackType.ObjectReference);
        foreach (var parameter in signature.ParameterTypes)
            arguments.Add(parameter.Stack);

        var locals = ImmutableArray<StackType>.Empty;
        if (!body.LocalSignature.IsNil)
        {
            var localSig = reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(provider, null);
            locals = [.. localSig.Select(t => t.Stack)];
        }

        return new MetadataStackTypeResolver(reader, provider, arguments.ToImmutable(), locals, !signature.ReturnType.IsVoid);
    }

    public StackType Argument(int index) => index >= 0 && index < _arguments.Length ? _arguments[index] : StackType.Unknown;

    public StackType Local(int index) => index >= 0 && index < _locals.Length ? _locals[index] : StackType.Unknown;

    public StackType Field(int fieldToken)
    {
        var handle = MetadataTokens.EntityHandle(fieldToken);
        return handle.Kind switch
        {
            HandleKind.FieldDefinition => _reader.GetFieldDefinition((FieldDefinitionHandle)handle).DecodeSignature(_provider, null).Stack,
            HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).DecodeFieldSignature(_provider, null).Stack,
            _ => StackType.Unknown,
        };
    }

    public StackType TypeOfToken(int typeToken) => _provider.Classify(MetadataTokens.EntityHandle(typeToken));

    public bool TryResolveCall(int methodToken, bool isNewObj, out int popCount, out bool pushes, out StackType pushType)
    {
        popCount = -1;
        pushes = false;
        pushType = StackType.Unknown;

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

    public bool TryResolveCalli(int signatureToken, out int popCount, out bool pushes, out StackType pushType)
    {
        popCount = -1;
        pushes = false;
        pushType = StackType.Unknown;
        var handle = MetadataTokens.EntityHandle(signatureToken);
        if (handle.Kind != HandleKind.StandaloneSignature)
            return false;
        var signature = _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle).DecodeMethodSignature(_provider, null);
        popCount = signature.ParameterTypes.Length + (signature.Header.IsInstance ? 1 : 0);
        pushes = !signature.ReturnType.IsVoid;
        pushType = signature.ReturnType.Stack;
        return true;
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
                var signature = definition.DecodeSignature(_provider, null);
                paramCount = signature.ParameterTypes.Length;
                hasThis = signature.Header.IsInstance;
                returnType = signature.ReturnType;
                declaringType = definition.GetDeclaringType();
                return true;
            }
            case HandleKind.MemberReference:
            {
                var member = _reader.GetMemberReference((MemberReferenceHandle)handle);
                var signature = member.DecodeMethodSignature(_provider, null);
                paramCount = signature.ParameterTypes.Length;
                hasThis = signature.Header.IsInstance;
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
