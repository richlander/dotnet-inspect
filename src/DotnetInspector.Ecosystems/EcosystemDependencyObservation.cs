using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems;

/// <summary>
/// Document-local identity for one direct-dependency observation occurrence.
/// </summary>
public readonly record struct EcosystemDependencyObservationIdentity
{
    public EcosystemDependencyObservationIdentity(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One owner-issued direct-dependency occurrence.</summary>
public abstract record EcosystemDependencyObservation
{
    private protected EcosystemDependencyObservation(
        EcosystemDependencyObservationIdentity identity,
        int sourceOrder)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceOrder, 1);
        Identity = identity;
        SourceOrder = sourceOrder;
    }

    public EcosystemDependencyObservationIdentity Identity { get; }

    public int SourceOrder { get; }

    internal abstract EcosystemDependencyIdentityDomain Domain { get; }

    internal abstract string MatchValue { get; }

    /// <summary>One direct Package declaration from the inspected Package.</summary>
    public sealed record PackageDeclaration : EcosystemDependencyObservation
    {
        public PackageDeclaration(
            EcosystemDependencyObservationIdentity identity,
            int sourceOrder,
            DeclaredPackageDependency dependency,
            RealizedMemberCoordinate.Package declaringPackage)
            : base(identity, sourceOrder)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            ArgumentNullException.ThrowIfNull(declaringPackage);
            ArgumentException.ThrowIfNullOrWhiteSpace(dependency.Id);
            Dependency = dependency;
            DeclaringPackage = declaringPackage;
        }

        public DeclaredPackageDependency Dependency { get; }

        public RealizedMemberCoordinate.Package DeclaringPackage { get; }

        internal override EcosystemDependencyIdentityDomain Domain =>
            EcosystemDependencyIdentityDomain.PackageId;

        internal override string MatchValue => Dependency.Id;
    }

    /// <summary>One direct assembly reference from the inspected Library.</summary>
    public sealed record AssemblyReference : EcosystemDependencyObservation
    {
        public AssemblyReference(
            EcosystemDependencyObservationIdentity identity,
            int sourceOrder,
            AssemblyReferenceIdentity reference,
            PortableLibraryIdentity declaringLibrary)
            : base(identity, sourceOrder)
        {
            ArgumentNullException.ThrowIfNull(reference);
            ArgumentNullException.ThrowIfNull(declaringLibrary);
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.Name);
            Reference = reference;
            DeclaringLibrary = declaringLibrary;
        }

        public AssemblyReferenceIdentity Reference { get; }

        public PortableLibraryIdentity DeclaringLibrary { get; }

        internal override EcosystemDependencyIdentityDomain Domain =>
            EcosystemDependencyIdentityDomain.AssemblyName;

        internal override string MatchValue => Reference.Name;
    }
}
