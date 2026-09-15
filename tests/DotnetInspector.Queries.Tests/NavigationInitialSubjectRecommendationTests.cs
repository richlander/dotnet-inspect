using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationInitialSubjectRecommendationTests
{
    [Fact]
    public void InitialRecommendation_PrefersLibraryThenPackage()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectTestData.Package(coordinate).Subject;
        StructuralSubjectIdentity.AllLibrariesSubject allLibraries =
            StructuralSubjectIdentity.ForAllLibraries(package);
        NavigationInitialLibraryCandidate primary = LibraryCandidate(
            package,
            "Primary",
            isPrimary: true);
        NavigationInitialLibraryCandidate other = LibraryCandidate(
            package,
            "Other",
            isPrimary: false,
            ("Widget", "public"));
        NavigationInitialLibraryCandidate otherWithoutTypes =
            LibraryCandidate(
                package,
                "OtherWithoutTypes",
                isPrimary: false);

        NavigationInitialSubjectOutcome recommendation =
            NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries,
                [primary, other]);
        Assert.Same(primary.Subject, recommendation.Subject);
        Assert.Same(package, recommendation.Basis.Package);
        Assert.Same(allLibraries, recommendation.Basis.AllLibraries);
        Assert.Equal([primary, other], recommendation.Basis.Libraries);

        NavigationInitialSubjectOutcome emptyLibrary =
            NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries,
                [primary, otherWithoutTypes]);
        Assert.Same(primary.Subject, emptyLibrary.Subject);

        Assert.Same(
            allLibraries,
            NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries,
                []).Subject);

        NavigationInitialSubjectOutcome primaryLibrary =
            NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries: null,
                [otherWithoutTypes, primary]);
        Assert.Same(primary.Subject, primaryLibrary.Subject);

        NavigationInitialSubjectOutcome firstLibrary =
            NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries,
                [otherWithoutTypes, LibraryCandidate(
                    package,
                    "Later",
                    isPrimary: false)]);
        Assert.Same(otherWithoutTypes.Subject, firstLibrary.Subject);

        NavigationInitialSubjectOutcome packageOnly =
            NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries: null,
                []);
        Assert.Same(package, packageOnly.Subject);

        NavigationInitialLibraryCandidate equalPrimary =
            EqualCandidate(primary);
        NavigationInitialLibraryCandidate equalOther =
            EqualCandidate(other);
        var equalBasis = new NavigationInitialSubjectBasis(
            package,
            allLibraries,
            [equalPrimary, equalOther]);
        Assert.Equal(primary, equalPrimary);
        Assert.Equal(
            primary.GetHashCode(),
            equalPrimary.GetHashCode());
        Assert.All(
            equalOther.Types,
            typeCandidate =>
                Assert.Equal(0, typeCandidate.Accessibility.Count));
        Assert.Equal(recommendation.Basis, equalBasis);
        Assert.Equal(
            recommendation.Basis.GetHashCode(),
            equalBasis.GetHashCode());
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries,
                default));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries: null,
                [
                    primary,
                    LibraryCandidate(
                        package,
                        "OtherPrimary",
                        isPrimary: true),
                ]));
    }

    [Fact]
    public void CandidateConstruction_RejectsInconsistentOwnerIssuedEvidence()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectTestData.Package(coordinate).Subject;
        NavigationInitialLibraryCandidate first = LibraryCandidate(
            package,
            "First",
            isPrimary: true,
            ("FirstType", "public"));
        NavigationInitialLibraryCandidate other = LibraryCandidate(
            package,
            "Other",
            isPrimary: false,
            ("OtherType", "public"));

        Assert.Throws<ArgumentException>(
            () => new NavigationInitialLibraryCandidate(
                first.Subject,
                isPrimary: false,
                default));
        Assert.Throws<ArgumentException>(
            () => new NavigationInitialLibraryCandidate(
                first.Subject,
                isPrimary: false,
                [other.Types[0]]));
        Assert.Throws<ArgumentException>(
            () => new NavigationInitialTypeCandidate(
                first.Types[0].Subject,
                new ApiAccessibilityBucket(
                    "private",
                    "private",
                    Order: 3,
                    IsDefault: true,
                    Count: 1)));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                package,
                StructuralSubjectIdentity.ForAllLibraries(
                    StructuralSubjectTestData.Package(
                        Coordinate("2.0.0")).Subject),
                []));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries: null,
                [
                    LibraryCandidate(
                        StructuralSubjectTestData.Package(
                            Coordinate("2.0.0")).Subject,
                        "OtherCoordinate",
                        isPrimary: false),
                ]));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries: null,
                [first, first]));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                package,
                allLibraries: null,
                [
                    first,
                    new NavigationInitialLibraryCandidate(
                        other.Subject,
                        isPrimary: true,
                        other.Types),
                ]));
    }

    [Fact]
    public void LibraryRecommendation_UsesPrimaryThenProducerOrderRegardlessOfTypes()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectTestData.Package(coordinate).Subject;
        NavigationInitialLibraryCandidate primary = LibraryCandidate(
            package,
            "Primary",
            isPrimary: true,
            ("PrivateFirst", "private"),
            ("PublicFirst", "public"),
            ("PublicSecond", "public"));
        NavigationInitialLibraryCandidate otherFirst = LibraryCandidate(
            package,
            "OtherFirst",
            isPrimary: false,
            ("OtherPublicFirst", "public"),
            ("OtherPublicSecond", "public"));
        NavigationInitialLibraryCandidate otherLater = LibraryCandidate(
            package,
            "OtherLater",
            isPrimary: false,
            ("LaterPublic", "public"));

        Assert.Same(
            primary.Subject,
            Recommend(package, primary, otherFirst, otherLater).Subject);

        NavigationInitialLibraryCandidate primaryNonDefault =
            LibraryCandidate(
                package,
                "PrimaryNonDefault",
                isPrimary: true,
                ("PrivateFirst", "private"),
                ("ProtectedSecond", "protected"));
        Assert.Same(
            primaryNonDefault.Subject,
            Recommend(
                package,
                primaryNonDefault,
                otherFirst,
                otherLater).Subject);

        NavigationInitialLibraryCandidate otherNonDefault =
            LibraryCandidate(
                package,
                "OtherNonDefault",
                isPrimary: false,
                ("OtherPrivate", "private"));
        Assert.Same(
            primaryNonDefault.Subject,
            Recommend(
                package,
                primaryNonDefault,
                otherNonDefault).Subject);

        NavigationInitialLibraryCandidate firstOtherNonDefault =
            LibraryCandidate(
                package,
                "FirstOtherNonDefault",
                isPrimary: false,
                ("PrivateBeforeProtected", "private"),
                ("ProtectedLater", "protected"));
        NavigationInitialLibraryCandidate laterOtherNonDefault =
            LibraryCandidate(
                package,
                "LaterOtherNonDefault",
                isPrimary: false,
                ("LaterPrivate", "private"));
        Assert.Same(
            firstOtherNonDefault.Subject,
            Recommend(
                package,
                firstOtherNonDefault,
                laterOtherNonDefault).Subject);
    }

    [Fact]
    public void InitialRecommendation_NeverChoosesTypeOrMember()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectTestData.Package(coordinate).Subject;
        NavigationInitialLibraryCandidate library = LibraryCandidate(
            package,
            "Primary",
            isPrimary: true,
            ("Widget", "public"));
        StructuralSubjectIdentity.TypeSubject type = library.Types[0].Subject;
        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(
                type,
                new MemberAnchor(
                    "Run()",
                    "Sample.Widget.Run()",
                    MemberAnchor.ComputeFingerprint(
                        "Sample.Widget.Run()"),
                    "Sample.Widget",
                    "Run"));

        NavigationInitialSubjectOutcome outcome = Recommend(package, library);

        Assert.Same(library.Subject, outcome.Subject);
        Assert.IsNotType<StructuralSubjectIdentity.TypeSubject>(
            outcome.Subject);
        Assert.IsNotType<StructuralSubjectIdentity.MemberSubject>(
            outcome.Subject);
        Assert.Throws<ArgumentException>(
            () => new NavigationInitialSubjectOutcome(
                outcome.Basis,
                member));
    }

    static NavigationInitialSubjectOutcome Recommend(
        StructuralSubjectIdentity.PackageSubject package,
        params NavigationInitialLibraryCandidate[] libraries) =>
        NavigationInitialSubjectRecommendation.Recommend(
            package,
            allLibraries: null,
            [.. libraries]);

    static NavigationInitialLibraryCandidate EqualCandidate(
        NavigationInitialLibraryCandidate candidate) =>
        new(
            candidate.Subject,
            candidate.IsPrimary,
            [
                .. candidate.Types.Select(
                    type => new NavigationInitialTypeCandidate(
                        type.Subject,
                        type.Accessibility with
                        {
                            Count = type.Accessibility.Count + 7,
                        })),
            ]);

    static NavigationInitialLibraryCandidate LibraryCandidate(
        StructuralSubjectIdentity.PackageSubject package,
        string name,
        bool isPrimary,
        params (string Name, string Accessibility)[] types)
    {
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                package,
                Library(package.Coordinate, name));
        return new NavigationInitialLibraryCandidate(
            library,
            isPrimary,
            [
                .. types.Select(
                    type => new NavigationInitialTypeCandidate(
                        StructuralSubjectIdentity.ForType(
                            library,
                            TypeName("Sample", type.Name)),
                        ApiAccessibility.Classify(type.Accessibility))),
            ]);
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
}
