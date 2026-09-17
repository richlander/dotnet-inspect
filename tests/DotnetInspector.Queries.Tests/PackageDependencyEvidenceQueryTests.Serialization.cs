using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyEvidenceQueryTests
{
    [Fact]
    public void Serialization_RoundTripsValidatedFrameworkIdentities()
    {
        const string opaqueIdentity =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        InertString sourceSpelling =
            new(TextPolicy.Field, "source\u202Espelling");
        PackageDependencyFrameworkScopeIdentity[] frameworkScopes =
        [
            PackageDependencyFrameworkScopeIdentity.Any(sourceSpelling),
            PackageDependencyFrameworkScopeIdentity.Exact(
                "net11.0",
                sourceSpelling),
            PackageDependencyFrameworkScopeIdentity.Unrecognized(
                opaqueIdentity,
                sourceSpelling),
            PackageDependencyFrameworkScopeIdentity.Unresolved(
                opaqueIdentity,
                sourceSpelling),
        ];
        AuthoredProjectTargetFrameworkIdentity[] authoredIdentities =
        [
            AuthoredProjectTargetFrameworkIdentity.Exact("net11.0"),
            AuthoredProjectTargetFrameworkIdentity.FromOpaqueIdentity(
                AuthoredProjectTargetFrameworkKind.Unrecognized,
                opaqueIdentity),
            AuthoredProjectTargetFrameworkIdentity.FromOpaqueIdentity(
                AuthoredProjectTargetFrameworkKind.Unresolved,
                opaqueIdentity),
        ];

        foreach (PackageDependencyFrameworkScopeIdentity scope in
            frameworkScopes)
        {
            PackageDependencyFrameworkScopeIdentity roundTripped =
                RoundTrip(scope);

            Assert.Equal(scope.Kind, roundTripped.Kind);
            Assert.Equal(
                scope.CanonicalFramework,
                roundTripped.CanonicalFramework);
            Assert.Equal(scope.OpaqueIdentity, roundTripped.OpaqueIdentity);
            Assert.Equal(
                scope.SourceSpelling.ToString(),
                roundTripped.SourceSpelling.ToString());
        }
        foreach (AuthoredProjectTargetFrameworkIdentity identity in
            authoredIdentities)
        {
            AuthoredProjectTargetFrameworkIdentity roundTripped =
                RoundTrip(identity);

            Assert.Equal(identity.Kind, roundTripped.Kind);
            Assert.Equal(
                identity.CanonicalFramework,
                roundTripped.CanonicalFramework);
            Assert.Equal(
                identity.ComparisonIdentity,
                roundTripped.ComparisonIdentity);
        }
    }

    [Fact]
    public void Serialization_RoundTripsEveryAdmittedRootKind()
    {
        RestoredProjectDependencyFacts restored = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json")),
                new RestoredProjectTargetRequest("net11.0")));
        AuthoredProjectDependencyFactsResult authored = AuthoredFacts(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Example.Authored" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        RuntimeDependencyFactsResult runtime =
            new RuntimeDependencyFactsResult.Available(
                RuntimeFacts(
                    """
                    {
                      "runtimeTarget": { "name": "net11.0" },
                      "targets": {
                        "net11.0": {
                          "Example.App/1.0.0": {
                            "dependencies": {
                              "Example.Runtime": "2.0.0"
                            }
                          },
                          "Example.Runtime/2.0.0": {}
                        }
                      },
                      "libraries": {
                        "Example.App/1.0.0": { "type": "project" },
                        "Example.Runtime/2.0.0": { "type": "package" }
                      }
                    }
                    """));
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            Manifest(
                                """
                                <group targetFramework="net11.0">
                                  <dependency id="Example.Package" version="[3.0.0]" />
                                </group>
                                """),
                            PackageDependencyEvidenceAcquisitionForm
                                .DirectNuspec,
                            requestedTargetFramework: "net11.0"),
                        PackageDependencyEvidenceQuery
                            .CreateRestoredProjectInput(
                                restored,
                                PackageDependencyEvidenceAcquisitionForm
                                    .ProjectAssets),
                        PackageDependencyEvidenceQuery
                            .CreateAuthoredProjectInput(authored),
                        PackageDependencyEvidenceQuery
                            .CreateRuntimeDependencyManifestInput(runtime),
                    ]));
        var document = new DependencyInspectionEvidenceDocument(
            outcome,
            [
                new DependencyRootOccurrenceIdentity(1),
                new DependencyRootOccurrenceIdentity(2),
                new DependencyRootOccurrenceIdentity(3),
                new DependencyRootOccurrenceIdentity(4),
            ],
            []);
        JsonTypeInfo<DependencyInspectionEvidenceDocument> typeInfo =
            (JsonTypeInfo<DependencyInspectionEvidenceDocument>)
                DependencyInspectionJsonContext.Default.GetTypeInfo(
                    typeof(DependencyInspectionEvidenceDocument))!;

        string json = JsonSerializer.Serialize(document, typeInfo);
        DependencyInspectionEvidenceDocument? roundTripped =
            JsonSerializer.Deserialize(json, typeInfo);

        Assert.NotNull(roundTripped);
        Assert.Collection(
            roundTripped.PackageInputs.Roots,
            root => Assert.IsType<
                PackageDependencyEvidenceRootIdentity.Package>(root.Identity),
            root => Assert.IsType<
                PackageDependencyEvidenceRootIdentity.RestoredProject>(
                    root.Identity),
            root => Assert.IsType<
                PackageDependencyEvidenceRootIdentity.AuthoredProject>(
                    root.Identity),
            root => Assert.IsType<
                PackageDependencyEvidenceRootIdentity
                    .RuntimeDependencyManifest>(root.Identity));
        Assert.Equal(
            [1, 2, 3, 4],
            roundTripped.AdmittedRootOccurrences.Select(
                static occurrence => occurrence.Value));
    }

    [Fact]
    public void Serialization_RoundTripsEveryExplicitRootFailureKind()
    {
        PackageDependencyEvidenceRootFailure[] failures =
        [
            new PackageDependencyEvidenceRootFailure.Package(
                PackageDependencyEvidenceAcquisitionForm.PackageArchive,
                PackageSourceCoordinate.Create("Example.Package", "1.0.0"),
                new PackageManifestFailure(
                    PackageManifestFailureReason.MalformedXml)),
            new PackageDependencyEvidenceRootFailure.RestoredProject(
                PackageDependencyEvidenceAcquisitionForm.ProjectAssets,
                new RestoredProjectDependencyFailure(
                    RestoredProjectDependencyFailureReason
                        .MalformedOrDuplicateBearingJson)),
            new PackageDependencyEvidenceRootFailure.AuthoredProject(
                PackageDependencyEvidenceAcquisitionForm.ProjectXml,
                new AuthoredProjectDependencyFactsFailure(
                    AuthoredProjectDependencyFactsFailureReason.MalformedXml)),
            new PackageDependencyEvidenceRootFailure.RuntimeDependencyManifest(
                PackageDependencyEvidenceAcquisitionForm
                    .RuntimeDependencyManifest,
                new RuntimeDependencyFailure(
                    RuntimeDependencyFailureReason
                        .MalformedOrDuplicateBearingJson)),
            new PackageDependencyEvidenceRootFailure.Acquisition(
                PackageDependencyEvidenceAcquisitionForm.ProjectLocator,
                PackageDependencyEvidenceAcquisitionFailureReason.NotRestored),
        ];
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([], [.. failures]));
        var document = new DependencyInspectionEvidenceDocument(
            outcome,
            [],
            [
                new DependencyRootOccurrenceIdentity(1),
                new DependencyRootOccurrenceIdentity(2),
                new DependencyRootOccurrenceIdentity(3),
                new DependencyRootOccurrenceIdentity(4),
                new DependencyRootOccurrenceIdentity(5),
            ]);

        DependencyInspectionEvidenceDocument roundTripped =
            RoundTrip(document);

        Assert.Collection(
            roundTripped.PackageInputs.FailedRoots,
            failure => Assert.IsType<
                PackageDependencyEvidenceRootFailure.Package>(failure),
            failure => Assert.IsType<
                PackageDependencyEvidenceRootFailure.RestoredProject>(failure),
            failure => Assert.IsType<
                PackageDependencyEvidenceRootFailure.AuthoredProject>(failure),
            failure => Assert.IsType<
                PackageDependencyEvidenceRootFailure
                    .RuntimeDependencyManifest>(failure),
            failure => Assert.IsType<
                PackageDependencyEvidenceRootFailure.Acquisition>(failure));
    }

    [Fact]
    public void Serialization_RoundTripsPackagePrefixSourceEvidence()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageDependencyEvidenceRootFailure.PackageProfile failure =
            PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                new PackageProfileFailure(
                    "Example.Bad",
                    "1.0.0",
                    source.Source,
                    PackageProfileFailureKind.SearchContract,
                    "Search failed"));
        var completion = new PackageDependencyEvidencePackagePrefixCompletion(
            new InertString(TextPolicy.Field, "Example."),
            PackageDependencyEvidenceSourceIdentity.Create(source.Source),
            candidates: 1,
            matches: 0,
            failures: 1,
            PackageSearchTruncationReason.None);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [],
                    [failure],
                    packagePrefixCompletion: completion));
        var document = new DependencyInspectionEvidenceDocument(
            outcome,
            [],
            [null]);
        var settledFailure = Assert.IsType<
            PackageDependencyEvidenceRootFailure.PackageProfile>(
                Assert.Single(document.PackageInputs.FailedRoots));

        Assert.True(
            settledFailure.Source.MatchesRuntimeAssociation(
                source.Source.Association));

        DependencyInspectionEvidenceDocument roundTripped =
            RoundTrip(document);

        var roundTrippedFailure = Assert.IsType<
            PackageDependencyEvidenceRootFailure.PackageProfile>(
                Assert.Single(roundTripped.PackageInputs.FailedRoots));
        Assert.Equal(
            source.Source.Producer.PortableKey,
            roundTrippedFailure.Source.PortableProducerKey);
        Assert.Equal(
            source.Source.TransportKind,
            roundTrippedFailure.Source.TransportKind);
        Assert.Equal(
            source.Source.Producer.PortableKey,
            roundTripped.PackageInputs.RootSet.PackagePrefixCompletion!
                .Source.PortableProducerKey);
        Assert.Equal(
            roundTrippedFailure.Source.Association,
            roundTripped.PackageInputs.RootSet.PackagePrefixCompletion!
                .Source.Association);
        Assert.False(
            roundTrippedFailure.Source.MatchesRuntimeAssociation(
                source.Source.Association));
        Assert.Null(Assert.Single(roundTripped.FailedRootOccurrences));
    }

    private static T RoundTrip<T>(T value)
    {
        JsonTypeInfo<T> typeInfo = (JsonTypeInfo<T>)
            DependencyInspectionJsonContext.Default.GetTypeInfo(typeof(T))!;
        string json = JsonSerializer.Serialize(value, typeInfo);

        return JsonSerializer.Deserialize(json, typeInfo)!;
    }

    private static void AssertPortableSource(
        PackageSourceResultIdentity expected,
        PackageDependencyEvidenceSourceIdentity? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Producer.Key, actual.ProducerKey);
        Assert.Equal(
            expected.Producer.PortableKey,
            actual.PortableProducerKey);
        Assert.True(actual.Association > 0);
        Assert.Equal(expected.TransportKind, actual.TransportKind);
        Assert.Equal(
            expected.Producer.Display.ToString(),
            actual.ProducerDisplay.ToString());
    }
}
