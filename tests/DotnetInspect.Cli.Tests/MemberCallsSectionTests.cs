using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class MemberCallsSectionTests
{
    [Fact]
    public async Task CallsSection_RendersOneRowPerCallSite()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Calls", result.Output);
        Assert.Contains("| IL Offset | Opcode | Call Kind | Callee | Operand Token | Return Address |", result.Output);
        Assert.Equal(2, CountOccurrences(result.Output, "`System.Console.WriteLine(string)`"));
        Assert.Contains("`IL_", result.Output);
        Assert.Contains("`0x0A", result.Output);
    }

    [Fact]
    public async Task CallsSection_SemanticTailSelectsTheSameCallSiteAcrossFormats()
    {
        string[] args =
        [
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            typeof(MemberCallsFixture).Assembly.Location,
            "-m",
            nameof(MemberCallsFixture.CallsWriteLineTwice),
            "-S",
            SectionNames.Calls,
            "-n",
            "1",
            "--tail",
            "--tips",
            "q",
        ];

        var markdown = await RunCliAsync(args);
        var table = await RunCliAsync([.. args, "--format=table"]);
        var tsv = await RunCliAsync(
            [.. args, "--format=tsv", "--no-headers"]);
        var jsonl = await RunCliAsync([.. args, "--format=jsonl"]);
        var json = await RunCliAsync([.. args, "--format=json"]);
        var count = await RunCliAsync([.. args, "--count"]);
        var jsonCount = await RunCliAsync(
            [.. args, "--count", "--format=json"]);

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            json,
            count,
            jsonCount,
        })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
        }

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            json,
        })
        {
            Assert.Contains("IL_000F", result.Output);
            Assert.DoesNotContain("IL_0005", result.Output);
        }

        Assert.Single(
            tsv.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(
            jsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(json.Output);
        JsonElement call = Assert.Single(
            document.RootElement
                .GetProperty("calls")
                .EnumerateArray());
        Assert.Equal(
            "IL_000F",
            call.GetProperty("il_offset").GetString());
        Assert.Equal("1", count.Output.Trim());
        Assert.Equal("1", jsonCount.Output.Trim());
    }

    [Fact]
    public async Task CallsSection_GeneratedEvidenceCompletesBeforeSemanticSelection()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            typeof(MemberCallsFixture).Assembly.Location,
            "-m",
            nameof(MemberCallsFixture.CallsWriteLineAfterYield),
            "-S",
            SectionNames.Calls,
            "--rows",
            "9..9",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement call = Assert.Single(
            document.RootElement
                .GetProperty("calls")
                .EnumerateArray());
        Assert.Equal(
            "System.Console.WriteLine(string)",
            call.GetProperty("callee").GetString());
        Assert.Contains(
            "MoveNext",
            call.GetProperty("evidence_method").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallsSection_EmptyStructuredJsonPreservesRowArray()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            typeof(MemberCallsFixture).Assembly.Location,
            "-m",
            nameof(MemberCallsFixture.Overloaded),
            "--index",
            "1",
            "-S",
            SectionNames.Calls,
            "-n",
            "1",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement calls = document.RootElement.GetProperty("calls");
        Assert.Equal(JsonValueKind.Array, calls.ValueKind);
        Assert.Empty(calls.EnumerateArray());
    }

    [Fact]
    public async Task CallsSection_UnavailableWindowWithholdsOutput()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            typeof(MemberCallsFixture).Assembly.Location,
            "-m",
            nameof(MemberCallsFixture.CallsWriteLineTwice),
            "-S",
            SectionNames.Calls,
            "--rows",
            "3..3",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires call row 3, but only 2 call rows are available",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallsSection_ExplicitLinesRejectJsonBeforeAcquisition()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"missing-calls-{Guid.NewGuid():N}.dll");
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            missingAssembly,
            "-m",
            nameof(MemberCallsFixture.CallsWriteLineTwice),
            "-S",
            SectionNames.Calls,
            "-n",
            "1",
            "--lines",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            missingAssembly,
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Calls,Callers")]
    [InlineData("@Calls")]
    public async Task CallsSection_NeighboringSelectionsRetainRenderedLineFallback(
        string selection)
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"missing-calls-{Guid.NewGuid():N}.dll");
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            missingAssembly,
            "-m",
            nameof(MemberCallsFixture.CallsWriteLineTwice),
            "-S",
            selection,
            "-n",
            "1",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            missingAssembly,
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallsSection_SemanticSelectionRetainsSingleOverloadRequirement()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            typeof(MemberCallsFixture).Assembly.Location,
            "-m",
            nameof(MemberCallsFixture.Overloaded),
            "-S",
            SectionNames.Calls,
            "-n",
            "1",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires a single selected overload",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallsSection_KeepsInterfaceCallvirtAsDeclaredTarget()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsInterfaceItem));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("`System.Collections.Generic.IList<int>.get_Item(int)`", result.Output);
        Assert.Contains("| callvirt | virtual |", result.Output);
    }

    [Fact]
    public async Task
        CallsSection_IdentifiesGeneratedEvidenceBody()
    {
        var result = await RunMemberCallsAsync(
            nameof(MemberCallsFixture
                .CallsWriteLineAfterYield));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Evidence Method", result.Output);
        Assert.Contains("MoveNext", result.Output);
        Assert.Contains(
            "`System.Console.WriteLine(string)`",
            result.Output);
    }

    [Fact]
    public async Task CallsSection_SelectedSecondOverload_UsesSelectedMethod()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.Overloaded), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Calls", result.Output);
        Assert.Contains("`System.Console.WriteLine(string)`", result.Output);
    }

    [Fact]
    public async Task CallsSection_TsvUsesPlainNormalizedValues()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("il_offset\topcode\tcall_kind\tcallee\toperand_token\treturn_address", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Equal(2, CountOccurrences(result.Output, "System.Console.WriteLine(string)"));
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallsForSelectedMember()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice), discover: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Calls\tsection", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunMemberCallsAsync(string memberName, bool tsv = false, bool discover = false, int? overloadIndex = null)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallsFixture).FullName,
            AssemblyPath = typeof(MemberCallsFixture).Assembly.Location,
            MemberFilter = [memberName],
            OverloadIndex = overloadIndex,
            IncludeSections = [SectionNames.Calls],
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Minimal,
            Tabular = tsv || discover,
            Tsv = tsv || discover,
            TabularExplicitlySet = tsv || discover,
            FormatExplicitlySet = true,
        }));

    static Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });

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

public static class MemberCallsFixture
{
    public static void CallsWriteLineTwice()
    {
        Console.WriteLine("first");
        Console.WriteLine("second");
    }

    public static int CallsInterfaceItem(IList<int> values) => values[0];

    public static async Task
        CallsWriteLineAfterYield()
    {
        await Task.Yield();
        Console.WriteLine("async");
    }

    public static void Overloaded(int value)
    {
    }

    public static void Overloaded(string value)
    {
        Console.WriteLine(value);
    }

    // Non-public member: only selectable under --all. Regression coverage for #1323,
    // where the body-load path counted overloads public-only and so reported "no IL body"
    // for a method that the Calls/IL index reads fine.
    internal static int InternalHelper(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}
