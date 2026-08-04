using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Decoder-retained metadata scope for one named type reference. This is
/// provenance for resolution, not part of <see cref="TypeRef"/> shape equality.
/// </summary>
public abstract record TypeReferenceOrigin
{
    private protected TypeReferenceOrigin()
    {
    }

    private protected abstract int Discriminator { get; }

    public sealed record AssemblyReference : TypeReferenceOrigin
    {
        internal AssemblyReference(AssemblyReferenceIdentity assembly) =>
            Assembly = assembly;

        public AssemblyReferenceIdentity Assembly { get; }
        private protected override int Discriminator => 0;
    }

    public sealed record CurrentAssembly : TypeReferenceOrigin
    {
        internal CurrentAssembly()
        {
        }

        private protected override int Discriminator => 1;
    }

    public sealed record IntrinsicCoreLibrary : TypeReferenceOrigin
    {
        internal IntrinsicCoreLibrary()
        {
        }

        private protected override int Discriminator => 2;
    }

    public sealed record ModuleReference : TypeReferenceOrigin
    {
        internal ModuleReference(string moduleName) => ModuleName = moduleName;

        public string ModuleName { get; }
        private protected override int Discriminator => 3;
    }
}

/// <summary>
/// Exact metadata lookup name paired with the scope that supplied it.
/// Resolution also requires the candidate image that supplied the row.
/// </summary>
public sealed record ResolvableTypeReference(
    TypeReferenceOrigin Origin,
    MetadataTypeDefinitionName Type);
