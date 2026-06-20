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
