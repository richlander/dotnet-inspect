using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Decompiler.Pipeline;

internal readonly record struct ParameterRefKindResult(
    ImmutableArray<ArgumentRefKind> Kinds,
    ParameterRefKindFacts State);

internal static class MethodDefinitionFacts
{
    internal static ParameterRefKindResult ReadParameterRefKinds(
        MetadataReader reader,
        MethodDefinition method,
        ImmutableArray<TypeRef> parameterTypes)
    {
        bool anyByRef = false;
        foreach (var p in parameterTypes)
            if (p.Kind == TypeRefKind.ByRef) { anyByRef = true; break; }
        if (!anyByRef)
            return new ParameterRefKindResult([], ParameterRefKindFacts.NotRequired);

        var kinds = new ArgumentRefKind[parameterTypes.Length];
        for (int i = 0; i < kinds.Length; i++)
            kinds[i] = parameterTypes[i].Kind == TypeRefKind.ByRef ? ArgumentRefKind.Ref : ArgumentRefKind.Value;
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            int index = parameter.SequenceNumber - 1;  // sequence 0 is the return parameter
            if (index < 0 || index >= kinds.Length || parameterTypes[index].Kind != TypeRefKind.ByRef)
                continue;
            kinds[index] = ClassifyByRefParameter(reader, parameter);
        }
        return new ParameterRefKindResult(ImmutableArray.Create(kinds), ParameterRefKindFacts.Known);
    }

    internal static bool HasRequiresUnsafeAttribute(MetadataReader reader, MethodDefinition method)
        => HasAttribute(reader, method.GetCustomAttributes(), "System.Diagnostics.CodeAnalysis", "RequiresUnsafeAttribute");

    internal static bool HasRequiresUnsafeAttribute(MetadataReader reader, TypeDefinition type)
        => HasAttribute(reader, type.GetCustomAttributes(), "System.Diagnostics.CodeAnalysis", "RequiresUnsafeAttribute");

    internal static bool HasCompilerGeneratedAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => HasAttribute(reader, attributes, "System.Runtime.CompilerServices", "CompilerGeneratedAttribute");

    internal static bool HasInlineArrayAttribute(MetadataReader reader, TypeDefinition type)
        => HasAttribute(reader, type.GetCustomAttributes(), "System.Runtime.CompilerServices", "InlineArrayAttribute");

    internal static bool HasUnionAttribute(MetadataReader reader, TypeDefinition type)
        => HasAttribute(reader, type.GetCustomAttributes(), "System.Runtime.CompilerServices", "UnionAttribute");

    // The compiler stamps ExtensionAttribute on an extension method (and on its
    // declaring class and module). The method-level mark is the precise signal.
    internal static bool HasExtensionAttribute(MetadataReader reader, MethodDefinition method)
        => HasAttribute(reader, method.GetCustomAttributes(), "System.Runtime.CompilerServices", "ExtensionAttribute");

    internal static bool IsPInvoke(MethodDefinition method)
        => (method.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0;

    internal static bool IsRuntimeAsync(MethodDefinition method)
    {
        const System.Reflection.MethodImplAttributes AsyncImplFlag = (System.Reflection.MethodImplAttributes)0x2000;
        return (method.ImplAttributes & AsyncImplFlag) != 0;
    }

    // A method marked [UnmanagedCallersOnly] is addressable only as an unmanaged
    // function pointer (with its declared calling convention); a normal managed
    // method is addressable only as a managed delegate*. The presence flag lets
    // the calli-spellability gate reject a convention-class mismatch when casting
    // an &Method address (CS8757).
    internal static bool IsUnmanagedCallersOnly(MetadataReader reader, MethodDefinition method)
        => HasAttribute(reader, method.GetCustomAttributes(), "System.Runtime.InteropServices", "UnmanagedCallersOnlyAttribute");

    internal static bool IsOperator(MethodDefinition method, string methodName, bool hasThis)
        => !hasThis
            && methodName.StartsWith("op_", StringComparison.Ordinal)
            && (method.Attributes & System.Reflection.MethodAttributes.SpecialName) != 0;

    internal static AccessorKind ReadAccessorKind(MetadataReader reader, TypeDefinition declaringType, MethodDefinitionHandle method)
    {
        foreach (var propertyHandle in declaringType.GetProperties())
        {
            var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
            if (accessors.Getter == method)
                return AccessorKind.PropertyGet;
            if (accessors.Setter == method)
                return AccessorKind.PropertySet;
        }

        foreach (var eventHandle in declaringType.GetEvents())
        {
            var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
            if (accessors.Adder == method)
                return AccessorKind.EventAdd;
            if (accessors.Remover == method)
                return AccessorKind.EventRemove;
        }
        return AccessorKind.None;
    }

    internal static ImmutableArray<string> GenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    internal static (string Namespace, string Name) AttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        switch (constructor.Kind)
        {
            case HandleKind.MemberReference
                when reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent:
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)parent);
                return (reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
            case HandleKind.MethodDefinition:
                var declaring = reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                return (reader.GetString(declaring.Namespace), reader.GetString(declaring.Name));
            default:
                return ("", "");
        }
    }

    static ArgumentRefKind ClassifyByRefParameter(MetadataReader reader, System.Reflection.Metadata.Parameter parameter)
    {
        if (HasReadOnlyRefAttribute(reader, parameter.GetCustomAttributes()))
            return ArgumentRefKind.In;
        var attributes = parameter.Attributes;
        if ((attributes & System.Reflection.ParameterAttributes.Out) != 0
            && (attributes & System.Reflection.ParameterAttributes.In) == 0)
            return ArgumentRefKind.Out;
        return ArgumentRefKind.Ref;
    }

    static bool HasReadOnlyRefAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => HasAttribute(reader, attributes, "System.Runtime.CompilerServices", "IsReadOnlyAttribute")
            || HasAttribute(reader, attributes, "System.Runtime.CompilerServices", "RequiresLocationAttribute");

    static bool HasAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string ns, string name)
    {
        foreach (var handle in attributes)
            if (AttributeTypeName(reader, reader.GetCustomAttribute(handle).Constructor) is var attr
                && attr.Namespace == ns
                && attr.Name == name)
            {
                return true;
            }
        return false;
    }
}
