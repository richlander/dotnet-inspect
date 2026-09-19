using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Inspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed partial class ApiSurfaceExtractorBoundsTests
{
    [Fact]
    public void LocalExtensionAttachment_DoesNotAllocateQuadratically()
    {
        byte[] image = BuildLocalExtensionFloodImage(methodCount: 4_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 32_000_000)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(
            8_000,
            extracted.Surface.Types.Sum(type => type.Members.Count));
        var attached = Assert.Single(
            extracted.Surface.Types,
            type => type.FullName == "Samples.Target").Members;
        Assert.Equal(
            Enumerable.Range(1, 4_000),
            attached.Select(member => member.DeclaringOverloadIndex!.Value));
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ExtensionScan_DoesNotHashNonCoreLibraryPublicKeyPerMethod()
    {
        byte[] image = BuildLocalExtensionFloodImage(
            methodCount: 64,
            assemblyPublicKeyLength: 1024 * 1024);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurface surface = ApiSurfaceExtractor.ExtractSummary(peReader);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(
            128,
            surface.Types.Sum(type => type.Members.Count));
        Assert.True(
            allocated < 24L * 1024 * 1024,
            $"extension scan allocated {allocated:N0} bytes");
    }

    [Fact]
    public void FinalizerScan_ChargesCoreLibraryPublicKeyBeforeCopying()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedFinalizerImage(
                typeCount: 64,
                publicKeyLength: 2_100_000));
    }
}
