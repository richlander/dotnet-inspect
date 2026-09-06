using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationInitialSubjectRecommendationTests
{
    [Fact]
    public void InitialRecommendation_PrefersOneLibraryThenAggregateThenRoot()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.RootSubject root =
            StructuralSubjectIdentity.ForRoot(coordinate);
        StructuralSubjectIdentity.AllLibrariesSubject allLibraries =
            StructuralSubjectIdentity.ForAllLibraries(coordinate);
        NavigationInitialLibraryCandidate primary = LibraryCandidate(
            coordinate,
            "Primary",
            isPrimary: true);
        NavigationInitialLibraryCandidate other = LibraryCandidate(
            coordinate,
            "Other",
            isPrimary: false,
            ("Widget", "public"));
        NavigationInitialLibraryCandidate otherWithoutTypes =
            LibraryCandidate(
                coordinate,
                "OtherWithoutTypes",
                isPrimary: false);

        NavigationInitialSubjectOutcome recommendation =
            NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries,
                [primary, other]);
        Assert.Same(primary.Subject, recommendation.Subject);
        Assert.Same(root, recommendation.Basis.Root);
        Assert.Same(allLibraries, recommendation.Basis.AllLibraries);
        Assert.Equal([primary, other], recommendation.Basis.Libraries);

        NavigationInitialSubjectOutcome emptyLibrary =
            NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries,
                [primary, otherWithoutTypes]);
        Assert.Same(primary.Subject, emptyLibrary.Subject);

        Assert.Same(
            allLibraries,
            NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries,
                []).Subject);

        NavigationInitialSubjectOutcome primaryLibrary =
            NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries: null,
                [otherWithoutTypes, primary]);
        Assert.Same(primary.Subject, primaryLibrary.Subject);

        NavigationInitialSubjectOutcome firstLibrary =
            NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries,
                [otherWithoutTypes, LibraryCandidate(
                    coordinate,
                    "Later",
                    isPrimary: false)]);
        Assert.Same(otherWithoutTypes.Subject, firstLibrary.Subject);

        NavigationInitialSubjectOutcome rootOnly =
            NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries: null,
                []);
        Assert.Same(root, rootOnly.Subject);

        NavigationInitialLibraryCandidate equalPrimary =
            EqualCandidate(primary);
        NavigationInitialLibraryCandidate equalOther =
            EqualCandidate(other);
        var equalBasis = new NavigationInitialSubjectBasis(
            root,
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
                root,
                allLibraries,
                default));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries: null,
                [
                    primary,
                    LibraryCandidate(
                        coordinate,
                        "OtherPrimary",
                        isPrimary: true),
                ]));
    }

    [Fact]
    public void CandidateConstruction_RejectsInconsistentOwnerIssuedEvidence()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.RootSubject root =
            StructuralSubjectIdentity.ForRoot(coordinate);
        NavigationInitialLibraryCandidate first = LibraryCandidate(
            coordinate,
            "First",
            isPrimary: true,
            ("FirstType", "public"));
        NavigationInitialLibraryCandidate other = LibraryCandidate(
            coordinate,
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
                root,
                StructuralSubjectIdentity.ForAllLibraries(
                    Coordinate("2.0.0")),
                []));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries: null,
                [
                    LibraryCandidate(
                        Coordinate("2.0.0"),
                        "OtherCoordinate",
                        isPrimary: false),
                ]));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                root,
                allLibraries: null,
                [first, first]));
        Assert.Throws<ArgumentException>(
            () => NavigationInitialSubjectRecommendation.Recommend(
                root,
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
        StructuralSubjectIdentity.RootSubject root =
            StructuralSubjectIdentity.ForRoot(coordinate);
        NavigationInitialLibraryCandidate primary = LibraryCandidate(
            coordinate,
            "Primary",
            isPrimary: true,
            ("PrivateFirst", "private"),
            ("PublicFirst", "public"),
            ("PublicSecond", "public"));
        NavigationInitialLibraryCandidate otherFirst = LibraryCandidate(
            coordinate,
            "OtherFirst",
            isPrimary: false,
            ("OtherPublicFirst", "public"),
            ("OtherPublicSecond", "public"));
        NavigationInitialLibraryCandidate otherLater = LibraryCandidate(
            coordinate,
            "OtherLater",
            isPrimary: false,
            ("LaterPublic", "public"));

        Assert.Same(
            primary.Subject,
            Recommend(root, primary, otherFirst, otherLater).Subject);

        NavigationInitialLibraryCandidate primaryNonDefault =
            LibraryCandidate(
                coordinate,
                "PrimaryNonDefault",
                isPrimary: true,
                ("PrivateFirst", "private"),
                ("ProtectedSecond", "protected"));
        Assert.Same(
            primaryNonDefault.Subject,
            Recommend(
                root,
                primaryNonDefault,
                otherFirst,
                otherLater).Subject);

        NavigationInitialLibraryCandidate otherNonDefault =
            LibraryCandidate(
                coordinate,
                "OtherNonDefault",
                isPrimary: false,
                ("OtherPrivate", "private"));
        Assert.Same(
            primaryNonDefault.Subject,
            Recommend(
                root,
                primaryNonDefault,
                otherNonDefault).Subject);

        NavigationInitialLibraryCandidate firstOtherNonDefault =
            LibraryCandidate(
                coordinate,
                "FirstOtherNonDefault",
                isPrimary: false,
                ("PrivateBeforeProtected", "private"),
                ("ProtectedLater", "protected"));
        NavigationInitialLibraryCandidate laterOtherNonDefault =
            LibraryCandidate(
                coordinate,
                "LaterOtherNonDefault",
                isPrimary: false,
                ("LaterPrivate", "private"));
        Assert.Same(
            firstOtherNonDefault.Subject,
            Recommend(
                root,
                firstOtherNonDefault,
                laterOtherNonDefault).Subject);
    }

    [Fact]
    public void InitialRecommendation_NeverChoosesTypeOrMember()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate("1.0.0");
        StructuralSubjectIdentity.RootSubject root =
            StructuralSubjectIdentity.ForRoot(coordinate);
        NavigationInitialLibraryCandidate library = LibraryCandidate(
            coordinate,
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

        NavigationInitialSubjectOutcome outcome = Recommend(root, library);

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
        StructuralSubjectIdentity.RootSubject root,
        params NavigationInitialLibraryCandidate[] libraries) =>
        NavigationInitialSubjectRecommendation.Recommend(
            root,
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
        RealizedMemberCoordinate.Package coordinate,
        string name,
        bool isPrimary,
        params (string Name, string Accessibility)[] types)
    {
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                Library(coordinate, name));
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
