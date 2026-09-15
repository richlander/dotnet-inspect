using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// Reader-independent SRM type-layout values. Zero size and packing are observed
/// defaults, not proof that a ClassLayout row is absent.
/// </summary>
public sealed record ApiTypeLayoutFacts(
    Guid ModuleVersionId,
    int TypeToken,
    int Size,
    int PackingSize)
{
    internal static ApiTypeLayoutFacts Read(
        MetadataReader reader,
        Guid moduleVersionId,
        TypeDefinitionHandle handle)
    {
        TypeLayout layout = reader.GetTypeDefinition(handle).GetLayout();
        return new(moduleVersionId, MetadataTokens.GetToken(handle), layout.Size, layout.PackingSize);
    }
}

/// <summary>
/// Reader-independent field-layout observation. Null Offset means SRM did not
/// provide a usable offset, not proof that a FieldLayout row is absent.
/// </summary>
public sealed record ApiFieldLayoutFacts(
    Guid ModuleVersionId,
    int DeclaringTypeToken,
    int FieldToken,
    int? Offset)
{
    internal static ApiFieldLayoutFacts Read(
        MetadataReader reader,
        Guid moduleVersionId,
        TypeDefinitionHandle declaringType,
        FieldDefinitionHandle handle)
    {
        int offset = reader.GetFieldDefinition(handle).GetOffset();
        return new(
            moduleVersionId,
            MetadataTokens.GetToken(declaringType),
            MetadataTokens.GetToken(handle),
            offset < 0 ? null : offset);
    }
}
