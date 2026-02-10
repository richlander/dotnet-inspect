using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotnetInspector.Metadata;

/// <summary>
/// Decodes type signatures from metadata into human-readable C# type names.
/// Handles primitives, generics, arrays, pointers, and ref types.
/// </summary>
public class SignatureDecoder : ISignatureTypeProvider<string, GenericContext?>
{
    /// <summary>
    /// Shared instance for common use cases.
    /// </summary>
    public static SignatureDecoder Instance { get; } = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
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
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => typeCode.ToString()
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        return reader.GetFullTypeName(typeDef);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        return reader.GetFullTypeName(typeRef);
    }

    public string GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        return typeSpec.DecodeSignature(this, context);
    }

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    
    public string GetArrayType(string elementType, ArrayShape shape) 
        => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    
    public string GetByReferenceType(string elementType) => $"ref {elementType}";
    
    public string GetPointerType(string elementType) => $"{elementType}*";
    
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        // Strip .NET arity suffix (e.g., List`1 -> List)
        var backtickIndex = genericType.IndexOf('`');
        var cleanName = backtickIndex >= 0 ? genericType[..backtickIndex] : genericType;
        return $"{cleanName}<{string.Join(", ", typeArguments)}>";
    }

    public string GetGenericMethodParameter(GenericContext? context, int index)
    {
        if (context is not null && index < context.MethodParameters.Count)
            return context.MethodParameters[index];
        return $"TM{index}";
    }

    public string GetGenericTypeParameter(GenericContext? context, int index)
    {
        if (context is not null && index < context.TypeParameters.Count)
            return context.TypeParameters[index];
        return $"T{index}";
    }

    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
    
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    
    public string GetPinnedType(string elementType) => elementType;
}
