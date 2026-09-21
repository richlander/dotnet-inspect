using DotnetInspect.Cli.Models;
using System.Reflection;
using System.Text.Json;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using InertText;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Tests;

public partial class OutputFormatterTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void PopulateOptimizationOpportunities_RendersRowsForMatchingType()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            ApiAnalysisInspection.OpenTypeAnalysisIndex(typeof(OutputFormatterTests).Assembly.Location),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage });

        var rows = Assert.IsType<List<OptimizationOpportunityRow>>(view.OptimizationOpportunityRows);
        Assert.NotEmpty(rows);
        var row = Assert.Single(rows, row =>
            row.Shape == "small-array"
            && row.Member.Contains(nameof(CreateSmallArrayOpportunity), StringComparison.Ordinal));
        Assert.Contains("pt~", row.Candidate, StringComparison.Ordinal);
        Assert.Equal("analysis.allocation", row.Finding);
        Assert.Equal("exact", row.Provenance);
        Assert.Equal("newarr", row.Operation);
        Assert.Contains("0x", row.Token, StringComparison.Ordinal);
        Assert.Equal("low", row.Priority);

        var generatedGenericBox = Assert.Single(rows, row =>
            row.Shape == "generic-parameter-object-box"
            && row.Member.Contains(
                nameof(HasGeneratedGenericObjectBoxOpportunity),
                StringComparison.Ordinal));
        Assert.Equal("medium", generatedGenericBox.Priority);
        Assert.Null(generatedGenericBox.Finding);
        Assert.Equal("unmatched", generatedGenericBox.Provenance);

        Assert.Contains(rows, row =>
            row.Shape == "generic-parameter-object-box"
            && row.Member.Contains(
                nameof(HasGeneratedGenericObjectBoxLambda),
                StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void PopulateOptimizationOpportunities_MapsAsyncStateMachineCallToSourceMember()
    {
        var type = new ApiType
        {
            Namespace =
                typeof(OutputFormatterAsyncSiblingFixture).Namespace,
            Name = nameof(OutputFormatterAsyncSiblingFixture),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            ApiAnalysisInspection.OpenTypeAnalysisIndex(
                typeof(OutputFormatterTests).Assembly.Location),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.PerformanceTriage
            },
            new PerformanceTriageOptions
            {
                Shapes = ["sync-call-in-async"]
            });

        var row = Assert.Single(
            Assert.IsType<List<OptimizationOpportunityRow>>(
                view.OptimizationOpportunityRows),
            row => row.Member.Contains(
                nameof(
                    OutputFormatterAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync),
                StringComparison.Ordinal));
        Assert.Contains(
            nameof(
                OutputFormatterAsyncSiblingFixture
                    .ReadValueAsync),
            row.Evidence,
            StringComparison.Ordinal);
        Assert.Equal("analysis.call-site", row.Finding);
        Assert.Equal("exact", row.Provenance);
        Assert.Contains("0x", row.EvidenceMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveNext", row.Member, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void PopulateOptimizationOpportunities_AllocationFanoutCountsRepeatedCallSites()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            ApiAnalysisInspection.OpenTypeAnalysisIndex(typeof(OutputFormatterTests).Assembly.Location),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage },
            new PerformanceTriageOptions { Shapes = ["allocation-fanout"] });

        var row = Assert.Single(
            Assert.IsType<List<OptimizationOpportunityRow>>(view.OptimizationOpportunityRows),
            row => row.Member.Contains(nameof(CreateAllocationFanout), StringComparison.Ordinal));
        Assert.Equal("allocation-fanout", row.Shape);
        Assert.Null(row.Finding);
        Assert.Equal("aggregate", row.Provenance);
        Assert.Equal("1", row.DirectSites);
        Assert.Equal("4", row.OncePaths);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void RenderTypeSectionsMarkdown_PopulatesOptimizationOpportunitiesWhenRequested()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateSmallArrayOpportunity)
                }
            ]
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("Performance Triage", markdown);
        Assert.Contains("small-array", markdown);
        Assert.Contains("| Member | Candidate | Finding | Provenance |", markdown);
        Assert.Contains("| Priority | Confidence |", markdown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Speed", "Slow")]
    public void RenderTypeSectionsMarkdown_AppliesPerformanceTriagePlan(
        bool memberScope)
    {
        var method = typeof(OutputFormatterTests).GetMethod(
            nameof(CreateAllocationFanout),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateAllocationFanout),
                    MetadataToken = method.MetadataToken,
                },
            ],
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.PerformanceTriage,
            },
            PerformanceTriage = new PerformanceTriageOptions
            {
                Shapes = ["allocation-fanout"],
                Where = [$"Member={nameof(CreateAllocationFanout)}()"],
                Top = 1,
            },
        };
        if (memberScope)
        {
            options = options with
            {
                OverloadIndex = 1,
                MemberFilter = [nameof(CreateAllocationFanout)],
            };
        }

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains(nameof(CreateAllocationFanout), markdown);
        Assert.Contains("allocation-fanout", markdown);
        Assert.DoesNotContain("small-array", markdown);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void RenderTypeSectionsMarkdown_ScopesOptimizationOpportunitiesToSelectedMember()
    {
        var method = typeof(OutputFormatterTests).GetMethod(
            nameof(CreateSmallArrayOpportunity),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateSmallArrayOpportunity),
                    MetadataToken = method.MetadataToken
                }
            ]
        };
        // OverloadIndex selects the single-member detail pipeline, which restricts rows
        // to the selected member instead of the whole declaring type.
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            OverloadIndex = 1,
            MemberFilter = [nameof(CreateSmallArrayOpportunity)],
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("Performance Triage", markdown);
        Assert.Contains(nameof(CreateSmallArrayOpportunity), markdown);
        Assert.Contains("| Member | Candidate | Finding | Provenance |", markdown);
        Assert.DoesNotContain(nameof(CreateTemporaryArray), markdown);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void RenderTypeSectionsMarkdown_MapsLiftedOpportunityToSelectedSourceMember()
    {
        var method = typeof(OutputFormatterTests).GetMethod(
            nameof(HasGeneratedGenericObjectBoxOpportunity),
            System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)!;
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(HasGeneratedGenericObjectBoxOpportunity),
                    MetadataToken = method.MetadataToken,
                },
            ],
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            OverloadIndex = 1,
            MemberFilter = [nameof(HasGeneratedGenericObjectBoxOpportunity)],
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.PerformanceTriage,
            },
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("generic-parameter-object-box", markdown);
        Assert.Contains(
            nameof(HasGeneratedGenericObjectBoxOpportunity),
            markdown);
    }

    private static int[] CreateSmallArrayOpportunity()
    {
        var value = 3;
        return new int[value];
    }

    private static void CreateTemporaryArray()
    {
        byte[] bytes = new byte[4];
        _ = bytes.Length;
    }

    private static object[] CreateAllocationFanout() =>
    [
        CreateFanoutLeaf(),
        CreateFanoutLeaf(),
        CreateFanoutLeaf(),
    ];

    private static object CreateFanoutLeaf() => new();

    private static async Task<int> CallsFileReadLinesFromAsync(
        string path)
    {
        await Task.Yield();
        return File.ReadLines(path).Count();
    }

    // The local function compiles to a compiler-generated method (<...>g__Make|...)
    // declared on this type, carrying the small-array opportunity from `new int[3]`.
    private static int[] HasGeneratedLocalFunctionOpportunity()
    {
        return Make();
        static int[] Make() => new int[3];
    }

    private static bool HasGeneratedGenericObjectBoxOpportunity<T>(
        T left,
        T right)
    {
        return EqualsCore(left, right);
        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    private static Func<T, bool> HasGeneratedGenericObjectBoxLambda<T>(T right)
        => left => left!.Equals(right);

    [Fact]
    [Trait("Speed", "Slow")]
    public void RenderOptimizationOpportunities_SuppressesGeneratedMethods()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = nameof(HasGeneratedLocalFunctionOpportunity) }
            ]
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        // Generated implementation details are not actionable source fixes and are not
        // selectable at member scope (the API surface omits them), so they are suppressed
        // unconditionally — including under --all — to keep the contract consistent (#1267).
        var defaultMarkdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);
        var allMarkdown = ApiCommand.RenderTypeSectionsMarkdown(type, options with { IncludeAll = true });

        Assert.DoesNotContain("g__Make", defaultMarkdown);
        Assert.DoesNotContain("g__Make", allMarkdown);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void OptimizationOpportunitiesQuery_PreservesGeneratedGenericObjectBox()
    {
        var rows = QueryOptimizationOpportunities();

        var row = Assert.Single(rows!, row =>
            row.Member.Contains(
                nameof(HasGeneratedGenericObjectBoxOpportunity),
                StringComparison.Ordinal)
            && row.Shape == "generic-parameter-object-box");
        Assert.Equal("medium", row.Priority);
        Assert.Equal("medium", row.Confidence);
        Assert.Null(row.Allocation);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_OrdersByTriagePriority()
    {
        var rows = QueryOptimizationOpportunities();

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        // Root Reach is the leverage join: at least one opportunity sits in a reached method.
        Assert.Contains(rows, r => r.RootReach > 0);

        // Verify the exposed default composite key is monotonically non-increasing.
        static (int priority, int conf, int weight, int reach) Key(OptimizationOpportunitySummary r) =>
            (r.Priority == "high" ? 2 : r.Priority == "medium" ? 1 : 0,
             r.Confidence == "high" ? 2 : r.Confidence == "medium" ? 1 : 0,
             r.Weight == "high" ? 2 : r.Weight == "medium" ? 1 : r.Weight == "low" ? 0 : -1,
             r.RootReach);
        for (int i = 1; i < rows.Count; i++)
            Assert.True(Compare(Key(rows[i - 1]), Key(rows[i])) >= 0,
                $"rows not ordered by triage priority at index {i}");

        static int Compare((int priority, int conf, int weight, int reach) a, (int priority, int conf, int weight, int reach) b)
        {
            if (a.priority != b.priority) return a.priority - b.priority;
            if (a.conf != b.conf) return a.conf - b.conf;
            if (a.weight != b.weight) return a.weight - b.weight;
            return a.reach - b.reach;
        }
    }

    [Fact]
    public void ProjectOptimizationOpportunity_OmitsUnknownModuleVersionId()
    {
        var opportunity = Opp(
            "UnknownModule",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "small-array");

        var projected =
            LibraryMetadataService.ProjectOptimizationOpportunity(
                opportunity);

        Assert.Null(projected.ModuleVersionId);
    }

    [Fact]
    public void ProjectOptimizationOpportunity_SeparatesAggregateSupportCoordinate()
    {
        var opportunity = Opp(
            "AggregateScan",
            inLoop: true,
            confidence: "low",
            rootReach: 1,
            shape: "scan-method-in-loop-call") with
        {
            Provenance =
                PerformanceTriageProvenance.Aggregate,
            SupportingCallSite =
                new OptimizationSupportingCallSite(
                    0x06000002,
                    0x001F)
                {
                    SourceFinding =
                        AnalysisFindings
                            .CallSiteDescriptor.Id,
                    Operation = "call",
                    OperandToken = 0x0A000001,
                },
        };

        var projected =
            LibraryMetadataService
                .ProjectOptimizationOpportunity(
                    opportunity);

        Assert.Equal("aggregate", projected.Provenance);
        Assert.Null(projected.Finding);
        Assert.Null(projected.IL);
        Assert.Equal(
            AnalysisFindings.CallSiteDescriptor.Id,
            projected.SupportingFinding);
        Assert.Equal(
            "0x06000002",
            projected.SupportingEvidenceMethod);
        Assert.Equal(
            "IL_001F",
            projected.SupportingIL);
        Assert.Equal(
            "call",
            projected.SupportingOperation);
        Assert.Equal(
            "0x0A000001",
            projected.SupportingToken);
    }

    // #1623 rung 5: a labeled, non-vacuous ranking guard for the Performance Triage
    // model. Unlike the monotonicity check above (which re-derives the production key and
    // only proves self-consistency), this asserts the model's intended priority on seeded
    // opportunities: in-loop and confidence dominate raw root-reach, so pay-dirt outranks
    // higher-reach known-good, and within a tier reach is the tie-break. Reordering the
    // key (e.g. ranking reach above loop/confidence) fails here.
    [Fact]
    public void OrderByTriagePriority_RanksSeededPayDirtAboveKnownGood()
    {
        var opportunities = new[]
        {
            Opp("KnownGoodHighReach", inLoop: false, confidence: "low", rootReach: 999, shape: "small-array"),
            Opp("KnownGoodHighConfNoLoop", inLoop: false, confidence: "high", rootReach: 5, shape: "stackalloc-candidate"),
            Opp("PayDirtLinqScanInLoop", inLoop: true, confidence: "medium", rootReach: 1, shape: "linq-scan-in-loop"),
            Opp("PayDirtLoopHigh", inLoop: true, confidence: "high", rootReach: 1, shape: "allocation-hotspot"),
            Opp("GenericLoopHigh", inLoop: true, confidence: "high", rootReach: 100, shape: "capturing-delegate"),
            Opp("KnownGoodReachLow", inLoop: false, confidence: "low", rootReach: 7, shape: "small-array"),
        };

        var ordered = LibraryMetadataService.OrderByTriagePriority(opportunities)
            .Select(o => o.Method.Name)
            .ToList();

        int payDirtHigh = ordered.IndexOf("PayDirtLoopHigh");
        int payDirtScan = ordered.IndexOf("PayDirtLinqScanInLoop");
        int knownHighConf = ordered.IndexOf("KnownGoodHighConfNoLoop");
        int genericLoopHigh = ordered.IndexOf("GenericLoopHigh");
        int knownHighReach = ordered.IndexOf("KnownGoodHighReach");
        int knownReachLow = ordered.IndexOf("KnownGoodReachLow");

        // Algorithmic pay-dirt outranks a generic repeated allocation even when its
        // confidence and reach are lower.
        Assert.True(payDirtHigh < knownHighConf, "in-loop high must precede not-loop high");
        Assert.True(payDirtScan < knownHighConf, "in-loop medium must precede not-loop high");
        Assert.True(payDirtScan < knownHighReach, "in-loop must precede higher-reach not-loop");
        Assert.True(payDirtScan < genericLoopHigh, "algorithmic amplification must precede generic loop allocation");
        // Within the high-priority tier, higher confidence first.
        Assert.True(payDirtHigh < payDirtScan, "in-loop high must precede in-loop medium");
        // Within not-in-loop, higher confidence beats higher reach.
        Assert.True(knownHighConf < knownHighReach, "high confidence must precede higher-reach low");
        // Within the same (loop, confidence) tier, higher reach is the tie-break.
        Assert.True(knownHighReach < knownReachLow, "higher reach must precede lower reach at equal tier");
    }

    [Fact]
    public void PerformanceGroupRows_PreserveGlobalTriageOrderAcrossKinds()
    {
        var view = new LibraryInspectionView(new LibraryInspection
        {
            PerformanceTriageOpportunities =
            [
                Opp(
                    "Algorithmic",
                    inLoop: true,
                    confidence: "high",
                    rootReach: 1,
                    shape: "string-build-in-loop"),
                Opp(
                    "Boxing",
                    inLoop: true,
                    confidence: "high",
                    rootReach: 1,
                    shape: "box-value-type"),
                Opp(
                    "Array",
                    inLoop: false,
                    confidence: "medium",
                    rootReach: 1,
                    shape: "small-array"),
            ],
        });

        var rows = view.PerformanceGroupRows(PerformanceKinds.Sections);

        Assert.Equal(
            [
                MarkoutInline.Code("Ns.Type.Algorithmic()"),
                MarkoutInline.Code("Ns.Type.Boxing()"),
                MarkoutInline.Code("Ns.Type.Array()"),
            ],
            rows.Select(row => row.Member));
        Assert.Equal(
            ["Loop Hot Paths", "Boxing", "Arrays"],
            rows.Select(row => row.Kind));
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AppliesPaydirtPredicatesAfterRanking()
    {
        var opportunities = new[]
        {
            Opp("LoopMediumDelegate", inLoop: true, confidence: "medium", rootReach: 10, shape: "capturing-delegate"),
            Opp("LoopHighDelegateLowReach", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
            Opp("LoopHighArray", inLoop: true, confidence: "high", rootReach: 99, shape: "small-array"),
            Opp("NoLoopHighDelegate", inLoop: false, confidence: "high", rootReach: 500, shape: "capturing-delegate"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    LoopOnly = true,
                    MinConfidence = "High",
                    Shapes = ["capturing-delegate"],
                    Top = 1
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["LoopHighDelegateLowReach"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AppliesWherePredicates()
    {
        var opportunities = new[]
        {
            Opp("BoxLoop", inLoop: true, confidence: "high", rootReach: 1, shape: "box-value-type") with
            {
                RuntimeAllocationType = "boxed System.Int32",
                PathContext = "loop body",
            },
            Opp("ArrayLoop", inLoop: true, confidence: "high", rootReach: 100, shape: "small-array") with
            {
                RuntimeAllocationType = "System.Int32[]",
                PathContext = "loop body",
            },
            Opp("BoxCold", inLoop: false, confidence: "medium", rootReach: 1000, shape: "box-value-type") with
            {
                RuntimeAllocationType = "boxed System.Guid",
                PathContext = "straight-line",
            },
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where =
                    [
                        "Allocation=boxed *",
                        "Path=loop body",
                        "Confidence>=medium",
                    ],
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["BoxLoop"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_DoesNotMatchMissingNumericFields()
    {
        var opportunities = new[]
        {
            Opp("Fanout", inLoop: false, confidence: "high", rootReach: 1, shape: "allocation-fanout") with
            {
                OnceAllocationPaths = 4,
            },
            Opp("Local", inLoop: false, confidence: "high", rootReach: 1, shape: "allocation-hotspot"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions { Where = ["OncePaths!=5"] })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["Fanout"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_FiltersByWeight()
    {
        var opportunities = new[]
        {
            Opp("HighWeight", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array", weight: "high"),
            Opp("MediumWeight", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array", weight: "medium"),
            Opp("NoWeight", inLoop: false, confidence: "medium", rootReach: 1000, shape: "linq-scan-in-loop"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Weight>=medium"],
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains("HighWeight", filtered);
        Assert.Contains("MediumWeight", filtered);
        Assert.DoesNotContain("NoWeight", filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_FiltersPrioritySeparatelyFromConfidence()
    {
        var opportunities = new[]
        {
            Opp("AlgorithmicLowConfidence", inLoop: true, confidence: "low", rootReach: 1, shape: "scan-method-in-recursive-traversal"),
            Opp("CacheFactoryHighConfidence", inLoop: false, confidence: "high", rootReach: 1, shape: "cache-lookup-factory-delegate"),
            Opp("GenericLoopHighConfidence", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
            Opp("OneShotHighConfidence", inLoop: false, confidence: "high", rootReach: 100, shape: "stackalloc-candidate"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions { Where = ["Priority>=medium"] })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(
            ["CacheFactoryHighConfidence", "GenericLoopHighConfidence", "AlgorithmicLowConfidence"],
            filtered);
    }

    [Fact]
    public void TriagePriority_DoesNotPromoteEscapeUnknownSmallArrayByWeightAlone()
    {
        var opportunity = Opp(
            "ReflectionParamsArray",
            inLoop: true,
            confidence: "medium",
            rootReach: 59,
            shape: "small-array",
            weight: "high");

        Assert.Equal("medium", LibraryMetadataService.TriagePriority(opportunity));
    }

    [Fact]
    public void TriagePriority_RecursiveScanWithoutSourceIdentity_IsMedium()
    {
        var opportunity = Opp(
            "RecursiveScan",
            inLoop: true,
            confidence: "low",
            rootReach: 1,
            shape: "scan-method-in-recursive-traversal");

        Assert.Equal("medium", LibraryMetadataService.TriagePriority(opportunity));
    }

    [Fact]
    public void TriagePriority_GenericObjectBoxRequiresLoopEvidenceForHigh()
    {
        var once = Opp(
            "GenericEqualsOnce",
            inLoop: false,
            confidence: "medium",
            rootReach: 100,
            shape: "generic-parameter-object-box");
        var repeated = once with
        {
            Method = once.Method with { Name = "GenericEqualsRepeated" },
            InLoop = true,
            Multiplicity = "loop",
        };

        Assert.Equal("medium", LibraryMetadataService.TriagePriority(once));
        Assert.Equal("high", LibraryMetadataService.TriagePriority(repeated));
    }

    [Fact]
    public void TriagePriority_CallerLoopEvidenceDoesNotChangeRanking()
    {
        var baseline = Opp(
            "GenericEquals",
            inLoop: false,
            confidence: "medium",
            rootReach: 100,
            shape: "generic-parameter-object-box");
        var callerLoop = baseline with
        {
            CallerLoop = new CallerLoopEvidence(
                1,
                []),
        };

        Assert.Equal(
            LibraryMetadataService.TriagePriority(baseline),
            LibraryMetadataService.TriagePriority(callerLoop));

        var ordinary = baseline with
        {
            Shape = "capturing-delegate",
        };
        Assert.Equal(
            LibraryMetadataService.TriagePriority(ordinary),
            LibraryMetadataService.TriagePriority(
                ordinary with
                {
                    CallerLoop = callerLoop.CallerLoop,
                }));
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AllowsOperatorsInsidePredicateValues()
    {
        var opportunities = new[]
        {
            Opp("ComparisonEvidence", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with
            {
                Evidence = "value >= threshold",
            },
            Opp("OtherEvidence", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with
            {
                Evidence = "plain value",
            },
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Evidence=*>=*"],
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["ComparisonEvidence"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AppliesExplicitOrderBeforeTop()
    {
        var opportunities = new[]
        {
            Opp("LowReach", inLoop: true, confidence: "high", rootReach: 1, shape: "box-value-type"),
            Opp("HighReach", inLoop: false, confidence: "low", rootReach: 100, shape: "box-value-type"),
            Opp("MediumReach", inLoop: true, confidence: "medium", rootReach: 50, shape: "small-array"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Shape=box-value-type"],
                    OrderBy = "RootReach desc",
                    Top = 1,
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["HighReach"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_OrdersByWeight()
    {
        var opportunities = new[]
        {
            Opp("HighWeightHighReach", inLoop: false, confidence: "medium", rootReach: 10, shape: "small-array", weight: "high"),
            Opp("LowWeightHighReach", inLoop: false, confidence: "medium", rootReach: 100, shape: "small-array", weight: "low"),
            Opp("HighWeightLowReach", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array", weight: "high"),
            Opp("NoWeight", inLoop: false, confidence: "medium", rootReach: 1000, shape: "linq-scan-in-loop"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    OrderBy = "Weight desc,RootReach desc",
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["HighWeightHighReach", "HighWeightLowReach", "LowWeightHighReach", "NoWeight"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_OrdersIlNumerically()
    {
        var opportunities = new[]
        {
            Opp("OffsetLarge", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with { ILOffset = 0x10000 },
            Opp("OffsetSmall", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with { ILOffset = 0x2000 },
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    OrderBy = "IL asc",
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["OffsetSmall", "OffsetLarge"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_TriageAscendingPreservesStableTies()
    {
        var opportunities = new[]
        {
            Opp("TieZ", inLoop: false, confidence: "low", rootReach: 1, shape: "capturing-delegate"),
            Opp("TieA", inLoop: false, confidence: "low", rootReach: 1, shape: "capturing-delegate"),
            Opp("High", inLoop: false, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
        };

        var ordered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    OrderBy = "Triage asc",
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["TieZ", "TieA", "High"], ordered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_ExplicitEqualOrderPreservesInputOrder()
    {
        var opportunities = new[]
        {
            Opp("TieZ", inLoop: false, confidence: "high", rootReach: 10, shape: "small-array") with
            {
                ILOffset = 20,
            },
            Opp("TieA", inLoop: true, confidence: "low", rootReach: 10, shape: "box-value-type") with
            {
                ILOffset = 1,
            },
            Opp("First", inLoop: false, confidence: "medium", rootReach: 1, shape: "capturing-delegate"),
        };

        var ordered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    OrderBy = "RootReach asc",
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["First", "TieZ", "TieA"], ordered);
    }

    [Fact]
    public void PerformanceTriageTop_UsesRankingWithoutPromotingItToBaseline()
    {
        var options = new PerformanceTriageOptions { Top = 2 };
        Assert.True(
            PerformanceTriageOptions.TryValidate(options, out var error),
            error.ToString());

        var plan = options.GetResolvedPlan();
        Assert.Null(plan.BaselineOrder);
        Assert.Single(plan.ResolvedOrders);
        Assert.Equal(
            RowSelectionStageKind.Top,
            Assert.Single(plan.SelectionPlan.Stages).Kind);

        var selected = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                [
                    Opp("Low", inLoop: false, confidence: "low", rootReach: 1, shape: "capturing-delegate"),
                    Opp("High", inLoop: false, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
                    Opp("Medium", inLoop: false, confidence: "medium", rootReach: 1, shape: "capturing-delegate"),
                ],
                options)
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["High", "Medium"], selected);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_PreservesAllocationFanoutRanking()
    {
        var opportunities = new[]
        {
            Opp("TieZ", inLoop: false, confidence: "low", rootReach: 1, shape: "allocation-fanout") with
            {
                OnceAllocationPaths = 5,
                RepeatedAllocationPaths = 2,
                ConditionalAllocationPaths = 1,
            },
            Opp("TieA", inLoop: false, confidence: "high", rootReach: 100, shape: "allocation-fanout") with
            {
                OnceAllocationPaths = 5,
                RepeatedAllocationPaths = 2,
                ConditionalAllocationPaths = 1,
            },
            Opp("Winner", inLoop: false, confidence: "low", rootReach: 1, shape: "allocation-fanout") with
            {
                OnceAllocationPaths = 6,
                RepeatedAllocationPaths = 0,
                ConditionalAllocationPaths = 0,
            },
        };
        var options = new PerformanceTriageOptions
        {
            Shapes = ["allocation-fanout"],
        };

        var ordered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                options)
            .Select(opportunity => opportunity.Method.Name)
            .ToList();
        var top = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                options with { Top = 1 })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["Winner", "TieZ", "TieA"], ordered);
        Assert.Equal(["Winner"], top);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_PreservesTokenAndMemberPredicates()
    {
        var target = Opp(
            "Target",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "small-array") with
        {
            OperandToken = 0x0A00113D,
        };
        var other = Opp(
            "Other",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "small-array") with
        {
            OperandToken = 0x0A00113E,
        };

        foreach (string predicate in new[]
        {
            "Token=0x0A00113D",
            "Token=0xA00113D",
            "Token=0x?A00113D",
            "Member=Ns.Type.Target()",
            "Member=Target()",
        })
        {
            var selected =
                LibraryMetadataService.FilterAndOrderTriageOpportunities(
                        [target, other],
                        new PerformanceTriageOptions
                        {
                            Where = [predicate],
                        })
                    .Select(opportunity => opportunity.Method.Name)
                    .ToList();
            Assert.Equal(["Target"], selected);
        }

        Assert.Empty(
            LibraryMetadataService.FilterAndOrderTriageOpportunities(
                [target, other],
                new PerformanceTriageOptions
                {
                    Where = ["Token=not-a-token"],
                }));
    }

    [Fact]
    public void PerformanceTriageFocusedPredicatesMatchCanonicalWherePredicates()
    {
        var opportunities = new[]
        {
            Opp("LoopHigh", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
            Opp("LoopLow", inLoop: true, confidence: "low", rootReach: 1, shape: "capturing-delegate"),
            Opp("OnceHigh", inLoop: false, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
        };

        static string[] Select(
            IEnumerable<ILInspector.Analysis.OptimizationOpportunity> rows,
            PerformanceTriageOptions options) =>
            [
                .. LibraryMetadataService.FilterAndOrderTriageOpportunities(
                        rows,
                        options)
                    .Select(opportunity => opportunity.Method.Name),
            ];

        Assert.Equal(
            Select(
                opportunities,
                new PerformanceTriageOptions { Where = ["Loop=loop"] }),
            Select(
                opportunities,
                new PerformanceTriageOptions { LoopOnly = true }));
        Assert.Equal(
            Select(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Confidence>=medium"],
                }),
            Select(
                opportunities,
                new PerformanceTriageOptions
                {
                    MinConfidence = "medium",
                }));
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_PreservesNullableNumericOrdering()
    {
        var missing = Opp(
            "Missing",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "small-array");
        var present = Opp(
            "Present",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "allocation-fanout") with
        {
            OnceAllocationPaths = 5,
        };

        string[] Ascending() =>
        [
            .. LibraryMetadataService.FilterAndOrderTriageOpportunities(
                    [present, missing],
                    new PerformanceTriageOptions
                    {
                        OrderBy = "OncePaths asc",
                    })
                .Select(opportunity => opportunity.Method.Name),
        ];
        string[] Descending() =>
        [
            .. LibraryMetadataService.FilterAndOrderTriageOpportunities(
                    [missing, present],
                    new PerformanceTriageOptions
                    {
                        OrderBy = "OncePaths desc",
                    })
                .Select(opportunity => opportunity.Method.Name),
        ];

        Assert.Equal(["Missing", "Present"], Ascending());
        Assert.Equal(["Present", "Missing"], Descending());
    }

    [Fact]
    public void PerformanceTriageSchema_ResolvesEveryAdvertisedField()
    {
        foreach (string field in PerformanceTriageOptions.FilterableFields)
        {
            string value = PerformanceTriageOptions.IsNumericField(field)
                ? "1"
                : field is "Priority" or "Confidence" or "Weight"
                    ? "low"
                    : "*";
            Assert.True(
                PerformanceTriageOptions.TryValidate(
                    new PerformanceTriageOptions
                    {
                        Where = [$"{field}={value}"],
                    },
                    out var error),
                $"{field}: {error}");
        }

        foreach (string field in PerformanceTriageOptions.SortableFields)
        {
            Assert.True(
                PerformanceTriageOptions.TryValidate(
                    new PerformanceTriageOptions
                    {
                        OrderBy = $"{field} asc",
                    },
                    out var error),
                $"{field}: {error}");
        }
    }

    [Fact]
    public void PerformanceTriageResolvedPlan_IsReusedWithoutCrossingRecordCopies()
    {
        var options = new PerformanceTriageOptions { Top = 1 };
        Assert.True(
            PerformanceTriageOptions.TryValidate(options, out var error),
            error.ToString());

        var plan = options.GetResolvedPlan();
        Assert.Same(plan, options.GetResolvedPlan());

        var copied = options with { Top = null };
        var copiedPlan = copied.GetResolvedPlan();
        Assert.NotSame(plan, copiedPlan);
        Assert.NotNull(copiedPlan.BaselineOrder);
    }

    static ILInspector.Analysis.OptimizationOpportunity Opp(string name, bool inLoop, string confidence, int rootReach, string shape, string? multiplicity = null, string? weight = null)
    {
        var declaring = ILInspector.Analysis.TypeRef.Definition("Asm", "Ns", "Type");
        var method = new ILInspector.Analysis.MethodIdentity(
            "Asm",
            System.Guid.Empty,
            declaring,
            name,
            [],
            ILInspector.Analysis.TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
        return new ILInspector.Analysis.OptimizationOpportunity(
            method, shape, "evidence", "fix", confidence, inLoop, ILOffset: null, Caveat: null, RootReach: rootReach)
        {
            Multiplicity = multiplicity,
            Weight = weight,
        };
    }

    static List<OptimizationOpportunitySummary>? QueryOptimizationOpportunities(
        PerformanceTriageOptions? options = null)
    {
        string path = typeof(OutputFormatterTests).Assembly.Location;
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));
        var result = OptimizationOpportunitiesQuery.Execute(
            analysis.Optimization,
            includeAllocationFanout:
                options?.IncludesAllocationFanout == true);
        var inspection = new LibraryInspection
        {
            PerformanceTriageOptions =
                options ?? PerformanceTriageOptions.Default,
        };
        LibraryMetadataService.ApplyOptimizationOpportunitiesResult(
            path,
            inspection,
            new VerboseLogger(false),
            result);
        return inspection.OptimizationOpportunities;
    }

    [Fact]
    public void IteratesInLoop_TrustsSemanticMultiplicityOverStructuralInLoop()
    {
        // Structurally in a loop but a return/throw early-exit (Multiplicity conditional)
        // is NOT a hot loop; a genuine loop is; a null multiplicity falls back to InLoop.
        var loopEarlyExit = Opp("LoopEarlyExit", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate", multiplicity: "conditional");
        var genuineLoop = Opp("GenuineLoop", inLoop: true, confidence: "high", rootReach: 1, shape: "allocation-hotspot", multiplicity: "loop");
        var unknownInLoop = Opp("UnknownInLoop", inLoop: true, confidence: "high", rootReach: 1, shape: "allocation-hotspot", multiplicity: null);
        var unknownNotInLoop = Opp("UnknownNotInLoop", inLoop: false, confidence: "high", rootReach: 1, shape: "allocation-hotspot", multiplicity: null);

        Assert.False(LibraryMetadataService.IteratesInLoop(loopEarlyExit));
        Assert.True(LibraryMetadataService.IteratesInLoop(genuineLoop));
        Assert.True(LibraryMetadataService.IteratesInLoop(unknownInLoop));
        Assert.False(LibraryMetadataService.IteratesInLoop(unknownNotInLoop));

        // The loop early-exit is deprioritized below a genuine loop despite equal confidence/reach.
        var ordered = LibraryMetadataService.OrderByTriagePriority([loopEarlyExit, genuineLoop])
            .Select(o => o.Method.Name).ToList();
        Assert.Equal(["GenuineLoop", "LoopEarlyExit"], ordered);

        // --loop keeps only genuine loops.
        var loopOnly = LibraryMetadataService.FilterAndOrderTriageOpportunities(
            [loopEarlyExit, genuineLoop],
            new PerformanceTriageOptions { LoopOnly = true })
            .Select(o => o.Method.Name).ToList();
        Assert.Equal(["GenuineLoop"], loopOnly);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void OptimizationOpportunitiesQuery_SuppressesGeneratedActionableMethodsExceptGenericObjectBox()
    {
        var rows = QueryOptimizationOpportunities();

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, r =>
            r.Shape != AnalysisFindings.StringMaterializationShape
            && r.Shape != "generic-parameter-object-box"
            && (r.Member.Contains("<>c")
                || r.Member.Contains(">g__")
                || r.Member.Contains(">b__")
                || r.Member.Contains(">d__")
                || r.Member.Contains("c__Display")
                || r.Member.Contains("<PrivateImplementationDetails>")));
    }

    [Fact]
    public void IncludePerformanceOpportunity_SuppressesLiftedGeneratedFrameworkMethod()
    {
        var opportunity = Opp(
            "<Build>b__0",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "generic-parameter-object-box");
        opportunity = opportunity with
        {
            Method = opportunity.Method with
            {
                DeclaringType = ILInspector.Analysis.TypeRef.Definition(
                    "Asm",
                    "Ns",
                    "GeneratedOuter+<>c__DisplayClass0_0"),
            },
        };

        Assert.False(LibraryMetadataService.IncludePerformanceOpportunity(
            opportunity,
            new HashSet<TypeRef>
            {
                TypeRef.Definition("Asm", "Ns", "GeneratedOuter"),
            }));
    }

    [Fact]
    public void SelectPerformanceTriageOpportunities_PreservesGeneratedStringCensusOnly()
    {
        var stringOccurrence = Opp(
            "<Main>$",
            inLoop: false,
            confidence: "high",
            rootReach: 1,
            shape: AnalysisFindings.StringMaterializationShape);
        var actionableOpportunity = Opp(
            "<Main>$",
            inLoop: false,
            confidence: "high",
            rootReach: 1,
            shape: "allocation-hotspot");
        var available = new OptimizationOpportunitiesResult.Available(
            [stringOccurrence, actionableOpportunity],
            [],
            [],
            []);

        Assert.Equal(
            [stringOccurrence],
            LibraryMetadataService.SelectPerformanceTriageOpportunities(
                available,
                PerformanceTriageOptions.Default));
    }

    [Fact]
    public void IncludePerformanceOpportunity_DoesNotTreatDisplayCollisionAsGeneratedFramework()
    {
        static TypeRef Exact(
            TypeReferenceOrigin.CurrentAssembly origin,
            string @namespace,
            params string[] segments)
        {
            MetadataTypeDefinitionName name =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        @namespace,
                        [.. segments]))
                .Name;
            TypeRef type = TypeRef.Definition(
                "Asm",
                @namespace,
                string.Join('+', segments));
            typeof(TypeRef).GetProperty(
                    nameof(TypeRef.Resolution),
                    BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(
                    type,
                    new ResolvableTypeReference(
                        origin,
                        name));
            return type;
        }

        var origin = Assert.IsType<TypeReferenceOrigin.CurrentAssembly>(
            typeof(TypeReferenceOrigin.CurrentAssembly)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(constructor =>
                    constructor.GetParameters() is
                    [
                        {
                            ParameterType:
                            var parameterType
                        },
                    ]
                    && parameterType
                        == typeof(AssemblyReferenceIdentity))
                .Invoke([null]));
        TypeRef compilerGenerated =
            Exact(
                origin,
                "Ns",
                "GeneratedOuter",
                "Leaf",
                "<>c__DisplayClass0_0");
        TypeRef collidingGenerated =
            Exact(
                origin,
                "Ns.GeneratedOuter",
                "Leaf");
        Assert.Equal(
            collidingGenerated.ToQualifiedDisplayString()
                + ".<>c__DisplayClass0_0",
            compilerGenerated.ToQualifiedDisplayString());

        var opportunity = Opp(
            "<Build>b__0",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "generic-parameter-object-box");
        opportunity = opportunity with
        {
            Method = opportunity.Method with
            {
                DeclaringType = compilerGenerated,
            },
        };

        Assert.True(LibraryMetadataService.IncludePerformanceOpportunity(
            opportunity,
            new HashSet<TypeRef> { collidingGenerated }));
    }
}

internal static class OutputFormatterAsyncSiblingFixture
{
    public static int ReadValue(int value) => value;

    public static Task<int> ReadValueAsync(
        int value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    public static async Task<int> CallsSyncSiblingFromAsync(
        int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }
}
