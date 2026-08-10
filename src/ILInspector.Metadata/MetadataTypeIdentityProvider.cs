using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Decodes presentation-independent signature type identity while retaining the
/// defining assembly for named types.
/// </summary>
internal sealed class MetadataTypeIdentityProvider
    : ISignatureTypeProvider<string, GenericContext?>
{
    public static MetadataTypeIdentityProvider Instance { get; } = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        => Named(
            "corelib",
            PrimitiveTypeNames.ToClrFullName(
                SignatureDecoder.Instance.GetPrimitiveType(typeCode)));

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
        => Named(
            CurrentAssembly(reader),
            SignatureDecoder.Instance.GetTypeFromDefinition(reader, handle, rawTypeKind));

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        string assembly = CurrentAssembly(reader);
        EntityHandle scope = reader.GetTypeReference(handle).ResolutionScope;
        int depth = 0;
        while (scope.Kind == HandleKind.TypeReference
            && depth++ < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            scope = reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        }
        if (scope.Kind == HandleKind.TypeReference)
            return "<degraded>";
        if (scope.Kind == HandleKind.AssemblyReference)
        {
            assembly = reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
        }

        return Named(
            assembly,
            SignatureDecoder.Instance.GetTypeFromReference(reader, handle, rawTypeKind));
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        GenericContext? context,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return "<degraded>";
        using (scope)
            return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    }

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape)
        => $"{elementType}[{new string(',', Math.Max(0, shape.Rank - 1))}]";
    public string GetByReferenceType(string elementType) => $"{elementType}@";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => unmodifiedType;

    public string GetGenericMethodParameter(GenericContext? context, int index)
        => $"M{index}";

    public string GetGenericTypeParameter(GenericContext? context, int index)
        => $"T{index}";

    public string GetGenericInstantiation(
        string genericType,
        ImmutableArray<string> typeArguments)
    {
        int close = genericType.IndexOf(']');
        string assembly = close >= 0 ? genericType[..(close + 1)] : "";
        string typeName = close >= 0 ? genericType[(close + 1)..] : genericType;
        string[] segments = typeName.Split('.');
        int totalArity = segments.Sum(Arity);
        if (totalArity != typeArguments.Length)
        {
            return $"{assembly}{string.Join('.', segments.Select(StripArity))}"
                + $"{{{string.Join(",", typeArguments)}}}";
        }

        var result = new StringBuilder(assembly);
        int argumentIndex = 0;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segmentIndex > 0)
                result.Append('.');
            string segment = segments[segmentIndex];
            result.Append(StripArity(segment));
            int arity = Arity(segment);
            if (arity <= 0)
                continue;
            result.Append('{');
            for (int index = 0; index < arity; index++)
            {
                if (index > 0)
                    result.Append(',');
                result.Append(typeArguments[argumentIndex++]);
            }
            result.Append('}');
        }
        return result.ToString();
    }

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        string convention = signature.Header.CallingConvention switch
        {
            SignatureCallingConvention.Default => "",
            SignatureCallingConvention.CDecl => " unmanaged[Cdecl]",
            SignatureCallingConvention.StdCall => " unmanaged[Stdcall]",
            SignatureCallingConvention.ThisCall => " unmanaged[Thiscall]",
            SignatureCallingConvention.FastCall => " unmanaged[Fastcall]",
            _ => " unmanaged",
        };
        return $"delegate*{convention}{{{string.Join(
            ",",
            signature.ParameterTypes.Append(signature.ReturnType))}}}";
    }

    static string Named(string assembly, string name)
        => $"[{CanonicalAssembly(assembly)}]{name.Replace('+', '.')}";

    static string CurrentAssembly(MetadataReader reader)
        => reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : "";

    static string CanonicalAssembly(string assembly)
        => assembly is "System.Private.CoreLib"
            or "System.Runtime"
            or "mscorlib"
            or "netstandard"
            or "System.Runtime.Extensions"
                ? "corelib"
                : assembly;

    static string StripArity(string value)
    {
        int tick = value.IndexOf('`');
        return tick < 0 ? value : value[..tick];
    }

    static int Arity(string value)
    {
        int tick = value.IndexOf('`');
        return tick >= 0
            && int.TryParse(value[(tick + 1)..], out int arity)
                ? arity
                : 0;
    }
}
