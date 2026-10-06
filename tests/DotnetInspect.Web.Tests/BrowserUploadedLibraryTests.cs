using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;
using DotnetInspect.Web.Interop.Library;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserUploadedLibraryTests
{
    [Fact]
    public async Task OpenUploadedLibrary_ProjectsBrowsableDetachedSurface()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(BrowserUploadedLibraryTests).Assembly.Location,
            TestContext.Current.CancellationToken);

        BrowserUploadedLibraryInspection inspection = Deserialize(
            await LibraryExports.OpenUploadedLibrary(
                "DotnetInspect.Web.Tests.dll",
                content));

        Assert.Equal(
            BrowserUploadedLibraryInspectionOutcome.Available,
            inspection.Content.Outcome);
        Assert.Equal(
            "DotnetInspect.Web.Tests.dll",
            inspection.Content.DeclaredName);
        Assert.Equal(content.Length, inspection.Content.ByteLength);
        Assert.Matches("^[0-9a-f]{64}$", inspection.Content.Digest);
        BrowserEmbeddedLibraryProvenance provenance =
            Assert.IsType<BrowserEmbeddedLibraryProvenance>(
                inspection.Content.Provenance);
        Assert.Equal("browser-upload", provenance.ContentRef);
        Assert.Equal(
            $"sha256:{inspection.Content.Digest}",
            provenance.Digest);
        Assert.Equal(inspection.Content.DeclaredName, provenance.DeclaredName);
        Assert.Equal(
            BrowserLibraryInspectionShareKind.NonProjectable,
            inspection.Share.Kind);
        Assert.Equal("embedded-library/share", inspection.Share.Path);
        Assert.Null(inspection.Share.FullUrl);
        Assert.Null(inspection.Share.Packet);

        BrowserUploadedLibrarySurface surface =
            Assert.IsType<BrowserUploadedLibrarySurface>(
                inspection.Content.Surface);
        BrowserLibraryAssemblySurface assembly =
            Assert.Single(surface.Assemblies);
        Assert.Equal(inspection.Content.DeclaredName, assembly.Asset);
        Assert.Equal(
            $"sha256:{inspection.Content.Digest}",
            assembly.Id);
        Assert.NotEmpty(surface.Types);
        Assert.Contains(surface.Types, type => type.Api.Length > 0);
        Assert.DoesNotContain(
            typeof(BrowserUploadedLibraryResult).GetProperties(),
            property => property.Name.Contains(
                "Package",
                StringComparison.Ordinal)
                || property.Name.Contains(
                    "Path",
                    StringComparison.Ordinal)
                || property.Name.Contains(
                    "Platform",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenUploadedLibrary_ReturnsVisibleTypedRejection()
    {
        BrowserUploadedLibraryInspection inspection = Deserialize(
            await LibraryExports.OpenUploadedLibrary(
                "broken.dll",
                [0x4d, 0x5a, 0x00, 0x01]));

        Assert.Equal(
            BrowserUploadedLibraryInspectionOutcome.Rejected,
            inspection.Content.Outcome);
        Assert.False(inspection.Content.IsComplete);
        Assert.Null(inspection.Content.Surface);
        Assert.NotNull(inspection.Content.Failure);
        Assert.NotEmpty(inspection.Content.Failure.Detail);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic => diagnostic.Severity == "Error"
                && diagnostic.Summary.Length > 0);
    }

    [Fact]
    public async Task OpenUploadedLibrary_RejectsBrowserTransportTruncation()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(BrowserUploadedLibraryTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        InspectionEnvelope<EmbeddedLibraryInspectionResult> inspection =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "DotnetInspect.Web.Tests.dll",
                [.. content],
                BrowserApiSurfacePolicy.Limits,
                cancellationToken: TestContext.Current.CancellationToken);
        var oversizedSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = new string('N', 4_000_000),
                    Name = "Amplifier",
                    MetadataName = "Amplifier",
                    Kind = "class",
                },
            ],
        };
        var oversizedInspection =
            new InspectionEnvelope<EmbeddedLibraryInspectionResult>(
                inspection.Content with { Surface = oversizedSurface },
                inspection.Share,
                inspection.Diagnostics);

        BrowserUploadedLibraryInspection projected =
            BrowserLibraryWireProjection.Project(oversizedInspection);

        Assert.Equal(
            BrowserUploadedLibraryInspectionOutcome.Rejected,
            projected.Content.Outcome);
        Assert.False(projected.Content.IsComplete);
        Assert.Null(projected.Content.Surface);
        BrowserUploadedLibraryFailure failure =
            Assert.IsType<BrowserUploadedLibraryFailure>(
                projected.Content.Failure);
        Assert.Equal(
            BrowserUploadedLibraryFailureKind.ProjectionTruncated,
            failure.Kind);
        Assert.Contains("transport truncated", failure.Detail);
        Assert.Contains(
            projected.Diagnostics,
            diagnostic => diagnostic.Code
                == "embedded-library.browser-projection-truncated"
                && diagnostic.Severity == "Error");
    }

    [Fact]
    public async Task OpenUploadedLibrary_RejectsWorkerCollectionEntryOverflow()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(BrowserUploadedLibraryTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        InspectionEnvelope<EmbeddedLibraryInspectionResult> inspection =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "DotnetInspect.Web.Tests.dll",
                [.. content],
                BrowserApiSurfacePolicy.Limits,
                cancellationToken: TestContext.Current.CancellationToken);
        var oversizedSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "Transport",
                    Name = "Amplifier",
                    MetadataName = "Amplifier",
                    Kind = "class",
                    Members =
                    [
                        .. Enumerable.Range(0, 20_000).Select(index =>
                            new ApiMember
                            {
                                Name = $"M{index}",
                                Kind = "method",
                                Signature = $"void M{index}()",
                                SignatureModel = new ApiSignature
                                {
                                    ReturnType = "void",
                                    MemberName = $"M{index}",
                                },
                            }),
                    ],
                },
            ],
        };
        var oversizedInspection =
            new InspectionEnvelope<EmbeddedLibraryInspectionResult>(
                inspection.Content with { Surface = oversizedSurface },
                inspection.Share,
                inspection.Diagnostics);

        BrowserUploadedLibraryInspection projected =
            BrowserLibraryWireProjection.Project(oversizedInspection);

        Assert.Equal(
            BrowserUploadedLibraryInspectionOutcome.Rejected,
            projected.Content.Outcome);
        Assert.False(projected.Content.IsComplete);
        Assert.Null(projected.Content.Surface);
        Assert.Empty(projected.Content.InspectionFailures);
        BrowserUploadedLibraryFailure failure =
            Assert.IsType<BrowserUploadedLibraryFailure>(
                projected.Content.Failure);
        Assert.Equal(
            BrowserUploadedLibraryFailureKind.ProjectionTruncated,
            failure.Kind);
        Assert.Contains("collection-entry limit", failure.Detail);

        using JsonDocument document = JsonSerializer.SerializeToDocument(
            projected,
            BrowserLibraryJsonContext.Default.BrowserUploadedLibraryInspection);
        Assert.True(
            BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead
                + BrowserOrdinaryWorkerJsonBudget.CollectionEntries(
                    document.RootElement)
                <= BrowserOrdinaryWorkerJsonBudget
                    .MaxOrdinaryWorkerCollectionEntries);
        Assert.True(
            BrowserOrdinaryWorkerJsonBudget.JsonStringifyCharacters(
                document.RootElement)
                + BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead
                <= BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerJsonCharacters);
    }

    static BrowserUploadedLibraryInspection Deserialize(string json) =>
        JsonSerializer.Deserialize(
            json,
            BrowserLibraryJsonContext.Default.BrowserUploadedLibraryInspection)
        ?? throw new InvalidOperationException(
            "The uploaded Library facade returned null JSON.");
}
