using System.CommandLine;
using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class MatchCommandTests
{
    [Fact]
    public async Task ExecuteAsync_UnrelatedMethods_ReportsDifferentRelation()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"relation\": \"Different\"", output);
    }

    [Fact]
    public async Task ExecuteAsync_StructurallyIdenticalMethods_ReportsExactRelation()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = $"{typeof(MatchSampleB).FullName}.AddOneToo",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"relation\": \"Exact\"", output);
    }

    [Fact]
    public async Task ExecuteAsync_MissingSelector_FailsWithoutRunning()
    {
        var options = new MatchOptions
        {
            LeftSelector = "",
            RightSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("match requires two method selectors", error);
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousOverloadSelector_ReportsDisambiguationError()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Overloaded",
            RightSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("matches", error);
        Assert.Contains("overloads", error);
    }

    [Fact]
    public async Task ExecuteAsync_PropertyWithGetterAndSetter_RejectsRatherThanSilentlySelectingGetter()
    {
        // A property with both accessors carries two addressable MethodDef tokens; silently
        // preferring the getter would compare a different body than the caller may have meant
        // without saying so (issue #4304 Slice 3 review).
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.ReadWriteProperty",
            RightSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("more than one addressable accessor", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_GetOnlyProperty_ResolvesToGetterBody(bool includeBody)
    {
        // A get-only property has exactly one addressable accessor, so it resolves
        // unambiguously to that getter's body (the real-world demo case: Aspire's
        // StringComparer-returning properties).
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.ReadOnlyProperty",
            RightSelector = $"{typeof(MatchSampleB).FullName}.ReadOnlyPropertyToo",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            JsonOutput = true,
            IncludeBody = includeBody,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"relation\": \"Exact\"", output);
    }

    [Fact]
    public async Task ExecuteAsync_BodyOnDifferentBodies_RendersCSharpAndIlEvidence()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            RightSelector = $"{typeof(MatchSampleA).FullName}.GreetFormal",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeBody = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Method Body Diff", output);
        Assert.Contains("C#", output);
        Assert.Contains("IL", output);
        Assert.Contains("Good day", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ExecuteAsync_BodyOnIdenticalBodies_RetainsNativeExactResults()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = $"{typeof(MatchSampleB).FullName}.AddOneToo",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeBody = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Exact", output);
        Assert.Contains("Method Body Diff", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ExecuteAsync_BodyJson_EmitsMatchAndBodyEnvelope()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            RightSelector = $"{typeof(MatchSampleA).FullName}.GreetFormal",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeBody = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"match\": {", output);
        Assert.Contains("\"body\": {", output);
        Assert.DoesNotContain("\"implementation\":", output);
        Assert.Contains("\"disposition\": \"Completed\"", output);
        using var document = JsonDocument.Parse(output);
        var match = document.RootElement.GetProperty("match");
        var body = document.RootElement.GetProperty("body");
        Assert.False(body.GetProperty("has_failures").GetBoolean());
        var producers = body.GetProperty("producers").EnumerateArray().ToArray();
        Assert.Equal(2, producers.Length);
        foreach (var producer in producers)
        {
            Assert.Equal("DesignatedPair", producer.GetProperty("basis").GetString());
            Assert.Equal(match.GetProperty("left_token").GetInt32(),
                producer.GetProperty("before").GetProperty("address").GetProperty("token").GetInt32());
            Assert.Equal(match.GetProperty("right_token").GetInt32(),
                producer.GetProperty("after").GetProperty("address").GetProperty("token").GetInt32());
            Assert.Equal(match.GetProperty("left").GetProperty("module_version_id").GetString(),
                producer.GetProperty("before").GetProperty("address").GetProperty("module_version_id").GetString());
        }
        var csharp = Assert.Single(producers,
            producer => producer.GetProperty("producer").GetString() == "CSharp");
        Assert.NotEmpty(csharp.GetProperty("c_sharp").GetProperty("rows").EnumerateArray());
        Assert.Contains(".Greet(", csharp.GetProperty("before").GetProperty("c_sharp_subject").GetProperty("display").GetString());
        Assert.Contains(".GreetFormal(", csharp.GetProperty("after").GetProperty("c_sharp_subject").GetProperty("display").GetString());
        var il = Assert.Single(producers,
            producer => producer.GetProperty("producer").GetString() == "IlBody");
        Assert.Equal("OperandDiff", il.GetProperty("il").GetProperty("outcome").GetString());
        Assert.NotEmpty(il.GetProperty("il").GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task ExecuteAsync_DefaultJson_IsUnaffectedByBodyFlagAbsence()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("\"match\": {", output);
        Assert.DoesNotContain("\"implementation\": {", output);
        Assert.DoesNotContain("\"body\": {", output);
        Assert.Contains("\"disposition\":", output);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ExecuteAsync_BodyWithTabularFormat_RejectsCombination(bool tabular, bool tsv, bool jsonl)
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            RightSelector = $"{typeof(MatchSampleA).FullName}.GreetFormal",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeBody = true,
            Tabular = tabular,
            Tsv = tsv,
            Jsonl = jsonl,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--body cannot be combined with", error);
    }

    [Fact]
    public async Task BodyFlag_ProductEntryPoint_UsesCurrentSpelling()
    {
        string[] args =
        [
            "match",
            $"{typeof(MatchSampleA).FullName}.Greet",
            $"{typeof(MatchSampleA).FullName}.GreetFormal",
            "--library", typeof(MatchCommandTests).Assembly.Location,
            "--body", "--json", "--compact", "--all",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(() => root.Parse(args).InvokeAsync());

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("match", out _));
        Assert.True(document.RootElement.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task ImplementationFlag_ProductEntryPoint_IsRemoved()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => root.Parse(
                ["match", "Left.Compute", "Right.Compute", "--implementation"])
                .InvokeAsync());

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--implementation", error);
        Assert.DoesNotContain("Method Body Diff", output);
    }

    [Fact]
    public async Task ExecuteAsync_SameMethodBody_RemainsAValidPair()
    {
        string selector = $"{typeof(MatchSampleA).FullName}.AddOne";
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(new MatchOptions
            {
                LeftSelector = selector,
                RightSelector = selector,
                AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
                IncludeAll = true,
                IncludeBody = true,
                JsonOutput = true,
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("Exact",
            document.RootElement.GetProperty("match").GetProperty("relation").GetString());
        var body = document.RootElement.GetProperty("body");
        Assert.False(body.GetProperty("has_failures").GetBoolean());
        Assert.All(body.GetProperty("producers").EnumerateArray(),
            producer => Assert.Equal("Exact", producer.GetProperty("native_verdict").GetString()));
    }

    [Fact]
    public async Task ExecuteAsync_RawPrivateMethodToken_PreservesTheSelectedBody()
    {
        int token = typeof(MatchPrivateBodySample).GetMethod(
            "HiddenBody",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MetadataToken;
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(new MatchOptions
            {
                LeftSelector = $"0x{token:X8}",
                RightSelector = $"{typeof(MatchSampleB).FullName}.AddOneToo",
                AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
                IncludeBody = true,
                JsonOutput = true,
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.All(document.RootElement.GetProperty("body").GetProperty("producers").EnumerateArray(),
            producer => Assert.Equal(token,
                producer.GetProperty("before").GetProperty("address").GetProperty("token").GetInt32()));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ExecuteAsync_CompilerGeneratedSeedAndDiscoveredToken_PreserveBodyEvidence()
    {
        Func<int> seed = static () => 42;
        string assemblyPath = typeof(MatchCommandTests).Assembly.Location;
        int token = seed.Method.MetadataToken;
        var discovery = new MatchOptions
        {
            LeftSelector = $"0x{token:X8}",
            AssemblyPath = assemblyPath,
            Similar = true,
            AssemblyWide = true,
            MaximumResults = 1,
            JsonOutput = true,
        };
        var (discoveryExit, discoveryOutput, discoveryError) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(discovery));
        Assert.Equal(0, discoveryExit);
        Assert.Empty(discoveryError);
        using var discovered = JsonDocument.Parse(discoveryOutput);
        string peer = Assert.Single(discovered.RootElement.GetProperty("candidates").EnumerateArray())
            .GetProperty("token").GetString()!;

        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(new MatchOptions
            {
                LeftSelector = discovery.LeftSelector,
                RightSelector = peer,
                AssemblyPath = assemblyPath,
                IncludeBody = true,
                JsonOutput = true,
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement body = document.RootElement.GetProperty("body");
        Assert.Equal("Research", body.GetProperty("stage").GetString());
        Assert.False(body.GetProperty("has_failures").GetBoolean());
        Assert.Equal(2, body.GetProperty("producers").GetArrayLength());
        Assert.All(body.GetProperty("producers").EnumerateArray(), producer =>
        {
            Assert.Equal(token,
                producer.GetProperty("before").GetProperty("address").GetProperty("token").GetInt32());
            Assert.Equal(int.Parse(peer.AsSpan(2), System.Globalization.NumberStyles.HexNumber),
                producer.GetProperty("after").GetProperty("address").GetProperty("token").GetInt32());
        });
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ExecuteAsync_BodylessPair_DoesNotReportEqualBodies()
    {
        string selector = $"{typeof(MatchSampleWithoutBody).FullName}.GetValue";
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(new MatchOptions
            {
                LeftSelector = selector,
                RightSelector = selector,
                AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
                IncludeAll = true,
                IncludeBody = true,
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("NoApplicableInput", output);
        Assert.DoesNotContain("No implementation differences detected", output);
    }

    [Fact]
    public async Task ExecuteAsync_BodylessJson_KeepsAvailabilitySeparateFromFindingsExactness()
    {
        string selector = $"{typeof(MatchSampleWithoutBody).FullName}.GetValue";
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(new MatchOptions
            {
                LeftSelector = selector,
                RightSelector = selector,
                AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
                IncludeAll = true,
                IncludeBody = true,
                JsonOutput = true,
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var body = document.RootElement.GetProperty("body");
        Assert.False(body.GetProperty("has_failures").GetBoolean());
        Assert.All(body.GetProperty("producers").EnumerateArray(), producer =>
        {
            Assert.Equal("NotApplicable", producer.GetProperty("native_verdict").GetString());
            Assert.Equal("NoApplicableInput", producer.GetProperty("before").GetProperty("state").GetString());
            Assert.Equal("NoApplicableInput", producer.GetProperty("after").GetProperty("state").GetString());
            Assert.True(producer.GetProperty("findings").GetProperty("is_exact").GetBoolean());
            Assert.False(producer.TryGetProperty("c_sharp", out _));
            Assert.False(producer.TryGetProperty("il", out _));
        });
    }

    [Fact]
    public async Task ExecuteAsync_BodyCancellation_PreservesStructuralResultAndFails()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => MatchCommand.ExecuteAsync(new MatchOptions
            {
                LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
                RightSelector = $"{typeof(MatchSampleB).FullName}.AddOneToo",
                AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
                IncludeAll = true,
                IncludeBody = true,
                JsonOutput = true,
            }, cancellation.Token));

        Assert.NotEqual(0, exitCode);
        Assert.NotEmpty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("Exact",
            document.RootElement.GetProperty("match").GetProperty("relation").GetString());
        var body = document.RootElement.GetProperty("body");
        Assert.Equal("Query", body.GetProperty("stage").GetString());
        Assert.Equal("Cancelled", body.GetProperty("outcome").GetString());
        Assert.True(body.GetProperty("has_failures").GetBoolean());
        Assert.Empty(body.GetProperty("producers").EnumerateArray());
    }

    [Fact]
    public void BodyFlag_HelpNamesBodyEvidence_NotASecondStructuralComparison()
    {
        var match = CommandLineBuilder.CreateRootCommand().Subcommands.Single(
            command => command.Name == "match");
        var option = Assert.Single(match.Options, option => option.Name == "--body");

        Assert.Equal(
            "Show decompiled C# and IL body differences alongside the structural-match result",
            option.Description);
        Assert.DoesNotContain(match.Options, option => option.Name == "--implementation");
    }
}

public static class MatchSampleA
{
    public static int AddOne(int x) => x + 1;

    public static string Greet(string name) => $"Hello, {name}!";

    public static string GreetFormal(string name) => $"Good day, {name}.";

    public static int Overloaded(int x) => x;

    public static int Overloaded(int x, int y) => x + y;

    public static int ReadWriteProperty { get; set; }

    public static int ReadOnlyProperty => 42;
}

public static class MatchSampleB
{
    public static int AddOneToo(int x) => x + 1;

    public static int ReadOnlyPropertyToo => 42;
}

public abstract class MatchSampleWithoutBody
{
    public abstract int GetValue();
}

public static class MatchPrivateBodySample
{
    private static int HiddenBody(int x) => x + 1;
}
