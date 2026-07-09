using System;
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

    // A degraded but syntactically valid C# type keeps composed source parseable; a type whose
    // signature could not be safely decoded is already un-reconstructable, so its exact text is moot.
    const string Unresolved = "object";

    // SRM invokes this provider's GetTypeFromSpecification override for every *nested* TypeSpec
    // reached by handle (e.g. a CMOD custom modifier referencing another TypeSpec). A malformed
    // metadata blob can form a cross-handle TypeSpec *cycle*, so each re-entry decodes a fresh
    // blob and re-enters SRM's native-recursive DecodeType — an uncatchable StackOverflow that a
    // caller-side guard on the *outer* blob cannot see. Mirror TypeRefDecoder's hardening: bound
    // per-blob length, cumulative cross-blob bytes, and re-entry depth (all [ThreadStatic] so the
    // shared Instance stays thread-safe), then run the structural SignatureBlobGuard prescan.
    [ThreadStatic]
    static int s_recursionDepth;
    const int MaxRecursionDepth = 256;

    [ThreadStatic]
    static int s_cumulativeSignatureBytes;
    const int MaxCumulativeSignatureBytes = 4096;

    const int MaxSignatureBlobLength = 1024;

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
        if (s_recursionDepth >= MaxRecursionDepth)
            return Unresolved;
        var typeSpec = reader.GetTypeSpecification(handle);
        // Cheap #2489 length / cumulative caps FIRST: an over-long blob is rejected in O(1), which
        // also keeps SignatureBlobGuard below from walking a huge blob. The length/cumulative caps
        // bound a cross-blob modreq *cycle*; the guard then bounds this single (now <= 1024-byte)
        // blob's structural depth and count-driven allocations.
        int blobLength = reader.GetBlobReader(typeSpec.Signature).Length;
        if (blobLength > MaxSignatureBlobLength)
            return Unresolved;
        if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
            return Unresolved;
        if (!SignatureBlobGuard.IsSafeToDecode(reader, typeSpec.Signature, SignatureBlobGuard.Kind.TypeSpecification))
            return Unresolved;
        s_cumulativeSignatureBytes += blobLength;
        s_recursionDepth++;
        try
        {
            return typeSpec.DecodeSignature(this, context);
        }
        finally
        {
            s_recursionDepth--;
            s_cumulativeSignatureBytes -= blobLength;
        }
    }

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    
    public string GetArrayType(string elementType, ArrayShape shape) 
        => $"{elementType}[{new string(',', Math.Max(0, shape.Rank - 1))}]";
    
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
