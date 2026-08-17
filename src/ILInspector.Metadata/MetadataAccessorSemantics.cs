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

    public static bool AreAllAbstract(
        MetadataReader reader,
        params MethodDefinitionHandle[] handles)
    {
        bool anyAccessor = false;
        foreach (var handle in handles)
        {
            if (handle.IsNil)
                continue;

            anyAccessor = true;
            if ((reader.GetMethodDefinition(handle).Attributes & MethodAttributes.Abstract) == 0)
                return false;
        }

        return anyAccessor;
    }

    public static bool TryGetUniformSealedOverride(
        MetadataReader reader,
        out bool isSealed,
        params MethodDefinitionHandle[] handles)
    {
        bool? expected = null;
        foreach (var handle in handles)
        {
            if (handle.IsNil)
                continue;

            var attributes = reader.GetMethodDefinition(handle).Attributes;
            bool accessorSealed =
                (attributes & MethodAttributes.Virtual) != 0
                && (attributes & MethodAttributes.NewSlot) == 0
                && (attributes & MethodAttributes.Final) != 0;
            if (expected is not null && expected != accessorSealed)
            {
                isSealed = false;
                return false;
            }
            expected = accessorSealed;
        }

        isSealed = expected == true;
        return true;
    }

    public static bool IsUniformlySealedOverride(
        MetadataReader reader,
        params MethodDefinitionHandle[] handles)
    {
        if (!TryGetUniformSealedOverride(reader, out bool isSealed, handles))
        {
            throw new BadImageFormatException(
                "The aggregate has inconsistent sealed accessor metadata.");
        }
        return isSealed;
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
        if (result.Value.ReturnType.IsMalformed)
            throw new BadImageFormatException("The init-only modifier type is malformed.");
        return result.Value.ReturnType.IsInitOnly;
    }

    readonly record struct InitOnlyModifierState(
        bool IsExternalInitType,
        bool IsInitOnly,
        bool IsMalformed);

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
                type.GetDeclaringType().IsNil
                    && IsExternalInit(
                        reader.GetString(type.Name),
                        reader.GetString(type.Namespace)),
                false,
                false);
        }

        public InitOnlyModifierState GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                Span<TypeReferenceHandle> chain =
                    stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
                if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                        reader,
                        handle,
                        chain,
                        out _,
                        out _,
                        out _))
                {
                    return new(false, false, true);
                }

                return default;
            }
            return new(
                IsExternalInit(reader.GetString(type.Name), reader.GetString(type.Namespace)),
                false,
                false);
        }

        public InitOnlyModifierState GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            var decoded = GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                context,
                new InitOnlyModifierState(false, false, true));
            return decoded with
            {
                IsExternalInitType = false,
                IsMalformed = true,
            };
        }

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
                    || isRequired && modifier.IsExternalInitType,
                modifier.IsMalformed || unmodifiedType.IsMalformed);

        public InitOnlyModifierState GetPinnedType(InitOnlyModifierState elementType)
            => elementType;

        static bool IsExternalInit(string name, string @namespace)
            => name == "IsExternalInit"
                && @namespace == "System.Runtime.CompilerServices";
    }
}
