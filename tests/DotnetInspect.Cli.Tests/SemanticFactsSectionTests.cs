using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.Analysis;

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
            TipLevel = TipLevel.Quiet,
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
            CoordinateRequest =
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
            CoordinateRequest =
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
                    CoordinateRequest =
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
            CoordinateRequest =
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
            CoordinateRequest =
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
            CoordinateRequest =
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

    private static LibraryCoordinateRequest.IlPoint ILCoordinate(
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
            TipLevel = TipLevel.Quiet,
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
