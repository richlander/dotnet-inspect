using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class StructuralSubjectIdentityTests
{
    [Fact]
    public void KindVocabulary_IsClosedAndStructurallyOrdered()
    {
        Assert.Equal(
            [
                StructuralSubjectKind.Root,
                StructuralSubjectKind.Library,
                StructuralSubjectKind.Type,
                StructuralSubjectKind.Member,
            ],
            Enum.GetValues<StructuralSubjectKind>());
    }

    [Fact]
    public void Identities_BindExactOwnerIssuedComponents()
    {
        RealizedMemberCoordinate.Package firstCoordinate =
            Coordinate("1.0.0");
        RealizedMemberCoordinate.Package equalCoordinate =
            Coordinate("1.0.0");
        RealizedMemberCoordinate.Package otherCoordinate =
            Coordinate("2.0.0");
        WorkspaceContextMember firstLibrary =
            Library(firstCoordinate, "Library");
        WorkspaceContextMember equalLibrary = new(
            WorkspaceMemberCoordinate.Package(
                "sample.package",
                version: null,
                framework: "net11.0",
                runtimeIdentifier: null),
            equalCoordinate,
            firstLibrary.Participant);
        WorkspaceContextMember otherCoordinateLibrary = new(
            firstLibrary.Declared,
            otherCoordinate,
            firstLibrary.Participant);
        WorkspaceContextMember otherLibrary =
            Library(firstCoordinate, "Library");
        MetadataTypeDefinitionName firstType = TypeName("Sample", "Widget");
        MetadataTypeDefinitionName equalType = TypeName("Sample", "Widget");
        MetadataTypeDefinitionName otherType = TypeName("Sample", "Other");

        StructuralSubjectIdentity.RootSubject root =
            StructuralSubjectIdentity.ForRoot(firstCoordinate);
        StructuralSubjectIdentity.AllLibrariesSubject allLibraries =
            StructuralSubjectIdentity.ForAllLibraries(firstCoordinate);
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(firstLibrary);
        StructuralSubjectIdentity.TypeSubject type =
            StructuralSubjectIdentity.ForType(
                library,
                firstType);

        Assert.Equal(StructuralSubjectKind.Root, root.Kind);
        Assert.True(root.IsPortable);
        Assert.Equal(StructuralSubjectKind.Library, allLibraries.Kind);
        Assert.True(allLibraries.IsPortable);
        Assert.Equal(StructuralSubjectKind.Library, library.Kind);
        Assert.False(library.IsPortable);
        Assert.Equal(StructuralSubjectKind.Type, type.Kind);
        Assert.False(type.IsPortable);
        Assert.Equal(
            root,
            StructuralSubjectIdentity.ForRoot(equalCoordinate));
        Assert.NotEqual(
            root,
            StructuralSubjectIdentity.ForRoot(otherCoordinate));
        Assert.NotEqual<StructuralSubjectIdentity>(
            allLibraries,
            library);
        Assert.NotEqual<StructuralSubjectIdentity>(
            root,
            allLibraries);
        Assert.Equal(
            library,
            StructuralSubjectIdentity.ForLibrary(equalLibrary));
        Assert.NotEqual(
            library,
            StructuralSubjectIdentity.ForLibrary(otherLibrary));
        Assert.Equal(
            type,
            StructuralSubjectIdentity.ForType(
                StructuralSubjectIdentity.ForLibrary(
                    equalLibrary),
                equalType));
        Assert.NotEqual(
            type,
            StructuralSubjectIdentity.ForType(
                StructuralSubjectIdentity.ForLibrary(
                    otherLibrary),
                firstType));
        Assert.NotEqual(
            type,
            StructuralSubjectIdentity.ForType(
                library,
                otherType));
        Assert.NotEqual(
            type,
            StructuralSubjectIdentity.ForType(
                StructuralSubjectIdentity.ForLibrary(
                    otherCoordinateLibrary),
                firstType));

        var identities = new HashSet<StructuralSubjectIdentity>
        {
            root,
            allLibraries,
            library,
            type,
        };
        Assert.Contains(
            StructuralSubjectIdentity.ForRoot(equalCoordinate),
            identities);
        Assert.Contains(
            StructuralSubjectIdentity.ForLibrary(
                equalLibrary),
            identities);
        Assert.Equal(4, identities.Count);
    }

    [Fact]
    public void MemberIdentity_BindsExactDeclaringTypeAndAnchor()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        WorkspaceContextMember library = Library(coordinate, "Library");
        StructuralSubjectIdentity.LibrarySubject librarySubject =
            StructuralSubjectIdentity.ForLibrary(library);
        StructuralSubjectIdentity.TypeSubject firstType =
            StructuralSubjectIdentity.ForType(
                librarySubject,
                TypeName("Sample", "Widget"));
        StructuralSubjectIdentity.TypeSubject equalType =
            StructuralSubjectIdentity.ForType(
                StructuralSubjectIdentity.ForLibrary(
                    new WorkspaceContextMember(
                        library.Declared,
                        Coordinate("1.0.0"),
                        library.Participant)),
                TypeName("Sample", "Widget"));
        MemberAnchor firstAnchor = Anchor("Sample.Widget", "Run");
        MemberAnchor equalAnchor = Anchor("Sample.Widget", "Run");
        MemberAnchor otherAnchor = Anchor("Sample.Widget", "Stop");

        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(firstType, firstAnchor);

        Assert.Equal(StructuralSubjectKind.Member, member.Kind);
        Assert.False(member.IsPortable);
        Assert.Same(firstType, member.DeclaringType);
        Assert.Same(firstType.Coordinate, member.Coordinate);
        Assert.Equal(
            member,
            StructuralSubjectIdentity.ForMember(equalType, equalAnchor));
        Assert.NotEqual(
            member,
            StructuralSubjectIdentity.ForMember(firstType, otherAnchor));
    }

    [Fact]
    public void Construction_RejectsAbsentOwnerIssuedComponents()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        WorkspaceContextMember library = Library(coordinate, "Library");
        StructuralSubjectIdentity.LibrarySubject librarySubject =
            StructuralSubjectIdentity.ForLibrary(library);
        MetadataTypeDefinitionName type = TypeName("Sample", "Widget");
        StructuralSubjectIdentity.TypeSubject typeSubject =
            StructuralSubjectIdentity.ForType(librarySubject, type);

        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForRoot(null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForAllLibraries(null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForLibrary(null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForType(
                null!,
                type));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForType(
                librarySubject,
                null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForMember(null!, Anchor(
                "Sample.Widget",
                "Run")));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForMember(typeSubject, null!));
    }

    static RealizedMemberCoordinate.Package Coordinate(string version) =>
        new(
            "sample.package",
            version,
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);

    static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate,
        string name)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream([0], writable: false),
            AssemblyResolutionProvenance.Package(
                "sample.package",
                "1.0.0",
                "net11.0",
                rid: null));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    static MemberAnchor Anchor(string type, string member) =>
        new(
            $"{member}()",
            $"{type}.{member}()",
            MemberAnchor.ComputeFingerprint($"{type}.{member}()"),
            type,
            member);
}
