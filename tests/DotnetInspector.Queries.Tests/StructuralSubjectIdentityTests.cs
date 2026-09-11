using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class StructuralSubjectIdentityTests
{
    [Fact]
    public void KindVocabulary_IsClosedAndWorkspaceRooted()
    {
        Assert.Equal(
            [
                StructuralSubjectKind.Workspace,
                StructuralSubjectKind.Package,
                StructuralSubjectKind.Library,
                StructuralSubjectKind.Type,
                StructuralSubjectKind.Member,
            ],
            Enum.GetValues<StructuralSubjectKind>());
    }

    [Fact]
    public void WorkspaceSubject_BindsOneExactWorkspaceOccurrence()
    {
        using var owner = new InspectionWorkspace();
        using var otherOwner = new InspectionWorkspace();
        StructuralSubjectIdentity.WorkspaceSubject workspace =
            StructuralSubjectIdentity.ForWorkspace(owner.Identity);

        Assert.Same(owner.Identity, workspace.Identity);
        Assert.Same(workspace, workspace.Workspace);
        Assert.Equal(StructuralSubjectKind.Workspace, workspace.Kind);
        Assert.False(workspace.IsPortable);
        Assert.Equal(
            workspace,
            StructuralSubjectIdentity.ForWorkspace(owner.Identity));
        Assert.NotEqual(
            workspace,
            StructuralSubjectIdentity.ForWorkspace(
                otherOwner.Identity));
    }

    [Fact]
    public void Identities_BindExactOwnerIssuedComponents()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectTestData.PackageContext context =
            StructuralSubjectTestData.Package(coordinate);
        WorkspaceContextMember libraryInput = Library(coordinate, "Library");
        MetadataTypeDefinitionName typeName = TypeName("Sample", "Widget");
        MemberAnchor anchor = Anchor("Sample.Widget", "Run");

        StructuralSubjectIdentity.AllLibrariesSubject allLibraries =
            StructuralSubjectIdentity.ForAllLibraries(context.Subject);
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                libraryInput);
        StructuralSubjectIdentity.TypeSubject type =
            StructuralSubjectIdentity.ForType(library, typeName);
        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(type, anchor);

        Assert.Same(context.Workspace, context.Subject.Workspace);
        Assert.Same(context.Occurrence, context.Subject.Occurrence);
        Assert.Same(context.Occurrence.Package, context.Subject.Descriptor);
        Assert.Equal(coordinate, context.Subject.Coordinate);
        Assert.Same(context.Subject, allLibraries.Package);
        Assert.Same(context.Subject, library.Package);
        Assert.Same(library, type.Library);
        Assert.Same(type, member.DeclaringType);
        Assert.All(
            new StructuralSubjectIdentity[]
            {
                context.Subject,
                allLibraries,
                library,
                type,
                member,
            },
            subject =>
            {
                Assert.Same(context.Workspace, subject.Workspace);
                Assert.False(subject.IsPortable);
            });
        Assert.Equal(StructuralSubjectKind.Package, context.Subject.Kind);
        Assert.Equal(StructuralSubjectKind.Library, allLibraries.Kind);
        Assert.Equal(StructuralSubjectKind.Library, library.Kind);
        Assert.Equal(StructuralSubjectKind.Type, type.Kind);
        Assert.Equal(StructuralSubjectKind.Member, member.Kind);
        Assert.Equal(typeName, type.Identity.Type);
        Assert.Equal(anchor, member.Identity.Member);
        Assert.NotEqual<StructuralSubjectIdentity>(allLibraries, library);
    }

    [Fact]
    public void Formatting_DoesNotRecurseThroughWorkspaceAncestry()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectTestData.PackageContext context =
            StructuralSubjectTestData.Package(coordinate);
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                Library(coordinate, "Library"));
        StructuralSubjectIdentity.TypeSubject type =
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "Widget"));
        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(
                type,
                Anchor("Sample.Widget", "Run"));

        Assert.All(
            new StructuralSubjectIdentity[]
            {
                context.Workspace,
                context.Subject,
                StructuralSubjectIdentity.ForAllLibraries(context.Subject),
                library,
                type,
                member,
            },
            subject =>
            {
                string diagnostic = subject.ToString();

                Assert.Contains(subject.GetType().Name, diagnostic);
                Assert.Contains(subject.Kind.ToString(), diagnostic);
            });
    }

    [Fact]
    public void PortableCoordinateAlone_CannotIdentifyRetainedPackageSubject()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectTestData.PackageContext first =
            StructuralSubjectTestData.Package(coordinate);
        StructuralSubjectTestData.PackageContext replacement =
            StructuralSubjectTestData.Package(
                Coordinate("1.0.0"),
                first.WorkspaceIdentity);
        StructuralSubjectTestData.PackageContext foreign =
            StructuralSubjectTestData.Package(Coordinate("1.0.0"));

        Assert.Equal(first.Subject.Coordinate, replacement.Subject.Coordinate);
        Assert.Equal(first.Subject.Coordinate, foreign.Subject.Coordinate);
        Assert.NotEqual(first.Subject, replacement.Subject);
        Assert.NotEqual(first.Subject, foreign.Subject);
        Assert.DoesNotContain(
            typeof(StructuralSubjectIdentity).GetMethods(),
            method =>
                method.Name == nameof(StructuralSubjectIdentity.ForPackage)
                && method.GetParameters().Any(parameter =>
                    parameter.ParameterType
                        == typeof(RealizedMemberCoordinate.Package)));
    }

    [Fact]
    public void PackageSubject_RequiresPackageOccurrence()
    {
        StructuralSubjectTestData.PackageContext first =
            StructuralSubjectTestData.Package(Coordinate("1.0.0"));
        StructuralSubjectTestData.PackageContext foreign =
            StructuralSubjectTestData.Package(Coordinate("1.0.0"));

        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForPackage(
                first.Workspace,
                null!));
        Assert.Throws<ArgumentException>(
            () => StructuralSubjectIdentity.ForPackage(
                first.Workspace,
                foreign.Occurrence));
    }

    [Fact]
    public void MemberIdentity_BindsExactDeclaringTypeAndAnchor()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectTestData.PackageContext context =
            StructuralSubjectTestData.Package(coordinate);
        WorkspaceContextMember libraryInput = Library(coordinate, "Library");
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                libraryInput);
        StructuralSubjectIdentity.TypeSubject firstType =
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "Widget"));
        StructuralSubjectIdentity.TypeSubject equalType =
            StructuralSubjectIdentity.ForType(
                StructuralSubjectIdentity.ForLibrary(
                    context.Subject,
                    new WorkspaceContextMember(
                        libraryInput.Declared,
                        Coordinate("1.0.0"),
                        libraryInput.Participant)),
                TypeName("Sample", "Widget"));
        MemberAnchor firstAnchor = Anchor("Sample.Widget", "Run");
        MemberAnchor equalAnchor = Anchor("Sample.Widget", "Run");
        MemberAnchor otherAnchor = Anchor("Sample.Widget", "Stop");

        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(firstType, firstAnchor);

        Assert.Same(firstType, member.DeclaringType);
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
        StructuralSubjectTestData.PackageContext context =
            StructuralSubjectTestData.Package(coordinate);
        WorkspaceContextMember libraryInput = Library(coordinate, "Library");
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                libraryInput);
        MetadataTypeDefinitionName type = TypeName("Sample", "Widget");
        StructuralSubjectIdentity.TypeSubject typeSubject =
            StructuralSubjectIdentity.ForType(library, type);

        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForWorkspace(null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForPackage(null!, context.Occurrence));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForAllLibraries(null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForLibrary(null!, libraryInput));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForLibrary(context.Subject, null!));
        Assert.Throws<ArgumentException>(
            () => StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                Library(Coordinate("2.0.0"), "Other")));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForType(null!, type));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForType(library, null!));
        Assert.Throws<ArgumentNullException>(
            () => StructuralSubjectIdentity.ForMember(
                null!,
                Anchor("Sample.Widget", "Run")));
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
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier));
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
