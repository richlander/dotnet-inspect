using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

/// <summary>Focus authorities shared by the Annotated Type Map planning and generation tests.</summary>
internal static class AnnotatedTypeMapTestData
{
    internal static SubjectRelationFocusAuthority PackageFocus() =>
        SubjectRelationFocusAuthority.Capture(
            StructuralSubjectTestData.Package(Coordinate()).Subject);

    internal static SubjectRelationFocusAuthority TypeFocus()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        StructuralSubjectTestData.PackageContext context = StructuralSubjectTestData.Package(coordinate);
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(context.Subject, Library(coordinate));
        return SubjectRelationFocusAuthority.Capture(
            StructuralSubjectIdentity.ForType(
                library,
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create("Sample", ["Widget"])).Name));
    }

    internal static RealizedMemberCoordinate.Package Coordinate() =>
        new("sample.package", "1.0.0", "nuget-org", "net11.0", runtimeIdentifier: null);

    internal static WorkspaceContextMember Library(RealizedMemberCoordinate.Package coordinate)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity("Sample", new Version(1, 0, 0, 0), Culture: null, PublicKeyToken: null),
            path: null,
            () => new MemoryStream([0], writable: false),
            AssemblyResolutionProvenance.Package(
                coordinate.PackageId, coordinate.Version, coordinate.Framework, coordinate.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId, coordinate.Version, coordinate.Framework, coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(assembly, NoResolverAssemblyBindingPolicy.Instance));
    }
}
