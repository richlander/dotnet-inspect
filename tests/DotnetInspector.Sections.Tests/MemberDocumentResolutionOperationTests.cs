using System.Text.Json;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Fixtures;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class MemberDocumentResolutionOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public void PlanRejectsUnsupportedMetadataSpelling()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new MemberDocumentResolutionPlan(
                    new(
                        Name("N", "C"),
                        "M",
                        spelling:
                            TypeMemberGroupSpelling.Metadata),
                    s_bounds));

        Assert.Equal("group", exception.ParamName);
    }

    [Fact]
    public async Task NameOnlySingletonResolvesExactMemberDocument()
    {
        byte[] content =
            await File.ReadAllBytesAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberDocument document =
            Assert.IsType<MemberDocumentResolutionOutcome.Exact>(
                    Execute(
                        library,
                        Name(
                            "ILInspector.Decompiler.Fixtures.NewUnsafe",
                            "MemorySafetySpellingFixture"),
                        "PointerFreeUnsafeMethod")
                    .Content)
                .Document;

        Assert.Equal(
            "PointerFreeUnsafeMethod",
            document.Subject.Group.Name);
        Assert.Equal(1, document.Subject.BaselineOrdinal);
        Assert.Equal(
            MemberOverloadReceiverFilter.All,
            document.Subject.Population.Receiver);

        await library.RetireAsync();
    }

    [Fact]
    public async Task NameOnlyMultiMemberResolvesCompleteOverview()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberDocumentResolutionOutcome outcome =
            Execute(
                    library,
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Serialize")
                .Content;
        MemberOverviewDocument document =
            Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
                    outcome)
                .Document;

        Assert.Equal(15, document.Members.Length);
        Assert.All(
            document.Members,
            member =>
            {
                Assert.Equal(document.Subject, member.Subject.Group);
                Assert.Equal(
                    document.Population,
                    member.Subject.Population);
            });
        string json = JsonSerializer.Serialize(
            outcome,
            TypeMemberInspectionDocumentJsonContext.Default
                .MemberDocumentResolutionOutcome);
        MemberDocumentResolutionOutcome copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .MemberDocumentResolutionOutcome)!;
        Assert.Equal(
            15,
            Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
                    copy)
                .Document.Members.Length);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        NameOnlyOverviewComposesDocumentationForEveryResolvedMember()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryInspectionTestLibrary.Identity(content);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                identity);
        var request = new MemberDocumentAttachmentCompositionRequest(
            library.Reference,
            new(
                new(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Serialize"),
                s_bounds),
            new(DocumentationDemand.CompiledXml));
        int calls = 0;

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentAttachmentCompositionResult>
            inspection =
                await MemberDocumentAttachmentCompositionOperation
                    .ExecuteAsync(
                        request,
                        operation,
                        (ids, demand, _) =>
                        {
                            calls++;
                            Assert.Equal(
                                DocumentationDemand.CompiledXml,
                                demand);
                            IReadOnlyDictionary<
                                string,
                                DocumentationQueryOutcome> outcomes =
                                    ids.ToDictionary(
                                        static id => id,
                                        id =>
                                            (DocumentationQueryOutcome)
                                                new DocumentationQueryOutcome
                                                    .RequestRejected(
                                                        DocumentationSubject(
                                                            identity,
                                                            id),
                                                        DocumentationQueryRequestRejectionReason
                                                            .LeaseReferenceMismatch),
                                        StringComparer.Ordinal);
                            return ValueTask.FromResult(outcomes);
                        },
                        TestContext.Current.CancellationToken);

        MemberOverviewDocument document =
            Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
                    inspection.Content.Resolution)
                .Document;
        var documentation =
            Assert.IsType<
                MemberDocumentationAttachmentOutcome.Available>(
                inspection.Content.Documentation);
        Assert.Equal(1, calls);
        Assert.Equal(15, document.Members.Length);
        Assert.Equal(
            document.Members.Select(
                static member => member.Subject),
            documentation.Attachments.Select(
                static attachment => attachment.Subject));

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        DocumentationFailurePreservesCompletedFinalResolution()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var request = new MemberDocumentAttachmentCompositionRequest(
            library.Reference,
            new(
                new(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Serialize"),
                s_bounds),
            new(DocumentationDemand.CompiledXml));

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentAttachmentCompositionResult>
            inspection =
                await MemberDocumentAttachmentCompositionOperation
                    .ExecuteAsync(
                        request,
                        operation,
                        static (_, _, _) =>
                            ValueTask.FromResult<
                                IReadOnlyDictionary<
                                    string,
                                    DocumentationQueryOutcome>>(
                                new Dictionary<
                                    string,
                                    DocumentationQueryOutcome>()),
                        TestContext.Current.CancellationToken);

        Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
            inspection.Content.Resolution);
        Assert.Equal(
            MemberDocumentationAttachmentFailure.ResultSetMismatch,
            Assert.IsType<
                    MemberDocumentationAttachmentOutcome.Failed>(
                    inspection.Content.Documentation)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task ResolutionRejectionSkipsDocumentationComposition()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var request = new MemberDocumentAttachmentCompositionRequest(
            library.Reference,
            new(
                new(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Missing"),
                s_bounds),
            new(DocumentationDemand.CompiledXml));
        int calls = 0;

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentAttachmentCompositionResult>
            inspection =
                await MemberDocumentAttachmentCompositionOperation
                    .ExecuteAsync(
                        request,
                        operation,
                        (_, _, _) =>
                        {
                            calls++;
                            throw new InvalidOperationException(
                                "Documentation should not execute after resolution rejection.");
                        },
                        TestContext.Current.CancellationToken);

        Assert.IsType<MemberDocumentResolutionOutcome.Rejected>(
            inspection.Content.Resolution);
        Assert.Null(inspection.Content.Documentation);
        Assert.Equal(0, calls);

        await library.RetireAsync();
    }

    [Fact]
    public async Task ExactSelectorsResolveTheSameNonFirstMember()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MemberOverviewDocument overview =
            Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
                    Execute(library, type, "Serialize").Content)
                .Document;
        MemberDeclaration expected = overview.Members[5];

        MemberDocument ordinal =
            Exact(
                Execute(
                    library,
                    type,
                    "Serialize",
                    new(
                        baselineOrdinal:
                            expected.Subject.BaselineOrdinal)));
        MemberDocument fingerprint =
            Exact(
                Execute(
                    library,
                    type,
                    "Serialize",
                    new(
                        fingerprintPrefix:
                            expected.Subject.Fingerprint.ToString())));
        MemberDocument token =
            Exact(
                Execute(
                    library,
                    type,
                    "Serialize",
                    new(
                        metadataToken:
                            expected.Subject.MetadataToken)));

        Assert.Equal(expected.Subject, ordinal.Subject);
        Assert.Equal(ordinal, fingerprint);
        Assert.Equal(ordinal, token);

        await library.RetireAsync();
    }

    [Fact]
    public async Task PopulationIntentAppliesBeforeDocumentRouting()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverviewDocument document =
            Assert.IsType<MemberDocumentResolutionOutcome.Overview>(
                    Execute(
                        library,
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        "Deserialize",
                        receiver:
                            MemberOverloadReceiverFilter.Extension)
                    .Content)
                .Document;

        Assert.Equal(15, document.Members.Length);
        Assert.Equal(
            MemberOverloadReceiverFilter.Extension,
            document.Population.Receiver);
        Assert.All(
            document.Members,
            static member =>
                Assert.Equal(
                    MemberReceiver.Extension,
                    member.Receiver));

        await library.RetireAsync();
    }

    [Fact]
    public async Task NameOnlyResolutionRejectsTruncatedOverview()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 5_000,
            maxMembers: 10,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

        var incomplete =
            Assert.IsType<MemberDocumentResolutionOutcome.Incomplete>(
                Execute(
                    library,
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Serialize",
                    bounds: bounds)
                .Content);

        Assert.Equal(
            MemberOverloadPopulationBound.Members,
            incomplete.Bound);
        Assert.Equal(10, incomplete.Limit);
        Assert.Equal(11, incomplete.Measured);

        await library.RetireAsync();
    }

    [Fact]
    public async Task NameOnlyResolutionPreservesMissingGroupRejection()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        var rejected =
            Assert.IsType<MemberDocumentResolutionOutcome.Rejected>(
                Execute(
                    library,
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Missing")
                .Content);

        Assert.Equal(
            MemberDocumentResolutionRejection.MemberGroupNotFound,
            rejected.Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task ExactResolutionPreservesSelectorRejection()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        var rejected =
            Assert.IsType<MemberDocumentResolutionOutcome.Rejected>(
                Execute(
                    library,
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    "Serialize",
                    new(baselineOrdinal: 16))
                .Content);

        Assert.Equal(
            MemberDocumentResolutionRejection
                .BaselineOrdinalOutOfRange,
            rejected.Reason);

        await library.RetireAsync();
    }

    private static MemberDocument Exact(
        InspectionEnvelope<MemberDocumentResolutionOutcome> envelope) =>
        Assert.IsType<MemberDocumentResolutionOutcome.Exact>(
                envelope.Content)
            .Document;

    private static InspectionEnvelope<MemberDocumentResolutionOutcome>
        Execute(
            LibraryInspectionTestLibrary library,
            MetadataTypeDefinitionName type,
            string memberName,
            MemberDocumentSelector? selector = null,
            ApiSurfaceExtractionBounds? bounds = null,
            MemberOverloadAccessibilityFilter accessibility =
                MemberOverloadAccessibilityFilter.Public,
            MemberOverloadReceiverFilter receiver =
                MemberOverloadReceiverFilter.All,
            bool includeHidden = false) =>
        MemberDocumentResolutionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(type, memberName),
                    bounds ?? s_bounds,
                    selector,
                    accessibility,
                    receiver,
                    includeHidden)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static CompiledDocumentationSubject DocumentationSubject(
        ManagedMetadataIdentity.Assembly identity,
        string documentationId)
    {
        AssemblyReferenceIdentity assembly = identity.Identity;
        return new(
            new(
                assembly.Name,
                assembly.Version?.ToString(),
                assembly.Culture,
                assembly.PublicKeyToken),
            documentationId);
    }
}
