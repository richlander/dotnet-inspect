using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class MetadataAccessorSemantics
{
    public static string Kind(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        string defaultKind)
        => defaultKind == "set" && IsInitOnlySetter(reader, handle)
            ? "init"
            : defaultKind;

    public static bool IsUniformlyAbstract(
        MetadataReader reader,
        params MethodDefinitionHandle[] handles)
    {
        bool anyAbstract = false;
        bool anyConcrete = false;
        foreach (var handle in handles)
        {
            if (handle.IsNil)
                continue;

            bool isAbstract =
                (reader.GetMethodDefinition(handle).Attributes & MethodAttributes.Abstract) != 0;
            anyAbstract |= isAbstract;
            anyConcrete |= !isAbstract;
        }

        if (anyAbstract && anyConcrete)
        {
            throw new BadImageFormatException(
                "The aggregate has inconsistent abstract accessor metadata.");
        }

        return anyAbstract;
    }

    static bool IsInitOnlySetter(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        var result = GuardedProviderDecode.MethodResult(
            reader,
            method,
            InitOnlyModifierDetector.Instance,
            (object?)null,
            default(InitOnlyModifierState));
        if (result.IsDegraded)
            throw new BadImageFormatException("The property setter signature could not be decoded.");
        return result.Value.ReturnType.IsInitOnly;
    }

    readonly record struct InitOnlyModifierState(
        bool IsExternalInitType,
        bool IsInitOnly);

    sealed class InitOnlyModifierDetector
        : ISignatureTypeProvider<InitOnlyModifierState, object?>
    {
        public static InitOnlyModifierDetector Instance { get; } = new();

        public InitOnlyModifierState GetPrimitiveType(PrimitiveTypeCode typeCode)
            => default;

        public InitOnlyModifierState GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return new(
                IsExternalInit(reader.GetString(type.Name), reader.GetString(type.Namespace)),
                false);
        }

        public InitOnlyModifierState GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return new(
                IsExternalInit(reader.GetString(type.Name), reader.GetString(type.Namespace)),
                false);
        }

        public InitOnlyModifierState GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
            => default;

        public InitOnlyModifierState GetSZArrayType(InitOnlyModifierState elementType)
            => default;

        public InitOnlyModifierState GetArrayType(
            InitOnlyModifierState elementType,
            ArrayShape shape)
            => default;

        public InitOnlyModifierState GetByReferenceType(InitOnlyModifierState elementType)
            => default;

        public InitOnlyModifierState GetPointerType(InitOnlyModifierState elementType)
            => default;

        public InitOnlyModifierState GetGenericInstantiation(
            InitOnlyModifierState genericType,
            ImmutableArray<InitOnlyModifierState> typeArguments)
            => default;

        public InitOnlyModifierState GetGenericMethodParameter(object? context, int index)
            => default;

        public InitOnlyModifierState GetGenericTypeParameter(object? context, int index)
            => default;

        public InitOnlyModifierState GetFunctionPointerType(
            MethodSignature<InitOnlyModifierState> signature)
            => default;

        public InitOnlyModifierState GetModifiedType(
            InitOnlyModifierState modifier,
            InitOnlyModifierState unmodifiedType,
            bool isRequired)
            => new(
                false,
                unmodifiedType.IsInitOnly
                    || isRequired && modifier.IsExternalInitType);

        public InitOnlyModifierState GetPinnedType(InitOnlyModifierState elementType)
            => elementType;

        static bool IsExternalInit(string name, string @namespace)
            => name == "IsExternalInit"
                && @namespace == "System.Runtime.CompilerServices";
    }
}
