using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemDependencyRecognitionOutcomeTests
{
    [Fact]
    public void LibrarySubjectRejectsMismatchedSourceIdentity()
    {
        PortableLibraryIdentity library = Library("Expected.Library");

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencySubject.Library(
                ExactPackageLibrarySource(Library("Other.Library")),
                library));
    }

    [Fact]
    public void CompleteEmptyLibraryDocumentsRemainSubjectAttributable()
    {
        EcosystemDependencyRecognitionOutcome first = RecognizeEmptyLibrary(
            "First.Library");
        EcosystemDependencyRecognitionOutcome second = RecognizeEmptyLibrary(
            "Second.Library");

        var firstDocument =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                first).Document;
        var secondDocument =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                second).Document;
        Assert.Empty(firstDocument.Classification.Recognized);
        Assert.Empty(firstDocument.Classification.Unrecognized);
        Assert.NotEqual(firstDocument.Subject, secondDocument.Subject);
        Assert.Equal(
            EcosystemDependencyRecognitionCoverage.Complete,
            firstDocument.Coverage);
    }

    [Fact]
    public void IncompleteDocumentRetainsTrustworthyRecognitionAndIssue()
    {
        PortableLibraryIdentity library = Library("Partial.Library");
        var issue = new EcosystemDependencyInputIssue(
            new(1),
            EcosystemDependencyInputRole.AssemblyReferenceProjection,
            new InspectionDiagnostic(
                "ecosystem.references.partial",
                InspectionDiagnosticSeverity.Warning,
                "Some direct assembly references could not be projected."),
            new EcosystemDependencyInputIssueSource.Library(library));
        var batch = new EcosystemDependencyObservationBatch.Incomplete(
            new EcosystemDependencySubject.Library(
                ExactPackageLibrarySource(library),
                library),
            new EcosystemDependencyInputContext.Library(
                new EcosystemDependencyReferenceInput.Unavailable(issue.Identity)),
            [
                new EcosystemDependencyObservation.AssemblyReference(
                    new(1),
                    1,
                    new AssemblyReferenceIdentity(
                        "System.Net.Http",
                        new Version(10, 0, 0, 0),
                        null,
                        null),
                    library),
            ],
            [issue]);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Incomplete>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Equal(
            EcosystemDependencyRecognitionCoverage.Incomplete,
            outcome.Document.Coverage);
        Assert.Equal(
            EcosystemPackIds.Runtime,
            Assert.Single(outcome.Document.Classification.Recognized)
                .Ecosystem.Id);
        Assert.Same(issue, Assert.Single(outcome.Document.InputIssues));
    }

    [Fact]
    public void UnavailableOutcomeContainsNoClassificationDocument()
    {
        PortableLibraryIdentity library = Library("Broken.Library");
        var issue = new EcosystemDependencyInputIssue(
            new(1),
            EcosystemDependencyInputRole.AssemblyReferenceProjection,
            new InspectionDiagnostic(
                "ecosystem.references.failed",
                InspectionDiagnosticSeverity.Error,
                "Direct assembly references are unavailable."));
        var batch = new EcosystemDependencyObservationBatch.Unavailable(
            new EcosystemDependencySubject.Library(
                ExactPackageLibrarySource(library),
                library),
            new EcosystemDependencyInputContext.Library(
                new EcosystemDependencyReferenceInput.Unavailable(issue.Identity)),
            [issue]);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Unavailable>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Equal(library, Assert.IsType<EcosystemDependencySubject.Library>(
            outcome.Subject).Identity);
        Assert.Same(issue, Assert.Single(outcome.InputIssues));
    }

    [Fact]
    public void UnavailablePackageRetainsSubjectWithoutFabricatingInputs()
    {
        RealizedMemberCoordinate.Package coordinate = PackageSource();
        var issue = new EcosystemDependencyInputIssue(
            new(1),
            EcosystemDependencyInputRole.PackageManifestProjection,
            new InspectionDiagnostic(
                "ecosystem.manifest.failed",
                InspectionDiagnosticSeverity.Error,
                "The Package manifest is unavailable."),
            new EcosystemDependencyInputIssueSource.Package(coordinate));
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Unavailable(issue.Identity),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.NotAttempted(issue.Identity),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.NotAttempted(
                    issue.Identity));
        var batch = new EcosystemDependencyObservationBatch.Unavailable(
            new EcosystemDependencySubject.Package(coordinate),
            context,
            [issue]);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Unavailable>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Equal(
            coordinate,
            Assert.IsType<EcosystemDependencySubject.Package>(
                outcome.Subject).Coordinate);
        Assert.Same(context, outcome.InputContext);
        Assert.Same(issue, Assert.Single(outcome.InputIssues));
    }

    [Fact]
    public void MissingPackageHouseSettlementProducesAttributedUnavailableOutcome()
    {
        Assert.True(
            PackageEcosystemDependencyRecognitionInspection
                .TryCreateUnavailableWithoutAcquiredSettlement(
                    "Example.Package",
                    "1.2.3",
                    "explicit-local-input",
                    out InspectionEnvelope<
                        EcosystemDependencyRecognitionOutcome>? inspection));

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Unavailable>(
                inspection.Content);
        RealizedMemberCoordinate.Package coordinate =
            Assert.IsType<EcosystemDependencySubject.Package>(
                outcome.Subject).Coordinate;
        Assert.Equal("example.package", coordinate.PackageId);
        Assert.Equal("1.2.3", coordinate.Version);
        Assert.Equal("explicit-local-input", coordinate.Producer);
        Assert.Null(coordinate.Framework);
        Assert.Equal(
            [
                EcosystemDependencyInputRole.PackageManifestProjection,
                EcosystemDependencyInputRole
                    .EffectiveTargetFrameworkSelection,
                EcosystemDependencyInputRole
                    .SelectedCompileLibraryEnumeration,
            ],
            outcome.InputIssues.Select(static issue => issue.Role));
        Assert.Equal(3, inspection.Diagnostics.Length);
        Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);
    }

    [Fact]
    public void CompletePackageDocumentRetainsSelectionContextAndOverlap()
    {
        PackageFixture fixture = CompletePackageFixture(
            "net10.0",
            "net10.0",
            "Microsoft.Extensions.AI.Abstractions");
        var subject =
            new EcosystemDependencySubject.Package(fixture.Coordinate);
        var batch = new EcosystemDependencyObservationBatch.Available(
            subject,
            fixture.Context,
            [
                new EcosystemDependencyObservation.PackageDeclaration(
                    new(1),
                    1,
                    fixture.Dependency!,
                    fixture.Coordinate),
            ]);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Same(fixture.Context, outcome.Document.InputContext);
        Assert.Equal(subject, outcome.Document.Subject);
        Assert.Equal(
            [
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemPackIds.AI,
            ],
            outcome.Document.Classification.Recognized.Select(
                recognition => recognition.Ecosystem.Id));
    }

    [Fact]
    public void SelectedAssetCorrespondenceUsesMetadataIdentityNotDllFileName()
    {
        PackageFixture fixture = CompletePackageFixture(
            "net10.0",
            "net10.0");
        var observation =
            new EcosystemDependencyObservation.AssemblyReference(
                new(1),
                1,
                new AssemblyReferenceIdentity(
                    "System.Net.Http",
                    new Version(10, 0, 0, 0),
                    null,
                    null),
                fixture.SelectedLibrary);

        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(fixture.Coordinate),
            fixture.Context,
            [observation]);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Equal(
            "Sample.Package.dll",
            Assert.Single(fixture.Compile.Receipt.Selection.Assets)
                .AssemblyName);
        Assert.Equal(
            "Sample.Package",
            fixture.SelectedLibrary.Name);
        Assert.Equal(
            EcosystemPackIds.Runtime,
            Assert.Single(outcome.Document.Classification.Recognized)
                .Ecosystem.Id);
    }

    [Fact]
    public void EquivalentTargetFrameworkSpellingsCorrespond()
    {
        PackageFixture fixture = CompletePackageFixture(
            "netstandard2.0",
            ".NETStandard2.0");

        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(fixture.Coordinate),
            fixture.Context);

        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch));
    }

    [Fact]
    public void UniversalDependencyGroupCorrespondsToConcreteCompileSlice()
    {
        PackageFixture fixture = CompletePackageFixture(
            "net10.0",
            "any",
            universalGroup: true);

        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(fixture.Coordinate),
            fixture.Context);

        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch));
    }

    [Fact]
    public async Task ActualDefaultQuerySelectionRetainsOneImplicitRun()
    {
        InMemoryPackageContent content = PackageWithManifest(
            """
            <dependency id="Microsoft.Extensions.Options" version="10.0.0" />
            <group targetFramework="net11.0">
              <dependency id="Middle" version="1.0.0" />
            </group>
            <dependency id="System.Text.Json" version="10.0.0" />
            """,
            "ref/net10.0/_._");
        var available = Assert.IsType<PackageDependencyGroupsResult.Available>(
            await PackageDependencyGroupsQuery.ExecuteAsync(
                content,
                "sample.package",
                "1.0.0",
                "net10.0",
                TestContext.Current.CancellationToken));
        EcosystemDependencyInputContext.Package context =
            Context(available, CompileSelection(content, "net10.0"));
        DeclaredPackageDependency dependency =
            Assert.Single(available.Value.SelectedGroup!.Dependencies);
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(PackageSource("net10.0")),
            context,
            [
                new EcosystemDependencyObservation.PackageDeclaration(
                    new(1),
                    1,
                    dependency,
                    PackageSource("net10.0")),
            ]);

        Assert.Equal("Microsoft.Extensions.Options", dependency.Id);
        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch));
    }

    [Fact]
    public async Task ActualCompatibleQuerySelectionRetainsCoalescedImplicitRuns()
    {
        InMemoryPackageContent content = PackageWithManifest(
            """
            <dependency id="Microsoft.Extensions.Options" version="10.0.0" />
            <group targetFramework="net11.0">
              <dependency id="Middle" version="1.0.0" />
            </group>
            <dependency id="System.Text.Json" version="10.0.0" />
            """,
            "ref/net10.0/_._");
        var available = Assert.IsType<PackageDependencyGroupsResult.Available>(
            await PackageDependencyGroupsQuery.ExecuteAsync(
                content,
                "sample.package",
                "1.0.0",
                "net10.0",
                TestContext.Current.CancellationToken,
                allowCompatibleFallbackForRequestedTfm: true));
        EcosystemDependencyInputContext.Package context =
            Context(available, CompileSelection(content, "net10.0"));
        EcosystemDependencyObservation[] observations =
        [
            .. available.Value.SelectedGroup!.Dependencies.Select(
                (dependency, index) =>
                    new EcosystemDependencyObservation.PackageDeclaration(
                        new(index + 1),
                        index + 1,
                        dependency,
                        PackageSource("net10.0"))),
        ];
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(PackageSource("net10.0")),
            context,
            observations);

        Assert.Equal(
            ["Microsoft.Extensions.Options", "System.Text.Json"],
            available.Value.SelectedGroup.Dependencies.Select(
                dependency => dependency.Id));
        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualEmptyTargetUniversalGroupIsSuccessful(
        bool includeDependency)
    {
        string group = includeDependency
            ? """
              <group targetFramework="">
                <dependency id="System.Text.Json" version="10.0.0" />
              </group>
              """
            : """<group targetFramework="" />""";
        InMemoryPackageContent content = PackageWithManifest(
            group,
            "ref/net10.0/_._");
        var available = Assert.IsType<PackageDependencyGroupsResult.Available>(
            await PackageDependencyGroupsQuery.ExecuteAsync(
                content,
                "sample.package",
                "1.0.0",
                "net10.0",
                TestContext.Current.CancellationToken));
        EcosystemDependencyInputContext.Package context =
            Context(available, CompileSelection(content, "net10.0"));
        EcosystemDependencyObservation[] observations =
        [
            .. (available.Value.SelectedGroup?.Dependencies ?? [])
                .Select(
                    (dependency, index) =>
                        new EcosystemDependencyObservation.PackageDeclaration(
                            new(index + 1),
                            index + 1,
                            dependency,
                            PackageSource("net10.0"))),
        ];
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(PackageSource("net10.0")),
            context,
            observations);

        Assert.Equal("", available.Value.SelectedTargetFramework);
        Assert.Equal(
            includeDependency ? 1 : 0,
            available.Value.SelectedGroup!.Dependencies.Length);
        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch));
    }

    [Fact]
    public void PackageContextRejectsMismatchedEffectiveTargetRequests()
    {
        PackageFixture fixture = CompletePackageFixture(
            "net10.0",
            "net8.0",
            dependencyRequestedTarget: "net8.0");

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Available(
                new EcosystemDependencySubject.Package(fixture.Coordinate),
                fixture.Context));
    }

    [Fact]
    public void FailedDependencySelectionCannotAppearAvailable()
    {
        RealizedMemberCoordinate.Package coordinate = PackageSource("net10.0");
        PackageManifestFacts manifest = Manifest(
            new DeclaredPackageDependencyGroup(
                "net8.0",
                ImmutableArray<DeclaredPackageDependency>.Empty));
        var dependencyGroups = new PackageDependencyGroups(
            manifest.DependencyGroups,
            "net10.0",
            SelectedTargetFramework: null,
            SelectedGroupIndex: null,
            PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework);
        EcosystemDependencyPackageCompileSelection compile =
            CompileSelection("net10.0").Selection;
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Available(manifest),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.Available(dependencyGroups),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.Available(compile));

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Available(
                new EcosystemDependencySubject.Package(coordinate),
                context));
    }

    [Fact]
    public void FailedCompileSelectionCannotAppearAvailable()
    {
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(
                PackageContent("ref/net8.0/Sample.Package.dll"),
                "sample.package",
                PackageCompileAssetSelectionPolicy.ExactTarget,
                "net10.0");

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
            receipt.Selection.Status);
        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyPackageCompileSelection(receipt));
    }

    [Fact]
    public void NoDependencyGroupsAndNoCompileAssetsAreCompleteEmptyInputs()
    {
        RealizedMemberCoordinate.Package coordinate = PackageSource("net10.0");
        PackageManifestFacts manifest = Manifest();
        var dependencies = new PackageDependencyGroups(
            manifest.DependencyGroups,
            "net10.0",
            SelectedTargetFramework: null,
            SelectedGroupIndex: null,
            PackageDependencyGroupSelectionStatus.NoDependencyGroups);
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(
                PackageContent(),
                "sample.package",
                PackageCompileAssetSelectionPolicy.ExactTarget,
                "net10.0");
        var compile =
            new EcosystemDependencyPackageCompileSelection(receipt);
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Available(manifest),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.Available(dependencies),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.Available(compile));
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Package(coordinate),
            context);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            receipt.Selection.Status);
        Assert.Equal(0, outcome.Document.Classification.Summary.ObservationCount);
    }

    [Fact]
    public void PackageDeclarationsMustBelongToSelectedLogicalGroup()
    {
        var net8Dependency =
            new DeclaredPackageDependency("Contoso.Net8", "1.0.0");
        var net10Dependency =
            new DeclaredPackageDependency("Contoso.Net10", "1.0.0");
        var net8 = new DeclaredPackageDependencyGroup(
            "net8.0",
            [net8Dependency]);
        var net10 = new DeclaredPackageDependencyGroup(
            "net10.0",
            [net10Dependency]);
        PackageManifestFacts manifest = Manifest(net8, net10);
        var dependencies = new PackageDependencyGroups(
            manifest.DependencyGroups,
            "net10.0",
            "net10.0",
            SelectedGroupIndex: 1,
            PackageDependencyGroupSelectionStatus.Selected)
        {
            SelectedGroup = net10,
        };
        CompileFixture compile = CompileSelection("net10.0");
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Available(manifest),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.Available(dependencies),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.Available(
                    compile.Selection));
        RealizedMemberCoordinate.Package coordinate = PackageSource("net10.0");
        var observation =
            new EcosystemDependencyObservation.PackageDeclaration(
                new(1),
                1,
                net8Dependency,
                coordinate);

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Available(
                new EcosystemDependencySubject.Package(coordinate),
                context,
                [observation]));
    }

    [Fact]
    public void ContextIssueReferencesMustJoinContainedIssues()
    {
        PortableLibraryIdentity library = Library("Broken.Library");

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Unavailable(
                new EcosystemDependencySubject.Library(
                    ExactPackageLibrarySource(library),
                    library),
                new EcosystemDependencyInputContext.Library(
                    new EcosystemDependencyReferenceInput.Unavailable(new(2))),
                [
                    new EcosystemDependencyInputIssue(
                        new(1),
                        EcosystemDependencyInputRole.AssemblyReferenceProjection,
                        new InspectionDiagnostic(
                            "ecosystem.references.failed",
                            InspectionDiagnosticSeverity.Error,
                            "Direct assembly references are unavailable.")),
                ]));
    }

    [Fact]
    public void UnavailableComponentsRequireTheMatchingIssueRole()
    {
        RealizedMemberCoordinate.Package coordinate = PackageSource();
        var issue = new EcosystemDependencyInputIssue(
            new(1),
            EcosystemDependencyInputRole.AssemblyReferenceProjection,
            new InspectionDiagnostic(
                "ecosystem.manifest.failed",
                InspectionDiagnosticSeverity.Error,
                "The Package manifest is unavailable."));
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Unavailable(issue.Identity),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.NotAttempted(issue.Identity),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.NotAttempted(
                    issue.Identity));

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Unavailable(
                new EcosystemDependencySubject.Package(coordinate),
                context,
                [issue]));
    }

    [Fact]
    public void PackageAssemblyObservationsJoinSelectedCompileLibraries()
    {
        PackageFixture fixture = CompletePackageFixture(
            "net10.0",
            "net10.0");
        var observation =
            new EcosystemDependencyObservation.AssemblyReference(
                new(1),
                1,
                new AssemblyReferenceIdentity(
                    "System.Net.Http",
                    new Version(10, 0, 0, 0),
                    null,
                    null),
                Library("Other.Library"));

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Available(
                new EcosystemDependencySubject.Package(fixture.Coordinate),
                fixture.Context,
                [observation]));
    }

    [Fact]
    public void EnvelopePreservesSubjectBoundShareAndDiagnostics()
    {
        PortableLibraryIdentity library = Library("Envelope.Library");
        var subject =
            new EcosystemDependencySubject.Library(
                ExactPackageLibrarySource(library),
                library);
        var batch = new EcosystemDependencyObservationBatch.Available(
            subject,
            new EcosystemDependencyInputContext.Library(
                new EcosystemDependencyReferenceInput.Available()));
        var share = new InspectionShare.Available(
            "https://example.test/inspect?packet=abc",
            "abc");
        var diagnostic = new InspectionDiagnostic(
            "ecosystem.test",
            InspectionDiagnosticSeverity.Information,
            "Test diagnostic.");

        InspectionEnvelope<EcosystemDependencyRecognitionOutcome> envelope =
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch,
                new EcosystemDependencyRecognitionShare(subject, share),
                [diagnostic]);

        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            envelope.Content);
        Assert.Same(share, envelope.Share);
        Assert.Same(diagnostic, Assert.Single(envelope.Diagnostics));
    }

    [Fact]
    public void EnvelopeRejectsShareForAnotherSemanticSubject()
    {
        PortableLibraryIdentity library = Library("Envelope.Library");
        var subject =
            new EcosystemDependencySubject.Library(
                ExactPackageLibrarySource(library),
                library);
        var batch = new EcosystemDependencyObservationBatch.Available(
            subject,
            new EcosystemDependencyInputContext.Library(
                new EcosystemDependencyReferenceInput.Available()));
        var otherSubject = new EcosystemDependencySubject.Library(
            ExactPackageLibrarySource(Library("Other.Library")),
            Library("Other.Library"));
        var share = new EcosystemDependencyRecognitionShare(
            otherSubject,
            new InspectionShare.NonProjectable(
                "ecosystem-dependencies/share",
                "Test."));

        Assert.Throws<ArgumentException>(() =>
            EcosystemDependencyRecognizer.Recognize(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                batch,
                share));
    }

    private static EcosystemDependencyRecognitionOutcome RecognizeEmptyLibrary(
        string name)
    {
        PortableLibraryIdentity library = Library(name);
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Library(
                ExactPackageLibrarySource(library),
                library),
            new EcosystemDependencyInputContext.Library(
                new EcosystemDependencyReferenceInput.Available()));
        return EcosystemDependencyRecognizer.Recognize(
            EcosystemPackCatalog.DependencyRecognitionProfile,
            batch);
    }

    private static PackageFixture CompletePackageFixture(
        string compileTarget,
        string dependencyGroupTarget,
        string? dependencyId = null,
        string? dependencyRequestedTarget = null,
        bool universalGroup = false)
    {
        DeclaredPackageDependency? dependency = dependencyId is null
            ? null
            : new DeclaredPackageDependency(dependencyId, "[1.0.0,)");
        var group = new DeclaredPackageDependencyGroup(
            dependencyGroupTarget,
            dependency is null ? [] : [dependency],
            universalGroup);
        PackageManifestFacts manifest = Manifest(group);
        var dependencyGroups = new PackageDependencyGroups(
            manifest.DependencyGroups,
            dependencyRequestedTarget ?? compileTarget,
            dependencyGroupTarget,
            SelectedGroupIndex: 0,
            PackageDependencyGroupSelectionStatus.Selected)
        {
            SelectedGroup = group,
        };
        CompileFixture compile = CompileSelection(compileTarget);
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Available(manifest),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.Available(dependencyGroups),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.Available(
                    compile.Selection));
        return new(
            PackageSource(compileTarget),
            context,
            dependency,
            compile.Library,
            compile.Selection);
    }

    private static CompileFixture CompileSelection(string targetFramework)
    {
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(
                PackageContent(
                    $"ref/{targetFramework}/Sample.Package.dll"),
                "sample.package",
                PackageCompileAssetSelectionPolicy.ExactTarget,
                targetFramework);
        PackageCompileAsset asset = Assert.Single(receipt.Selection.Assets);
        PortableLibraryIdentity library = Library("Sample.Package");
        var selection = new EcosystemDependencyPackageCompileSelection(
            receipt,
            [new EcosystemDependencySelectedLibrary(asset, library)]);
        return new(selection, library);
    }

    private static EcosystemDependencyPackageCompileSelection CompileSelection(
        InMemoryPackageContent content,
        string targetFramework)
    {
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                "sample.package",
                PackageCompileAssetSelectionPolicy.ExactTarget,
                targetFramework);
        return new(receipt);
    }

    private static EcosystemDependencyInputContext.Package Context(
        PackageDependencyGroupsResult.Available dependencies,
        EcosystemDependencyPackageCompileSelection compile) =>
        new(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Available(dependencies.Manifest),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.Available(dependencies.Value),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.Available(compile));

    private static PackageManifestFacts Manifest(
        params DeclaredPackageDependencyGroup[] groups) =>
        new(
            PackageSourceCoordinate.Create("sample.package", "1.0.0"),
            "1.0.0",
            Description: null,
            Authors: null,
            Repository: null,
            RepositoryType: null,
            RepositoryCommit: null,
            License: null,
            LicenseUrl: null,
            PackageTypes: [],
            IsToolPackage: false,
            ReadmeFile: null,
            DependencyGroups: [.. groups]);

    private static InMemoryPackageContent PackageContent(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (string path in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream stream = entry.Open();
                stream.Write([1, 2, 3]);
            }
        }

        return new InMemoryPackageContent(
            buffer.ToArray(),
            fromCache: false,
            producerKey: "tests");
    }

    private static InMemoryPackageContent PackageWithManifest(
        string dependencies,
        params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            ZipArchiveEntry manifest =
                archive.CreateEntry("sample.package.nuspec");
            using (Stream stream = manifest.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(
                    $$"""
                    <package>
                      <metadata>
                        <id>sample.package</id>
                        <version>1.0.0</version>
                        <authors>Tests</authors>
                        <description>Tests</description>
                        <dependencies>
                          {{dependencies}}
                        </dependencies>
                      </metadata>
                    </package>
                    """);
                stream.Write(bytes);
            }

            foreach (string path in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream stream = entry.Open();
                stream.Write([1, 2, 3]);
            }
        }

        return new InMemoryPackageContent(
            buffer.ToArray(),
            fromCache: false,
            producerKey: "tests");
    }

    private static RealizedMemberCoordinate.Package PackageSource(
        string targetFramework = "net10.0") =>
        new(
            "sample.package",
            "1.0.0",
            PackageProducerIdentity.NuGetOrg.PortableKey,
            targetFramework,
            runtimeIdentifier: null);

    private static ExactLibrarySourceCoordinate.Package
        ExactPackageLibrarySource(PortableLibraryIdentity library) =>
        new(
            PackageSourceCoordinate.Create("sample.package", "1.0.0"),
            new ManagedMetadataIdentity.Assembly(
                new AssemblyReferenceIdentity(
                    library.Name,
                    Version.Parse(library.Version),
                    library.Culture,
                    library.PublicKeyToken)));

    private static PortableLibraryIdentity Library(string name) =>
        new(name, "1.0.0.0", culture: null, publicKeyToken: null);

    private sealed record CompileFixture(
        EcosystemDependencyPackageCompileSelection Selection,
        PortableLibraryIdentity Library);

    private sealed record PackageFixture(
        RealizedMemberCoordinate.Package Coordinate,
        EcosystemDependencyInputContext.Package Context,
        DeclaredPackageDependency? Dependency,
        PortableLibraryIdentity SelectedLibrary,
        EcosystemDependencyPackageCompileSelection Compile);
}
