using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// The SRM-only decode half of custom-attribute reading: resolves an
/// attribute's type name and decodes its constructor blob to typed argument
/// values. Rendering those values to C# (or any other surface) is the caller's
/// concern — this layer is shared by anything that needs attribute data.
/// </summary>
public static class AttributeDecoder
{
    /// <summary>
    /// The fully qualified type name of an attribute, from its constructor handle.
    /// </summary>
    public static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructorHandle)
    {
        if (constructorHandle.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
            return TypeResolver.GetTypeName(reader, memberRef.Parent);
        }
        if (constructorHandle.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructorHandle);
            var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
            return TypeResolver.GetFullName(reader, typeDef);
        }
        return null;
    }

    /// <summary>
    /// Decodes an attribute's fixed and named arguments to typed values, or null
    /// when the blob cannot be decoded. Argument <c>Type</c> strings are C#
    /// keywords for primitives, <c>System.Type</c> for typeof targets, and the
    /// full type name otherwise (enums, etc.).
    /// </summary>
    public static CustomAttributeValue<string>? TryDecode(MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            return attribute.DecodeValue(new ArgTypeProvider(reader));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Type provider for attribute-blob decoding: primitives as C# keywords, everything else as its full name (enums and typeof targets).</summary>
    sealed class ArgTypeProvider(MetadataReader reader) : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            _ => "object",
        };

        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeResolver.GetFullName(r, r.GetTypeDefinition(handle));
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeResolver.GetTypeName(r, handle) ?? "object";
        public string GetTypeFromSerializedName(string name)
        {
            int comma = name.IndexOf(',');
            return comma >= 0 ? name[..comma] : name;
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var def = reader.GetTypeDefinition(handle);
                if (TypeResolver.GetFullName(reader, def) != type)
                    continue;
                foreach (var fieldHandle in def.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.Static) == 0)
                        return field.DecodeSignature(new PrimitiveCodeProvider(), null);
                }
            }
            return PrimitiveTypeCode.Int32;
        }
    }

    /// <summary>Minimal signature provider that reports an enum's underlying primitive type code.</summary>
    sealed class PrimitiveCodeProvider : ISignatureTypeProvider<PrimitiveTypeCode, object?>
    {
        public PrimitiveTypeCode GetPrimitiveType(PrimitiveTypeCode code) => code;
        public PrimitiveTypeCode GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetSZArrayType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetArrayType(PrimitiveTypeCode e, ArrayShape s) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetByReferenceType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetPointerType(PrimitiveTypeCode e) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericInstantiation(PrimitiveTypeCode g, ImmutableArray<PrimitiveTypeCode> a) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericMethodParameter(object? ctx, int i) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetGenericTypeParameter(object? ctx, int i) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetModifiedType(PrimitiveTypeCode m, PrimitiveTypeCode u, bool r) => u;
        public PrimitiveTypeCode GetPinnedType(PrimitiveTypeCode e) => e;
        public PrimitiveTypeCode GetFunctionPointerType(MethodSignature<PrimitiveTypeCode> s) => PrimitiveTypeCode.Int32;
        public PrimitiveTypeCode GetTypeFromSpecification(MetadataReader r, object? ctx, TypeSpecificationHandle h, byte k) => PrimitiveTypeCode.Int32;
    }
}
