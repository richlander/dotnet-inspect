using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallersSectionTests
{
    [Fact]
    public async Task CallersSection_RendersOneRowPerCallSite()
    {
        var result = await RunMemberCallersAsync(typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Callers", result.Output);
        Assert.Contains("| Caller | IL Offset | Opcode | Call Kind | Operand Token | Return Address |", result.Output);
        Assert.Equal(1, CountOccurrences(result.Output, $"{nameof(MemberCallersFixture.CallsTargetOnce)}()"));
        Assert.Equal(2, CountOccurrences(result.Output, $"{nameof(MemberCallersFixture.CallsTargetTwice)}()"));
        Assert.Contains("| call | direct |", result.Output);
        Assert.Contains("`IL_", result.Output);
        Assert.Contains("`0x06", result.Output);
    }

    [Fact]
    public async Task CallersSection_KeepsVirtualCallvirtAsDeclaredTarget()
    {
        var result = await RunMemberCallersAsync(typeof(CallersBase).FullName!, nameof(CallersBase.Speak));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"{nameof(MemberCallersFixture.InvokesSpeak)}(", result.Output);
        Assert.Contains("| callvirt | virtual |", result.Output);
    }

    [Fact]
    public async Task CallersSection_TsvUsesPlainNormalizedValues()
    {
        var result = await RunMemberCallersAsync(typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("caller\til_offset\topcode\tcall_kind\toperand_token\treturn_address", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Contains($"{nameof(MemberCallersFixture.CallsTargetOnce)}()", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallersForSelectedMember()
    {
        var result = await RunMemberCallersAsync(
            typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target), discover: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Callers\tsection", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallersStructurally_WhenNoInAssemblyCallers()
    {
        // Orphan has no callers in this assembly. Index-backed sections are opt-in and listed
        // by their structural CanRender gate, not a content probe, so Callers (and Calls /
        // Unsafe Operations) still appear in effective -D without opening the whole-assembly index.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallersFixture).FullName!,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallersFixture.Orphan)],
            OverloadIndex = 1,
            TipLevel = TipLevel.Quiet,
            Discover = [],
            Verbosity = Verbosity.Normal,
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Callers\tsection (opt-in)", result.Output);
        Assert.Contains("Calls\tsection (opt-in)", result.Output);
        Assert.Contains("Unsafe Operations\tsection (opt-in)", result.Output);
        // Regular sections sort before opt-in sections.
        Assert.True(
            result.Output.IndexOf("Signature\tsection\n", StringComparison.Ordinal)
                < result.Output.IndexOf("Calls\tsection (opt-in)", StringComparison.Ordinal),
            "Regular sections should be listed before opt-in sections.");
    }

    [Fact]
    public async Task CallersSection_RendersEmptyStateNote_WhenExplicitlySelectedAndNoCallers()
    {
        // Orphan has no callers in this assembly. Explicitly selecting the Callers section
        // (-S Callers) renders the heading plus an empty-state note instead of vanishing.
        var result = await RunMemberCallersAsync(
            typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Orphan));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Callers", result.Output);
        Assert.Contains("No callers found in this assembly.", result.Output);
    }

    [Fact]
    public async Task CallersSection_StaysSilent_WhenEmptyAndOnlyVerbosityIncluded()
    {
        // No explicit -S selection: Callers is included only by verbosity. An empty section
        // must not clutter a broad view with an empty-state note; it is omitted silently.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallersFixture).FullName!,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallersFixture.Orphan)],
            OverloadIndex = 1,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("No callers found in this assembly.", result.Output);
    }

    [Fact]
    public async Task CallersSection_CrossAssembly_AttributesCallersToSourceAssembly()
    {
        // Target a product member (in dotnet-inspect.dll) and scope the test bin directory,
        // which contains the test assembly that calls it. The caller in the other assembly is
        // attributed to its source assembly and the Source column appears.
        var ownAssembly = typeof(MemberCommand).Assembly.Location;
        var scopeDir = Path.GetDirectoryName(typeof(MemberCallersSectionTests).Assembly.Location)!;
        var testAssemblyName = Path.GetFileNameWithoutExtension(
            typeof(MemberCallersSectionTests).Assembly.Location);

        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCommand).FullName!,
            AssemblyPath = ownAssembly,
            MemberFilter = [nameof(MemberCommand.ExecuteAsync)],
            OverloadIndex = 1,
            CallerScopeDirectories = [scopeDir],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Callers", result.Output);
        // Cross-assembly callers add the Source column and attribute hits to the scanned assembly.
        Assert.Contains("Source", result.Output);
        Assert.Contains(testAssemblyName, result.Output);
    }

    [Fact]
    public async Task CallersSection_NoScope_OmitsSourceColumn()
    {
        // Without a caller scope, callers come from a single assembly, so the Source column is
        // hidden and the output shape is unchanged.
        var result = await RunMemberCallersAsync(
            typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("caller\til_offset\topcode\tcall_kind\toperand_token\treturn_address", result.Output);
        Assert.DoesNotContain("source\t", result.Output);
    }

    [Fact]
    public async Task CallerGraph_CrossAssembly_IncorporatesExternalCallersTaggedWithSource()
    {
        // Target a product member (in dotnet-inspect.dll) and scope the test bin directory,
        // which contains the test assembly that calls it. The external caller is incorporated
        // into the reverse graph and annotated with its source assembly (#1337).
        var ownAssembly = typeof(MemberCommand).Assembly.Location;
        var scopeDir = Path.GetDirectoryName(typeof(MemberCallersSectionTests).Assembly.Location)!;
        var testAssemblyName = Path.GetFileNameWithoutExtension(
            typeof(MemberCallersSectionTests).Assembly.Location);

        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCommand).FullName!,
            AssemblyPath = ownAssembly,
            MemberFilter = [nameof(MemberCommand.ExecuteAsync)],
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caller Graph" },
            CallerScopeDirectories = [scopeDir],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Caller Graph", result.Output);
        Assert.Contains($"from {testAssemblyName}", result.Output);
    }

    [Fact]
    public async Task CallerGraph_NoScope_OmitsSourceAnnotation()
    {
        // Without a caller scope, the reverse graph stays single-assembly and no external
        // source annotation appears.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallersFixture).FullName!,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallersFixture.Target)],
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caller Graph" },
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(" from ", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunMemberCallersAsync(
        string typeName, string memberName, bool tsv = false, bool discover = false)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [memberName],
            IncludeSections = [SectionNames.Callers],
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Normal,
            OneLine = tsv || discover,
            Tsv = tsv || discover,
            OneLineExplicitlySet = tsv || discover,
            FormatExplicitlySet = true,
        }));

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}

public class CallersBase
{
    public virtual void Speak()
    {
    }
}

public static class MemberCallersFixture
{
    public static void Target()
    {
    }

    public static void CallsTargetOnce() => Target();

    public static void CallsTargetTwice()
    {
        Target();
        Target();
    }

    public static void InvokesSpeak(CallersBase thing) => thing.Speak();

    public static void Orphan()
    {
    }
}
