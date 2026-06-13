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
        {
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            return TypeResolver.GetFullName(ns, name);
        }

        /// <summary>
        /// Gets the fully qualified name (Namespace.Name) of a <see cref="TypeReference"/>.
        /// </summary>
        public string GetFullTypeName(TypeReference typeRef)
        {
            string ns = reader.GetString(typeRef.Namespace);
            string name = reader.GetString(typeRef.Name);
            return TypeResolver.GetFullName(ns, name);
        }

        /// <summary>
        /// Gets the fully qualified name (Namespace.Name) of an <see cref="ExportedType"/>.
        /// </summary>
        public string GetFullTypeName(ExportedType exportedType)
        {
            string ns = reader.GetString(exportedType.Namespace);
            string name = reader.GetString(exportedType.Name);
            return TypeResolver.GetFullName(ns, name);
        }

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
