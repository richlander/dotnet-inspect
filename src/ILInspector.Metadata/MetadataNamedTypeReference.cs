using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// The metadata scope that supplies one named signature type.
/// </summary>
public abstract record MetadataTypeReferenceScope
{
    private protected MetadataTypeReferenceScope()
    {
    }

    public sealed record CurrentAssembly : MetadataTypeReferenceScope;

    public sealed record AssemblyReference : MetadataTypeReferenceScope
    {
        public AssemblyReference(AssemblyReferenceIdentity assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            Assembly = assembly;
        }

        public AssemblyReferenceIdentity Assembly { get; }
    }

    public sealed record IntrinsicCoreLibrary : MetadataTypeReferenceScope;

    public sealed record ModuleReference : MetadataTypeReferenceScope
    {
        public ModuleReference(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name;
        }

        public string Name { get; }
    }
}

/// <summary>
/// A reader-independent named type from one metadata signature.
/// </summary>
public sealed record MetadataNamedTypeReference
{
    public MetadataNamedTypeReference(
        MetadataTypeReferenceScope scope,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(type);
        Scope = scope;
        Type = type;
    }

    public MetadataTypeReferenceScope Scope { get; }
    public MetadataTypeDefinitionName Type { get; }
}

internal static class MetadataNamedTypeSignatureDecoder
{
    internal static MetadataNamedTypeReference? DecodeType(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context,
        Action<int>? beforeDecodeWork = null)
    {
        var provider = beforeDecodeWork is null
            ? MetadataNamedTypeProvider.Instance
            : new MetadataNamedTypeProvider(beforeDecodeWork);
        return
        handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => provider.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => provider.GetTypeFromSpecification(
                reader,
                context,
                (TypeSpecificationHandle)handle,
                rawTypeKind: 0),
            _ => null,
        };
    }

    internal static MethodSignature<MetadataNamedTypeReference?>? DecodeMethod(
        MetadataReader reader,
        MethodDefinition method,
        GenericContext? context)
    {
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return null;
        }

        try
        {
            return method.DecodeSignature(
                MetadataNamedTypeProvider.Instance,
                context);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return null;
        }
    }

    private sealed class MetadataNamedTypeProvider :
        ISignatureTypeProvider<
            MetadataNamedTypeReference?,
            GenericContext?>
    {
        internal static MetadataNamedTypeProvider Instance { get; } = new();
        readonly Action<int>? beforeDecodeWork;

        internal MetadataNamedTypeProvider(Action<int>? beforeDecodeWork = null)
        {
            this.beforeDecodeWork = beforeDecodeWork;
        }

        public MetadataNamedTypeReference? GetPrimitiveType(
            PrimitiveTypeCode typeCode) =>
            Named(
                new MetadataTypeReferenceScope.IntrinsicCoreLibrary(),
                "System",
                typeCode switch
                {
                    PrimitiveTypeCode.Boolean => "Boolean",
                    PrimitiveTypeCode.Byte => "Byte",
                    PrimitiveTypeCode.SByte => "SByte",
                    PrimitiveTypeCode.Char => "Char",
                    PrimitiveTypeCode.Int16 => "Int16",
                    PrimitiveTypeCode.UInt16 => "UInt16",
                    PrimitiveTypeCode.Int32 => "Int32",
                    PrimitiveTypeCode.UInt32 => "UInt32",
                    PrimitiveTypeCode.Int64 => "Int64",
                    PrimitiveTypeCode.UInt64 => "UInt64",
                    PrimitiveTypeCode.Single => "Single",
                    PrimitiveTypeCode.Double => "Double",
                    PrimitiveTypeCode.IntPtr => "IntPtr",
                    PrimitiveTypeCode.UIntPtr => "UIntPtr",
                    PrimitiveTypeCode.String => "String",
                    PrimitiveTypeCode.Object => "Object",
                    PrimitiveTypeCode.Void => "Void",
                    PrimitiveTypeCode.TypedReference => "TypedReference",
                    _ => typeCode.ToString(),
                });

        public MetadataNamedTypeReference? GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            MetadataTypeDefinitionNameReader.Read(
                reader,
                handle,
                beforeDecodeWork)
                is MetadataTypeDefinitionNameReadResult.Read read
                    ? new MetadataNamedTypeReference(
                        new MetadataTypeReferenceScope.CurrentAssembly(),
                        read.Name)
                    : null;

        public MetadataNamedTypeReference? GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            if (MetadataTypeDefinitionNameReader.Read(
                    reader,
                    handle,
                    beforeDecodeWork)
                is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                return null;
            }

            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        handle,
                        chain,
                        out _,
                        out EntityHandle terminal,
                        out _))
            {
                return null;
            }

            MetadataTypeReferenceScope? scope = terminal.Kind switch
            {
                HandleKind.AssemblyReference =>
                    new MetadataTypeReferenceScope.AssemblyReference(
                        AssemblyReference(
                            reader,
                            (AssemblyReferenceHandle)terminal)),
                HandleKind.ModuleReference =>
                    ModuleScope(
                        reader,
                        (ModuleReferenceHandle)terminal),
                HandleKind.ModuleDefinition =>
                    new MetadataTypeReferenceScope.CurrentAssembly(),
                _ when terminal.IsNil =>
                    new MetadataTypeReferenceScope.CurrentAssembly(),
                _ => null,
            };
            return scope is null
                ? null
                : new MetadataNamedTypeReference(scope, read.Name);
        }

        public MetadataNamedTypeReference? GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return null;

            using (scope)
            {
                TypeSpecification specification =
                    reader.GetTypeSpecification(handle);
                beforeDecodeWork?.Invoke(
                    reader.GetBlobReader(specification.Signature).Length);
                return specification.DecodeSignature(this, context);
            }
        }

        public MetadataNamedTypeReference? GetSZArrayType(
            MetadataNamedTypeReference? elementType) =>
            null;

        public MetadataNamedTypeReference? GetArrayType(
            MetadataNamedTypeReference? elementType,
            ArrayShape shape) =>
            null;

        public MetadataNamedTypeReference? GetByReferenceType(
            MetadataNamedTypeReference? elementType) =>
            elementType;

        public MetadataNamedTypeReference? GetPointerType(
            MetadataNamedTypeReference? elementType) =>
            null;

        public MetadataNamedTypeReference? GetPinnedType(
            MetadataNamedTypeReference? elementType) =>
            elementType;

        public MetadataNamedTypeReference? GetGenericInstantiation(
            MetadataNamedTypeReference? genericType,
            ImmutableArray<MetadataNamedTypeReference?> typeArguments) =>
            genericType;

        public MetadataNamedTypeReference? GetGenericTypeParameter(
            GenericContext? context,
            int index) =>
            null;

        public MetadataNamedTypeReference? GetGenericMethodParameter(
            GenericContext? context,
            int index) =>
            null;

        public MetadataNamedTypeReference? GetFunctionPointerType(
            MethodSignature<MetadataNamedTypeReference?> signature) =>
            null;

        public MetadataNamedTypeReference? GetModifiedType(
            MetadataNamedTypeReference? modifier,
            MetadataNamedTypeReference? unmodifiedType,
            bool isRequired) =>
            unmodifiedType;

        static MetadataNamedTypeReference? Named(
            MetadataTypeReferenceScope scope,
            string @namespace,
            string name) =>
            MetadataTypeDefinitionName.Create(
                @namespace,
                ImmutableArray.Create(name))
                is MetadataTypeDefinitionNameResult.Valid valid
                    ? new MetadataNamedTypeReference(scope, valid.Name)
                    : null;

        AssemblyReferenceIdentity AssemblyReference(
            MetadataReader reader,
            AssemblyReferenceHandle handle)
        {
            System.Reflection.Metadata.AssemblyReference reference =
                reader.GetAssemblyReference(handle);
            beforeDecodeWork?.Invoke(reader.GetBlobReader(reference.Name).Length);
            beforeDecodeWork?.Invoke(reader.GetBlobReader(reference.Culture).Length);
            beforeDecodeWork?.Invoke(
                reader.GetBlobReader(reference.PublicKeyOrToken).Length);
            return AssemblyReferenceIdentity.From(reader, handle);
        }

        MetadataTypeReferenceScope? ModuleScope(
            MetadataReader reader,
            ModuleReferenceHandle handle)
        {
            StringHandle nameHandle = reader.GetModuleReference(handle).Name;
            beforeDecodeWork?.Invoke(reader.GetBlobReader(nameHandle).Length);
            string name = reader.GetString(nameHandle);
            return string.IsNullOrWhiteSpace(name)
                ? null
                : new MetadataTypeReferenceScope.ModuleReference(name);
        }
    }
}
