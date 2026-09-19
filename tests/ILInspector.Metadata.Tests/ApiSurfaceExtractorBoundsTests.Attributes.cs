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
    public void RepeatedEnumAttributeLookups_DoNotAllocateQuadratically()
    {
        byte[] image = BuildRepeatedEnumAttributeLookupImage(
            typeCount: 2_000,
            namedArgumentCount: 2_000,
            attributeCount: 1);
        AssertEnumAttributeLookupsDoNotAllocateQuadratically(image);
    }

    [Fact]
    public void SeparateEnumAttributes_ReuseTheChargedTypeNameIndex()
    {
        byte[] image = BuildRepeatedEnumAttributeLookupImage(
            typeCount: 2_000,
            namedArgumentCount: 1,
            attributeCount: 2_000,
            poisonTypeDefinitionIndex: false);
        AssertEnumAttributeLookupsDoNotAllocateQuadratically(image);
    }

    [Fact]
    public void FailedEnumAttributeIndexBuild_IsCachedAndVisible()
        => AssertFailedEnumAttributeIndexIsCachedAndVisible(bounded: true);

    [Fact]
    public void FailedEnumAttributeIndexBuild_IsCachedOnTheUnboundedPath()
        => AssertFailedEnumAttributeIndexIsCachedAndVisible(bounded: false);

    [Fact]
    public void ParameterEnumAttributes_ReuseTheChargedTypeNameIndex()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(bounded: true);

    [Fact]
    public void ParameterEnumAttributes_ReuseTheChargedTypeNameIndexOnTheUnboundedPath()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(bounded: false);

    [Fact]
    public void DecimalConstantParameterAttributes_ReuseTheChargedTypeNameIndex()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(
            bounded: true,
            attributeNamespace: "System.Runtime.CompilerServices",
            attributeName: "DecimalConstantAttribute");

    [Fact]
    public void DecimalConstantParameterAttributes_ReuseTheChargedTypeNameIndexOnTheUnboundedPath()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(
            bounded: false,
            attributeNamespace: "System.Runtime.CompilerServices",
            attributeName: "DecimalConstantAttribute");

    static void AssertFailedEnumAttributeIndexIsCachedAndVisible(bool bounded)
    {
        byte[] image = BuildRepeatedEnumAttributeLookupImage(
            typeCount: 2_000,
            namedArgumentCount: 1,
            attributeCount: 2_000,
            poisonTypeDefinitionIndex: true);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurface surface;
        if (bounded)
        {
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
                        maxRetainedTextCharacters: 8_000_000)));
            surface = extracted.Surface;
        }
        else
        {
            surface = ApiSurfaceExtractor.Extract(peReader);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ApiSurfaceInspectionFailure failure = Assert.Single(surface.InspectionFailures);
        Assert.Equal("enum attribute type index", failure.Operation);
        Assert.Equal(MetadataTypeNameFailureMechanism.Metadata, failure.Mechanism);
        ApiType attributed = Assert.Single(surface.Types, type => type.Name == "Attributed");
        Assert.Empty(attributed.Attributes);
        var inspection = MetadataFindings.InspectApiAttributes(
            surface,
            new FindingSubject("Attributed", "Attributed"),
            attributed.FullName);
        var failed = Assert.IsType<FindingInspection<ApiAttributeHandle>.Failed>(
            inspection.Value);
        Assert.Contains("enum attribute type index", failed.Error.Reason);
        var missingType = Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
            MetadataFindings.InspectApiType(
                surface,
                new FindingSubject("Missing", "Missing"),
                "Missing").Value);
        Assert.Empty(missingType.Findings);
        var missingMembers =
            Assert.IsType<FindingInspection<ApiMemberHandle>.Absent>(
                MetadataFindings.InspectApiMembers(
                    surface,
                    new FindingSubject("Missing", "Missing"),
                    "Missing").Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            missingMembers.Kind);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"{(bounded ? "bounded" : "unbounded")} extraction allocated {allocated:N0} bytes");
    }

    static void AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(
        bool bounded,
        string attributeNamespace = "Samples",
        string attributeName = "SampleAttribute")
    {
        byte[] image = BuildRepeatedParameterEnumAttributeLookupImage(
            typeCount: 2_000,
            methodCount: 2_000,
            attributeNamespace,
            attributeName);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurface surface;
        if (bounded)
        {
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
                        maxRetainedTextCharacters: 8_000_000)));
            surface = extracted.Surface;
        }
        else
        {
            surface = ApiSurfaceExtractor.Extract(peReader);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ApiType host = Assert.Single(surface.Types, type => type.Name == "Host");
        Assert.Equal(2_000, host.Members.Count);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"{(bounded ? "bounded" : "unbounded")} parameter-attribute extraction allocated {allocated:N0} bytes");
    }

    static void AssertEnumAttributeLookupsDoNotAllocateQuadratically(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void LargeVisibilityAttribute_StopsBeforeDecodingItsMessage()
    {
        byte[] image = BuildLargeObsoleteAttributeImage(messageLength: 40_000_000);
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
                    maxRetainedTextCharacters: 8_000_000)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void GenericAttributeConstructorHasBoundedUnboundedParity()
    {
        using var unboundedStream = File.OpenRead(SelfPath);
        using var unboundedReader = new PEReader(unboundedStream);
        ApiSurface unbounded = ApiSurfaceExtractor.Extract(
            unboundedReader,
            ApiSurfaceExtractionScope.Public);
        Assert.Contains(
            unbounded.Types,
            type => type.FullName.EndsWith(
                nameof(GenericAttributeBoundedFixture),
                StringComparison.Ordinal));
        using var boundedStream = File.OpenRead(SelfPath);
        using var boundedReader = new PEReader(boundedStream);
        var bounded = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                boundedReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(
            unbounded.Types.Select(type => type.FullName),
            bounded.Surface.Types.Select(type => type.FullName));
        Assert.Equal(
            unbounded.InspectionFailures.Count,
            bounded.Surface.InspectionFailures.Count);
    }

    [Fact]
    public void LegalNamedAttribute_HasBoundedUnboundedParity()
    {
        using var unboundedStream = File.OpenRead(SelfPath);
        using var unboundedReader = new PEReader(unboundedStream);
        ApiSurface unbounded = ApiSurfaceExtractor.Extract(
            unboundedReader,
            ApiSurfaceExtractionScope.Public);
        ApiType unboundedType = Assert.Single(
            unbounded.Types,
            type => type.FullName.EndsWith(
                nameof(LegalNamedAttributeFixture),
                StringComparison.Ordinal));
        using var boundedStream = File.OpenRead(SelfPath);
        using var boundedReader = new PEReader(boundedStream);
        var bounded = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                boundedReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        ApiType boundedType = Assert.Single(
            bounded.Surface.Types,
            type => type.FullName.EndsWith(
                nameof(LegalNamedAttributeFixture),
                StringComparison.Ordinal));

        Assert.Equal(unboundedType.Attributes, boundedType.Attributes);
        Assert.Contains(
            unboundedType.Attributes,
            attribute => attribute.Contains("LegalNamed", StringComparison.Ordinal)
                && attribute.Contains("Count", StringComparison.Ordinal));
    }
}
