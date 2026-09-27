using System.Text.Json;

using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

/// <summary>
/// Gates for the Enablements fact group
/// (docs/design/library-inspection-document.md#library-facts).
/// </summary>
public sealed class LibraryInspectionEnablementsTests
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
    public async Task PairedPlatformLibrary_JudgesEnablementsOnTheImplementation()
    {
        byte[] reference = await LibraryInspectionTestLibrary.PinnedNet11Async("ref", "System.Net.Sockets.dll");
        byte[] implementation = await LibraryInspectionTestLibrary.PinnedNet11Async("runtime", "System.Net.Sockets.dll");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                reference,
                LibraryInspectionTestLibrary.Identity(reference),
                implementation);

        LibraryEnablementsOutcome.Available available = Enablements(Execute(library, enablements: true));

        Assert.Equal(LibraryEnablementsRole.ImplementationAssembly, available.Role);
        AssertCase<LibraryEnablement.Enabled>(available, LibraryEnablementId.AotCompatible);
        AssertCase<LibraryEnablement.Enabled>(available, LibraryEnablementId.RuntimeAsync);
        AssertCase<LibraryEnablement.NotEnabled>(available, LibraryEnablementId.MemorySafetyV2);
    }

    [Fact]
    public async Task ReferenceOnlyLibrary_ReportsEveryEnablementUnavailable()
    {
        byte[] reference = await LibraryInspectionTestLibrary.PinnedNet11Async("ref", "System.Net.Sockets.dll");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                reference,
                LibraryInspectionTestLibrary.Identity(reference),
                implementation: null);

        LibraryEnablementsOutcome.Available available = Enablements(Execute(library, enablements: true));

        Assert.Equal(LibraryEnablementsRole.ApiAssembly, available.Role);
        Assert.All(
            available.Facts.Items,
            item =>
            {
                Assert.Equal(
                    LibraryEnablementUnavailableReason.ReferenceAssembly,
                    Assert.IsType<LibraryEnablement.Unavailable>(item).Reason);
            });
        Assert.Empty(available.Facts.Enabled());
    }

    [Fact]
    public async Task UnrequestedEnablements_AreAbsentFromTheDocumentAndJson()
    {
        byte[] implementation = await LibraryInspectionTestLibrary.PinnedNet11Async("runtime", "System.Net.Sockets.dll");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));

        InspectionEnvelope<LibraryInspectionOutcome> envelope = Execute(library, enablements: false);

        Assert.Null(Document(envelope).Enablements);
        Assert.DoesNotContain(
            "\"enablements\"",
            JsonSerializer.Serialize(envelope, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enablements_SerializeStableIdentifiersAndRoundTrip()
    {
        byte[] implementation = await LibraryInspectionTestLibrary.PinnedNet11Async("runtime", "System.Net.Sockets.dll");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                implementation,
                LibraryInspectionTestLibrary.Identity(implementation));

        InspectionEnvelope<LibraryInspectionOutcome> envelope = Execute(library, enablements: true);
        string json = JsonSerializer.Serialize(envelope, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope);

        Assert.Contains("\"kind\": \"available\"", json, StringComparison.Ordinal);
        // One assembly serves both roles here, so the implementation is judged.
        Assert.Contains("\"role\": \"implementation-assembly\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"aot-compatible\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"runtime-async\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"memory-safety-v2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"not-enabled\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"state\"", json, StringComparison.Ordinal);
        InspectionEnvelope<LibraryInspectionOutcome>? roundTripped =
            JsonSerializer.Deserialize(json, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope);
        Assert.Equal(Document(envelope).Enablements, Document(roundTripped!).Enablements);

        string planJson = JsonSerializer.Serialize(
            Plan(enablements: true),
            LibraryInspectionJsonContext.Default.LibraryInspectionPlan);
        Assert.NotNull(
            JsonSerializer.Deserialize(planJson, LibraryInspectionJsonContext.Default.LibraryInspectionPlan)!
                .Enablements);
    }

    [Fact]
    public async Task UnavailableEnablements_RoundTripWithIdBeforeReason()
    {
        byte[] reference = await LibraryInspectionTestLibrary.PinnedNet11Async("ref", "System.Net.Sockets.dll");
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                reference,
                LibraryInspectionTestLibrary.Identity(reference),
                implementation: null);

        InspectionEnvelope<LibraryInspectionOutcome> envelope = Execute(library, enablements: true);
        string json = JsonSerializer.Serialize(envelope, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope);

        Assert.Matches(
            "\"id\": \"aot-compatible\",\\s*\"reason\": \"reference-assembly\"",
            json);
        InspectionEnvelope<LibraryInspectionOutcome>? roundTripped =
            JsonSerializer.Deserialize(json, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope);
        Assert.Equal(Document(envelope).Enablements, Document(roundTripped!).Enablements);
        Assert.All(
            Assert.IsType<LibraryEnablementsOutcome.Available>(Document(roundTripped!).Enablements).Facts.Items,
            item => Assert.Equal(
                LibraryEnablementUnavailableReason.ReferenceAssembly,
                Assert.IsType<LibraryEnablement.Unavailable>(item).Reason));
    }

    private static LibraryInspectionPlan Plan(bool enablements) =>
        new(
            new(LibraryTypeAccessibility.Public, new()),
            s_bounds,
            enablements ? new LibraryEnablementsRequest() : null);

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library,
        bool enablements) =>
        LibraryInspectionOperation.Execute(
            new(library.Reference, Plan(enablements)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static LibraryDocument Document(InspectionEnvelope<LibraryInspectionOutcome> envelope) =>
        Assert.IsType<LibraryInspectionOutcome.Available>(envelope.Content).Document;

    private static LibraryEnablementsOutcome.Available Enablements(InspectionEnvelope<LibraryInspectionOutcome> envelope) =>
        Assert.IsType<LibraryEnablementsOutcome.Available>(Document(envelope).Enablements);

    private static void AssertCase<TCase>(
        LibraryEnablementsOutcome.Available available,
        LibraryEnablementId id)
        where TCase : LibraryEnablement =>
        Assert.IsType<TCase>(Assert.Single(available.Facts.Items, item => item.Id == id));
}
