using System.Reflection;
using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryInspectionOperationTests
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
        RealSystemTextJson_ReturnsDetachedPublicTypeCountAndSettlesLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        LibraryDocument document = Document(envelope);
        Assert.Equal("System.Text.Json", document.Assembly.Name.ToString());
        Assert.Equal(new Version(10, 0, 0, 0), document.Assembly.Version);
        Assert.NotEqual(Guid.Empty, document.ModuleVersionId);
        Assert.Equal(content.Length, document.Work.AssemblyBytes);
        Assert.Equal(
            document.ModuleVersionId,
            document.Types.Binding.ModuleVersionId);
        Assert.Equal(
            LibraryTypeAccessibility.Public,
            document.Types.Binding.Accessibility);
        LibraryTypePopulationCountOutcome.Counted count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                    document.Types.Count);
        Assert.True(count.Total > 0);
        Assert.Equal(
            count.Total,
            count.Classes
                + count.Structs
                + count.Interfaces
                + count.Enums
                + count.Delegates);
        Assert.Empty(envelope.Diagnostics);
        var share = Assert.IsType<InspectionShare.NonProjectable>(
            envelope.Share);
        Assert.Equal("library-inspection/share", share.Path);
        Assert.Equal(
            "A complete portable Workspace scenario was not supplied.",
            share.Reason.ToString());
        await library.RetireAsync();
        AssertDetachedContract();
    }

    [Fact]
    public async Task
        TypeForwarderFallbackBoundRetainsDocumentAndIncompleteCount()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 1,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library, bounds);

        Assert.Equal(
            ApiSurfaceExtractionBound.Types,
            Assert.IsType<
                    LibraryTypePopulationCountOutcome.Incomplete>(
                    Document(envelope).Types.Count)
                .Bound);
        Assert.Equal(
            "library-inspection.types.count.incomplete.extraction-bound",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        EquivalentLibrariesProduceEqualDocumentsAndRejectForeignLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryInspectionTestLibrary.Identity(content);
        var equivalentIdentity = new ManagedMetadataIdentity.Assembly(
            identity.Identity with
            {
                Name = identity.Identity.Name.ToUpperInvariant(),
                Culture = "neutral",
            });
        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                identity);
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                equivalentIdentity);

        Assert.Equal(
            Document(Execute(first)),
            Document(Execute(second)));

        InspectionEnvelope<LibraryInspectionOutcome> rejected =
            LibraryInspectionOperation.Execute(
                Request(first.Reference),
                second.IssueOperation(),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            LibraryInspectionRejection.LeaseReferenceMismatch,
            Assert.IsType<LibraryInspectionOutcome.Rejected>(
                    rejected.Content)
                .Reason);
        Assert.Equal(
            "library-inspection.rejected.lease-reference-mismatch",
            Assert.Single(rejected.Diagnostics).Code);

        await first.RetireAsync();
        await second.RetireAsync();
    }

    [Fact]
    public async Task
        MalformedTypePopulationRetainsLibraryDocumentAndTypedCountFailure()
    {
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                malformedPublicType: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        LibraryDocument document = Document(envelope);
        Assert.Equal("Probe", document.Assembly.Name.ToString());
        Assert.NotEqual(Guid.Empty, document.ModuleVersionId);
        Assert.Equal(
            LibraryTypePopulationCountUnavailableReason.MalformedTypeRow,
            Assert.IsType<
                    LibraryTypePopulationCountOutcome.Unavailable>(
                    document.Types.Count)
                .Reason);
        Assert.Equal(
            "library-inspection.types.count.unavailable.malformed-type-row",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task AssemblyIdentityMismatchReturnsTypedRejection()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                wrongIdentity);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        Assert.Equal(
            LibraryInspectionRejection.AssemblyIdentityMismatch,
            Assert.IsType<LibraryInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            "library-inspection.rejected.assembly-identity-mismatch",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Theory]
    [MemberData(nameof(FailedImages))]
    public async Task UnsupportedContentReturnsTypedFailure(
        byte[] content,
        LibraryInspectionFailure expected,
        string diagnosticCode)
    {
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        Assert.Equal(
            expected,
            Assert.IsType<LibraryInspectionOutcome.Failed>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            diagnosticCode,
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    public static TheoryData<
        byte[],
        LibraryInspectionFailure,
        string> FailedImages()
    {
        byte[] windowsMetadata =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                metadataVersion:
                    "WindowsRuntime 1.4;CLR v4.0.30319");
        return new TheoryData<
            byte[],
            LibraryInspectionFailure,
            string>
        {
            {
                [1, 2, 3],
                LibraryInspectionFailure.MalformedMetadata,
                "library-inspection.failed.malformed-metadata"
            },
            {
                LibraryInspectionTestLibrary.BuildMetadataImage(
                    includeAssembly: false),
                LibraryInspectionFailure.ManagedModule,
                "library-inspection.failed.managed-module"
            },
            {
                windowsMetadata,
                LibraryInspectionFailure.UnsupportedWindowsMetadata,
                "library-inspection.failed.unsupported-windows-metadata"
            },
            {
                LibraryInspectionTestLibrary.BuildMetadataImage(
                    emptyModuleVersionId: true),
                LibraryInspectionFailure.EmptyModuleVersionId,
                "library-inspection.failed.empty-module-version-id"
            },
        };
    }

    [Fact]
    public async Task CancellationAndRequestFailureSettleTransferredLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary cancelled =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibraryInspectionOperation.Execute(
                Request(cancelled.Reference),
                cancelled.IssueOperation(),
                cancellation.Token));
        await cancelled.RetireAsync();

        await using LibraryInspectionTestLibrary invalid =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        Assert.Throws<ArgumentNullException>(
            () => LibraryInspectionOperation.Execute(
                null!,
                invalid.IssueOperation(),
                TestContext.Current.CancellationToken));
        await invalid.RetireAsync();
    }

    [Fact]
    public void PlanAndClosedOutcomeSerializeWithSourceGeneration()
    {
        Guid moduleVersionId = Guid.NewGuid();
        var plan = new LibraryInspectionPlan(
            new(
                LibraryTypeAccessibility.Public,
                new()),
            s_bounds);
        var document = new LibraryDocument(
            new(
                new InertString(TextPolicy.Field, "Example"),
                new Version(1, 2, 3, 4),
                null,
                null),
            moduleVersionId,
            new(
                new(moduleVersionId, LibraryTypeAccessibility.Public),
                new LibraryTypePopulationCountOutcome.Counted(
                    classes: 1,
                    structs: 2,
                    interfaces: 3,
                    enums: 4,
                    delegates: 5)),
            new(1234),
            s_bounds);
        InspectionEnvelope<LibraryInspectionOutcome>[] envelopes =
        [
            Envelope(new LibraryInspectionOutcome.Available(document)),
            Envelope(
                new LibraryInspectionOutcome.Available(
                    document with
                    {
                        Types = document.Types with
                        {
                            Count =
                                new LibraryTypePopulationCountOutcome
                                    .Unavailable(
                                        LibraryTypePopulationCountUnavailableReason
                                            .TypeForwardersRequireResolution),
                        },
                    })),
            Envelope(
                new LibraryInspectionOutcome.Rejected(
                    LibraryInspectionRejection.AssemblyIdentityMismatch)),
            Envelope(
                new LibraryInspectionOutcome.Failed(
                    LibraryInspectionFailure.MalformedMetadata)),
        ];

        string planJson = JsonSerializer.Serialize(
            plan,
            LibraryInspectionJsonContext.Default.LibraryInspectionPlan);
        Assert.Contains("\"types\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"count\"", planJson, StringComparison.Ordinal);
        foreach (
            InspectionEnvelope<LibraryInspectionOutcome> envelope
            in envelopes)
        {
            string json = JsonSerializer.Serialize(
                envelope,
                LibraryInspectionJsonContext.Default
                    .LibraryInspectionEnvelope);
            Assert.Contains("\"content\"", json, StringComparison.Ordinal);
            Assert.Contains("\"share\"", json, StringComparison.Ordinal);
        }
    }

    private static LibraryInspectionRequest Request(
        LibraryReference library) =>
        new(
            library,
            new(
                new(
                    LibraryTypeAccessibility.Public,
                    new()),
                s_bounds));

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library) =>
        Execute(library, s_bounds);

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library,
        ApiSurfaceExtractionBounds bounds) =>
        LibraryInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(
                        LibraryTypeAccessibility.Public,
                        new()),
                    bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static InspectionEnvelope<LibraryInspectionOutcome> Envelope(
        LibraryInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                "library-inspection/share",
                "A complete portable Workspace scenario was not supplied."));

    private static LibraryDocument Document(
        InspectionEnvelope<LibraryInspectionOutcome> envelope) =>
        Assert.IsType<LibraryInspectionOutcome.Available>(
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
            typeof(InspectionEnvelope<LibraryInspectionOutcome>),
            typeof(LibraryInspectionOutcome),
            typeof(LibraryInspectionOutcome.Available),
            typeof(LibraryInspectionOutcome.Rejected),
            typeof(LibraryInspectionOutcome.Failed),
            typeof(LibraryAssemblyIdentity),
            typeof(LibraryDocument),
            typeof(LibraryTypePopulationResult),
            typeof(LibraryTypePopulationBinding),
            typeof(LibraryTypePopulationCountOutcome),
            typeof(LibraryTypePopulationCountOutcome.Counted),
            typeof(LibraryTypePopulationCountOutcome.Unavailable),
            typeof(LibraryTypePopulationCountOutcome.Incomplete),
            typeof(LibraryInspectionWork),
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
