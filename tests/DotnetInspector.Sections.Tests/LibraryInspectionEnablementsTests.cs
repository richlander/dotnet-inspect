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

        LibraryEnablementsOutcome.Judged judged = Judged(Execute(library, enablements: true));

        Assert.Equal(LibraryEnablementsContent.ImplementationAssembly, judged.Content);
        AssertState(judged, LibraryEnablementKind.AotCompatible, LibraryEnablementState.Enabled);
        AssertState(judged, LibraryEnablementKind.RuntimeAsync, LibraryEnablementState.Enabled);
        AssertState(judged, LibraryEnablementKind.MemorySafetyV2, LibraryEnablementState.NotEnabled);
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

        LibraryEnablementsOutcome.Judged judged = Judged(Execute(library, enablements: true));

        Assert.Equal(LibraryEnablementsContent.ApiAssembly, judged.Content);
        Assert.All(
            judged.Enablements.Items,
            item =>
            {
                Assert.Equal(LibraryEnablementState.Unavailable, item.State);
                Assert.Equal(LibraryEnablementUnavailableReason.ReferenceAssembly, item.Reason);
            });
        Assert.Empty(judged.Enablements.Enabled());
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

        Assert.Contains("\"kind\": \"aot-compatible\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"runtime-async\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"memory-safety-v2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"not-enabled\"", json, StringComparison.Ordinal);
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

    private static LibraryEnablementsOutcome.Judged Judged(InspectionEnvelope<LibraryInspectionOutcome> envelope) =>
        Assert.IsType<LibraryEnablementsOutcome.Judged>(Document(envelope).Enablements);

    private static void AssertState(
        LibraryEnablementsOutcome.Judged judged,
        LibraryEnablementKind kind,
        LibraryEnablementState state) =>
        Assert.Equal(state, Assert.Single(judged.Enablements.Items, item => item.Kind == kind).State);
}
