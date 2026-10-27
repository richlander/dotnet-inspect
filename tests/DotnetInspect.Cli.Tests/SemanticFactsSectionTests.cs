using System.Reflection;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class SemanticFactsSectionTests
{
    static readonly string TestAssemblyPath = typeof(SemanticFactsSectionTests).Assembly.Location;

    [Fact]
    public async Task MemberSemanticFacts_RenderObjectiveFacts()
    {
        var result = await RunMemberAsync(
            nameof(SemanticFactsFixture.AllSignals),
            SectionNames.AllocationFacts,
            SectionNames.SafetyFacts,
            SectionNames.CostFacts);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Allocation Facts", result.Output);
        Assert.Contains("## Safety Facts", result.Output);
        Assert.Contains("## Cost Facts", result.Output);
        Assert.Contains("array", result.Output);
        Assert.Contains("stackalloc", result.Output);
        Assert.Contains("virtual dispatch", result.Output);
        Assert.Contains("Lifetime Uses", result.Output);
        Assert.Contains("length-read", result.Output);
        Assert.DoesNotContain("Confidence", result.Output);
        Assert.DoesNotContain("construction", result.Output);
        Assert.DoesNotContain("Root Reach", result.Output);
        Assert.DoesNotContain("Fix Direction", result.Output);
    }

    [Fact]
    public async Task TypeSemanticFacts_RenderMemberIndexedFacts()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(SemanticFactsFixture).FullName,
            AssemblyPath = TestAssemblyPath,
            IncludeSections = [SectionNames.AllocationFacts, SectionNames.SafetyFacts, SectionNames.CostFacts],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Allocation Facts", result.Output);
        Assert.Contains("| Member | IL Offset | Allocation Kind |", result.Output);
        Assert.Contains("`DotnetInspect.Cli.Tests.SemanticFactsFixture.AllSignals", result.Output);
        Assert.Contains("## Safety Facts", result.Output);
        Assert.Contains("## Cost Facts", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LibraryIlOffsetSemanticContexts_RenderPointFacts()
    {
        var method = typeof(SemanticFactsFixture).GetMethod(nameof(SemanticFactsFixture.AllSignals))!;
        LibraryBodyAnalysisExecution analysis = Analyze();
        var allocationOffset =
            analysis.Allocations.Occurrences[
                method.MetadataToken][0].ILOffset;

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            AddressRequest =
                ILCoordinate(method.MetadataToken, allocationOffset),
            IncludeSections = [SectionNames.AllocationContext],
            Select = [SectionNames.AllocationContext],
            Verbosity = Verbosity.Minimal,
            Markdown = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Context: Allocation", result.Output);
        Assert.Contains("| IL Offset | Allocation Kind |", result.Output);
        Assert.Contains("Lifetime Uses", result.Output);
        Assert.Contains("length-read", result.Output);
        Assert.DoesNotContain("Performance Triage", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LibraryIlOffsetAllocationContext_RendersEstimatedSize()
    {
        var method = typeof(SemanticFactsFixture).GetMethod(nameof(SemanticFactsFixture.ConstArray))!;
        LibraryBodyAnalysisExecution analysis = Analyze();
        var allocationOffset =
            analysis.Allocations.Occurrences[
                method.MetadataToken][0].ILOffset;

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            AddressRequest =
                ILCoordinate(method.MetadataToken, allocationOffset),
            IncludeSections = [SectionNames.AllocationContext],
            Select = [SectionNames.AllocationContext],
            Verbosity = Verbosity.Minimal,
            Markdown = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Est Size", result.Output);
        Assert.Contains("Size Tier", result.Output);
        Assert.Contains("40", result.Output);
        Assert.Contains("Exact", result.Output);
        Assert.Contains("escapes", result.Output);
        Assert.Contains("Path Confidence", result.Output);
        Assert.Contains("Post Dominance", result.Output);
        Assert.Contains("straight-line", result.Output);
        Assert.Contains("dominates-return", result.Output);
        Assert.Contains("return-post-dominates", result.Output);
    }

    [Fact]
    public async Task
        MemberCostFacts_DoNotAttachGeneratedBodyOffsets()
    {
        var result = await RunMemberAsync(
            nameof(SemanticFactsFixture
                .AsyncVirtualDispatch),
            SectionNames.CostFacts);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            nameof(SemanticFactsVirtualTarget.Compute),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        LibraryIlOffsetCostContext_UsesGeneratedEvidenceBody()
    {
        var method =
            typeof(SemanticFactsFixture).GetMethod(
                nameof(SemanticFactsFixture
                    .AsyncVirtualDispatch))!;
        LibraryBodyAnalysisExecution analysis = Analyze();
        DirectCall call = Assert.Single(
            analysis.CallGraph.DirectCalls,
            call => call.Caller.MetadataToken
                    == method.MetadataToken
                && call.EvidenceMethod != call.Caller
                && call.Callee.Name
                    == nameof(
                        SemanticFactsVirtualTarget.Compute));

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                new LibraryOptions
                {
                    AssemblyName = TestAssemblyPath,
                    AddressRequest =
                        ILCoordinate(
                            call.EvidenceMethod.MetadataToken,
                            call.ILOffset),
                    IncludeSections =
                        [SectionNames.CostContext],
                    Select = [SectionNames.CostContext],
                    Verbosity = Verbosity.Minimal,
                    Markdown = true,
                    FormatExplicitlySet = true,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "virtual dispatch",
            result.Output);
        Assert.Contains(
            nameof(SemanticFactsVirtualTarget.Compute),
            result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LibraryIlOffsetSafetyContext_FlagsUnsafeCall()
    {
        var method = typeof(SemanticFactsFixture).GetMethod(nameof(SemanticFactsFixture.UnsafeAs))!;
        LibraryBodyAnalysisExecution analysis = Analyze();
        var call = Assert.Single(analysis.CallGraph.DirectCalls, call =>
            call.Caller.MetadataToken == method.MetadataToken
            && call.Callee.DeclaringType.ToQualifiedDisplayString() == "System.Runtime.CompilerServices.Unsafe"
            && call.Callee.Name == "As");

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            AddressRequest =
                ILCoordinate(method.MetadataToken, call.ILOffset),
            IncludeSections = [SectionNames.SafetyContext],
            Select = [SectionNames.SafetyContext],
            Verbosity = Verbosity.Minimal,
            Markdown = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Context: Safety", result.Output);
        Assert.Contains("Unsafe call", result.Output);
        Assert.Contains("Unsafe.As", result.Output);
        Assert.Contains("requires unsafe", result.Output);
    }

    [Fact]
    public void
        ILAnalysisPreparation_SafetyOnlyUsesExactMethodSource()
    {
        MethodInfo method =
            typeof(SemanticFactsFixture).GetMethod(
                nameof(SemanticFactsFixture.UnsafeAs))!;
        using var source =
            SourceLinkService.OpenPrefetched(TestAssemblyPath);
        var options = new LibraryOptions
        {
            IncludeSections = [SectionNames.SafetyContext],
            Select = [SectionNames.SafetyContext],
        };

        ILOffsetAnalysisPreparation preparation =
            ILOffsetQuery.PrepareAnalysis(
                source,
                options,
                [method.MetadataToken, method.MetadataToken]);

        Assert.Null(preparation.AllocationInput);
        Assert.Null(preparation.CostInput);
        Assert.Null(preparation.Failure);
        ILOffsetSafetyAnalysisInput safety =
            Assert.IsType<ILOffsetSafetyAnalysisInput>(
                preparation.SafetyInput);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            safety.SourceReceipt.Breadth.Kind);
        Assert.Single(safety.SourceReceipt.Breadth.Methods);
        Assert.Equal(
            source.Context.GetPrefetchedImage().Length,
            safety.SourceReceipt.TerminalWorkLimits
                .MaximumEncodedIlBytes);
        Assert.Equal(1, safety.SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, safety.SourceReceipt.BodiesAcquired);
        Assert.Equal(0, safety.SourceReceipt.ModuleLookups);
    }

    [Fact]
    public void
        ILAnalysisPreparation_AllocationOnlyUsesExactMethodSource()
    {
        MethodInfo method =
            typeof(SemanticFactsFixture).GetMethod(
                nameof(SemanticFactsFixture.AllSignals))!;
        using var source =
            SourceLinkService.OpenPrefetched(TestAssemblyPath);
        var options = new LibraryOptions
        {
            IncludeSections = [SectionNames.AllocationContext],
            Select = [SectionNames.AllocationContext],
        };

        ILOffsetAnalysisPreparation preparation =
            ILOffsetQuery.PrepareAnalysis(
                source,
                options,
                [method.MetadataToken, method.MetadataToken]);

        Assert.Null(preparation.SafetyInput);
        Assert.Null(preparation.CostInput);
        Assert.Null(preparation.Failure);
        ILOffsetAllocationAnalysisInput allocation =
            Assert.IsType<ILOffsetAllocationAnalysisInput>(
                preparation.AllocationInput);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            allocation.SourceReceipt.Breadth.Kind);
        Assert.Single(allocation.SourceReceipt.Breadth.Methods);
        Assert.Equal(
            source.Context.GetPrefetchedImage().Length,
            allocation.SourceReceipt.TerminalWorkLimits
                .MaximumEncodedIlBytes);
        Assert.Equal(1, allocation.SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, allocation.SourceReceipt.BodiesAcquired);
        Assert.Equal(0, allocation.SourceReceipt.ModuleLookups);
        Assert.Single(allocation.WorkReceipt.Producers);
    }

    [Fact]
    public void
        ILAnalysisPreparation_CostOnlyUsesExactMethodSource()
    {
        MethodInfo method =
            typeof(SemanticFactsFixture).GetMethod(
                nameof(SemanticFactsFixture.AllSignals))!;
        using var source =
            SourceLinkService.OpenPrefetched(TestAssemblyPath);
        var options = new LibraryOptions
        {
            IncludeSections = [SectionNames.CostContext],
            Select = [SectionNames.CostContext],
        };

        ILOffsetAnalysisPreparation preparation =
            ILOffsetQuery.PrepareAnalysis(
                source,
                options,
                [method.MetadataToken, method.MetadataToken]);

        Assert.Null(preparation.AllocationInput);
        Assert.Null(preparation.SafetyInput);
        Assert.Null(preparation.Failure);
        ILOffsetCostAnalysisInput cost =
            Assert.IsType<ILOffsetCostAnalysisInput>(
                preparation.CostInput);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            cost.SourceReceipt.Breadth.Kind);
        Assert.Single(cost.SourceReceipt.Breadth.Methods);
        Assert.Equal(
            source.Context.GetPrefetchedImage().Length,
            cost.SourceReceipt.TerminalWorkLimits
                .MaximumEncodedIlBytes);
        Assert.Equal(1, cost.SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, cost.SourceReceipt.BodiesAcquired);
        Assert.Equal(0, cost.SourceReceipt.ModuleLookups);
        Assert.Equal(1, cost.SourceReceipt.Coverage.Calls.CallReads);
        Assert.Equal(
            1,
            cost.WorkReceipt.For(
                FocusedMethodCallProducer.Instance)
                .UnitsCompleted);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void
        ILAnalysisPreparation_MixedRequestsUseExactMethodSources(
            bool allocation,
            bool safety,
            bool cost)
    {
        MethodInfo method =
            typeof(SemanticFactsFixture).GetMethod(
                nameof(SemanticFactsFixture.AllSignals))!;
        using var source =
            SourceLinkService.OpenPrefetched(TestAssemblyPath);
        var sections = new HashSet<string>();
        if (allocation)
            sections.Add(SectionNames.AllocationContext);
        if (safety)
            sections.Add(SectionNames.SafetyContext);
        if (cost)
            sections.Add(SectionNames.CostContext);
        var options = new LibraryOptions
        {
            IncludeSections = sections,
            Select = [.. sections],
        };

        ILOffsetAnalysisPreparation preparation =
            ILOffsetQuery.PrepareAnalysis(
                source,
                options,
                [method.MetadataToken, method.MetadataToken]);

        Assert.Null(preparation.Failure);
        Assert.Equal(allocation, preparation.AllocationInput is not null);
        Assert.Equal(safety, preparation.SafetyInput is not null);
        Assert.Equal(cost, preparation.CostInput is not null);
        MethodDefinitionSourceReceipt?[] receipts =
        [
            preparation.AllocationInput?.SourceReceipt,
            preparation.SafetyInput?.SourceReceipt,
            preparation.CostInput?.SourceReceipt,
        ];
        foreach (MethodDefinitionSourceReceipt receipt
            in receipts.OfType<MethodDefinitionSourceReceipt>())
        {
            Assert.Equal(
                MethodDefinitionSourceBreadthKind.ExactMethods,
                receipt.Breadth.Kind);
            Assert.Single(receipt.Breadth.Methods);
            Assert.Equal(1, receipt.DefinitionsVisited);
            Assert.Equal(1, receipt.BodiesAcquired);
            Assert.Equal(0, receipt.ModuleLookups);
        }
    }

    [Fact]
    public async Task LibraryIlOffsetSafetyContext_DoesNotFlagSafeSpanCall()
    {
        var method = typeof(SemanticFactsFixture).GetMethod(nameof(SemanticFactsFixture.SafeSpan))!;
        LibraryBodyAnalysisExecution analysis = Analyze();
        var call = Assert.Single(analysis.CallGraph.DirectCalls, call =>
            call.Caller.MetadataToken == method.MetadataToken
            && call.Callee.Name == nameof(Span<int>.Slice));

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            AddressRequest =
                ILCoordinate(method.MetadataToken, call.ILOffset),
            IncludeSections = [SectionNames.SafetyContext],
            Select = [SectionNames.SafetyContext],
            Verbosity = Verbosity.Minimal,
            Markdown = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            "This section (Context: Safety) produced no output.",
            result.Error.Trim());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LibraryIlOffsetSafetyContext_DoesNotFlagSafeSpanOfUnsafeNamedType()
    {
        var method = typeof(SemanticFactsFixture).GetMethod(nameof(SemanticFactsFixture.SafeSpanOfUnsafeNamedType))!;
        LibraryBodyAnalysisExecution analysis = Analyze();
        var call = Assert.Single(analysis.CallGraph.DirectCalls, call =>
            call.Caller.MetadataToken == method.MetadataToken
            && call.Callee.Name == nameof(Span<int>.Slice));

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            AddressRequest =
                ILCoordinate(method.MetadataToken, call.ILOffset),
            IncludeSections = [SectionNames.SafetyContext],
            Select = [SectionNames.SafetyContext],
            Verbosity = Verbosity.Minimal,
            Markdown = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            "This section (Context: Safety) produced no output.",
            result.Error.Trim());
    }


    [Fact]
    public async Task EffectiveDiscovery_ListsSemanticFactSectionsAsOptIn()
    {
        var result = await RunMemberAsync(
            nameof(SemanticFactsFixture.AllSignals),
            discover: true,
            SectionNames.AllocationFacts,
            SectionNames.SafetyFacts,
            SectionNames.CostFacts);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("@Audit (category)", result.Output);
        Assert.Contains("Safety Facts", result.Output);
        Assert.Contains("@Performance (category)", result.Output);
        Assert.Contains("Allocation Facts", result.Output);
        Assert.Contains("Cost Facts", result.Output);
    }

    private static LibraryAddressRequest.IlPoint ILCoordinate(
        int methodToken,
        int ilOffset) =>
        new(
            $"0x{methodToken:X}+0x{ilOffset:X}",
            methodToken,
            ilOffset);

    static LibraryBodyAnalysisExecution Analyze() =>
        LibraryBodyAnalysisService.ExecutePath(
            TestAssemblyPath,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.Default));

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        string member,
        params string[] sections)
        => RunMemberAsync(member, discover: false, sections);

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        string member,
        bool discover,
        params string[] sections)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SemanticFactsFixture).FullName,
            AssemblyPath = TestAssemblyPath,
            MemberFilter = [member],
            IncludeSections = new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase),
            CompanionOutput = CompanionOutput.None,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));
}

public static class SemanticFactsFixture
{
    public static unsafe int AllSignals(IList<int> values)
    {
        var array = new int[values.Count];
        Span<int> scratch = stackalloc int[1];
        scratch[0] = values[0];
        return array.Length + scratch[0];
    }

    public static int SafeSpan(Span<int> values) => values.Slice(0).Length;

    public static int[] ConstArray() => new int[4];

    public static int LoopConstArray(int count)
    {
        var total = 0;
        for (int i = 0; i < count; i++)
        {
            var array = new int[4];
            total += array.Length;
        }
        return total;
    }

    public static int SafeSpanOfUnsafeNamedType(Span<System.Runtime.CompilerServices.UnsafeAccessorAttribute> values)
        => values.Slice(0).Length;

    public static uint UnsafeAs(ref int value)
        => System.Runtime.CompilerServices.Unsafe.As<int, uint>(ref value);

    public static async Task<int> AsyncVirtualDispatch(
        SemanticFactsVirtualTarget target)
    {
        await Task.Yield();
        return target.Compute();
    }
}

public class SemanticFactsVirtualTarget
{
    public virtual int Compute() => 1;
}
