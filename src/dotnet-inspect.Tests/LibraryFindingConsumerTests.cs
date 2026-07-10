using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class LibraryFindingConsumerTests
{
    [Fact]
    public void UnionScanner_RetainsMetadataFindingInspection()
    {
        var inspection = LibraryMetadataService.ScanUnionTypes(
            typeof(SampleDiscoveredUnion).Assembly.Location,
            new VerboseLogger(enabled: false));

        var finding = Assert.Single(
            inspection.Findings(),
            finding => finding.Payload.TypeName == typeof(SampleDiscoveredUnion).FullName);
        Assert.Same(MetadataFindings.UnionTypeDescriptor, finding.Descriptor);
    }

    [Fact]
    public void LibraryJson_ProjectsFindingPayloadsWithExistingShape()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll",
            ResourceInspection = MetadataFindings.InspectResources(
                [new ManifestResourceInfo("Test.resources", IsPublic: true, IsEmbedded: true, Size: 42)],
                FindingTestData.Subject),
            AssemblyAttributeInspection = MetadataFindings.InspectAssemblyAttributes(
                [new AssemblyAttributeInfo("AssemblyMetadata(Serviceable)", "Assembly", "True")],
                FindingTestData.Subject),
            TypeForwarderInspection = MetadataFindings.InspectTypeForwarders(
                [new TypeForwarderInfo("Test.Forwarded", "Test.Target")],
                FindingTestData.Subject),
            UnionTypeInspection = MetadataFindings.InspectUnionTypes(
                [new UnionTypeInfo("Test.Union", "struct", true, ["Test.Case"])],
                FindingTestData.Subject),
            SwitchInspection = MetadataFindings.InspectSwitches(
                [new SwitchInfo("Feature Switch", "Test.Switch", "Test.Api")],
                FindingTestData.Subject),
            EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
                [new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.AI, "Chat", "Test.ChatClient")],
                FindingTestData.Subject),
            OpenTelemetryInspection = MetadataFindings.InspectOpenTelemetrySignals(
                [new OpenTelemetrySignalInfo("Tracing", "Test.ActivitySource")],
                FindingTestData.Subject),
        };

        var json = JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("public", root.GetProperty("resources")[0].GetProperty("visibility").GetString());
        Assert.Equal("AssemblyMetadata(Serviceable)", root.GetProperty("custom_attributes")[0].GetProperty("name").GetString());
        Assert.Equal("Test.Forwarded", root.GetProperty("type_forwarders")[0].GetProperty("type_name").GetString());
        Assert.Equal("Test.Union", root.GetProperty("union_types")[0].GetProperty("type_name").GetString());
        Assert.Equal("Test.Switch", root.GetProperty("switches")[0].GetProperty("switch").GetString());
        Assert.Equal(EcosystemIntegrationNames.AI, root.GetProperty("integrations")[0].GetProperty("integration").GetString());
        Assert.Equal("Test.ChatClient", root.GetProperty("ai")[0].GetProperty("name").GetString());
        Assert.Equal("Test.ActivitySource", root.GetProperty("open_telemetry")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("inspection", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedFindingInspection_DoesNotRenderAsEmpty()
    {
        var inspection = new LibraryInspection
        {
            SwitchInspection = new FindingInspection<SwitchInfo>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.SwitchDescriptor,
                    "scan failed")),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => _ = inspection.Switches);
        Assert.Contains("scan failed", exception.Message);
    }
}
