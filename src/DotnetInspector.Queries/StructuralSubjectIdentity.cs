using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspector.SourceSelection;

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

    /// <summary>Creates one exact acquired Library outside Package navigation.</summary>
    public static ContextLibrarySubject ForContextLibrary(
        WorkspaceSubject workspace,
        NavigationAssemblyIdentity identity,
        ExactLibrarySourceCoordinate? coordinate) =>
        new(workspace, identity, coordinate);

    /// <summary>Creates one exact metadata Type subject.</summary>
    public static TypeSubject ForType(
        LibrarySubject library,
        MetadataTypeDefinitionName type) =>
        new(library, type);

    /// <summary>Creates one exact metadata Type in a context Library.</summary>
    public static ContextTypeSubject ForContextType(
        ContextLibrarySubject library,
        MetadataTypeDefinitionName type) =>
        new(library, type);

    /// <summary>
    /// Creates one exact metadata Type referenced outside the acquired
    /// candidate population.
    /// </summary>
    public static ReferencedTypeSubject ForReferencedType(
        WorkspaceSubject workspace,
        AssemblyReferenceIdentity assembly,
        MetadataTypeDefinitionName type) =>
        new(workspace, assembly, type);

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

        /// <inheritdoc />
        public override string ToString() =>
            $"{nameof(WorkspaceSubject)} {{ {nameof(Kind)} = {Kind}, " +
            $"{nameof(IsPortable)} = {IsPortable}, " +
            $"{nameof(Identity)} = {Identity} }}";
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
            ResolvedAssemblyReference assembly = library.Participant.Assembly;
            Identity = new NavigationAssemblyIdentity(
                assembly.Registration, assembly.Identity, assembly.Provenance);
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
        public NavigationAssemblyIdentity Identity { get; }
    }

    /// <summary>
    /// One exact acquired Library in a Workspace declaration context.
    /// </summary>
    public sealed record ContextLibrarySubject : StructuralSubjectIdentity
    {
        internal ContextLibrarySubject(
            WorkspaceSubject workspace,
            NavigationAssemblyIdentity identity,
            ExactLibrarySourceCoordinate? coordinate)
        {
            Workspace = workspace
                ?? throw new ArgumentNullException(nameof(workspace));
            Identity = identity
                ?? throw new ArgumentNullException(nameof(identity));
            Coordinate = coordinate;
        }

        public override WorkspaceSubject Workspace { get; }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Library;

        public NavigationAssemblyIdentity Identity { get; }

        public ExactLibrarySourceCoordinate? Coordinate { get; }
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
            Identity = new NavigationTypeIdentity(
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
        public NavigationTypeIdentity Identity
        {
            get;
        }
    }

    /// <summary>
    /// One exact metadata Type in a source-neutral Workspace Library.
    /// </summary>
    public sealed record ContextTypeSubject : StructuralSubjectIdentity
    {
        internal ContextTypeSubject(
            ContextLibrarySubject library,
            MetadataTypeDefinitionName type)
        {
            Library = library
                ?? throw new ArgumentNullException(nameof(library));
            Identity = new NavigationTypeIdentity(
                library.Identity.Registration,
                type ?? throw new ArgumentNullException(nameof(type)));
        }

        public override WorkspaceSubject Workspace => Library.Workspace;

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Type;

        public ContextLibrarySubject Library { get; }

        public NavigationTypeIdentity Identity { get; }
    }

    /// <summary>
    /// One exact metadata Type referenced by an acquired candidate Library.
    /// </summary>
    public sealed record ReferencedTypeSubject : StructuralSubjectIdentity
    {
        internal ReferencedTypeSubject(
            WorkspaceSubject workspace,
            AssemblyReferenceIdentity assembly,
            MetadataTypeDefinitionName type)
        {
            Workspace = workspace
                ?? throw new ArgumentNullException(nameof(workspace));
            Assembly = assembly
                ?? throw new ArgumentNullException(nameof(assembly));
            Type = type
                ?? throw new ArgumentNullException(nameof(type));
        }

        public override WorkspaceSubject Workspace { get; }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Type;

        public AssemblyReferenceIdentity Assembly { get; }

        public MetadataTypeDefinitionName Type { get; }
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
            Identity = new NavigationMemberIdentity(
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
        public NavigationMemberIdentity Identity { get; }
    }
}
