using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class PlatformStructuralSignatureScope
{
    internal static bool IsTrustedPlatformType(
        MetadataReader reader,
        EntityHandle handle,
        bool currentAssemblyHasPlatformIdentityTrust = false)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                currentAssemblyHasPlatformIdentityTrust
                && reader.IsAssembly
                && PlatformKeys.IsPlatform(
                    AssemblyReferenceIdentity
                        .FromAssemblyDefinition(reader)
                        .PublicKeyToken),
            HandleKind.TypeReference =>
                IsTrustedPlatformReference(
                    reader,
                    (TypeReferenceHandle)handle),
            _ => false,
        };

    static bool IsTrustedPlatformReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _)
            || terminal.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        return PlatformKeys.IsPlatform(
            AssemblyReferenceIdentity.From(
                reader,
                (AssemblyReferenceHandle)terminal)
            .PublicKeyToken);
    }
}
