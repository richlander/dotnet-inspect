using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Metadata identity for the physical module that produced one
/// <see cref="LibraryBodyIndex"/>.
/// </summary>
/// <remarks>
/// This is image-derived evidence, not a path, display label, artifact
/// authenticity claim, or persistent cache key. <see cref="AssemblyIdentity"/>
/// is null only for a standalone managed module.
/// </remarks>
public sealed record LibraryBodyModuleIdentity
{
    internal LibraryBodyModuleIdentity(
        AssemblyReferenceIdentity? assemblyIdentity,
        Guid moduleVersionId)
    {
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
    }

    /// <summary>
    /// Exact assembly-definition identity, or null for a standalone managed
    /// module.
    /// </summary>
    public AssemblyReferenceIdentity? AssemblyIdentity { get; }

    /// <summary>The module version identifier read from the same image.</summary>
    public Guid ModuleVersionId { get; }

    internal static LibraryBodyModuleIdentity FromImage(
        MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Guid moduleVersionId = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        if (moduleVersionId == Guid.Empty)
        {
            throw new BadImageFormatException(
                "The analyzed module has an empty module version identifier.");
        }

        return new(
            reader.IsAssembly
                ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                : null,
            moduleVersionId);
    }
}
