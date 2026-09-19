using System.Collections.Immutable;
using System.IO.Compression;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemDependencyRecognitionOutcomeTests
{
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
            new EcosystemDependencySubject.Library(PackageSource(), library),
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
            new EcosystemDependencySubject.Library(PackageSource(), library),
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
                EcosystemDependencyGroupSelection>.NotAttempted(issue.Identity),
            new EcosystemDependencyInputComponent<
                PackageCompileAssetSelectionReceipt>.NotAttempted(
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
    public void CompletePackageDocumentRetainsSelectionContextAndOverlap()
    {
        RealizedMemberCoordinate.Package coordinate = PackageSource();
        EcosystemDependencyInputContext.Package context =
            CompletePackageContext("net10.0");
        var subject = new EcosystemDependencySubject.Package(coordinate);
        var batch = new EcosystemDependencyObservationBatch.Available(
            subject,
            context,
            [
                new EcosystemDependencyObservation.PackageDeclaration(
                    new(1),
                    1,
                    new DeclaredPackageDependency(
                        "Microsoft.Extensions.AI.Abstractions",
                        "[10.0.0,)"),
                    coordinate),
            ]);

        var outcome =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                EcosystemDependencyRecognizer.Recognize(
                    EcosystemPackCatalog.DependencyRecognitionProfile,
                    batch));

        Assert.Same(context, outcome.Document.InputContext);
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
    public void PackageContextRejectsMismatchedSelectionSlices()
    {
        RealizedMemberCoordinate.Package coordinate = PackageSource();
        EcosystemDependencyInputContext.Package context =
            CompletePackageContext(
                dependencyTargetFramework: "net8.0",
                compileTargetFramework: "net10.0");

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Available(
                new EcosystemDependencySubject.Package(coordinate),
                context));
    }

    [Fact]
    public void ContextIssueReferencesMustJoinContainedIssues()
    {
        PortableLibraryIdentity library = Library("Broken.Library");

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyObservationBatch.Unavailable(
                new EcosystemDependencySubject.Library(
                    PackageSource(),
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
                EcosystemDependencyGroupSelection>.NotAttempted(issue.Identity),
            new EcosystemDependencyInputComponent<
                PackageCompileAssetSelectionReceipt>.NotAttempted(
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
        RealizedMemberCoordinate.Package coordinate = PackageSource();
        EcosystemDependencyInputContext.Package context =
            CompletePackageContext("net10.0");
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
                new EcosystemDependencySubject.Package(coordinate),
                context,
                [observation]));
    }

    [Fact]
    public void EnvelopePreservesContentShareAndDiagnostics()
    {
        PortableLibraryIdentity library = Library("Envelope.Library");
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Library(PackageSource(), library),
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
                share,
                [diagnostic]);

        Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
            envelope.Content);
        Assert.Same(share, envelope.Share);
        Assert.Same(diagnostic, Assert.Single(envelope.Diagnostics));
    }

    private static EcosystemDependencyRecognitionOutcome RecognizeEmptyLibrary(
        string name)
    {
        PortableLibraryIdentity library = Library(name);
        var batch = new EcosystemDependencyObservationBatch.Available(
            new EcosystemDependencySubject.Library(PackageSource(), library),
            new EcosystemDependencyInputContext.Library(
                new EcosystemDependencyReferenceInput.Available()));
        return EcosystemDependencyRecognizer.Recognize(
            EcosystemPackCatalog.DependencyRecognitionProfile,
            batch);
    }

    private static EcosystemDependencyInputContext.Package CompletePackageContext(
        string dependencyTargetFramework,
        string? compileTargetFramework = null)
    {
        compileTargetFramework ??= dependencyTargetFramework;
        PackageManifestFacts manifest = Manifest();
        PackageCompileAssetSelectionReceipt compile =
            PackageCompileAssetSelector.Evaluate(
                PackageContent(
                    $"ref/{compileTargetFramework}/Sample.Package.dll"),
                "sample.package",
                PackageCompileAssetSelectionPolicy.ExactTarget,
                compileTargetFramework);
        return new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Available(manifest),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyGroupSelection>.Available(
                    new EcosystemDependencyGroupSelection(
                        dependencyTargetFramework,
                        PackageDependencyGroupSelectionStatus.Selected,
                        dependencyTargetFramework,
                        selectedGroupIndex: 0)),
            new EcosystemDependencyInputComponent<
                PackageCompileAssetSelectionReceipt>.Available(compile));
    }

    private static PackageManifestFacts Manifest() =>
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
            DependencyGroups:
            [
                new DeclaredPackageDependencyGroup(
                    "net10.0",
                    ImmutableArray<DeclaredPackageDependency>.Empty),
            ]);

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

    private static RealizedMemberCoordinate.Package PackageSource() =>
        new(
            "sample.package",
            "1.0.0",
            PackageProducerIdentity.NuGetOrg.PortableKey,
            "net10.0",
            runtimeIdentifier: null);

    private static PortableLibraryIdentity Library(string name) =>
        new(name, "1.0.0.0", culture: null, publicKeyToken: null);
}
