using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Extension members for <see cref="MetadataReader"/> and related metadata types,
/// reducing boilerplate around common name-resolution patterns.
/// </summary>
public static class MetadataReaderExtensions
{
    /// <summary>
    /// Extensions for resolving fully-qualified names from metadata handles.
    /// </summary>
    extension(MetadataReader reader)
    {
        /// <summary>
        /// Gets the fully qualified name (Namespace.Name) of a <see cref="TypeDefinition"/>.
        /// </summary>
        public string GetFullTypeName(TypeDefinition typeDef)
            => TypeResolver.ResolveFullName(reader, typeDef).GetValueOrThrow();

        /// <summary>
        /// Resolves the fully qualified name of a TypeDefinition through a
        /// bounded declaring-type walk.
        /// </summary>
        public RelationshipTraversalResult<string> ResolveFullTypeName(
            TypeDefinitionHandle handle)
            => TypeResolver.ResolveTypeNameFromDefinition(reader, handle);

        /// <summary>
        /// Gets the fully qualified name of a <see cref="TypeReference"/>,
        /// qualifying a nested type through its declaring type (Outer.Inner) to
        /// match <see cref="GetFullTypeName(TypeDefinition)"/>.
        /// </summary>
        public string GetFullTypeName(TypeReference typeRef)
            => TypeResolver.ResolveFullName(reader, typeRef).GetValueOrThrow();

        /// <summary>
        /// Resolves the fully qualified name of a TypeReference through a
        /// bounded resolution-scope walk.
        /// </summary>
        public RelationshipTraversalResult<string> ResolveFullTypeName(
            TypeReferenceHandle handle)
            => TypeResolver.ResolveTypeNameFromReference(reader, handle);

        /// <summary>
        /// Gets the fully qualified name (Namespace.Name) of an <see cref="ExportedType"/>.
        /// </summary>
        public string GetFullTypeName(ExportedType exportedType)
            => TypeResolver.ResolveFullName(reader, exportedType).GetValueOrThrow();

        /// <summary>
        /// Resolves the fully qualified name of an ExportedType through a
        /// bounded implementation walk.
        /// </summary>
        public RelationshipTraversalResult<string> ResolveFullTypeName(
            ExportedTypeHandle handle)
            => TypeResolver.ResolveTypeNameFromExportedType(reader, handle);

        /// <summary>
        /// Gets the name of a generic parameter from its handle.
        /// </summary>
        public string GetGenericParameterName(GenericParameterHandle handle)
            => reader.GetString(reader.GetGenericParameter(handle).Name);
    }

    /// <summary>
    /// Extensions for common <see cref="TypeDefinition"/> attribute checks.
    /// </summary>
    extension(TypeDefinition typeDef)
    {
        /// <summary>
        /// True when the type has Public or NestedPublic visibility.
        /// </summary>
        public bool IsPublic
            => (typeDef.Attributes & TypeAttributes.VisibilityMask) is
                TypeAttributes.Public or TypeAttributes.NestedPublic;
    }
}
