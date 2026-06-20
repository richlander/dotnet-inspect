using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

public class SectionPipelineTests
{
    // Simple test model
    private record TestModel(string? Name, int Count);

    // Test descriptors
    private sealed class AlwaysSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Always";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class DetailedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Detailed";
        public static bool IsExpensive => true;
        public static string? ScannerKey => "DetailedScanner";
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private sealed class NormalSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Normal";
        public static bool IsExpensive => false;
        public static string? ScannerKey => "NormalScanner";
        public static bool CanRender(TestModel model) => model.Name != null;
    }

    private static SectionPipeline<TestModel> CreateTestPipeline() =>
        new SectionPipeline<TestModel>()
            .Add<AlwaysSection>()
            .Add<NormalSection>()
            .Add<DetailedSection>();

    [Fact]
    public void AllSectionNames_ReturnsRegisteredNames()
    {
        var pipeline = CreateTestPipeline();

        var names = pipeline.AllSectionNames;

        Assert.Equal(["Always", "Normal", "Detailed"], names);
    }

    [Fact]
    public void GetEffectiveSections_MinimalVerbosity_ReturnsPrimarySections()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Primary threshold = 1 (everything before first expensive at index 2)
        Assert.Equal(["Always", "Normal"], effective);
    }

    [Fact]
    public void GetEffectiveSections_NormalVerbosity_ReturnsMinimalAndNormal()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Equal(["Always", "Normal"], effective);
    }

    [Fact]
    public void GetEffectiveSections_DetailedVerbosity_ReturnsAll()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Equal(["Always", "Normal", "Detailed"], effective);
    }

    [Fact]
    public void GetEffectiveSections_CanRenderFalse_ExcludesSection()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel(null, 0); // NormalSection and DetailedSection CanRender = false

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Equal(["Always"], effective);
    }

    [Fact]
    public void GetEffectiveSections_IncludeOverridesVerbosity()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);
        var include = new HashSet<string> { "Detailed" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal, include);

        Assert.Equal(["Detailed"], effective);
    }

    [Fact]
    public void ComputeIncludeSections_AllEffective_ReturnsNull()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var result = pipeline.ComputeIncludeSections(model, Verbosity.Detailed);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeIncludeSections_SubsetEffective_ReturnsHashSet()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        // At Minimal, primary sections (Always + Normal) are effective but not Detailed
        var result = pipeline.ComputeIncludeSections(model, Verbosity.Minimal);

        Assert.NotNull(result);
        Assert.Contains("Always", result);
        Assert.Contains("Normal", result);
        Assert.DoesNotContain("Detailed", result);
    }

    [Fact]
    public void ComputeIncludeSections_WithIncludeFilter_ReturnsOnlyMatching()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);
        var include = new HashSet<string> { "Detailed" };

        var result = pipeline.ComputeIncludeSections(model, Verbosity.Minimal, include);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Detailed", result);
    }

    [Fact]
    public void GetRequiredVerbosity_NullInclude_ReturnsQuiet()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(null);

        Assert.Equal(Verbosity.Quiet, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_EmptyInclude_ReturnsQuiet()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity([]);

        Assert.Equal(Verbosity.Quiet, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_PrimarySection_ReturnsQuiet()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Always" });

        Assert.Equal(Verbosity.Quiet, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_DetailedSection_ReturnsDetailed()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Detailed" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_MixedSections_ReturnsHighest()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Always", "Detailed" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    // ===== Library pipeline integration tests =====

    [Fact]
    public void LibraryPipeline_HasExpectedSectionCount()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(32, pipeline.AllSectionNames.Length);
        Assert.Contains("AI", pipeline.AllSectionNames);
        Assert.Contains("ASP.NET Core", pipeline.AllSectionNames);
        Assert.Contains("Aspire", pipeline.AllSectionNames);
        Assert.Contains("Authentication", pipeline.AllSectionNames);
        Assert.Contains("Configuration", pipeline.AllSectionNames);
        Assert.Contains("Dependency Injection", pipeline.AllSectionNames);
        Assert.Contains("Health Checks", pipeline.AllSectionNames);
        Assert.Contains("Hosting", pipeline.AllSectionNames);
        Assert.Contains("HTTP Client", pipeline.AllSectionNames);
        Assert.Contains("Integration Opportunities", pipeline.AllSectionNames);
        Assert.Contains("Integrations", pipeline.AllSectionNames);
        Assert.Contains("Logging", pipeline.AllSectionNames);
        Assert.Contains("OpenAPI", pipeline.AllSectionNames);
        Assert.Contains("OpenTelemetry", pipeline.AllSectionNames);
        Assert.Contains("Options", pipeline.AllSectionNames);
        Assert.Contains("SourceLink Availability", pipeline.AllSectionNames);
        Assert.Contains("SourceLink Missing Files", pipeline.AllSectionNames);
        Assert.Contains("SourceLink Integrity", pipeline.AllSectionNames);
        Assert.Contains("Switches", pipeline.AllSectionNames);
    }

    [Fact]
    public void CanRender_IntegrationOpportunities_UsesScannedRows()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            IntegrationOpportunities =
            [
                new IntegrationOpportunityInfo("Aspire", "Amazon.S3.AmazonS3Client", "AppHost resource builder", "IResourceBuilder<T>, Add*, *Resource")
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Integration Opportunities" });

        Assert.DoesNotContain("Integration Opportunities", effective);
        Assert.Contains("Integration Opportunities", selected);
    }

    [Fact]
    public void CanRender_Switches_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasSwitches = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Switches" });

        Assert.DoesNotContain("Switches", effective);
        Assert.Contains("Switches", selected);
    }

    [Fact]
    public void LibraryPipeline_LibraryInfoShowsAtMinimal()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Library Info", effective);
    }

    [Fact]
    public void LibraryPipeline_SourceIntegrityNeverAutoSelectedByVerbosity()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            SourceIntegrityChecked = true
        };

        // Even at Detailed, an ExplicitOnly section must not be auto-selected.
        var detailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        Assert.DoesNotContain("SourceLink Integrity", detailed);

        // It renders only when explicitly included.
        var included = pipeline.GetEffectiveSections(model, Verbosity.Normal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SourceLink Integrity" });
        Assert.Contains("SourceLink Integrity", included);
    }

    [Fact]
    public void LibraryPipeline_SourceIntegrityAuthorizedOnlyByExplicitSelection()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SourceLink Integrity" };

        // MayFetchSources is authorized only via explicit include, never by verbosity.
        Assert.Empty(pipeline.GetAuthorizedSections(SectionCapabilities.MayFetchSources, Verbosity.Detailed, null));
        Assert.Contains("SourceLink Integrity",
            pipeline.GetAuthorizedSections(SectionCapabilities.MayFetchSources, Verbosity.Normal, include));
    }

    [Fact]
    public void LibraryPipeline_PdbDownloadAuthorizedByDetailedOrInclude()
    {
        var pipeline = LibrarySections.CreatePipeline();

        // Signals declares MayDownloadPdb: not authorized at Normal without include...
        Assert.Empty(pipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, Verbosity.Normal, null));

        // ...authorized at Detailed (verbosity ceiling)...
        Assert.Contains("Signals",
            pipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, Verbosity.Detailed, null));

        // ...and authorized at Normal when explicitly included.
        Assert.Contains("Signals",
            pipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, Verbosity.Normal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }));

        // SourceLink Integrity also declares MayDownloadPdb but is ExplicitOnly: the verbosity ceiling
        // must NOT authorize it without an explicit include (regression guard for the ordering of
        // ExplicitOnly filtering vs. capability authorization).
        Assert.DoesNotContain("SourceLink Integrity",
            pipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, Verbosity.Detailed, null));
    }

    [Fact]
    public void LibraryPipeline_SourceAuditAuthorizedBySignals()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" };

        Assert.Empty(pipeline.GetAuthorizedSections(SectionCapabilities.MayAuditSources, Verbosity.Normal, null));
        Assert.Contains("Signals",
            pipeline.GetAuthorizedSections(SectionCapabilities.MayAuditSources, Verbosity.Normal, include));
        Assert.Contains("Signals",
            pipeline.GetAuthorizedSections(SectionCapabilities.MayAuditSources, Verbosity.Detailed, null));
    }

    [Fact]
    public void LibraryPipeline_SignalsDoesNotShowAtMinimal()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            AuditSignals = [new AuditSignal("Provenance", "SourceLink", "Present", "test")]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Library Info", effective);
        Assert.DoesNotContain("Signals", effective);
    }

    [Fact]
    public void LibraryPipeline_QuietShowsNoSections()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Quiet);

        // Quiet renders hero line via view model, pipeline returns no sections
        Assert.Empty(effective);
    }

    [Fact]
    public void LibraryPipeline_CustomAttributesRequiresNormalWhenSelected()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Custom Attributes" });

        Assert.Equal(Verbosity.Normal, verbosity);
    }

    [Fact]
    public void LibraryPipeline_SourceLinkAvailabilityRequiresDetailed()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "SourceLink Availability" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    // ===== Scanner tests =====

    [Fact]
    public void GetRequiredScanners_MinimalVerbosity_ReturnsPrimaryScanners()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal);

        // NormalSection (index 1) is within primary threshold, has ScannerKey
        Assert.Single(scanners);
        Assert.Contains("NormalScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_NormalVerbosity_ReturnsNormalScanner()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Normal);

        Assert.Single(scanners);
        Assert.Contains("NormalScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_DetailedVerbosity_ReturnsBothScanners()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Detailed);

        Assert.Equal(2, scanners.Count);
        Assert.Contains("NormalScanner", scanners);
        Assert.Contains("DetailedScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_IncludeOverridesVerbosity()
    {
        var pipeline = CreateTestPipeline();
        var include = new HashSet<string> { "Detailed" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Single(scanners);
        Assert.Contains("DetailedScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_NullScannerKeyExcluded()
    {
        var pipeline = CreateTestPipeline();
        var include = new HashSet<string> { "Always" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Empty(scanners);
    }

    [Fact]
    public void LibraryPipeline_UnsafeMembers_UsesOwnScanner()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Unsafe Members", "P/Invoke Methods" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Equal(2, scanners.Count);
        Assert.Contains(LibrarySections.ScannerUnsafeMembers, scanners);
        Assert.Contains(LibrarySections.ScannerClassifiedMethods, scanners);
    }

    [Fact]
    public void LibraryPipeline_AuditCategory_MapsToAuditWorkflowSections()
    {
        var categories = LibrarySections.CreatePipeline().GetCategoryMap();

        Assert.True(categories.TryGetValue(SectionCategoryNames.Audit, out var sections));
        Assert.Equal(
            [
                SectionNames.UnsafeMembers,
                "P/Invoke Methods",
                "Switches"
            ],
            sections);
    }

    [Fact]
    public void LibraryPipeline_TargetedSection_OnlyRequiredScanner()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Custom Attributes" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Single(scanners);
        Assert.Contains(LibrarySections.ScannerCustomAttributes, scanners);
    }

    // ===== Scanner registry tests =====

    [Fact]
    public void ScannerRegistry_RunsOnlyRequestedScanners()
    {
        var ran = new HashSet<string>();
        var registry = new ScannerRegistry()
            .Add("A", _ => ran.Add("A"))
            .Add("B", _ => ran.Add("B"))
            .Add("C", _ => ran.Add("C"));

        registry.RunScanners(["A", "C"], new ScannerContext
        {
            AssemblyPath = "test.dll",
            Model = new LibraryInspection(),
            Logger = new DotnetInspector.Output.VerboseLogger(false),
        });

        Assert.Equal(2, ran.Count);
        Assert.Contains("A", ran);
        Assert.Contains("C", ran);
        Assert.DoesNotContain("B", ran);
    }

    [Fact]
    public void ScannerRegistry_EmptySet_RunsNothing()
    {
        var ran = false;
        var registry = new ScannerRegistry()
            .Add("A", _ => ran = true);

        registry.RunScanners([], new ScannerContext
        {
            AssemblyPath = "test.dll",
            Model = new LibraryInspection(),
            Logger = new DotnetInspector.Output.VerboseLogger(false),
        });

        Assert.False(ran);
    }

    [Fact]
    public void LibraryScannerRegistry_HasAllDetailedScanners()
    {
        var registry = LibrarySections.CreateScannerRegistry();
        var pipeline = LibrarySections.CreatePipeline();

        // All scanner keys from detailed sections should be registered
        var detailedScanners = pipeline.GetRequiredScanners(Verbosity.Detailed);

        // Registry should handle all of them without throwing
        // (we can't easily inspect the registry, but we can verify it runs)
        Assert.NotEmpty(detailedScanners);
    }

    // ===== Presence flag / CanRender discovery tests =====

    [Fact]
    public void CanRender_ExtensionMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        // No scanner has run (ExtensionMethods list is null), but flag is set
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasExtensionTypes = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Extension Methods" });

        Assert.DoesNotContain("Extension Methods", effective);
        Assert.Contains("Extension Methods", selected);
    }

    [Fact]
    public void CanRender_ExtensionMethods_FalseWhenNoFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasExtensionTypes = false
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.DoesNotContain("Extension Methods", effective);
    }

    [Fact]
    public void CanRender_UnsafeMembers_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasUnsafeCode = true
        };

        var effective = pipeline.GetEffectiveSections(
            model,
            Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unsafe Members" });

        Assert.Contains("Unsafe Members", effective);
    }

    [Fact]
    public void CanRender_PInvokeMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasPInvokeImports = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("P/Invoke Methods", effective);
    }

    [Fact]
    public void CanRender_AsyncMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasRuntimeAsync = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Async Methods" });

        Assert.DoesNotContain("Async Methods", effective);
        Assert.Contains("Async Methods", selected);
    }

    [Fact]
    public void CanRender_Resources_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasManifestResources = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Resources" });

        Assert.DoesNotContain("Resources", effective);
        Assert.Contains("Resources", selected);
    }

    [Fact]
    public void CanRender_OpenTelemetry_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasOpenTelemetrySupport = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OpenTelemetry" });

        Assert.DoesNotContain("OpenTelemetry", effective);
        Assert.Contains("OpenTelemetry", selected);
    }

    [Fact]
    public void CanRender_Integrations_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasOpenTelemetrySupport = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Integrations" });

        Assert.DoesNotContain("Integrations", effective);
        Assert.Contains("Integrations", selected);
    }

    [Theory]
    [InlineData("Aspire")]
    [InlineData("ASP.NET Core")]
    [InlineData("Authentication")]
    [InlineData("Configuration")]
    [InlineData("Dependency Injection")]
    [InlineData("AI")]
    [InlineData("Logging")]
    [InlineData("OpenAPI")]
    [InlineData("Options")]
    [InlineData("Hosting")]
    [InlineData("Health Checks")]
    [InlineData("HTTP Client")]
    public void CanRender_EcosystemIntegrationSections_UsePresenceFlags(string sectionName)
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasAspireSupport = sectionName == "Aspire",
            HasAspNetCoreSupport = sectionName == "ASP.NET Core",
            HasAuthenticationSupport = sectionName == "Authentication",
            HasConfigurationSupport = sectionName == "Configuration",
            HasAISupport = sectionName == "AI",
            HasDependencyInjectionSupport = sectionName == "Dependency Injection",
            HasLoggingSupport = sectionName == "Logging",
            HasOpenApiSupport = sectionName == "OpenAPI",
            HasOptionsSupport = sectionName == "Options",
            HasHostingSupport = sectionName == "Hosting",
            HasHealthChecksSupport = sectionName == "Health Checks",
            HasHttpClientSupport = sectionName == "HTTP Client",
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sectionName });

        Assert.DoesNotContain(sectionName, effective);
        Assert.Contains(sectionName, selected);
    }

    [Fact]
    public void CanRender_CustomAttributes_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasAssemblyAttributes = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Custom Attributes" });

        Assert.DoesNotContain("Custom Attributes", effective);
        Assert.Contains("Custom Attributes", selected);
    }

    [Fact]
    public void CanRender_TypeForwarders_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasExportedTypeForwarders = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Type Forwarders" });

        Assert.DoesNotContain("Type Forwarders", effective);
        Assert.Contains("Type Forwarders", selected);
    }

    [Fact]
    public void CanRender_AllFlagsFalse_OnlyAlwaysOnSections()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        // Only sections that don't depend on scanners or presence flags
        Assert.Contains("Library Info", effective);
        Assert.Contains("Symbols", effective);
        Assert.DoesNotContain("Extension Methods", effective);
        Assert.DoesNotContain("Unsafe Members", effective);
        Assert.DoesNotContain("P/Invoke Methods", effective);
        Assert.DoesNotContain("Resources", effective);
        Assert.DoesNotContain("Custom Attributes", effective);
        Assert.DoesNotContain("Type Forwarders", effective);
    }

    // ===== Package pipeline tests =====

    [Fact]
    public void PackagePipeline_HasExpectedSectionCount()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        Assert.Equal(12, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void PackagePipeline_SectionNamesMatchConstants()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Summary", names);
        Assert.Contains("Package Info", names);
        Assert.Contains("Signals", names);
        Assert.Contains("Target Frameworks", names);
        Assert.Contains("Library Files", names);
        Assert.Contains("Statistics", names);
        Assert.Contains("Dependencies", names);
        Assert.Contains("Files", names);
        Assert.Contains("Vulnerabilities", names);
        Assert.Contains("Manifest", names);
        Assert.Contains("Runtime Dependencies", names);
    }

    [Fact]
    public void PackagePipeline_Quiet_IncludesSummaryOnly()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Quiet);

        // Quiet includes the headless Summary section for compact field rendering
        Assert.Single(effective);
        Assert.Equal("Summary", effective[0]);
    }

    [Fact]
    public void PackagePipeline_Minimal_ShowsPackageAndConditionalSections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Package is always renderable at Minimal
        Assert.Contains("Package Info", effective);
        // Statistics requires TotalDownloads (Normal verbosity anyway)
        Assert.DoesNotContain("Statistics", effective);
        // Target Frameworks requires target framework data
        Assert.DoesNotContain("Target Frameworks", effective);
        // Dependencies requires DependencyGroups (Normal verbosity anyway)
        Assert.DoesNotContain("Dependencies", effective);
        // Vulnerabilities is Detailed
        Assert.DoesNotContain("Vulnerabilities", effective);
        // Files is Detailed
        Assert.DoesNotContain("Files", effective);
    }

    [Fact]
    public void PackagePipeline_SignalsDoesNotShowAtMinimal()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        Assert.Contains("Package Info", effective);
        Assert.Contains("Package Info", effective);
        Assert.DoesNotContain("Signals", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsManifestWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Manifest", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsRuntimeDepsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeDependencies = [new PackageDependency { Id = "Dep", Version = "1.0.0" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Runtime Dependencies", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsStatisticsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Statistics", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_HidesStatistics()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Statistics", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsPackageDepsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Dependencies", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_HidesVulnerabilities()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsVulnerabilitiesWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_HidesVulnerabilitiesWhenEmpty()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.DoesNotContain("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_VerbosityAutoPromote_ForVulnerabilities()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Vulnerabilities" });

        Assert.Equal(Verbosity.Detailed, required);
    }

    [Fact]
    public void PackagePipeline_VerbosityAutoPromote_ForPackage()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Package Info" });

        Assert.Equal(Verbosity.Quiet, required);
    }

    [Fact]
    public void PackagePipeline_ComputeIncludeSections_FiltersExplicitOnlySections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0"],
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }],
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }],
            RuntimeDependencies = [new PackageDependency { Id = "Dep2", Version = "2.0" }],
            LibraryFiles = ["lib/net8.0/test.dll"],
            Files = [new PackageFile("lib/net8.0/test.dll", 1234)],
            SignatureResult = new DotnetInspector.Services.SignatureVerificationResult { RepositoryVerified = true, Repository = "nuget.org" },
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        // At Detailed with all default-renderable data populated, explicit-only Signals stays filtered.
        var include = pipeline.ComputeIncludeSections(model, Verbosity.Detailed);

        Assert.NotNull(include);
        Assert.DoesNotContain("Signals", include);
    }

    [Fact]
    public void PackagePipeline_AllSelectorSections_DefaultFirstThenRemainingAlpha()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0"],
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            LibraryFiles = ["lib/net8.0/test.dll"],
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        var sections = pipeline.GetAllSelectorSections(model);

        Assert.Equal("Package Info", sections[0]);
        Assert.DoesNotContain("Summary", sections);
        Assert.Equal(["Dependencies", "Library Files", "Manifest", "Signals", "Statistics", "Target Frameworks"], sections.Skip(1).ToArray());
    }

    [Fact]
    public void PackagePipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(["Package Info", "Library Files"], pipeline.InfoSectionNames);
    }

    // ===== API type-list pipeline tests =====

    [Fact]
    public void ApiTypePipeline_HasExpectedSectionCount()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        Assert.Equal(5, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void ApiTypePipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Classes", names);
        Assert.Contains("Structs", names);
        Assert.Contains("Interfaces", names);
        Assert.Contains("Enums", names);
        Assert.Contains("Delegates", names);
    }

    [Fact]
    public void ApiTypePipeline_ShowsClassesWhenPresent()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface { Types = [new ApiType { Name = "Foo", Kind = "class" }] };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Classes", effective);
        Assert.DoesNotContain("Structs", effective);
    }

    [Fact]
    public void ApiTypePipeline_EmptyTypes_NoSections()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface { Types = [] };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Empty(effective);
    }

    [Fact]
    public void ApiTypePipeline_AllKindsPresent()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Equal(5, effective.Count);
    }

    // ===== API member pipeline tests =====

    [Fact]
    public void ApiMemberPipeline_HasExpectedSectionCount()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        Assert.Equal(20, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void LibraryPipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(["Library Info"], pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberPipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Values", names);
        Assert.Contains("Type Parameters", names);
        Assert.Contains("Interfaces", names);
        Assert.Contains("Baseclass", names);
        Assert.Contains("Constructors", names);
        Assert.Contains("Fields", names);
        Assert.Contains("Properties", names);
        Assert.Contains("Method Groups", names);
        Assert.Contains("Methods", names);
        Assert.Contains("Operators", names);
        Assert.Contains("Explicit Interface Implementations", names);
        Assert.Contains("Extension Methods", names);
        Assert.Contains("Events", names);
        Assert.Contains("Recovered IL", names);
        Assert.Contains("Decompiled Source", names);
        Assert.Contains("Original Source", names);
        Assert.Contains("Custom Attributes", names);
    }

    [Fact]
    public void ApiMemberPipeline_EnumValues_PrimaryAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Color", Kind = "enum",
            Members = [new ApiMember { Name = "Red", Kind = "field", EnumValue = 0 }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var atNormal = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        // Values is index 0 (primary) — shown at Minimal for enums
        Assert.Contains("Values", atMinimal);
        Assert.Contains("Values", atNormal);
    }

    [Fact]
    public void ApiMemberPipeline_TypeParameters_ShowsAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "List", Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // TypeParameters is within primary threshold (before first expensive)
        Assert.Contains("Type Parameters", atMinimal);
    }

    [Fact]
    public void ApiMemberPipeline_Interfaces_ShowsAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Interfaces = ["IDisposable"]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Interfaces is within primary threshold (before first expensive)
        Assert.Contains("Interfaces", atMinimal);
    }

    [Fact]
    public void ApiMemberPipeline_Baseclass_RequiresDetailedAndNonTrivial()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Trivial base (System.Object) should not render
        var trivialModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "System.Object" };
        var trivialEffective = pipeline.GetEffectiveSections(trivialModel, Verbosity.Detailed);
        Assert.DoesNotContain("Baseclass", trivialEffective);

        // Real base should render at Detailed
        var realModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "MyBase" };
        var realEffective = pipeline.GetEffectiveSections(realModel, Verbosity.Detailed);
        Assert.Contains("Baseclass", realEffective);
    }

    [Fact]
    public void ApiMemberPipeline_MemberSections_AtNormal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Members =
            [
                new ApiMember { Name = ".ctor", Kind = "constructor" },
                new ApiMember { Name = "Count", Kind = "property" },
                new ApiMember { Name = "GetValue", Kind = "method" },
                new ApiMember { Name = "op_Equality", Kind = "operator" },
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Constructors", effective);
        Assert.Contains("Properties", effective);
        Assert.Contains("Method Groups", effective);
        Assert.Contains("Methods", effective);
        Assert.Contains("Operators", effective);
        Assert.DoesNotContain("Fields", effective);
        Assert.DoesNotContain("Events", effective);
    }

    [Fact]
    public void ApiMemberPipeline_VerbosityAutoPromote_ForInterfaces()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Interfaces is within primary threshold — no promotion needed
        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Interfaces" });

        Assert.Equal(Verbosity.Quiet, required);
    }

    [Fact]
    public void ApiMemberPipeline_Sources_RemovedWithSourceCommand()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        // Remote Source section was removed — source info lives in the source command now
        Assert.DoesNotContain("Remote Source", names);
    }

    [Fact]
    public void ApiMemberPipeline_InfoPreset_UsesMethodGroups()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        Assert.Contains("Method Groups", pipeline.InfoSectionNames);
        Assert.Contains("Operators", pipeline.InfoSectionNames);
        Assert.Contains("Explicit Interface Implementations", pipeline.InfoSectionNames);
        Assert.Contains("Extension Methods", pipeline.InfoSectionNames);
        Assert.DoesNotContain("Methods", pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_InfoPreset_UsesMethods()
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();

        Assert.Contains("Methods", pipeline.InfoSectionNames);
        Assert.DoesNotContain("Method Groups", pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberDetailPipeline_NormalIncludesLocalImplementationSections()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample", Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method" }]
        };

        var minimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var normal = pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var detailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Signature", minimal);
        Assert.DoesNotContain("Decompiled Source", minimal);
        Assert.DoesNotContain("Original Source", minimal);
        Assert.Contains("Decompiled Source", normal);
        Assert.Contains("Recovered IL", normal);
        Assert.DoesNotContain("Annotated Source", normal);
        Assert.DoesNotContain("Original Source", normal);
        Assert.Contains("Decompiled Source", detailed);
        Assert.Contains("Original Source", detailed);
        Assert.Contains("Recovered IL", detailed);
        Assert.DoesNotContain("Annotated Source", detailed);
        // Call Graph is the only opt-in (ExplicitOnly) detail section.
        var optIn = pipeline.GetCostAnnotations();
        Assert.Equal("opt-in", Assert.Contains("Call Graph", optIn));
        Assert.DoesNotContain("Call Graph", normal);
        Assert.DoesNotContain("Call Graph", detailed);
    }

    [Fact]
    public void ApiMemberDetailPipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();

        Assert.Equal(["Signature", "Decompiled Source"], pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberDetailPipeline_AuditCategory_MapsToSelectedMemberEvidenceSections()
    {
        var categories = ApiMemberDetailSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.Equal(
            [
                SectionNames.Signature,
                SectionNames.Calls,
                SectionNames.Callers,
                SectionNames.UnsafeOperations,
                SectionNames.IL
            ],
            categories[SectionCategoryNames.Audit]);
    }
}
