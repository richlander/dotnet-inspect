using System.Reflection;
using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryOverviewInspectionOperationTests
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
    public async Task
        RealSystemTextJson_ReturnsDetachedPortableOverviewAndSettlesLease()
    {
        byte[] content =
            await LibraryOverviewTestLibrary.RealSystemTextJsonAsync();
        await using LibraryOverviewTestLibrary library =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                LibraryOverviewTestLibrary.Identity(content));
        LibraryOperationLease operation = library.IssueOperation();

        InspectionEnvelope<LibraryOverviewOutcome> envelope =
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(
                    library.Reference,
                    s_bounds),
                operation,
                TestContext.Current.CancellationToken);

        LibraryOverviewDocument document =
            Assert.IsType<LibraryOverviewOutcome.Available>(
                    envelope.Content)
                .Document;
        Assert.Equal("System.Text.Json", document.Assembly.Name.ToString());
        Assert.Equal(new Version(10, 0, 0, 0), document.Assembly.Version);
        Assert.NotEqual(Guid.Empty, document.ModuleVersionId);
        Assert.True(document.PublicTypeCount > 0);
        Assert.True(document.PublicMethodCount > 0);
        Assert.Equal(
            (long)document.PublicMethodCount
                + document.PublicPropertyCount
                + document.PublicEventCount
                + document.PublicFieldCount,
            document.TotalPublicMemberCount);
        Assert.True(document.MetadataRows > 0);
        Assert.True(document.RetainedTextCharacters > 0);
        Assert.Equal(s_bounds, document.Bounds);
        Assert.Empty(envelope.Diagnostics);
        var projection =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(
                envelope.PortableProjection);
        Assert.Equal("library-overview", envelope.ResourcePath.Value);
        Assert.Equal("scenario", projection.Location);
        Assert.Equal(
            InspectionPortableProjectionFailureReason.Unavailable,
            projection.Reason);
        Assert.Equal(
            "A complete portable Workspace scenario was not supplied.",
            projection.Explanation);
        await library.RetireAsync();
        AssertDetachedContract();
    }

    [Fact]
    public async Task
        EquivalentLibrariesProduceEqualDocumentsAndRejectForeignLease()
    {
        byte[] content =
            await LibraryOverviewTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryOverviewTestLibrary.Identity(content);
        var equivalentIdentity = new ManagedMetadataIdentity.Assembly(
            identity.Identity with
            {
                Name = identity.Identity.Name.ToUpperInvariant(),
                Culture = "neutral",
            });
        await using LibraryOverviewTestLibrary first =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                identity);
        await using LibraryOverviewTestLibrary second =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                equivalentIdentity);

        LibraryOverviewDocument firstDocument = AvailableDocument(
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(first.Reference, s_bounds),
                first.IssueOperation(),
                TestContext.Current.CancellationToken));
        LibraryOverviewDocument secondDocument = AvailableDocument(
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(second.Reference, s_bounds),
                second.IssueOperation(),
                TestContext.Current.CancellationToken));

        Assert.Equal(firstDocument, secondDocument);

        InspectionEnvelope<LibraryOverviewOutcome> rejected =
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(first.Reference, s_bounds),
                second.IssueOperation(),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            LibraryOverviewRejection.LeaseReferenceMismatch,
            Assert.IsType<LibraryOverviewOutcome.Rejected>(
                    rejected.Content)
                .Reason);
        Assert.Equal(
            "library-overview.rejected.lease-reference-mismatch",
            Assert.Single(rejected.Diagnostics).Code);

        await first.RetireAsync();
        await second.RetireAsync();
    }

    [Fact]
    public async Task ExtractionBoundReturnsIncompleteWithoutDocument()
    {
        byte[] content =
            await LibraryOverviewTestLibrary.RealSystemTextJsonAsync();
        await using LibraryOverviewTestLibrary library =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                LibraryOverviewTestLibrary.Identity(content));
        var zeroTypes = new ApiSurfaceExtractionBounds(
            maxTypes: 0,
            maxMembers: int.MaxValue,
            maxInspectionFailures: int.MaxValue,
            maxTypeForwarders: int.MaxValue,
            maxMetadataRows: int.MaxValue,
            maxRetainedTextCharacters: int.MaxValue);

        InspectionEnvelope<LibraryOverviewOutcome> envelope =
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(
                    library.Reference,
                    zeroTypes),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var incomplete =
            Assert.IsType<LibraryOverviewOutcome.Incomplete>(
                envelope.Content);
        Assert.Equal(
            ApiSurfaceExtractionBound.Types,
            Assert.IsType<
                    LibraryOverviewIncompleteReason.ExtractionBound>(
                    incomplete.Reason)
                .Bound);
        Assert.Equal(
            "library-overview.incomplete.extraction-bound",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task RetainedInspectionFailureReturnsIncomplete()
    {
        byte[] content =
            LibraryOverviewTestLibrary.BuildMetadataImage(
                malformedPublicType: true);
        await using LibraryOverviewTestLibrary library =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                LibraryOverviewTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryOverviewOutcome> envelope =
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(
                    library.Reference,
                    s_bounds),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        var incomplete =
            Assert.IsType<LibraryOverviewOutcome.Incomplete>(
                envelope.Content);
        Assert.True(
            Assert.IsType<
                    LibraryOverviewIncompleteReason
                        .MetadataInspectionFailures>(
                    incomplete.Reason)
                .Count > 0);
        Assert.Equal(
            "library-overview.incomplete.metadata-inspection-failures",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task AssemblyIdentityMismatchReturnsRejected()
    {
        byte[] content =
            await LibraryOverviewTestLibrary.RealSystemTextJsonAsync();
        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using LibraryOverviewTestLibrary library =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                wrongIdentity);

        InspectionEnvelope<LibraryOverviewOutcome> envelope =
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(
                    library.Reference,
                    s_bounds),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            LibraryOverviewRejection.AssemblyIdentityMismatch,
            Assert.IsType<LibraryOverviewOutcome.Rejected>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            "library-overview.rejected.assembly-identity-mismatch",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Theory]
    [MemberData(nameof(FailedImages))]
    public async Task UnsupportedContentReturnsTypedFailure(
        byte[] content,
        LibraryOverviewFailure expected,
        string diagnosticCode)
    {
        await using LibraryOverviewTestLibrary library =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                LibraryOverviewTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryOverviewOutcome> envelope =
            LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(
                    library.Reference,
                    s_bounds),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            expected,
            Assert.IsType<LibraryOverviewOutcome.Failed>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            diagnosticCode,
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    public static TheoryData<
        byte[],
        LibraryOverviewFailure,
        string> FailedImages()
    {
        byte[] windowsMetadata =
            LibraryOverviewTestLibrary.BuildMetadataImage(
                metadataVersion:
                    "WindowsRuntime 1.4;CLR v4.0.30319");
        return new TheoryData<
            byte[],
            LibraryOverviewFailure,
            string>
        {
            {
                [1, 2, 3],
                LibraryOverviewFailure.MalformedMetadata,
                "library-overview.failed.malformed-metadata"
            },
            {
                LibraryOverviewTestLibrary.BuildMetadataImage(
                    includeAssembly: false),
                LibraryOverviewFailure.ManagedModule,
                "library-overview.failed.managed-module"
            },
            {
                windowsMetadata,
                LibraryOverviewFailure.UnsupportedWindowsMetadata,
                "library-overview.failed.unsupported-windows-metadata"
            },
            {
                LibraryOverviewTestLibrary.BuildMetadataImage(
                    emptyModuleVersionId: true),
                LibraryOverviewFailure.EmptyModuleVersionId,
                "library-overview.failed.empty-module-version-id"
            },
        };
    }

    [Fact]
    public async Task CancellationAndRequestFailureSettleTransferredLease()
    {
        byte[] content =
            await LibraryOverviewTestLibrary.RealSystemTextJsonAsync();
        await using LibraryOverviewTestLibrary cancelled =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                LibraryOverviewTestLibrary.Identity(content));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibraryOverviewInspectionOperation.Execute(
                new LibraryOverviewRequest(
                    cancelled.Reference,
                    s_bounds),
                cancelled.IssueOperation(),
                cancellation.Token));
        await cancelled.RetireAsync();

        await using LibraryOverviewTestLibrary invalid =
            await LibraryOverviewTestLibrary.CreateAsync(
                content,
                LibraryOverviewTestLibrary.Identity(content));
        Assert.Throws<ArgumentNullException>(
            () => LibraryOverviewInspectionOperation.Execute(
                null!,
                invalid.IssueOperation(),
                TestContext.Current.CancellationToken));
        await invalid.RetireAsync();
    }

    [Fact]
    public void ClosedOutcomeSerializesWithSourceGeneration()
    {
        LibraryOverviewDocument document = new(
            new LibraryOverviewAssemblyIdentity(
                new InertString(TextPolicy.Field, "Example"),
                new Version(1, 2, 3, 4),
                null,
                null),
            Guid.NewGuid(),
            PublicTypeCount: 1,
            PublicMethodCount: 2,
            PublicPropertyCount: 3,
            PublicEventCount: 4,
            PublicFieldCount: 5,
            TotalPublicMemberCount: 14,
            MetadataRows: 20,
            RetainedTextCharacters: 30,
            s_bounds);
        InspectionEnvelope<LibraryOverviewOutcome>[] envelopes =
        [
            Envelope(new LibraryOverviewOutcome.Available(document)),
            Envelope(
                new LibraryOverviewOutcome.Incomplete(
                    new LibraryOverviewIncompleteReason.ExtractionBound(
                        ApiSurfaceExtractionBound.Members))),
            Envelope(
                new LibraryOverviewOutcome.Incomplete(
                    new LibraryOverviewIncompleteReason
                        .MetadataInspectionFailures(2))),
            Envelope(
                new LibraryOverviewOutcome.Rejected(
                    LibraryOverviewRejection.AssemblyIdentityMismatch)),
            Envelope(
                new LibraryOverviewOutcome.Failed(
                    LibraryOverviewFailure.MalformedMetadata)),
        ];

        foreach (
            InspectionEnvelope<LibraryOverviewOutcome> envelope
            in envelopes)
        {
            string json = JsonSerializer.Serialize(
                envelope,
                LibraryOverviewInspectionJsonContext.Default
                    .LibraryOverviewInspectionEnvelope);
            Assert.Contains("\"resourcePath\"", json, StringComparison.Ordinal);
            Assert.Contains("\"content\"", json, StringComparison.Ordinal);
            Assert.Contains(
                "\"portableProjection\"",
                json,
                StringComparison.Ordinal);
        }
    }

    private static InspectionEnvelope<LibraryOverviewOutcome> Envelope(
        LibraryOverviewOutcome outcome) =>
        new(
            new ResourcePath("library-overview"),
            InspectionContentKind.Outcome,
            outcome,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.Unavailable,
                location: "scenario",
                explanation:
                    "A complete portable Workspace scenario was not supplied."));

    private static LibraryOverviewDocument AvailableDocument(
        InspectionEnvelope<LibraryOverviewOutcome> envelope) =>
        Assert.IsType<LibraryOverviewOutcome.Available>(
                envelope.Content)
            .Document;

    private static void AssertDetachedContract()
    {
        Type[] forbidden =
        [
            typeof(IDisposable),
            typeof(IAsyncDisposable),
            typeof(Stream),
            typeof(Delegate),
            typeof(LibraryReference),
            typeof(LibraryOperationLease),
        ];
        Type[] contract =
        [
            typeof(InspectionEnvelope<LibraryOverviewOutcome>),
            typeof(LibraryOverviewOutcome),
            typeof(LibraryOverviewOutcome.Available),
            typeof(LibraryOverviewOutcome.Incomplete),
            typeof(LibraryOverviewOutcome.Rejected),
            typeof(LibraryOverviewOutcome.Failed),
            typeof(LibraryOverviewIncompleteReason),
            typeof(LibraryOverviewAssemblyIdentity),
            typeof(LibraryOverviewDocument),
        ];
        foreach (Type type in contract)
        {
            foreach (
                PropertyInfo property
                in type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public))
            {
                Assert.DoesNotContain(
                    forbidden,
                    candidate =>
                        candidate.IsAssignableFrom(
                            property.PropertyType));
            }
        }
    }
}
