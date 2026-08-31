using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>The ordered structural level of one inspection subject.</summary>
public enum StructuralSubjectKind
{
    Root,
    Library,
    Type,
    Member,
}

/// <summary>
/// One exact structural subject inside a realized inspection coordinate.
/// </summary>
/// <remarks>
/// Display text, list position, filenames, and metadata tokens alone are not
/// identity. Construction composes owner-issued identity currencies, and the
/// sealed variants make kind, parent, and identity shape agree by construction.
/// </remarks>
public abstract record StructuralSubjectIdentity
{
    private protected StructuralSubjectIdentity(
        RealizedMemberCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        Coordinate = coordinate;
    }

    /// <summary>The realized input containing this subject.</summary>
    public RealizedMemberCoordinate Coordinate { get; }

    /// <summary>The subject's structural level.</summary>
    public abstract StructuralSubjectKind Kind { get; }

    /// <summary>
    /// Whether this identity can cross the loaded-workspace boundary.
    /// </summary>
    public abstract bool IsPortable { get; }

    /// <summary>Creates the product-owned root for one realized coordinate.</summary>
    public static RootSubject ForRoot(RealizedMemberCoordinate coordinate) =>
        new(coordinate);

    /// <summary>
    /// Creates the explicit aggregate over all admitted libraries.
    /// </summary>
    public static AllLibrariesSubject ForAllLibraries(
        RealizedMemberCoordinate coordinate) =>
        new(coordinate);

    /// <summary>Creates one exact acquired Library subject.</summary>
    public static LibrarySubject ForLibrary(WorkspaceContextMember library) =>
        new(library);

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

    /// <summary>One realized coordinate root.</summary>
    public sealed record RootSubject : StructuralSubjectIdentity
    {
        internal RootSubject(RealizedMemberCoordinate coordinate)
            : base(coordinate)
        {
        }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Root;

        public override bool IsPortable => true;
    }

    /// <summary>The explicit aggregate over all admitted libraries.</summary>
    public sealed record AllLibrariesSubject : StructuralSubjectIdentity
    {
        internal AllLibrariesSubject(RealizedMemberCoordinate coordinate)
            : base(coordinate)
        {
        }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Library;

        public override bool IsPortable => true;
    }

    /// <summary>One exact acquired Library.</summary>
    public sealed record LibrarySubject : StructuralSubjectIdentity
    {
        internal LibrarySubject(
            WorkspaceContextMember library)
            : base(RequireLibrary(library).Realized)
        {
            Identity = new InspectionGraphAssemblyIdentity.Acquired(
                library.Participant.Assembly);
        }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Library;

        public override bool IsPortable => false;

        /// <summary>
        /// The exact acquired assembly identity that identifies the Library.
        /// </summary>
        public InspectionGraphAssemblyIdentity.Acquired Identity { get; }

        static WorkspaceContextMember RequireLibrary(
            WorkspaceContextMember? library)
        {
            ArgumentNullException.ThrowIfNull(library);
            return library;
        }
    }

    /// <summary>One exact metadata Type in one acquired Library.</summary>
    public sealed record TypeSubject : StructuralSubjectIdentity
    {
        internal TypeSubject(
            LibrarySubject library,
            MetadataTypeDefinitionName type)
            : base(RequireLibrary(library).Coordinate)
        {
            ArgumentNullException.ThrowIfNull(type);
            Library = library;
            Identity = new InspectionGraphTypeIdentity.AcquiredDefinition(
                library.Identity.Registration,
                type);
        }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Type;

        public override bool IsPortable => false;

        /// <summary>The exact acquired Library containing the Type.</summary>
        public LibrarySubject Library { get; }

        /// <summary>The exact acquired metadata Type identity.</summary>
        public InspectionGraphTypeIdentity.AcquiredDefinition Identity
        {
            get;
        }

        static LibrarySubject RequireLibrary(LibrarySubject? library)
        {
            ArgumentNullException.ThrowIfNull(library);
            return library;
        }
    }

    /// <summary>One exact API Member in one exact Type.</summary>
    public sealed record MemberSubject : StructuralSubjectIdentity
    {
        internal MemberSubject(
            TypeSubject declaringType,
            MemberAnchor member)
            : base(RequireDeclaringType(declaringType).Coordinate)
        {
            ArgumentNullException.ThrowIfNull(member);
            DeclaringType = declaringType;
            Identity = new InspectionGraphMemberIdentity.AcquiredApi(
                declaringType.Identity.Registration,
                declaringType.Identity.Type,
                member);
        }

        public override StructuralSubjectKind Kind =>
            StructuralSubjectKind.Member;

        public override bool IsPortable => false;

        /// <summary>The exact Type containing the Member.</summary>
        public TypeSubject DeclaringType { get; }

        /// <summary>The exact acquired API Member identity.</summary>
        public InspectionGraphMemberIdentity.AcquiredApi Identity { get; }

        static TypeSubject RequireDeclaringType(TypeSubject? declaringType)
        {
            ArgumentNullException.ThrowIfNull(declaringType);
            return declaringType;
        }
    }
}
