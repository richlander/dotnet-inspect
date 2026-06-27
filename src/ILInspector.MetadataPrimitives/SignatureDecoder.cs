using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

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
        => TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle));

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => TypeResolver.GetTypeNameFromReference(reader, handle);

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
        => TypeResolver.ApplyGenericArguments(genericType, typeArguments);

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

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        var types = signature.ParameterTypes.Add(signature.ReturnType);
        string arguments = string.Join(", ", types);
        string convention = ConventionText(signature.Header.CallingConvention);
        return convention.Length == 0
            ? $"delegate*<{arguments}>"
            : $"delegate* {convention}<{arguments}>";
    }

    static string ConventionText(SignatureCallingConvention convention) => convention switch
    {
        SignatureCallingConvention.Default => "",
        SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
        SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
        SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
        SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
        _ => "unmanaged",
    };
    
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    
    public string GetPinnedType(string elementType) => elementType;
}
