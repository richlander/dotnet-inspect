using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

internal sealed record EnumMetadataFacts(
    IReadOnlyDictionary<long, string> Members,
    TypeRef UnderlyingType,
    bool IsFlags);

internal static class EnumMetadataFactReader
{
    internal static EnumMetadataFacts? Read(
        MetadataReader reader,
        TypeDefinition typeDefinition)
    {
        if (!IsEnum(reader, typeDefinition))
            return null;

        TypeRef? underlyingType = null;
        var members = new Dictionary<long, string>();
        var scope = new GenericScope([], []);

        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string name = reader.GetString(field.Name);
            if (name == "value__" && (field.Attributes & FieldAttributes.Static) == 0)
            {
                underlyingType = GuardedDecode.FieldType(reader, field, scope);
                continue;
            }

            if ((field.Attributes & (FieldAttributes.Static | FieldAttributes.Literal))
                != (FieldAttributes.Static | FieldAttributes.Literal))
            {
                continue;
            }
            if (AttributeReader.TryGetObsoleteAttribute(
                reader,
                field.GetCustomAttributes(),
                out _,
                out bool isError)
                && isError)
            {
                continue;
            }

            ConstantHandle constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil)
                continue;

            if (TryReadConstant(reader, constantHandle, out long normalized))
                members.TryAdd(normalized, name);
        }

        return underlyingType is null
            ? null
            : new EnumMetadataFacts(
                members,
                underlyingType,
                AttributeReader.HasFlagsAttribute(reader, typeDefinition.GetCustomAttributes()));
    }

    static bool IsEnum(MetadataReader reader, TypeDefinition typeDefinition)
    {
        EntityHandle baseType = typeDefinition.BaseType;
        return baseType.Kind switch
        {
            HandleKind.TypeReference => IsSystemEnum(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)baseType)),
            HandleKind.TypeDefinition => IsSystemEnum(
                reader,
                reader.GetTypeDefinition((TypeDefinitionHandle)baseType)),
            _ => false,
        };
    }

    static bool IsSystemEnum(MetadataReader reader, TypeReference type)
        => reader.StringComparer.Equals(type.Namespace, "System")
            && reader.StringComparer.Equals(type.Name, "Enum");

    static bool IsSystemEnum(MetadataReader reader, TypeDefinition type)
        => reader.StringComparer.Equals(type.Namespace, "System")
            && reader.StringComparer.Equals(type.Name, "Enum");

    static bool TryReadConstant(
        MetadataReader reader,
        ConstantHandle handle,
        out long normalized)
    {
        var constant = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(constant.Value);
        // Match the IL constant representation used by the importer: high-bit
        // unsigned values arrive as signed two's-complement stack values.
        switch (constant.TypeCode)
        {
            case ConstantTypeCode.SByte:
                normalized = blob.ReadSByte();
                return true;
            case ConstantTypeCode.Byte:
                normalized = blob.ReadByte();
                return true;
            case ConstantTypeCode.Int16:
                normalized = blob.ReadInt16();
                return true;
            case ConstantTypeCode.UInt16:
                normalized = blob.ReadUInt16();
                return true;
            case ConstantTypeCode.Int32:
                normalized = blob.ReadInt32();
                return true;
            case ConstantTypeCode.UInt32:
                normalized = unchecked((int)blob.ReadUInt32());
                return true;
            case ConstantTypeCode.Int64:
                normalized = blob.ReadInt64();
                return true;
            case ConstantTypeCode.UInt64:
                normalized = unchecked((long)blob.ReadUInt64());
                return true;
            case ConstantTypeCode.Char:
                normalized = blob.ReadChar();
                return true;
            case ConstantTypeCode.Boolean:
                normalized = blob.ReadBoolean() ? 1L : 0L;
                return true;
            default:
                normalized = 0;
                return false;
        }
    }
}
