using DotnetInspector.Commands;
using DotnetInspector.Options;
using ILInspector.Metadata;

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

    [Fact]
    public async Task ExecuteAsync_GetOnlyProperty_ResolvesToGetterBody()
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
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"relation\": \"Exact\"", output);
    }

    [Fact]
    public async Task ExecuteAsync_ImplementationOnDifferentBodies_RendersCSharpAndIlEvidence()
    {
        // Issue #4304 Slice 4: --implementation independently decompiles both selectors and
        // renders a Research-owned side-by-side C#/IL implementation-diff view, even though
        // these two methods are not structurally clone-related (a genuine content diff, not an
        // identity assumption).
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            RightSelector = $"{typeof(MatchSampleA).FullName}.GreetFormal",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeImplementation = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("# Implementation Diff:", output);
        Assert.Contains("| Mechanism | Difference | Change | Evidence |", output);
        Assert.Contains("Good day", output);
    }

    [Fact]
    public async Task ExecuteAsync_ImplementationOnIdenticalBodies_ReportsNoDifferences()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.AddOne",
            RightSelector = $"{typeof(MatchSampleB).FullName}.AddOneToo",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeImplementation = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("No implementation differences detected.", output);
    }

    [Fact]
    public async Task ExecuteAsync_ImplementationJson_EmitsMatchAndImplementationEnvelope()
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            RightSelector = $"{typeof(MatchSampleA).FullName}.GreetFormal",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeImplementation = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("\"match\": {", output);
        Assert.Contains("\"implementation\": {", output);
        Assert.Contains("\"disposition\": \"Completed\"", output);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultJson_IsUnaffectedByImplementationFlagAbsence()
    {
        // The default (non --implementation) --json output must stay byte-identical to what
        // Slice 3 shipped: the flat StructuralCloneComparisonDocument, no wrapping envelope.
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
        Assert.Contains("\"disposition\":", output);
    }

    [Fact]
    public void SelectorsShareAcquisition_UsesRegistrationInsteadOfDisplayPath()
    {
        string path = typeof(MatchCommandTests).Assembly.Location;
        ResolvedAssemblyReference retained =
            TestAssemblyReferences.Designated(path);
        ResolvedAssemblyReference sameAcquisition =
            retained.WithoutLocalPath();
        ResolvedAssemblyReference separateAcquisition =
            TestAssemblyReferences.Designated(path).WithoutLocalPath();
        var left = new MatchCommand.ResolvedSelector(
            Token: 1,
            Display: "left",
            OriginAssemblyPath: "/same-display.dll",
            OriginAssembly: retained,
            Error: null);

        Assert.True(
            MatchCommand.SelectorsShareAcquisition(
                left,
                left with
                {
                    OriginAssembly = sameAcquisition,
                }));
        Assert.False(
            MatchCommand.SelectorsShareAcquisition(
                left,
                left with
                {
                    OriginAssembly = separateAcquisition,
                }));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ExecuteAsync_ImplementationWithTabularFormat_RejectsCombination(bool tabular, bool tsv, bool jsonl)
    {
        var options = new MatchOptions
        {
            LeftSelector = $"{typeof(MatchSampleA).FullName}.Greet",
            RightSelector = $"{typeof(MatchSampleA).FullName}.GreetFormal",
            AssemblyPath = typeof(MatchCommandTests).Assembly.Location,
            IncludeAll = true,
            IncludeImplementation = true,
            Tabular = tabular,
            Tsv = tsv,
            Jsonl = jsonl,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => MatchCommand.ExecuteAsync(options));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--implementation cannot be combined with", error);
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
