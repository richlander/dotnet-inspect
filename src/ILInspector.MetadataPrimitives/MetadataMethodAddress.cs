using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.MetadataPrimitives;

/// <summary>
/// A method-definition handle scoped to its physical metadata module. A raw
/// handle contains only a table row and cannot detect use with another reader.
/// </summary>
public readonly record struct MetadataMethodAddress(
    Guid ModuleVersionId,
    MethodDefinitionHandle Handle)
{
    public int Token => MetadataTokens.GetToken(Handle);

    public static MetadataMethodAddress Create(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        var module = reader.GetModuleDefinition();
        return new MetadataMethodAddress(reader.GetGuid(module.Mvid), handle);
    }

    public bool BelongsTo(MetadataReader reader)
    {
        var module = reader.GetModuleDefinition();
        return ModuleVersionId == reader.GetGuid(module.Mvid);
    }
}
