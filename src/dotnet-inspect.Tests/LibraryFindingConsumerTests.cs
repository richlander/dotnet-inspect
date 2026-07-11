using System.Text.Json;
using DotnetInspector.Commands;
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
    public void ClassifiedMethodScanner_RetainsFindingSemanticsAndDisplayProjection()
    {
        var inspection = new LibraryInspection();

        LibraryMetadataService.ScanClassifiedMethods(
            typeof(SampleUnsafeClass).Assembly.Location,
            inspection,
            new VerboseLogger(enabled: false));

        var finding = Assert.Single(
            inspection.ClassifiedMethodInspection.Findings(),
            finding => finding.Payload.Anchor.MemberName == nameof(SampleUnsafeClass.UnsafePointerMethod));
        Assert.Same(MetadataFindings.ClassifiedMethodDescriptor, finding.Descriptor);
        Assert.Equal(MethodClassification.Unsafe, finding.Payload.Classification);
        Assert.Contains(
            inspection.UnsafeMethods!,
            method => method.MethodName == nameof(SampleUnsafeClass.UnsafePointerMethod)
                      && method.Signature.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public void ExtensionScanner_RetainsFindingSemanticsAndDisplayProjection()
    {
        var inspection = new LibraryInspection();

        LibraryMetadataService.ScanExtensionMembers(
            typeof(ExtensionsCommandTests).Assembly.Location,
            inspection,
            new VerboseLogger(enabled: false));

        var finding = Assert.Single(
            inspection.ExtensionMemberInspection.Findings(),
            finding => finding.Payload.Anchor.MemberName == "ToUpperCase");
        var row = Assert.Single(
            inspection.ExtensionMethods!,
            row => row.MethodName == "ToUpperCase");

        Assert.Same(MetadataFindings.ExtensionMemberDescriptor, finding.Descriptor);
        Assert.Equal(ExtensionMemberKind.Method, finding.Payload.Kind);
        Assert.Contains("string", row.ExtendedType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LibraryJson_ProjectsFindingPayloadsWithExistingShape()
    {
        AssemblyAttributeInfo[] attributes =
        [
            new("AssemblyMetadata(Serviceable)", "Assembly", "True"),
            new("Marker", "Module", null),
        ];
        ManifestResourceInfo[] resources =
        [
            new("SKILL.md", IsPublic: true, IsEmbedded: true, Size: 42),
            new("release-notes.md", IsPublic: true, IsEmbedded: true, Size: 43),
        ];
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll",
            ResourceInspection = MetadataFindings.InspectResources(
                resources,
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
        inspection.SetAssemblyAttributeInspection(
            MetadataFindings.InspectAssemblyAttributes(
                attributes,
                FindingTestData.Subject),
            attributes);
        ExtensionMethodInfo[] extensionMembers =
        [
            FindingTestData.ExtensionMember("Ext", "Test.Target"),
        ];
        inspection.SetExtensionMemberInspection(
            MetadataFindings.InspectExtensionMembers(
                extensionMembers,
                FindingTestData.Subject),
            extensionMembers);

        var json = JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var customAttributes = root.GetProperty("custom_attributes").EnumerateArray().ToArray();
        var resourcesJson = root.GetProperty("resources").EnumerateArray().ToArray();
        var expectedResourceOrder = resources.OrderBy(resource => resource.Name).ToArray();

        Assert.Equal(
            expectedResourceOrder.Select(resource => resource.Name),
            resourcesJson.Select(resource => resource.GetProperty("name").GetString()));
        Assert.Equal("public", resourcesJson[0].GetProperty("visibility").GetString());
        Assert.Equal("AssemblyMetadata(Serviceable)", customAttributes[0].GetProperty("name").GetString());
        Assert.Equal("True", customAttributes[0].GetProperty("value").GetString());
        Assert.Equal("Marker", customAttributes[1].GetProperty("name").GetString());
        Assert.False(customAttributes[1].TryGetProperty("value", out _));
        Assert.Equal("Test.Forwarded", root.GetProperty("type_forwarders")[0].GetProperty("type_name").GetString());
        Assert.Equal("Test.Union", root.GetProperty("union_types")[0].GetProperty("type_name").GetString());
        Assert.Equal("Test.Switch", root.GetProperty("switches")[0].GetProperty("switch").GetString());
        Assert.Equal("Ext", root.GetProperty("extension_methods")[0].GetProperty("method_name").GetString());
        Assert.Equal("Test.Target", root.GetProperty("extension_methods")[0].GetProperty("extended_type").GetString());
        Assert.Equal(EcosystemIntegrationNames.AI, root.GetProperty("integrations")[0].GetProperty("integration").GetString());
        Assert.Equal("Test.ChatClient", root.GetProperty("ai")[0].GetProperty("name").GetString());
        Assert.Equal("Test.ActivitySource", root.GetProperty("open_telemetry")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("inspection", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssemblyAttributeInspection_RequiresExplicitJsonOrder()
    {
        var attributeProperty = typeof(LibraryInspection).GetProperty(
            nameof(LibraryInspection.AssemblyAttributeInspection));
        var extensionProperty = typeof(LibraryInspection).GetProperty(
            nameof(LibraryInspection.ExtensionMemberInspection));

        Assert.NotNull(attributeProperty);
        Assert.False(attributeProperty.CanWrite);
        Assert.NotNull(extensionProperty);
        Assert.False(extensionProperty.CanWrite);
    }

    [Fact]
    public void FailedScannerAcquisition_RetainsFindingFailures()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var logger = new VerboseLogger(enabled: false);
        var inspection = new LibraryInspection
        {
            ResourceInspection = LibraryMetadataService.ScanResources(missingPath, logger),
            UnionTypeInspection = LibraryMetadataService.ScanUnionTypes(missingPath, logger),
            SwitchInspection = LibraryMetadataService.ScanSwitches(missingPath, logger),
        };

        LibraryMetadataService.ScanClassifiedMethods(missingPath, inspection, logger);
        LibraryMetadataService.ScanExtensionMembers(missingPath, inspection, logger);
        LibraryMetadataService.ScanCustomAttributes(missingPath, inspection, logger);
        LibraryMetadataService.ScanTypeForwarders(missingPath, inspection, logger);
        LibraryMetadataService.ScanIntegrations(missingPath, inspection, logger);

        AssertFailure(inspection.ClassifiedMethodInspection, MetadataFindings.ClassifiedMethodDescriptor);
        AssertFailure(inspection.ExtensionMemberInspection, MetadataFindings.ExtensionMemberDescriptor);
        AssertFailure(inspection.ResourceInspection, MetadataFindings.ResourceDescriptor);
        AssertFailure(inspection.AssemblyAttributeInspection, MetadataFindings.AssemblyAttributeDescriptor);
        AssertFailure(inspection.TypeForwarderInspection, MetadataFindings.TypeForwarderDescriptor);
        AssertFailure(inspection.UnionTypeInspection, MetadataFindings.UnionTypeDescriptor);
        AssertFailure(inspection.SwitchInspection, MetadataFindings.SwitchDescriptor);
        AssertFailure(inspection.EcosystemIntegrationInspection, MetadataFindings.EcosystemIntegrationDescriptor);
        AssertFailure(inspection.OpenTelemetryInspection, MetadataFindings.OpenTelemetrySignalDescriptor);
        Assert.Equal(9, inspection.InspectionFailures!.Count);
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

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.SwitchInspection.Findings());
        Assert.Contains("scan failed", exception.Message);
        Assert.Null(inspection.Switches);

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal("Switches", failure.Section);
        Assert.Equal("scan failed", failure.Reason);

        var json = JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection);
        Assert.Contains("\"inspection_failures\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"switches\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedClassifiedMethodInspection_DoesNotRenderPresentationRows()
    {
        var inspection = new LibraryInspection
        {
            ClassifiedMethodInspection = new FindingInspection<ClassifiedMethodObservation>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.ClassifiedMethodDescriptor,
                    "method scan failed")),
            UnsafeMethods =
            [
                new ClassifiedMethodSummary
                {
                    MethodName = "M",
                    DeclaringType = "Test.Type",
                    Signature = "void M()",
                },
            ],
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.ClassifiedMethodInspection.Findings());
        Assert.Contains("method scan failed", exception.Message);
        Assert.Null(inspection.UnsafeMethods);
        Assert.Equal(0, inspection.UnsafeMethodCount);

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal("Classified Methods", failure.Section);
        Assert.Equal("method scan failed", failure.Reason);
    }

    [Fact]
    public void FailedExtensionInspection_DoesNotRenderPresentationRows()
    {
        var member = FindingTestData.ExtensionMember("Ext", "Test.Target");
        var inspection = new LibraryInspection();
        inspection.SetExtensionMemberInspection(
            new FindingInspection<ExtensionMemberObservation>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.ExtensionMemberDescriptor,
                    "extension scan failed")),
            [member]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => inspection.ExtensionMemberInspection.Findings());
        Assert.Contains("extension scan failed", exception.Message);
        Assert.Null(inspection.ExtensionMethods);

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal("Extension Methods", failure.Section);
        Assert.Equal("extension scan failed", failure.Reason);
    }

    [Fact]
    public void FindingJsonProjections_AreCachedAndInvalidatedWithTheirInspection()
    {
        var inspection = new LibraryInspection
        {
            ResourceInspection = MetadataFindings.InspectResources(
                [new ManifestResourceInfo("First", true, true, 1)],
                FindingTestData.Subject),
            EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
                [new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.AI, "Chat", "Test.Chat")],
                FindingTestData.Subject),
            SwitchInspection = new FindingInspection<SwitchInfo>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.SwitchDescriptor,
                    "first failure")),
        };

        var resources = inspection.Resources;
        var ai = inspection.AI;
        var failures = inspection.InspectionFailures;

        Assert.Same(resources, inspection.Resources);
        Assert.Same(ai, inspection.AI);
        Assert.Same(failures, inspection.InspectionFailures);

        inspection.ResourceInspection = MetadataFindings.InspectResources(
            [new ManifestResourceInfo("Second", true, true, 2)],
            FindingTestData.Subject);
        inspection.EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
            [new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Logging, "Logger", "Test.Logger")],
            FindingTestData.Subject);
        inspection.SwitchInspection = MetadataFindings.InspectSwitches([], FindingTestData.Subject);

        Assert.NotSame(resources, inspection.Resources);
        Assert.Equal("Second", Assert.Single(inspection.Resources!).Name);
        Assert.Null(inspection.AI);
        Assert.Null(inspection.InspectionFailures);
    }

    [Fact]
    public void ExplicitSelectionFailureWarnings_AreCorrelatedToAffectedSections()
    {
        Assert.True(LibraryCommand.FailureAffectsSection("Classified Methods", "P/Invoke Methods"));
        Assert.True(LibraryCommand.FailureAffectsSection("Extension Methods", "Library Info"));
        Assert.True(LibraryCommand.FailureAffectsSection("Switches", "Library Info"));
        Assert.True(LibraryCommand.FailureAffectsSection(
            LibraryIntegrationCatalog.RollupName,
            EcosystemIntegrationNames.AI));
        Assert.False(LibraryCommand.FailureAffectsSection("Custom Attributes", "Type Forwarders"));
    }

    static void AssertFailure<T>(
        FindingInspection<T>? inspection,
        FindingDescriptor expectedDescriptor)
        where T : notnull
    {
        var failure = Assert.IsType<FindingInspection<T>.Failed>(inspection?.Value);
        Assert.Same(expectedDescriptor, failure.Error.Descriptor);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error.Reason));
    }
}
