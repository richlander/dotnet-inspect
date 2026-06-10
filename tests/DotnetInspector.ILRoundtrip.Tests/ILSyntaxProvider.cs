using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotnetInspector.ILRoundtrip.Tests;

/// <summary>
/// Renders signature types in IL assembler syntax (int32, float64,
/// class [asm]Ns.Type, valuetype ..., !!N) for scaffolding generation.
/// </summary>
public sealed class ILSyntaxProvider(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    const byte ValueTypeRawKind = 0x11;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.SByte => "int8",
        PrimitiveTypeCode.Byte => "uint8",
        PrimitiveTypeCode.Int16 => "int16",
        PrimitiveTypeCode.UInt16 => "uint16",
        PrimitiveTypeCode.Int32 => "int32",
        PrimitiveTypeCode.UInt32 => "uint32",
        PrimitiveTypeCode.Int64 => "int64",
        PrimitiveTypeCode.UInt64 => "uint64",
        PrimitiveTypeCode.Single => "float32",
        PrimitiveTypeCode.Double => "float64",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.IntPtr => "native int",
        PrimitiveTypeCode.UIntPtr => "native uint",
        PrimitiveTypeCode.TypedReference => "typedref",
        _ => "object"
    };

    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = r.GetTypeDefinition(handle);
        string ns = r.GetString(td.Namespace);
        string nm = r.GetString(td.Name);
        string full = ns.Length > 0 ? $"{ns}.{nm}" : nm;
        return rawTypeKind == ValueTypeRawKind ? $"valuetype {full}" : $"class {full}";
    }

    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = r.GetTypeReference(handle);
        string ns = r.GetString(tr.Namespace);
        string nm = r.GetString(tr.Name);
        string scope = tr.ResolutionScope.Kind == HandleKind.AssemblyReference
            ? $"[{r.GetString(r.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name)}]"
            : "";
        string full = ns.Length > 0 ? $"{scope}{ns}.{nm}" : $"{scope}{nm}";
        return rawTypeKind == ValueTypeRawKind ? $"valuetype {full}" : $"class {full}";
    }

    public string GetTypeFromSpecification(MetadataReader r, object? ctx, TypeSpecificationHandle handle, byte rawTypeKind)
        => r.GetTypeSpecification(handle).DecodeSignature(this, ctx);

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => $"{elementType} pinned";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";

    public string GetGenericMethodParameter(object? ctx, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? ctx, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method void *()";

    /// <summary>Renders a catch-type handle for an exception-region directive.</summary>
    public string RenderTypeHandle(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        _ => "[System.Runtime]System.Object"
    };
}
