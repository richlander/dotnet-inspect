using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemDependencyClassifierTests
{
    [Fact]
    public void ProductProfilePreservesOverlapAndDomainSeparation()
    {
        RealizedMemberCoordinate.Package package = PackageSubject();
        PortableLibraryIdentity library = Library("Sample");
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(1),
                        1,
                        new DeclaredPackageDependency(
                            "microsoft.extensions.ai.abstractions",
                            "[10.0.0,)"),
                        package),
                    new EcosystemDependencyObservation.AssemblyReference(
                        new(2),
                        2,
                        new AssemblyReferenceIdentity(
                            "SYSTEM.NET.HTTP",
                            new Version(10, 0, 0, 0),
                            null,
                            null),
                        library),
                ]);

        Assert.Equal(
            [
                EcosystemPackIds.Runtime,
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemPackIds.AI,
            ],
            classification.RecognizedEcosystems.Select(
                ecosystem => ecosystem.Id));
        Assert.Equal(3, classification.Summary.RecognizedEcosystemCount);
        Assert.Equal(2, classification.Summary.RecognizedObservationCount);
        Assert.Equal(3, classification.Summary.RecognitionCount);
        Assert.Empty(classification.Unrecognized);
        Assert.Equal(
            [
                EcosystemPackIds.Runtime,
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemPackIds.AI,
            ],
            classification.Recognized.Select(entry => entry.Ecosystem.Id));
    }

    [Fact]
    public void FamilyMatchingRequiresADotSegmentBoundary()
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(1),
                        1,
                        new DeclaredPackageDependency(
                            "Microsoft.Extensions.AIBogus",
                            "1.0.0"),
                        PackageSubject()),
                ]);

        EcosystemDependencyRecognitionEntry recognition =
            Assert.Single(classification.Recognized);
        Assert.Equal(
            EcosystemPackIds.MicrosoftExtensions,
            recognition.Ecosystem.Id);
        Assert.DoesNotContain(
            classification.RecognizedEcosystems,
            ecosystem => ecosystem.Id == EcosystemPackIds.AI);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.AI.")]
    [InlineData("Microsoft.Extensions.AI..Abstractions")]
    public void FamilyMatchingRequiresANonemptyDescendantSegment(
        string packageId)
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [PackageObservation(1, 1, packageId)]);

        Assert.DoesNotContain(
            classification.RecognizedEcosystems,
            ecosystem => ecosystem.Id == EcosystemPackIds.AI);
    }

    [Fact]
    public void ExtensionsAIDependencyRetainsIntentionalCrossPackOverlap()
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(1),
                        1,
                        new DeclaredPackageDependency(
                            "Microsoft.Extensions.AI.OpenAI",
                            "10.10.1"),
                        PackageSubject()),
                ]);

        Assert.Equal(
            [EcosystemPackIds.MicrosoftExtensions, EcosystemPackIds.AI],
            classification.Recognized.Select(entry => entry.Ecosystem.Id));
    }

    [Fact]
    public void AIRecognizesLabRootsExactlyAndNotCommunityNamesakes()
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(1),
                        1,
                        new DeclaredPackageDependency("Anthropic", "12.50.0"),
                        PackageSubject()),
                ]);
        Assert.Equal(
            [EcosystemPackIds.AI],
            classification.Recognized.Select(entry => entry.Ecosystem.Id));

        EcosystemDependencyClassification community =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(1),
                        1,
                        new DeclaredPackageDependency("Anthropic.SDK", "5.5.0"),
                        PackageSubject()),
                ]);
        Assert.Empty(community.Recognized);
    }

    [Fact]
    public void SeveralMatchingBasesFromOneEcosystemCollapseIntoOnePair()
    {
        var profile = new EcosystemDependencyRecognitionProfile(
            EcosystemPackCatalog.Discover(),
            [
                new(
                    EcosystemPackIds.Blazor,
                    [
                        EcosystemDependencyAssociation.PackageIdFamily("Contoso"),
                        EcosystemDependencyAssociation.ExactPackageId(
                            "Contoso.Widget"),
                    ],
                    []),
            ]);

        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                profile,
                [
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(1),
                        1,
                        new DeclaredPackageDependency(
                            "Contoso.Widget",
                            "[1.0.0]"),
                        PackageSubject()),
                ]);

        EcosystemDependencyRecognitionEntry pair =
            Assert.Single(classification.Recognized);
        Assert.Equal(2, pair.MatchingAssociations.Length);
        Assert.Equal(1, classification.Summary.RecognitionCount);
        Assert.Equal(1, classification.Summary.RecognizedObservationCount);
    }

    [Fact]
    public void EqualInputsProduceStructurallyEqualClassifications()
    {
        EcosystemDependencyClassification first =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [PackageObservation(1, 1, "YamlDotNet")]);
        EcosystemDependencyClassification second =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [PackageObservation(1, 1, "YamlDotNet")]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Empty(first.Recognized);
        Assert.Equal(
            "YamlDotNet",
            Assert.IsType<
                EcosystemDependencyObservation.PackageDeclaration>(
                    Assert.Single(first.Unrecognized)).Dependency.Id);
    }

    [Fact]
    public void EqualDependencyNamesFromDifferentSourcesRemainDistinct()
    {
        AssemblyReferenceIdentity reference = new(
                    "Microsoft.Extensions.Options",
                    new Version(10, 0, 0, 0),
                    null,
                    null);
        EcosystemDependencyClassification classification =
                    EcosystemDependencyClassifier.Classify(
                        EcosystemPackCatalog.DependencyRecognitionProfile,
                        [
                            new EcosystemDependencyObservation.AssemblyReference(
                                new(1),
                                1,
                                reference,
                                Library("First.Library")),
                            new EcosystemDependencyObservation.AssemblyReference(
                                new(2),
                                2,
                                reference,
                                Library("Second.Library")),
                        ]);

        Assert.Equal(2, classification.Summary.ObservationCount);
        Assert.Equal(2, classification.Summary.RecognizedObservationCount);
        Assert.Equal(2, classification.Summary.RecognitionCount);
        Assert.Equal(
                    ["First.Library", "Second.Library"],
                    classification.Recognized.Select(entry =>
                        Assert.IsType<
                            EcosystemDependencyObservation.AssemblyReference>(
                                entry.Observation).DeclaringLibrary.Name));
    }

    [Fact]
    public void AssemblyAssociationWithoutMatchingPackageEvidenceIsCandidate()
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.AssemblyReference(
                        new(1),
                        1,
                        new AssemblyReferenceIdentity(
                            "Microsoft.Extensions.Options",
                            new Version(10, 0, 0, 0),
                            null,
                            "0011223344556677"),
                        Library("Consumer.Library")),
                ]);

        EcosystemDependencyCandidateEntry candidate =
            Assert.Single(classification.Candidates);
        Assert.Equal(
            EcosystemPackIds.MicrosoftExtensions,
            candidate.Ecosystem.Id);
        Assert.Empty(classification.Recognized);
        Assert.Empty(classification.Unrecognized);
        Assert.Equal(1, classification.Summary.CandidateOnlyObservationCount);
        Assert.Equal(1, classification.Summary.CandidateCount);
        Assert.Equal([candidate], classification.Matches);
    }

    [Fact]
    public void CatalogBackedAssemblyRecognitionRetainsExactPackageEvidence()
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.AssemblyReference(
                        new(1),
                        1,
                        new AssemblyReferenceIdentity(
                            "Microsoft.Extensions.Options",
                            new Version(11, 0, 0, 0),
                            null,
                            "adb9793829ddae60"),
                        Library("Consumer.Library")),
                ]);

        EcosystemDependencyRecognitionEntry recognition =
            Assert.Single(classification.Recognized);
        EcosystemAssemblyDefinitionEvidence evidence =
            Assert.Single(recognition.MatchingAssemblyEvidence);
        Assert.Equal(
            "Microsoft.Extensions.Options",
            evidence.Package.PackageId);
        Assert.Equal("10.0.0", evidence.Package.Version);
        Assert.Equal(
            "lib/net10.0/Microsoft.Extensions.Options.dll",
            evidence.AssetPath);
        Assert.Empty(classification.Candidates);
        Assert.Equal(0, classification.Summary.CandidateOnlyObservationCount);
    }

    [Fact]
    public void MissingObservedPublicKeyTokenAdmitsSameNameCatalogEvidence()
    {
        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    new EcosystemDependencyObservation.AssemblyReference(
                        new(1),
                        1,
                        new AssemblyReferenceIdentity(
                            "Microsoft.Extensions.Options",
                            new Version(11, 0, 0, 0),
                            null,
                            null),
                        Library("Consumer.Library")),
                ]);

        EcosystemDependencyRecognitionEntry recognition =
            Assert.Single(classification.Recognized);
        Assert.Equal(
            EcosystemPackIds.MicrosoftExtensions,
            recognition.Ecosystem.Id);
        Assert.Single(recognition.MatchingAssemblyEvidence);
        Assert.Empty(classification.Candidates);
    }

    [Fact]
    public void ObservationIdentityAndSourceOrderMustBeUnambiguous()
    {
        Assert.Throws<ArgumentException>(() =>
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    PackageObservation(1, 1, "Contoso.First"),
                    PackageObservation(1, 2, "Contoso.Second"),
                ]));
        Assert.Throws<ArgumentException>(() =>
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [
                    PackageObservation(1, 2, "Contoso.First"),
                    PackageObservation(2, 1, "Contoso.Second"),
                ]));
    }

    private static EcosystemDependencyObservation.PackageDeclaration
        PackageObservation(
            int identity,
            int sourceOrder,
            string packageId) =>
        new(
            new(identity),
            sourceOrder,
            new DeclaredPackageDependency(packageId, "1.0.0"),
            PackageSubject());

    private static RealizedMemberCoordinate.Package PackageSubject() =>
        new(
            "sample.package",
            "1.0.0",
            PackageProducerIdentity.NuGetOrg.PortableKey,
            "net10.0",
            runtimeIdentifier: null);

    private static PortableLibraryIdentity Library(string name) =>
        new(name, "1.0.0.0", culture: null, publicKeyToken: null);
}
