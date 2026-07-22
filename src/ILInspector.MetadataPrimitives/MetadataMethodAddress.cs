using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.MetadataPrimitives;

/// <summary>
/// A method-definition handle scoped to its physical metadata module. A raw
/// handle contains only a table row and cannot detect use with another reader.
/// <para>
/// Scoping uses the module version id (MVID). <see cref="BelongsTo"/> confirms a
/// reader carries the same MVID before its handle is dereferenced; it is not a
/// cryptographic identity, so two byte-distinct modules that deliberately share
/// an MVID (for example a rewritten or adversarial pair) can both satisfy it.
/// This never yields an out-of-range read: every consumer additionally
/// validates the handle's row against the target reader's <c>MethodDef</c> table
/// before use, so an MVID collision can at worst select a same-row method in the
/// wrong module, never read outside the module.
/// </para>
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
