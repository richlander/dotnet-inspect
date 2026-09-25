using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyEvidenceQueryTests
{
    [Fact]
    public void Serialization_RoundTripsValidatedFrameworkIdentities()
    {
        const string factsDigest =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string opaqueIdentity =
            RestoredProjectIdentityText.Opaque("custom-framework");
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
            AuthoredProjectTargetFrameworkIdentity.Unrecognized(
                "custom-framework"),
            AuthoredProjectTargetFrameworkIdentity.Unresolved(
                "$(TargetFramework)"),
        ];
        RestoredProjectSelectionIdentity[] restoredIdentities =
        [
            new("net11.0", factsDigest),
            new(opaqueIdentity, factsDigest),
            new(
                $"net11.0/{RestoredProjectIdentityText.Opaque("custom-runtime")}",
                factsDigest),
        ];
        RuntimeDependencyManifestIdentity[] runtimeIdentities =
        [
            new("net11.0/linux-x64", factsDigest),
            new(opaqueIdentity, factsDigest),
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
        foreach (RestoredProjectSelectionIdentity identity in
            restoredIdentities)
        {
            Assert.Equal(identity, RoundTrip(identity));
        }
        foreach (RuntimeDependencyManifestIdentity identity in
            runtimeIdentities)
        {
            Assert.Equal(identity, RoundTrip(identity));
        }
    }

    [Theory]
    [InlineData(
        """{"kind":"ExactFramework","canonical_framework":"NET8.0","source_spelling":"NET8.0"}""")]
    [InlineData(
        """{"kind":"UnrecognizedFramework","opaque_identity":"not-a-hash","source_spelling":"custom"}""")]
    [InlineData(
        """{"kind":"UnrecognizedFramework","opaque_identity":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","source_spelling":"custom"}""")]
    [InlineData(
        """{"kind":"UnresolvedFramework","opaque_identity":"sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","source_spelling":"custom"}""")]
    public void Serialization_RejectsInvalidPackageFrameworkIdentities(
        string json)
    {
        JsonTypeInfo<PackageDependencyFrameworkScopeIdentity> typeInfo =
            (JsonTypeInfo<PackageDependencyFrameworkScopeIdentity>)
                DependencyInspectionJsonContext.Default.GetTypeInfo(
                    typeof(PackageDependencyFrameworkScopeIdentity))!;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(json, typeInfo));
    }

    [Theory]
    [InlineData(
        """{"kind":"Exact","canonical_framework":"NET8.0"}""")]
    [InlineData(
        """{"kind":"Unrecognized","comparison_identity":"not-a-hash"}""")]
    [InlineData(
        """{"kind":"Unrecognized","comparison_identity":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    [InlineData(
        """{"kind":"Unresolved","comparison_identity":"sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    public void Serialization_RejectsInvalidAuthoredFrameworkIdentities(
        string json)
    {
        JsonTypeInfo<AuthoredProjectTargetFrameworkIdentity> typeInfo =
            (JsonTypeInfo<AuthoredProjectTargetFrameworkIdentity>)
                DependencyInspectionJsonContext.Default.GetTypeInfo(
                    typeof(AuthoredProjectTargetFrameworkIdentity))!;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(json, typeInfo));
    }

    [Theory]
    [InlineData(
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("NET8.0")]
    [InlineData("net8.0/WIN-X64")]
    public void Serialization_RejectsInvalidTargetIdentities(
        string targetIdentity)
    {
        const string factsDigest =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string json =
            $$"""{"target_identity":"{{targetIdentity}}","facts_digest":"{{factsDigest}}"}""";

        AssertInvalidTargetIdentity<RestoredProjectSelectionIdentity>(json);
        AssertInvalidTargetIdentity<RuntimeDependencyManifestIdentity>(json);
    }

    [Fact]
    public void Serialization_RoundTripsProductionRestoredOpaqueTargetIdentity()
    {
        const string sourceFramework = "custom-framework";
        byte[] assets = MutateRestoredAssets(
            static root =>
            {
                JsonObject targets = root["targets"]!.AsObject();
                KeyValuePair<string, JsonNode?> sourceTarget = targets.First();
                JsonNode target = sourceTarget.Value!.DeepClone();
                targets.Remove(sourceTarget.Key);
                targets[sourceFramework] = target;
            });
        RestoredProjectDependencyFacts restored = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                assets,
                new RestoredProjectTargetRequest(sourceFramework)));
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateRestoredProjectInput(
                                restored,
                                PackageDependencyEvidenceAcquisitionForm
                                    .ProjectAssets),
                    ]));
        PackageDependencyEvidenceRoot root = Assert.Single(outcome.Roots);
        PackageDependencyEvidenceRoot roundTripped = RoundTrip(root);
        var identity = Assert.IsType<
            PackageDependencyEvidenceRootIdentity.RestoredProject>(
                roundTripped.Identity);

        Assert.StartsWith(
            RestoredProjectIdentityText.OpaquePrefix,
            identity.Identity.Selection.TargetIdentity,
            StringComparison.Ordinal);
        Assert.Equal(
            restored.SelectionIdentity.TargetIdentity,
            identity.Identity.Selection.TargetIdentity);
    }

    [Fact]
    public void Serialization_RejectsMismatchedEvidenceProducerIdentity()
    {
        const string json =
            """
            {
              "association": 1,
              "producer_key": "producer",
              "portable_producer_key": "nfp-1.0000000000000000000000000000000000000000000000000000000000000000",
              "transport_kind": "NuGetV3",
              "producer_display": ""
            }
            """;
        JsonTypeInfo<PackageDependencyEvidenceSourceIdentity> typeInfo =
            (JsonTypeInfo<PackageDependencyEvidenceSourceIdentity>)
                DependencyInspectionJsonContext.Default.GetTypeInfo(
                    typeof(PackageDependencyEvidenceSourceIdentity))!;

        Exception? exception = Record.Exception(
            () => JsonSerializer.Deserialize(json, typeInfo));
        Assert.True(
            exception is JsonException or ArgumentException,
            $"Expected mismatched producer identity rejection; received {exception?.GetType().Name ?? "no exception"}.");
    }

    [Fact]
    public void Serialization_RoundTripsMultilinePackageProfileMessage()
    {
        const string message = "Profile unavailable\r\nRetry\tlater";
        PackageProducerIdentity producer = PackageProducerIdentity.NuGetOrg;
        var failure = new PackageDependencyEvidenceRootFailure.PackageProfile(
            new PackageDependencyEvidenceSourceIdentity(
                association: 1,
                producer.Key,
                producer.PortableKey,
                PackageSourceKind.NuGetV3,
                producer.Display),
            PackageProfileFailureKind.Search,
            ManifestFailureReason: null,
            Coordinate: null,
            PackageId: null,
            Version: null,
            new InertString(TextPolicy.Prose, message));

        PackageDependencyEvidenceRootFailure.PackageProfile roundTripped =
            RoundTrip(failure);

        Assert.Equal(message, roundTripped.Message.ToString());
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
            "Search failed",
            roundTrippedFailure.Message.ToString());
        Assert.Equal(
            source.Source.Producer.Display.ToString(),
            roundTrippedFailure.Source.ProducerDisplay.ToString());
        Assert.Equal(
            source.Source.Producer.PortableKey,
            roundTripped.PackageInputs.RootSet.PackagePrefixCompletion!
                .Source.PortableProducerKey);
        Assert.Equal(
            "Example.",
            roundTripped.PackageInputs.RootSet.PackagePrefixCompletion!
                .Prefix.ToString());
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

    private static void AssertInvalidTargetIdentity<T>(string json)
    {
        JsonTypeInfo<T> typeInfo = (JsonTypeInfo<T>)
            DependencyInspectionJsonContext.Default.GetTypeInfo(typeof(T))!;
        Exception? exception = Record.Exception(
            () => JsonSerializer.Deserialize(json, typeInfo));

        Assert.True(
            exception is JsonException or ArgumentException,
            $"Expected invalid target identity rejection; received {exception?.GetType().Name ?? "no exception"}.");
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
