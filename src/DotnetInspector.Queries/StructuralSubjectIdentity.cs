using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>The immutable structural level of one inspection subject.</summary>
public enum StructuralSubjectKind
{
    Workspace,
    Package,
    Library,
    Type,
    Member,
}

/// <summary>
/// One exact structural subject inside one live inspection Workspace.
/// </summary>
/// <remarks>
/// Display text, list position, filenames, portable coordinates, and metadata
/// tokens alone are not identity. Construction composes owner-issued identity
/// currencies, and the sealed variants make kind, ancestry, and identity shape
/// agree by construction.
/// </remarks>
public abstract record StructuralSubjectIdentity
{
    private protected StructuralSubjectIdentity()
    {
    }

    /// <summary>The exact Workspace subject containing this subject.</summary>
    public abstract WorkspaceSubject Workspace { get; }

    /// <summary>The subject's structural level.</summary>
    public abstract StructuralSubjectKind Kind { get; }

    /// <summary>
    /// Whether this process-local identity can cross the loaded-Workspace boundary.
    /// </summary>
    public bool IsPortable => false;

    /// <summary>Creates the subject for one exact open Workspace.</summary>
    public static WorkspaceSubject ForWorkspace(
        InspectionWorkspaceIdentity workspace) =>
        new(workspace);

    /// <summary>Creates one exact retained Package subject.</summary>
    public static PackageSubject ForPackage(
        WorkspaceSubject workspace,
        WorkspacePackageOccurrence occurrence) =>
        new(workspace, occurrence);

    /// <summary>
    /// Creates the explicit aggregate over all admitted libraries in a Package.
    /// </summary>
    public static AllLibrariesSubject ForAllLibraries(
        PackageSubject package) =>
        new(package);

    /// <summary>Creates one exact acquired Library subject.</summary>
    public static LibrarySubject ForLibrary(
        PackageSubject package,
        WorkspaceContextMember library) =>
        new(package, library);

    /// <summary>Creates one exact metadata Type subject.</summary>
    public static TypeSubject ForType(
        LibrarySubject library,
        MetadataTypeDefinitionName type) =>
        new(library, type);

    /// <summary>Creates one exact API Member subject.</summary>
    public static MemberSubject ForMember(
        TypeSubject declaringType,
        MemberAnchor member) =>
        new(declaringType, member);

    /// <summary>One exact live Workspace.</summary>
    public sealed record WorkspaceSubject : StructuralSubjectIdentity
    {
        internal WorkspaceSubject(InspectionWorkspaceIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public override WorkspaceSubject Workspace => this;

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Workspace;

        /// <summary>The Artifact-owner identity of the exact Workspace.</summary>
        public InspectionWorkspaceIdentity Identity { get; }
    }

    /// <summary>One exact retained Package occurrence.</summary>
    public sealed record PackageSubject : StructuralSubjectIdentity
    {
        internal PackageSubject(
            WorkspaceSubject workspace,
            WorkspacePackageOccurrence occurrence)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(occurrence);
            if (!ReferenceEquals(
                    occurrence.Identity.WorkspaceIdentity,
                    workspace.Identity))
            {
                throw new ArgumentException(
                    "The Package occurrence must belong to the exact Workspace.",
                    nameof(occurrence));
            }

            Workspace = workspace;
            Occurrence = occurrence;
        }

        public override WorkspaceSubject Workspace { get; }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Package;

        /// <summary>The complete Scope-issued retained Package occurrence.</summary>
        public WorkspacePackageOccurrence Occurrence { get; }

        /// <summary>The Scope-issued descriptive Package facts.</summary>
        public WorkspacePackageDescriptor Descriptor => Occurrence.Package;

        /// <summary>The portable coordinate projected by the Package descriptor.</summary>
        public RealizedMemberCoordinate.Package Coordinate =>
            Descriptor.Coordinate;
    }

    /// <summary>The explicit aggregate over all admitted Package libraries.</summary>
    public sealed record AllLibrariesSubject : StructuralSubjectIdentity
    {
        internal AllLibrariesSubject(PackageSubject package)
        {
            ArgumentNullException.ThrowIfNull(package);
            Package = package;
        }

        public override WorkspaceSubject Workspace => Package.Workspace;

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Library;

        /// <summary>The exact Package whose libraries are aggregated.</summary>
        public PackageSubject Package { get; }

        public RealizedMemberCoordinate.Package Coordinate =>
            Package.Coordinate;
    }

    /// <summary>One exact acquired Library.</summary>
    public sealed record LibrarySubject : StructuralSubjectIdentity
    {
        internal LibrarySubject(
            PackageSubject package,
            WorkspaceContextMember library)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentNullException.ThrowIfNull(library);
            if (library.Realized != package.Coordinate)
            {
                throw new ArgumentException(
                    "The Library must belong to the exact Package coordinate.",
                    nameof(library));
            }

            Package = package;
            Identity = new InspectionGraphAssemblyIdentity.Acquired(
                library.Participant.Assembly);
        }

        public override WorkspaceSubject Workspace => Package.Workspace;

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Library;

        /// <summary>The exact Package containing the Library.</summary>
        public PackageSubject Package { get; }

        public RealizedMemberCoordinate.Package Coordinate =>
            Package.Coordinate;

        /// <summary>
        /// The exact acquired assembly identity that identifies the Library.
        /// </summary>
        public InspectionGraphAssemblyIdentity.Acquired Identity { get; }
    }

    /// <summary>One exact metadata Type in one acquired Library.</summary>
    public sealed record TypeSubject : StructuralSubjectIdentity
    {
        internal TypeSubject(
            LibrarySubject library,
            MetadataTypeDefinitionName type)
        {
            ArgumentNullException.ThrowIfNull(library);
            ArgumentNullException.ThrowIfNull(type);
            Library = library;
            Identity = new InspectionGraphTypeIdentity.AcquiredDefinition(
                library.Identity.Registration,
                type);
        }

        public override WorkspaceSubject Workspace => Library.Workspace;

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Type;

        /// <summary>The exact acquired Library containing the Type.</summary>
        public LibrarySubject Library { get; }

        public RealizedMemberCoordinate.Package Coordinate =>
            Library.Coordinate;

        /// <summary>The exact acquired metadata Type identity.</summary>
        public InspectionGraphTypeIdentity.AcquiredDefinition Identity
        {
            get;
        }
    }

    /// <summary>One exact API Member in one exact Type.</summary>
    public sealed record MemberSubject : StructuralSubjectIdentity
    {
        internal MemberSubject(
            TypeSubject declaringType,
            MemberAnchor member)
        {
            ArgumentNullException.ThrowIfNull(declaringType);
            ArgumentNullException.ThrowIfNull(member);
            DeclaringType = declaringType;
            Identity = new InspectionGraphMemberIdentity.AcquiredApi(
                declaringType.Identity.Registration,
                declaringType.Identity.Type,
                member);
        }

        public override WorkspaceSubject Workspace =>
            DeclaringType.Workspace;

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Member;

        /// <summary>The exact Type containing the Member.</summary>
        public TypeSubject DeclaringType { get; }

        public RealizedMemberCoordinate.Package Coordinate =>
            DeclaringType.Coordinate;

        /// <summary>The exact acquired API Member identity.</summary>
        public InspectionGraphMemberIdentity.AcquiredApi Identity { get; }
    }
}
