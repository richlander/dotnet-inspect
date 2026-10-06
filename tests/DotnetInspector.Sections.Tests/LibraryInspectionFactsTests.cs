using System.Text.Json;

using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

/// <summary>
/// Gates for the Image and Description fact groups and facts-only plans
/// (docs/design/library-inspection-document.md#library-facts).
/// </summary>
public sealed class LibraryInspectionFactsTests
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
    public async Task FactsOnlyPlan_ReturnsRequestedGroupsWithoutPopulationWork()
    {
        await using LibraryInspectionTestLibrary library = await SocketsAsync();

        InspectionEnvelope<LibraryInspectionOutcome> envelope = Execute(
            library,
            new(types: null, s_bounds, image: new(), description: new()));
        LibraryDocument document = Document(envelope);

        Assert.Null(document.Types);
        Assert.Equal(0, document.Work.MetadataRows);
        Assert.Equal(0, document.Work.RetainedDeclarations);
        Assert.Equal("System.Net.Sockets", document.Assembly.Name.ToString());
        Assert.NotEqual(Guid.Empty, document.ModuleVersionId);

        LibraryImageFacts image = Assert.IsType<LibraryImageFacts>(document.Image);
        Assert.Equal(document.Work.AssemblyBytes, image.ImageBytes);
        Assert.Equal(".NETCoreApp,Version=v11.0", Text(image.TargetFramework));
        Assert.Equal(LibraryCompilationForm.ReadyToRun, image.Compilation);
        Assert.True(image.StrongNameSigned);
        Assert.Equal(LibraryReproducibility.Reproducible, image.Reproducibility);

        LibraryDescriptionFacts description = Assert.IsType<LibraryDescriptionFacts>(document.Description);
        Assert.Equal("Microsoft Corporation", Text(description.Company));
        Assert.Equal("Microsoft® .NET", Text(description.Product));
        Assert.StartsWith("11.0.0-rc.1.26425.128", Text(description.InformationalVersion), StringComparison.Ordinal);

        string json = JsonSerializer.Serialize(envelope, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope);
        Assert.DoesNotContain("\"types\"", json, StringComparison.Ordinal);
        Assert.Contains("\"compilation\": \"ready-to-run\"", json, StringComparison.Ordinal);
        InspectionEnvelope<LibraryInspectionOutcome>? roundTripped =
            JsonSerializer.Deserialize(json, LibraryInspectionJsonContext.Default.LibraryInspectionEnvelope);
        Assert.Equal(document.Image, Document(roundTripped!).Image);
        Assert.Equal(document.Description, Document(roundTripped!).Description);
    }

    [Fact]
    public async Task FactsOnlyPlan_DoesNotRunTheDeclarationInventory()
    {
        // The declaration inventory fails this image, so a facts-only plan can
        // only succeed if it never runs the inventory.
        byte[] image = LibraryInspectionTestLibrary.BuildMetadataImage(malformedPublicType: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));

        Assert.IsType<LibraryInspectionOutcome.Failed>(Execute(
            library,
            new(new(LibraryTypeAccessibility.Public, new()), s_bounds)).Content);
        LibraryDocument document = Document(Execute(
            library,
            new(types: null, s_bounds, image: new())));

        Assert.Null(document.Types);
        Assert.NotNull(document.Image);
    }

    [Fact]
    public async Task UnrequestedGroups_AreAbsent()
    {
        await using LibraryInspectionTestLibrary library = await SocketsAsync();

        LibraryDocument withTypes = Document(Execute(
            library,
            new(new(LibraryTypeAccessibility.Public, new()), s_bounds, image: new())));

        Assert.NotNull(withTypes.Types);
        Assert.NotNull(withTypes.Image);
        Assert.Null(withTypes.Description);
        Assert.Null(withTypes.Enablements);
    }

    [Fact]
    public async Task EqualBytesInEqualRoles_ProduceEqualFacts()
    {
        await using LibraryInspectionTestLibrary first = await SocketsAsync();
        await using LibraryInspectionTestLibrary second = await SocketsAsync();
        var plan = new LibraryInspectionPlan(types: null, s_bounds, image: new(), description: new());

        LibraryDocument left = Document(Execute(first, plan));
        LibraryDocument right = Document(Execute(second, plan));

        Assert.Equal(left.Image, right.Image);
        Assert.Equal(left.Description, right.Description);
    }

    [Fact]
    public async Task UndecodableDescriptiveAttribute_IsUnavailableWhileOtherGroupsSucceed()
    {
        byte[] image = LibraryInspectionTestLibrary.BuildMetadataImage(undecodableCompany: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));

        LibraryDocument document = Document(Execute(
            library,
            new(
                types: null,
                s_bounds,
                enablements: new(),
                image: new(),
                description: new())));

        LibraryDescriptionFacts description = Assert.IsType<LibraryDescriptionFacts>(document.Description);
        Assert.Equal(
            LibraryTextFactUnavailableReason.UndecodableMetadata,
            Assert.IsType<LibraryTextFact.Unavailable>(description.Company).Reason);
        Assert.Null(description.Product);
        Assert.Equal(LibraryCompilationForm.IL, Assert.IsType<LibraryImageFacts>(document.Image).Compilation);
        Assert.IsType<LibraryEnablementsOutcome.Available>(document.Enablements);
    }

    private static async Task<LibraryInspectionTestLibrary> SocketsAsync()
    {
        byte[] implementation = await LibraryInspectionTestLibrary.PinnedNet11Async("runtime", "System.Net.Sockets.dll");
        return await LibraryInspectionTestLibrary.CreateAsync(
            implementation,
            LibraryInspectionTestLibrary.Identity(implementation));
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library,
        LibraryInspectionPlan plan) =>
        LibraryInspectionOperation.Execute(
            new(library.Reference, plan),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static LibraryDocument Document(InspectionEnvelope<LibraryInspectionOutcome> envelope) =>
        Assert.IsType<LibraryInspectionOutcome.Available>(envelope.Content).Document;

    private static string Text(LibraryTextFact? fact) =>
        Assert.IsType<LibraryTextFact.Present>(fact).Value.ToString();
}
