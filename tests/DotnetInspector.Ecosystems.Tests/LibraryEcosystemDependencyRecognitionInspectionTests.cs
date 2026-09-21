using System.Collections.Immutable;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class LibraryEcosystemDependencyRecognitionInspectionTests
{
    [Fact]
    public void ExecuteRecognizesDirectAssemblyReferences()
    {
        ExactLibrarySourceCoordinate source = LocalSource(
            "Microsoft.Extensions.Http");
        var references = new AssemblyReferencesResult.Available(
            [
                Reference("System.Net.Http"),
                Reference("Microsoft.Extensions.Options"),
                Reference("ThirdParty.Client"),
            ]);

        var inspection =
            LibraryEcosystemDependencyRecognitionInspection.Execute(
                source,
                references);

        var complete =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                inspection.Content);
        Assert.Equal(source, Assert.IsType<EcosystemDependencySubject.Library>(
            complete.Document.Subject).Source);
        Assert.Equal(
            [".NET Runtime", "Microsoft.Extensions"],
            complete.Document.Classification.RecognizedEcosystems
                .Select(static ecosystem => ecosystem.Title));
        Assert.Equal(
            ["ThirdParty.Client"],
            complete.Document.Classification.Unrecognized
                .Select(static observation =>
                    Assert.IsType<
                        EcosystemDependencyObservation.AssemblyReference>(
                            observation)
                        .Reference.Name));
        Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public void ExecutePreservesOverlappingEcosystems()
    {
        var references = new AssemblyReferencesResult.Available(
            [Reference("Microsoft.AspNetCore.Components.WebView.Maui")]);

        var complete =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                LibraryEcosystemDependencyRecognitionInspection.Execute(
                    LocalSource("Sample.Library"),
                    references).Content);

        Assert.Equal(
            ["ASP.NET Core", "Blazor", ".NET MAUI"],
            complete.Document.Classification.Recognized
                .Select(static entry => entry.Ecosystem.Title));
    }

    [Fact]
    public void ExecutePreservesCompleteEmptyReferences()
    {
        var complete =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Complete>(
                LibraryEcosystemDependencyRecognitionInspection.Execute(
                    LocalSource("Sample.Library"),
                    new AssemblyReferencesResult.Available([])).Content);

        Assert.Equal(0, complete.Document.Classification.Summary.ObservationCount);
        Assert.Empty(complete.Document.Classification.Recognized);
        Assert.Empty(complete.Document.Classification.Unrecognized);
    }

    [Fact]
    public void ExecuteReportsUnavailableReferences()
    {
        var inspection =
            LibraryEcosystemDependencyRecognitionInspection.Execute(
                LocalSource("Sample.Library"),
                new AssemblyReferencesResult.Failed(
                    new BadImageFormatException("broken references")));

        var unavailable =
            Assert.IsType<EcosystemDependencyRecognitionOutcome.Unavailable>(
                inspection.Content);
        Assert.Single(unavailable.InputIssues);
        Assert.IsType<EcosystemDependencyReferenceInput.Unavailable>(
            Assert.IsType<EcosystemDependencyInputContext.Library>(
                unavailable.InputContext).DirectReferences);
        Assert.Collection(
            inspection.Diagnostics,
            diagnostic => Assert.Equal(
                "ecosystem-dependency-recognition.library-references-unavailable",
                diagnostic.Code));
    }

    private static ExactLibrarySourceCoordinate LocalSource(string name) =>
        new ExactLibrarySourceCoordinate.Local(
            new ManagedMetadataIdentity.Assembly(Reference(name)));

    private static AssemblyReferenceIdentity Reference(string name) =>
        new(name, new Version(1, 0, 0, 0), null, null);
}
