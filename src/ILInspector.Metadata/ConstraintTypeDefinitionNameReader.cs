using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class ConstraintTypeDefinitionNameReader
{
    internal static ConstraintTypeDefinitionNameReadResult? Read(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext context,
        bool allowUnmanagedValueTypeEncoding)
    {
        try
        {
            ConstraintShape shape = handle.Kind switch
            {
                HandleKind.TypeDefinition => Named(
                    reader,
                    (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => Named(
                    reader,
                    (TypeReferenceHandle)handle),
                HandleKind.TypeSpecification =>
                    GuardedProviderDecode.TypeSpec(
                        reader,
                        (TypeSpecificationHandle)handle,
                        Provider.Instance,
                        (GenericContext?)context,
                        ConstraintShape.Unavailable),
                _ => ConstraintShape.Unavailable,
            };
            return shape.IsAvailable
                && shape.IsConstraintType
                && (allowUnmanagedValueTypeEncoding
                    || !shape.IsUnmanagedValueTypeEncoding)
                ? new(
                    [.. shape.DefinitionNames.Distinct()],
                    shape.IsCoreLibraryPseudoConstraint)
                : null;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return null;
        }
    }

    static ConstraintShape Named(
        MetadataReader reader,
        TypeDefinitionHandle handle) =>
        Named(
            MetadataTypeDefinitionNameReader.Read(reader, handle),
            CoreLibraryRootAuthentication
                .DeclaresUniqueTopLevelCoreLibraryRoot(reader));

    static ConstraintShape Named(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        TypeReference reference = reader.GetTypeReference(handle);
        return Named(
            MetadataTypeDefinitionNameReader.Read(reader, handle),
            ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                reader,
                reference.ResolutionScope));
    }

    static ConstraintShape Named(
        MetadataTypeDefinitionNameReadResult result,
        bool resolvesThroughCoreLibrary) =>
        result is MetadataTypeDefinitionNameReadResult.Read read
            ? IsPseudoConstraintName(read.Name)
                ? resolvesThroughCoreLibrary
                    ? ConstraintShape.Named(
                        read.Name,
                        isCoreLibraryPseudoConstraint: true)
                    : ConstraintShape.Unavailable
                : ConstraintShape.Named(
                    read.Name,
                    isCoreLibraryPseudoConstraint: false)
            : ConstraintShape.Unavailable;

    static bool IsPseudoConstraintName(
        MetadataTypeDefinitionName name) =>
        name.Namespace == "System"
            && name.Segments is ["Object" or "ValueType"];

    readonly record struct ConstraintShape(
        bool IsAvailable,
        bool IsConstraintType,
        ImmutableArray<MetadataTypeDefinitionName> DefinitionNames,
        bool IsCoreLibraryPseudoConstraint = false,
        bool IsUnmanagedValueTypeEncoding = false)
    {
        internal static ConstraintShape EmptyConstraint { get; } =
            new(true, true, []);

        internal static ConstraintShape NonConstraint { get; } =
            new(true, false, []);

        internal static ConstraintShape Unavailable { get; } =
            new(false, false, []);

        internal static ConstraintShape Named(
            MetadataTypeDefinitionName name,
            bool isCoreLibraryPseudoConstraint) =>
            new(
                true,
                true,
                [name],
                isCoreLibraryPseudoConstraint);
    }

    sealed class Provider :
        ISignatureTypeProvider<ConstraintShape, GenericContext?>
    {
        internal static Provider Instance { get; } = new();

        public ConstraintShape GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            ConstraintShape.NonConstraint;

        public ConstraintShape GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            Named(reader, handle);

        public ConstraintShape GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            Named(reader, handle);

        public ConstraintShape GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                context,
                ConstraintShape.Unavailable);

        public ConstraintShape GetSZArrayType(
            ConstraintShape elementType) =>
            elementType.IsAvailable
                ? elementType with { IsConstraintType = false }
                : elementType;

        public ConstraintShape GetArrayType(
            ConstraintShape elementType,
            ArrayShape shape) =>
            elementType.IsAvailable
                ? elementType with { IsConstraintType = false }
                : elementType;

        public ConstraintShape GetByReferenceType(
            ConstraintShape elementType) =>
            elementType.IsAvailable
                ? elementType with { IsConstraintType = false }
                : elementType;

        public ConstraintShape GetPointerType(
            ConstraintShape elementType) =>
            elementType.IsAvailable
                ? elementType with { IsConstraintType = false }
                : elementType;

        public ConstraintShape GetPinnedType(
            ConstraintShape elementType) =>
            elementType.IsAvailable
                ? elementType with { IsConstraintType = false }
                : elementType;

        public ConstraintShape GetGenericInstantiation(
            ConstraintShape genericType,
            ImmutableArray<ConstraintShape> typeArguments)
        {
            if (!genericType.IsAvailable
                || !genericType.IsConstraintType
                || genericType.IsUnmanagedValueTypeEncoding
                || typeArguments.Any(argument =>
                    !argument.IsAvailable
                    || argument.IsUnmanagedValueTypeEncoding))
            {
                return ConstraintShape.Unavailable;
            }

            if (genericType.DefinitionNames is not [var definitionName])
            {
                return ConstraintShape.Unavailable;
            }

            int? declaredArity = GetDeclaredArity(definitionName);
            if (declaredArity != typeArguments.Length)
                return ConstraintShape.Unavailable;

            var definitions =
                ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
            definitions.AddRange(genericType.DefinitionNames);
            foreach (ConstraintShape argument in typeArguments)
                definitions.AddRange(argument.DefinitionNames);
            return new(
                IsAvailable: true,
                IsConstraintType: true,
                definitions.ToImmutable());
        }

        static int? GetDeclaredArity(
            MetadataTypeDefinitionName definitionName)
        {
            int arity = 0;
            foreach (string segment in definitionName.Segments)
            {
                int segmentArity = MetadataNameArity.OfSegment(segment);
                if (segmentArity > int.MaxValue - arity)
                    return null;
                arity += segmentArity;
            }

            return arity;
        }

        public ConstraintShape GetGenericTypeParameter(
            GenericContext? context,
            int index) =>
            context is not null
                && (uint)index < (uint)context.TypeParameters.Count
                    ? ConstraintShape.EmptyConstraint
                    : ConstraintShape.Unavailable;

        public ConstraintShape GetGenericMethodParameter(
            GenericContext? context,
            int index) =>
            context is not null
                && (uint)index < (uint)context.MethodParameters.Count
                    ? ConstraintShape.EmptyConstraint
                    : ConstraintShape.Unavailable;

        public ConstraintShape GetFunctionPointerType(
            MethodSignature<ConstraintShape> signature) =>
            ConstraintShape.Unavailable;

        public ConstraintShape GetModifiedType(
            ConstraintShape modifier,
            ConstraintShape unmodifiedType,
            bool isRequired)
        {
            if (!isRequired
                || !IsExactNamed(
                    modifier,
                    "System.Runtime.InteropServices",
                    "UnmanagedType")
                || !IsExactNamed(
                    unmodifiedType,
                    "System",
                    "ValueType"))
            {
                return ConstraintShape.Unavailable;
            }

            return unmodifiedType with
            {
                IsUnmanagedValueTypeEncoding = true,
            };
        }

        static bool IsExactNamed(
            ConstraintShape shape,
            string @namespace,
            string simpleName) =>
            shape is
            {
                IsAvailable: true,
                IsConstraintType: true,
                IsUnmanagedValueTypeEncoding: false,
                DefinitionNames: [var definitionName],
            }
            && definitionName.Namespace == @namespace
            && definitionName.Segments is [var segment]
            && segment == simpleName;
    }
}

internal sealed record ConstraintTypeDefinitionNameReadResult(
    IReadOnlyList<MetadataTypeDefinitionName> DefinitionNames,
    bool IsCoreLibraryPseudoConstraint);
