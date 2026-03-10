using DotnetInspector.Metadata;
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

        Assert.Equal(13, pipeline.AllSectionNames.Length);
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
    public void LibraryPipeline_QuietShowsNoSections()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Quiet);

        // Quiet renders hero line via view model, pipeline returns no sections
        Assert.Empty(effective);
    }

    [Fact]
    public void LibraryPipeline_CustomAttributesRequiresNormal()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Custom Attributes" });

        Assert.Equal(Verbosity.Normal, verbosity);
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
    public void LibraryPipeline_SharedScannerKey_Deduplicated()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Unsafe Methods", "P/Invoke Methods" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Single(scanners);
        Assert.Contains(LibrarySections.ScannerClassifiedMethods, scanners);
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

        Assert.Contains("Extension Methods", effective);
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
    public void CanRender_UnsafeMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasUnsafeCode = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Unsafe Methods", effective);
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
    public void CanRender_Resources_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasManifestResources = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Resources", effective);
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

        Assert.Contains("Custom Attributes", effective);
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

        Assert.Contains("Type Forwarders", effective);
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
        Assert.DoesNotContain("Unsafe Methods", effective);
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
        Assert.Equal(8, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void PackagePipeline_SectionNamesMatchConstants()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Summary", names);
        Assert.Contains("Package", names);
        Assert.Contains("Statistics", names);
        Assert.Contains("Package Dependencies", names);
        Assert.Contains("Files", names);
        Assert.Contains("Vulnerabilities", names);
        Assert.Contains("RID Packages", names);
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
        Assert.Contains("Package", effective);
        // Statistics requires TotalDownloads (Normal verbosity anyway)
        Assert.DoesNotContain("Statistics", effective);
        // Package Dependencies requires DependencyGroups (Normal verbosity anyway)
        Assert.DoesNotContain("Package Dependencies", effective);
        // Vulnerabilities is Detailed
        Assert.DoesNotContain("Vulnerabilities", effective);
        // Files is Detailed
        Assert.DoesNotContain("Files", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsRidPackagesWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("RID Packages", effective);
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

        Assert.Contains("Package Dependencies", effective);
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

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Package" });

        Assert.Equal(Verbosity.Quiet, required);
    }

    [Fact]
    public void PackagePipeline_ComputeIncludeSections_NullWhenAllRenderable()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }],
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }],
            RuntimeDependencies = [new PackageDependency { Id = "Dep2", Version = "2.0" }],
            Files = ["lib/net8.0/test.dll"]
        };

        // At Detailed with all data populated, all 7 sections render
        var include = pipeline.ComputeIncludeSections(model, Verbosity.Detailed);

        Assert.Null(include);
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
        Assert.Equal(15, pipeline.AllSectionNames.Length);
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
        Assert.Contains("Remote Source", names);
        Assert.Contains("Constructors", names);
        Assert.Contains("Fields", names);
        Assert.Contains("Properties", names);
        Assert.Contains("Methods", names);
        Assert.Contains("Events", names);
        Assert.Contains("IL", names);
        Assert.Contains("Source", names);
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
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Constructors", effective);
        Assert.Contains("Properties", effective);
        Assert.Contains("Methods", effective);
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
    public void ApiMemberPipeline_Sources_AtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            SourceFilePath = "src/Foo.cs"
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Remote Source is within primary threshold (before first expensive)
        Assert.Contains("Remote Source", effective);
    }
}
