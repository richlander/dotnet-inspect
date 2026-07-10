using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataInventoryFindingsTests
{
    static readonly FindingSubject Subject = new("assembly", "Test assembly");

    [Fact]
    public void EmptyInventory_IsACompleteExactCensus()
    {
        var inspection = MetadataFindings.InspectResources([], Subject);
        var comparison = MetadataFindings.CompareResources([], [], Subject);

        Assert.Empty(Findings(inspection));
        Assert.True(comparison.IsExact);
        Assert.Empty(Pairs(comparison));
    }

    [Fact]
    public void IdentitySetInventory_DoesNotFabricateOrdinals()
    {
        var findings = Findings(MetadataFindings.InspectResources(
            [new ManifestResourceInfo("data.json", IsPublic: true, IsEmbedded: true, Size: 10)],
            Subject));

        Assert.Null(Assert.Single(findings).Ordinal);
    }

    [Fact]
    public void AssemblyReferences_IgnoreOrderAndPromoteVersionChanges()
    {
        AssemblyReference systemRuntimeV1 = new(
            "System.Runtime",
            "9.0.0.0",
            Culture: null,
            PublicKeyToken: "b03f5f7f11d50a3a");
        AssemblyReference systemRuntimeV2 = systemRuntimeV1 with { Version = "10.0.0.0" };
        AssemblyReference collections = new(
            "System.Collections",
            "9.0.0.0",
            Culture: null,
            PublicKeyToken: "b03f5f7f11d50a3a");

        var comparison = MetadataFindings.CompareAssemblyReferences(
            [systemRuntimeV1, collections],
            [collections, systemRuntimeV2],
            Subject);

        Assert.Collection(
            Pairs(comparison).OrderBy(static pair => pair.Subject.Display).ThenBy(static pair => pair.Kind),
            pair => Assert.Equal(PairKind.Present, pair.Kind),
            pair =>
            {
                Assert.Equal(PairKind.Changed, pair.Kind);
                Assert.Null(pair.Detail);
            });
    }

    [Fact]
    public void AssemblyReferenceNames_AreCaseInsensitiveIdentities()
    {
        var comparison = MetadataFindings.CompareAssemblyReferences(
            [new AssemblyReference("System.Runtime", "9.0.0.0", null, null)],
            [new AssemblyReference("system.runtime", "9.0.0.0", null, null)],
            Subject);

        var pair = Assert.Single(Pairs(comparison));

        Assert.Equal(PairKind.Changed, pair.Kind);
    }

    [Fact]
    public void TypeForwarders_ReportAddedRemovedAndChangedTargets()
    {
        var comparison = MetadataFindings.CompareTypeForwarders(
            [
                new TypeForwarderInfo("Sample.Removed", "Old"),
                new TypeForwarderInfo("Sample.Moved", "Old"),
            ],
            [
                new TypeForwarderInfo("Sample.Moved", "New"),
                new TypeForwarderInfo("Sample.Added", "New"),
            ],
            Subject);

        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Removed));
        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Changed));
    }

    [Fact]
    public void Resources_PromoteVisibilityEmbeddingAndSizeChanges()
    {
        var comparison = MetadataFindings.CompareResources(
            [new ManifestResourceInfo("data.json", IsPublic: true, IsEmbedded: true, Size: 10)],
            [new ManifestResourceInfo("data.json", IsPublic: false, IsEmbedded: false, Size: 20)],
            Subject);

        var pair = Assert.Single(Pairs(comparison));

        Assert.Equal(PairKind.Changed, pair.Kind);
        Assert.Null(pair.Detail);
    }

    [Fact]
    public void DuplicateAttributes_UseValueAsExactIdentityWithoutShiftingExistingPairs()
    {
        AssemblyAttributeInfo a = new("Tag", "Assembly", "A");
        AssemblyAttributeInfo b = new("Tag", "Assembly", "B");
        AssemblyAttributeInfo c = new("Tag", "Assembly", "C");

        var reordered = MetadataFindings.CompareAssemblyAttributes(
            [b, c],
            [c, b],
            Subject);
        var inserted = MetadataFindings.CompareAssemblyAttributes(
            [b, c],
            [a, b, c],
            Subject);
        var replaced = MetadataFindings.CompareAssemblyAttributes(
            [a, b],
            [a, c],
            Subject);

        Assert.All(Pairs(reordered), static pair => Assert.Equal(PairKind.Present, pair.Kind));
        Assert.Equal(2, Pairs(inserted).Count(static pair => pair.Kind == PairKind.Present));
        Assert.Equal(1, Pairs(inserted).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(replaced).Count(static pair => pair.Kind == PairKind.Present));
        Assert.Equal(1, Pairs(replaced).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(replaced).Count(static pair => pair.Kind == PairKind.Removed));
        Assert.DoesNotContain(Pairs(inserted), static pair => pair.Kind == PairKind.Changed);
        Assert.DoesNotContain(Pairs(replaced), static pair => pair.Kind == PairKind.Changed);
    }

    [Fact]
    public void AttributeIdentity_DistinguishesNullAndEmptyValues()
    {
        var comparison = MetadataFindings.CompareAssemblyAttributes(
            [new AssemblyAttributeInfo("Tag", "Assembly", null)],
            [new AssemblyAttributeInfo("Tag", "Assembly", "")],
            Subject);

        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, Pairs(comparison).Count(static pair => pair.Kind == PairKind.Removed));
        Assert.DoesNotContain(Pairs(comparison), static pair => pair.Kind == PairKind.Changed);
    }

    [Fact]
    public void CompareInventory_ValidatesPublicInputsBeforeEnumeration()
    {
        bool enumerated = false;
        IEnumerable<AssemblyReference> oldReferences = Enumerate();

        var exception = Assert.Throws<ArgumentNullException>(
            () => MetadataFindings.CompareAssemblyReferences(
                oldReferences,
                null!,
                Subject));

        Assert.Equal("newReferences", exception.ParamName);
        Assert.False(enumerated);

        IEnumerable<AssemblyReference> Enumerate()
        {
            enumerated = true;
            yield break;
        }
    }

    [Fact]
    public void InventoryNullElements_ReportThePublicCollectionParameter()
    {
        AssemblyReference valid = new("System.Runtime", "9.0.0.0", null, null);
        AssemblyReference[] containingNull = [null!];

        var inspectException = Assert.Throws<ArgumentException>(
            () => MetadataFindings.InspectAssemblyReferences(containingNull, Subject));
        var oldException = Assert.Throws<ArgumentException>(
            () => MetadataFindings.CompareAssemblyReferences(containingNull, [valid], Subject));
        var newException = Assert.Throws<ArgumentException>(
            () => MetadataFindings.CompareAssemblyReferences([valid], containingNull, Subject));

        Assert.Equal("references", inspectException.ParamName);
        Assert.Equal("oldReferences", oldException.ParamName);
        Assert.Equal("newReferences", newException.ParamName);
    }

    [Fact]
    public void RealScannerOutputs_ProjectWithoutChangingTheCensus()
    {
        using var stream = File.OpenRead(typeof(ApiSurfaceExtractor).Assembly.Location);
        using var peReader = new PEReader(stream);

        var references = AssemblyInspector
            .ExtractAssemblyInfo(peReader, includeReferences: true)
            .References!;
        var attributes = AssemblyDetailScanner.ScanCustomAttributes(peReader);
        var forwarders = AssemblyDetailScanner.ScanTypeForwarders(peReader);
        var resources = ResourceScanner.Scan(peReader);

        Assert.Equal(
            references.Count,
            Findings(MetadataFindings.InspectAssemblyReferences(references, Subject)).Length);
        Assert.Equal(
            attributes.Count,
            Findings(MetadataFindings.InspectAssemblyAttributes(attributes, Subject)).Length);
        Assert.Equal(
            forwarders.Count,
            Findings(MetadataFindings.InspectTypeForwarders(forwarders, Subject)).Length);
        Assert.Equal(
            resources.Count,
            Findings(MetadataFindings.InspectResources(resources, Subject)).Length);
        Assert.NotEmpty(references);
        Assert.NotEmpty(attributes);
    }

    static ImmutableArray<Finding<T>> Findings<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings
            : throw new Xunit.Sdk.XunitException("Expected a complete inventory inspection.");

    static ImmutableArray<PairFinding<T>> Pairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison is FindingComparison<T>.Complete complete
            ? complete.Pairs
            : throw new Xunit.Sdk.XunitException($"Expected a complete comparison: {comparison.Failure}");
}
