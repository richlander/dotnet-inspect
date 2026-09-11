using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.SourceSelection;

/// <summary>
/// Identifies one exact managed library within a package or platform population.
/// </summary>
public abstract class ExactLibrarySourceCoordinate :
    IEquatable<ExactLibrarySourceCoordinate>
{
    private protected ExactLibrarySourceCoordinate(
        ManagedMetadataIdentity.Assembly libraryIdentity)
    {
        ArgumentNullException.ThrowIfNull(libraryIdentity);
        ArgumentNullException.ThrowIfNull(libraryIdentity.Identity);
        if (libraryIdentity.Identity.Version is null)
        {
            throw new ArgumentException(
                "An exact library coordinate requires an assembly version.",
                nameof(libraryIdentity));
        }

        LibraryIdentity = libraryIdentity;
    }

    /// <summary>Gets the exact managed assembly identity.</summary>
    public ManagedMetadataIdentity.Assembly LibraryIdentity { get; }

    /// <inheritdoc/>
    public bool Equals(ExactLibrarySourceCoordinate? other) =>
        ReferenceEquals(this, other)
        || other is not null
            && GetType() == other.GetType()
            && EqualsCore(other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ExactLibrarySourceCoordinate other
        && Equals(other);

    /// <inheritdoc/>
    public abstract override int GetHashCode();

    private protected abstract bool EqualsCore(
        ExactLibrarySourceCoordinate other);

    private protected static bool LibraryIdentityEquals(
        ManagedMetadataIdentity.Assembly left,
        ManagedMetadataIdentity.Assembly right) =>
        AssemblyReferenceIdentity.EquivalentComparer.Equals(
            left.Identity,
            right.Identity);

    private protected static int LibraryIdentityHashCode(
        ManagedMetadataIdentity.Assembly identity) =>
        AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
            identity.Identity);

    public static bool operator ==(
        ExactLibrarySourceCoordinate? left,
        ExactLibrarySourceCoordinate? right) =>
        EqualityComparer<ExactLibrarySourceCoordinate>.Default.Equals(
            left,
            right);

    public static bool operator !=(
        ExactLibrarySourceCoordinate? left,
        ExactLibrarySourceCoordinate? right) =>
        !(left == right);

    /// <summary>Identifies an exact managed library within one package.</summary>
    public sealed class Package : ExactLibrarySourceCoordinate
    {
        public Package(
            PackageSourceCoordinate packageCoordinate,
            ManagedMetadataIdentity.Assembly libraryIdentity)
            : base(libraryIdentity)
        {
            ArgumentNullException.ThrowIfNull(packageCoordinate);
            PackageCoordinate = packageCoordinate;
        }

        /// <summary>Gets the exact package coordinate.</summary>
        public PackageSourceCoordinate PackageCoordinate { get; }

        private protected override bool EqualsCore(
            ExactLibrarySourceCoordinate other)
        {
            var package = (Package)other;
            return PackageCoordinate == package.PackageCoordinate
                && LibraryIdentityEquals(
                    LibraryIdentity,
                    package.LibraryIdentity);
        }

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                0,
                PackageCoordinate,
                LibraryIdentityHashCode(LibraryIdentity));
    }

    /// <summary>
    /// Identifies an exact managed library within one platform population.
    /// </summary>
    public sealed class Platform : ExactLibrarySourceCoordinate
    {
        public Platform(
            PlatformLibraryPopulationDeclaration population,
            ManagedMetadataIdentity.Assembly libraryIdentity)
            : base(libraryIdentity)
        {
            ArgumentNullException.ThrowIfNull(population);
            Population = population;
        }

        /// <summary>Gets the declared platform population.</summary>
        public PlatformLibraryPopulationDeclaration Population { get; }

        private protected override bool EqualsCore(
            ExactLibrarySourceCoordinate other)
        {
            var platform = (Platform)other;
            return Population == platform.Population
                && LibraryIdentityEquals(
                    LibraryIdentity,
                    platform.LibraryIdentity);
        }

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                1,
                Population,
                LibraryIdentityHashCode(LibraryIdentity));
    }
}
