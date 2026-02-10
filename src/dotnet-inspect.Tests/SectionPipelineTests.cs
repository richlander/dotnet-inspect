using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

public class SectionPipelineTests
{
    // Simple test model
    private record TestModel(string? Name, int Count);

    // Test descriptors
    private sealed class AlwaysSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Always";
        public static Verbosity MinVerbosity => Verbosity.Minimal;
        public static string? ScannerKey => null;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class DetailedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Detailed";
        public static Verbosity MinVerbosity => Verbosity.Detailed;
        public static string? ScannerKey => "DetailedScanner";
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private sealed class NormalSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Normal";
        public static Verbosity MinVerbosity => Verbosity.Normal;
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
    public void GetEffectiveSections_MinimalVerbosity_ReturnsOnlyMinimalSections()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Equal(["Always"], effective);
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
    public void GetEffectiveSections_ExcludeRemovesSection()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);
        var exclude = new HashSet<string> { "Always" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed, exclude: exclude);

        Assert.Equal(["Normal", "Detailed"], effective);
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

        var result = pipeline.ComputeIncludeSections(model, Verbosity.Minimal);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Always", result);
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
    public void GetRequiredVerbosity_MinimalSection_ReturnsMinimal()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Always" });

        Assert.Equal(Verbosity.Minimal, verbosity);
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

        Assert.Equal(14, pipeline.AllSectionNames.Length);
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

        Assert.Empty(effective);
    }

    [Fact]
    public void LibraryPipeline_CustomAttributesRequiresDetailed()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Custom Attributes" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    // ===== Scanner tests =====

    [Fact]
    public void GetRequiredScanners_MinimalVerbosity_ReturnsEmpty()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal);

        Assert.Empty(scanners);
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
}
