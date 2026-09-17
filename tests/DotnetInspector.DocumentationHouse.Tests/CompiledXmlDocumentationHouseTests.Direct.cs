using DotnetInspector.DocumentationHouse.Direct;
using DotnetInspector.Libraries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed partial class CompiledXmlDocumentationHouseTests
{
    [Fact]
    public async Task
        DirectLibraryAdapterSettlesRealSystemTextJsonMember()
    {
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);

        IReadOnlyList<CompiledXmlContribution> contributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    subject);

        CompiledXmlContribution contribution =
            Assert.Single(contributions);
        Assert.Equal(
            CompiledXmlContributionKind.Candidate,
            contribution.Kind);
        Assert.Equal(
            DocumentationSourceKind.DirectLibrary,
            contribution.Source.Kind);
        Assert.Equal(
            "direct-library:System.Text.Json",
            contribution.Source.Name);
        Assert.Same(library.Reference, contribution.Library);
        Assert.Same(
            library.Reference.ApiAssembly,
            contribution.ApiContent);
        Assert.Same(
            library.Reference.ApiAssembly,
            contribution.CompiledXmlContent!.AssociatedAssembly);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(subject, contributions));
        DocumentationCompiledXmlAttempt.Available available =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Available>(
                    completed.CompiledXmlAttempt);
        Assert.Contains(
            "Converts the JsonDocument",
            available.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.Same(contribution, available.Selected);
    }

    [Fact]
    public async Task
        DirectLibraryWithoutAdmittedXmlIsUnavailable()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);

        IReadOnlyList<CompiledXmlContribution> contributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    subject);

        CompiledXmlContribution contribution =
            Assert.Single(contributions);
        Assert.Equal(
            CompiledXmlContributionKind.Unavailable,
            contribution.Kind);
        Assert.Null(contribution.CompiledXmlContent);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(subject, contributions));
        Assert.IsType<DocumentationCompiledXmlAttempt.Unavailable>(
            completed.CompiledXmlAttempt);
        Assert.False(completed.Work.ParsedCompiledXml);
        Assert.Equal(0, completed.Work.CompiledXmlBytesObserved);
    }

    [Fact]
    public async Task
        DirectLibraryAdapterPreservesMultipleCompanions()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                Xml(DeserializeIdentity, "first"),
                Xml(DeserializeIdentity, "second"));
        DocumentationSubjectReference subject = Subject(library);

        IReadOnlyList<CompiledXmlContribution> contributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    library.Reference,
                    subject);

        Assert.Equal(2, contributions.Count);
        Assert.All(
            contributions,
            contribution =>
                Assert.Equal(
                    CompiledXmlContributionKind.Candidate,
                    contribution.Kind));
        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(subject, contributions));
        Assert.IsType<DocumentationCompiledXmlAttempt.Ambiguous>(
            completed.CompiledXmlAttempt);
    }

    [Fact]
    public async Task
        ByteIdenticalForeignDirectLibraryCannotSatisfySubject()
    {
        byte[] xml =
            Xml(DeserializeIdentity, "selected");
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync(xml);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(selected);
        IReadOnlyList<CompiledXmlContribution> contributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateCompiledXmlContributions(
                    foreign.Reference,
                    subject);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                selected,
                Request(subject, contributions));
        DocumentationCompiledXmlAttempt.Rejected rejected =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Rejected>(
                    completed.CompiledXmlAttempt);
        Assert.Equal(
            DocumentationCompiledXmlRejectionKind.LibraryMismatch,
            Assert.Single(rejected.Rejections).Kind);
        Assert.False(completed.Work.ParsedCompiledXml);
    }

    [Fact]
    public async Task
        SourceCoordinatedLibraryIsRejected()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        LibraryReference sourceLibrary =
            LibraryReference.CreateFromSource(
                new ExactLibrarySourceCoordinate.Local(
                    library.Reference.ApiAssembly
                        .AssemblyIdentity!),
                library.Reference.AssemblyCorrespondence,
                library.Reference.CompanionCorrespondences);

        ArgumentException error =
            Assert.Throws<ArgumentException>(
                () => DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(
                        sourceLibrary,
                        subject));
        Assert.Equal("library", error.ParamName);
    }
}
