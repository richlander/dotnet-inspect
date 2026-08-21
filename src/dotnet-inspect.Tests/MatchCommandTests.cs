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
}

public static class MatchSampleA
{
    public static int AddOne(int x) => x + 1;

    public static string Greet(string name) => $"Hello, {name}!";

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
