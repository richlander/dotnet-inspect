using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Findings;
using ILInspector.Research;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void AllSectionNames_ReturnsRegisteredNames()
    {
        var pipeline = CreateTestPipeline();

        var names = pipeline.AllSectionNames;

        Assert.Equal(["Always", "Normal", "Detailed"], names);
    }

    [Fact]
    public void Add_RuntimeEntry_PreservesPipelineSemantics()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add(new SectionEntry<TestModel>
            {
                Name = "Runtime",
                IsExpensive = false,
                ExplicitOnly = false,
                Info = true,
                ProbeEffectiveness = true,
                Capabilities = SectionCapabilities.None,
                HasExplicitApplicability = true,
                IsApplicable = model => model.Name != null,
                CanRender = model => model.Count > 0,
            });

        Assert.Equal(["Runtime"], pipeline.AllSectionNames);
        Assert.Equal(["Runtime"], pipeline.GetDiscoverableSections(new TestModel("target", 0)));
        Assert.Empty(pipeline.GetEffectiveSections(new TestModel("target", 0), Verbosity.Minimal));
        Assert.Equal(["Runtime"], pipeline.GetEffectiveSections(new TestModel("target", 1), Verbosity.Minimal));
    }

    [Fact]
    public void Add_UnprobedRuntimeEntry_AllowsStructuralApplicability()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add(new SectionEntry<TestModel>
            {
                Name = "Heavy",
                IsExpensive = true,
                ExplicitOnly = false,
                Info = false,
                ProbeEffectiveness = false,
                Capabilities = SectionCapabilities.None,
                HasExplicitApplicability = true,
                IsApplicable = model => model.Name != null,
                CanRender = model => model.Count > 0,
            });

        Assert.Equal(["Heavy"], pipeline.GetDiscoverableSections(new TestModel("target", 0)));
        Assert.Contains("Heavy", pipeline.GetUnprobedSections());
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
    public void GetApplicableSections_UsesRegistrationApplicability()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add<StructurallyApplicableSection>(model => model.Name != null);
        var model = new TestModel("target", 0);

        var applicable = pipeline.GetApplicableSections(model);
        var available = pipeline.GetAvailableSections(model);
        var explicitlyApplicable = pipeline.GetExplicitlyApplicableSections(model);

        Assert.Equal(["Structural"], applicable);
        Assert.Empty(available);
        Assert.Equal(["Structural"], explicitlyApplicable);
    }

    [Fact]
    public void GetDiscoverableSections_UnprobedStillRequiresApplicability()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add<UnprobedSection>();

        Assert.Empty(pipeline.GetDiscoverableSections(new TestModel("target", 0)));
        Assert.Equal(["Unprobed"], pipeline.GetDiscoverableSections(new TestModel("target", 1)));
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
}
