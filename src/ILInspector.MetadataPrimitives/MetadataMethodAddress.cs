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
        => new(MetadataModuleIdentity.ReadVersionId(reader), handle);

    public bool BelongsTo(MetadataReader reader)
        => ModuleVersionId == MetadataModuleIdentity.ReadVersionId(reader);

    /// <summary>
    /// Resolves this address only after validating its module and physical
    /// MethodDef row.
    /// </summary>
    public bool TryResolve(
        MetadataReader reader,
        out MethodDefinitionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        handle = default;

        int row = MetadataTokens.GetRowNumber(Handle);
        if (row <= 0 || row > reader.GetTableRowCount(TableIndex.MethodDef))
            return false;

        try
        {
            if (!BelongsTo(reader))
                return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return false;
        }

        handle = MetadataTokens.MethodDefinitionHandle(row);
        return true;
    }
}
