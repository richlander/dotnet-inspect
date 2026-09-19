using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

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
    public async Task CallersSection_SemanticTailSelectsTheSameCallSiteAcrossFormats()
    {
        string[] args =
        [
            "member",
            typeof(MemberCallersFixture).FullName!,
            "--library",
            typeof(MemberCallersFixture).Assembly.Location,
            "-m",
            nameof(MemberCallersFixture.Target),
            "-S",
            SectionNames.Callers,
            "-n",
            "1",
            "--tail",
            "--tips",
            "q",
        ];

        var markdown = await RunCliAsync(args);
        var table = await RunCliAsync([.. args, "--table"]);
        var tsv = await RunCliAsync(
            [.. args, "--tsv", "--no-headers"]);
        var jsonl = await RunCliAsync([.. args, "--jsonl"]);
        var json = await RunCliAsync([.. args, "--json"]);
        var count = await RunCliAsync([.. args, "--count"]);
        var jsonCount = await RunCliAsync(
            [.. args, "--count", "--json"]);

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
            Assert.Contains(
                nameof(MemberCallersFixture.CallsTargetTwice),
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                nameof(MemberCallersFixture.CallsTargetOnce),
                result.Output,
                StringComparison.Ordinal);
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
        JsonElement caller = Assert.Single(
            document.RootElement
                .GetProperty("callers")
                .EnumerateArray());
        Assert.EndsWith(
            $"{nameof(MemberCallersFixture.CallsTargetTwice)}()",
            caller.GetProperty("caller").GetString(),
            StringComparison.Ordinal);
        Assert.Equal("1", count.Output.Trim());
        Assert.Equal("1", jsonCount.Output.Trim());
    }

    [Fact]
    public async Task CallersSection_ScansAuthorizedScopesBeforeSemanticSelection()
    {
        string scopeDirectory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-callers-").FullName;
        try
        {
            string testAssembly = typeof(MemberCallersSectionTests)
                .Assembly.Location;
            string testAssemblyName =
                Path.GetFileNameWithoutExtension(testAssembly);
            File.Copy(
                testAssembly,
                Path.Combine(
                    scopeDirectory,
                    Path.GetFileName(testAssembly)));

            var result = await RunCliAsync(
                "member",
                typeof(MemberCommand).FullName!,
                "--library",
                typeof(MemberCommand).Assembly.Location,
                "-m",
                $"{nameof(MemberCommand.ExecuteAsync)}:1",
                "-S",
                SectionNames.Callers,
                "--bin",
                scopeDirectory,
                "-n",
                "1",
                "--json",
                "--tips",
                "q");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var document = JsonDocument.Parse(result.Output);
            JsonElement caller = Assert.Single(
                document.RootElement
                    .GetProperty("callers")
                    .EnumerateArray());
            Assert.Equal(
                testAssemblyName,
                caller.GetProperty("source").GetString());
            Assert.StartsWith(
                $"{testAssemblyName}.",
                caller.GetProperty("caller").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(scopeDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CallersSection_UnavailableWindowWithholdsOutput()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallersFixture).FullName!,
            "--library",
            typeof(MemberCallersFixture).Assembly.Location,
            "-m",
            nameof(MemberCallersFixture.Target),
            "-S",
            SectionNames.Callers,
            "--rows",
            "4..4",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires caller row 4, but only 3 caller rows are available",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallersSection_ExplicitLinesRejectJsonBeforeAcquisition()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"missing-callers-{Guid.NewGuid():N}.dll");
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallersFixture).FullName!,
            "--library",
            missingAssembly,
            "-m",
            nameof(MemberCallersFixture.Target),
            "-S",
            SectionNames.Callers,
            "-n",
            "1",
            "--lines",
            "--json",
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
    [InlineData("Callers,Calls")]
    [InlineData("@Calls")]
    public async Task CallersSection_MultiSectionSelectionRetainsRenderedLineFallback(
        string selection)
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"missing-callers-{Guid.NewGuid():N}.dll");
        var result = await RunCliAsync(
            "member",
            typeof(MemberCallersFixture).FullName!,
            "--library",
            missingAssembly,
            "-m",
            nameof(MemberCallersFixture.Target),
            "-S",
            selection,
            "-n",
            "1",
            "--json",
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
    [Trait("Speed", "Slow")]
    public async Task CallersSection_KeepsVirtualCallvirtAsDeclaredTarget()
    {
        var result = await RunMemberCallersAsync(typeof(CallersBase).FullName!, nameof(CallersBase.Speak));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"{nameof(MemberCallersFixture.InvokesSpeak)}(", result.Output);
        Assert.Contains("| callvirt | virtual |", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CallersSection_TsvUsesPlainNormalizedValues()
    {
        var result = await RunMemberCallersAsync(typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("caller\til_offset\topcode\tcall_kind\toperand_token\treturn_address", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Contains($"{nameof(MemberCallersFixture.CallsTargetOnce)}()", result.Output);
    }

    [Fact]
    public async Task
        CallersSection_PreservesDistinctGeneratedEvidenceBodies()
    {
        var result = await RunMemberCallersAsync(
            typeof(MemberCallersFixture).FullName!,
            nameof(MemberCallersFixture.GeneratedTarget));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Evidence Method", result.Output);
        Assert.Equal(
            2,
            CountOccurrences(
                result.Output,
                ">b__"));
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
        // by their structural CanRender gate, not a content probe, so the @Calls sections still
        // appear in effective discovery without opening the whole-assembly index.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallersFixture).FullName!,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallersFixture.Orphan)],
            OverloadIndex = 1,
            TipLevel = TipLevel.Quiet,
            Discover = [SectionCategoryNames.Calls],
            Verbosity = Verbosity.Normal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Callers\tsection", result.Output);
        Assert.Contains("Calls\tsection", result.Output);
        Assert.DoesNotContain("(opt-in)", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
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
    [Trait("Speed", "Slow")]
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
    [Trait("Speed", "Slow")]
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
    [Trait("Speed", "Slow")]
    public async Task CallGraph_CrossAssembly_IncorporatesExternalCallersTaggedWithSource()
    {
        // Target a product member (in dotnet-inspect.dll) and scope the test bin directory,
        // which contains the test assembly that calls it. The external caller is incorporated
        // into the bidirectional graph and annotated with its source assembly (#1337).
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
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Call Graph" },
            CallerScopeDirectories = [scopeDir],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains($"from {testAssemblyName}", result.Output);
    }

    [Fact]
    public async Task CallGraph_NoScope_OmitsSourceAnnotation()
    {
        // Without a caller scope, the caller half stays single-assembly and no external
        // source annotation appears.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallersFixture).FullName!,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallersFixture.Target)],
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Call Graph" },
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
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

    public static void GeneratedTarget()
    {
    }

    public static void CallsTargetOnce() => Target();

    public static void CallsTargetTwice()
    {
        Target();
        Target();
    }

    public static void CallsTargetFromTwoLambdas()
    {
        Action first = static () => GeneratedTarget();
        Action second = static () => GeneratedTarget();
        first();
        second();
    }

    public static void InvokesSpeak(CallersBase thing) => thing.Speak();

    public static void Orphan()
    {
    }
}
