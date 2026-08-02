using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Structured evidence describing how an assembly candidate was selected.</summary>
public abstract record AssemblyResolutionProvenance
{
    private protected AssemblyResolutionProvenance()
    {
    }

    private protected abstract int Discriminator { get; }

    public static AssemblyResolutionProvenance Package(
        string packageId,
        string packageVersion,
        string? tfm,
        string? rid) =>
        new PackageAsset(packageId, packageVersion, tfm, rid);

    public static AssemblyResolutionProvenance Platform(
        string framework,
        string? frameworkVersion,
        string resolverSource) =>
        new PlatformAsset(framework, frameworkVersion, resolverSource);

    public static AssemblyResolutionProvenance Project(
        string project,
        string? tfm,
        string? rid) =>
        new ProjectAsset(project, tfm, rid);

    public static AssemblyResolutionProvenance Local(string resolverSource) =>
        new LocalAsset(resolverSource);

    public sealed record PackageAsset : AssemblyResolutionProvenance
    {
        public PackageAsset(
            string packageId,
            string packageVersion,
            string? tfm,
            string? rid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
            ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
            PackageId = packageId;
            PackageVersion = packageVersion;
            Tfm = tfm;
            Rid = rid;
        }

        private protected override int Discriminator => 0;
        public string PackageId { get; }
        public string PackageVersion { get; }
        public string? Tfm { get; }
        public string? Rid { get; }
    }

    public sealed record PlatformAsset : AssemblyResolutionProvenance
    {
        public PlatformAsset(
            string framework,
            string? frameworkVersion,
            string resolverSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(framework);
            ArgumentException.ThrowIfNullOrWhiteSpace(resolverSource);
            Framework = framework;
            FrameworkVersion = frameworkVersion;
            ResolverSource = resolverSource;
        }

        private protected override int Discriminator => 1;
        public string Framework { get; }
        public string? FrameworkVersion { get; }
        public string ResolverSource { get; }
    }

    public sealed record ProjectAsset : AssemblyResolutionProvenance
    {
        public ProjectAsset(
            string project,
            string? tfm,
            string? rid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(project);
            Project = project;
            Tfm = tfm;
            Rid = rid;
        }

        private protected override int Discriminator => 2;
        public new string Project { get; }
        public string? Tfm { get; }
        public string? Rid { get; }
    }

    public sealed record LocalAsset : AssemblyResolutionProvenance
    {
        public LocalAsset(string resolverSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resolverSource);
            ResolverSource = resolverSource;
        }

        private protected override int Discriminator => 3;
        public string ResolverSource { get; }
    }
}

/// <summary>
/// Opaque reference-identity handle minted with one canonical acquisition
/// descriptor.
/// </summary>
public sealed class AssemblyAcquisitionRegistration
{
    internal AssemblyAcquisitionRegistration()
    {
    }
}

/// <summary>
/// Roslyn-free descriptor for one assembly selected by an acquisition owner.
/// </summary>
public sealed class ResolvedAssemblyReference
{
    ResolvedAssemblyReference(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity identity,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance)
    {
        Registration = registration;
        Identity = identity;
        Path = path;
        OpenRead = openRead;
        Provenance = provenance;
    }

    public static ResolvedAssemblyReference Create(
        AssemblyReferenceIdentity selectedIdentity,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(selectedIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedIdentity.Name);
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(provenance);

        return new ResolvedAssemblyReference(
            new AssemblyAcquisitionRegistration(),
            selectedIdentity,
            path,
            openRead,
            provenance);
    }

    public static ResolvedAssemblyReference CreateFromPath(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(provenance);

        string fullPath = System.IO.Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        using var peReader =
            new System.Reflection.PortableExecutable.PEReader(stream);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException(
                "The selected image has no managed metadata.");

        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                peReader.GetMetadataReader());
        return Create(
            identity,
            fullPath,
            () => File.OpenRead(fullPath),
            provenance);
    }

    public static bool TryCreateFromPath(
        string path,
        AssemblyResolutionProvenance provenance,
        [NotNullWhen(true)] out ResolvedAssemblyReference? reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(provenance);

        try
        {
            reference = CreateFromPath(path, provenance);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
        {
            reference = null;
            return false;
        }
    }

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public string? Path { get; }
    public Func<Stream> OpenRead { get; }
    public AssemblyResolutionProvenance Provenance { get; }
}

/// <summary>Identifies one acquisition catalog's candidate key space.</summary>
public readonly record struct AssemblyCatalogId(Guid Value);

internal readonly record struct AssemblyCandidateId(Guid Value);

/// <summary>One descriptor interned by a Metadata-owned acquisition catalog.</summary>
public sealed class ResolvedAssemblyCandidate
{
    internal ResolvedAssemblyCandidate(
        AssemblyCatalogId catalog,
        AssemblyCandidateId id,
        ResolvedAssemblyReference assembly)
    {
        Catalog = catalog;
        Id = id;
        Assembly = assembly;
    }

    internal AssemblyCatalogId Catalog { get; }
    internal AssemblyCandidateId Id { get; }
    public ResolvedAssemblyReference Assembly { get; }
}
