using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Cache;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class RouterDomainSelectionTests
{
    public RouterDomainSelectionTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData("System.Text.Json", "Platform")]
    [InlineData("System.Text.Json.JsonSerializer", "Platform")]
    [InlineData("System.CommandLine", "Platform")]
    [InlineData("Newtonsoft.Json", "Package")]
    [InlineData("List<T>", "Package")]
    [InlineData("string.ctor", "Package")]
    [InlineData("not/a/nuget/id", "Package")]
    public void ClassificationDoesNotRequireSourceAcquisition(
        string target, string domain)
    {
        Assert.Equal(domain, RouterTargetAnalysis.Classify(target).ToString());
    }

    [Theory]
    [InlineData("System.CommandLine", "package")]
    [InlineData("System.Text.Json@10.0.0", "package")]
    [InlineData("Newtonsoft.Json@13.0.4", "package")]
    public async Task PackageDomainAndOverridesBindBeforeLookup(
        string target, string expectedCommand)
    {
        using var decisions = RouterDecisionLog.Begin();
        var result = await Invoke(
            target.Contains('@')
                ? [target, "--help"]
                : [target, "--package", "--help"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            decisions.Decisions,
            decision => decision.Stage == "router-rewrite"
                && decision.Detail.Contains($"-> {expectedCommand} ", StringComparison.Ordinal));
        Assert.DoesNotContain("Routing to platform", result.Error);
    }

    [Fact]
    public async Task ConflictingOverridesFailWithoutRouting()
    {
        using var decisions = RouterDecisionLog.Begin();
        var result = await Invoke(
            "System.Text.Json", "--platform", "--package");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Choose only one", result.Error);
        Assert.DoesNotContain(
            decisions.Decisions, decision => decision.Stage == "router-rewrite");
    }

    [Theory]
    [InlineData("--platform", "--platform")]
    [InlineData("--platform", "--platform=System.Runtime")]
    [InlineData("--package", "--package")]
    public async Task DuplicateSourceIntentFailsBeforeRouting(string first, string second)
    {
        using var decisions = RouterDecisionLog.Begin();
        var result = await Invoke("System.String", first, second);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unambiguous", result.Error);
        Assert.DoesNotContain(
            decisions.Decisions, decision => decision.Stage == "router-rewrite");
    }

    [Fact]
    public async Task PackageCoordinateCannotBeOverriddenToPlatform()
    {
        var result = await Invoke("System.Text.Json@10.0.0", "--platform");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unambiguous", result.Error);
    }

    [Theory]
    [InlineData("System.String", "runtime@10.0.10", "System.String")]
    [InlineData("System.AppDomain.FriendlyName", "runtime@3.1.0", "System.AppDomain")]
    public async Task SelectedFrameworkAnalysisRetainsTypeAndMemberIdentity(
        string target, string framework, string type)
    {
        var (analyzed, error) = await RouterTargetAnalysis.ResolveFrameworkAsync(
            target, framework, new NuGetSourceOptions(),
            TestContext.Current.CancellationToken);
        Assert.Null(error);
        Assert.NotNull(analyzed);
        Assert.Equal(framework, analyzed.Framework);
        Assert.Equal(type, analyzed.TypeName);
        Assert.Equal(
            target == type ? null : "FriendlyName",
            analyzed.Member?.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedFrameworkMissDoesNotUseNewerRuntime(bool explicitMember)
    {
        string[] command = explicitMember
            ? ["member", "System.DateOnly.DayNumber"]
            : ["System.DateOnly"];
        var result = await Invoke(
            [.. command, "--framework", "runtime@3.1.0", "--markdown"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("runtime@3.1.0", result.Error);
        Assert.DoesNotContain("11.0", result.Error);
    }

    [Theory]
    [InlineData("--package", "System.Text.Json")]
    [InlineData("--platform=System.Text.Json", null)]
    public async Task ValuedSourceSelectorRemainsATypeSource(
        string option, string? value)
    {
        using var decisions = RouterDecisionLog.Begin();
        string[] tokens = value is null
            ? ["JsonSerializer", option, "--help"]
            : ["JsonSerializer", option, value, "--help"];
        var result = await Invoke(tokens);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            decisions.Decisions,
            decision => decision.Stage == "router-rewrite"
                && decision.Detail.Contains("-> type JsonSerializer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlatformMissDoesNotBecomePackageOrSecondSource()
    {
        using var decisions = RouterDecisionLog.Begin();
        var result = await Invoke("System.Text.Json.NotARealType.OrMember");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("selected Platform domain", result.Error);
        Assert.DoesNotContain(
            decisions.Decisions, decision => decision.Stage == "router-rewrite");
    }

    [Fact]
    public async Task RouterAndExplicitMemberUseSameResolvedTypeAndSelector()
    {
        string target = "System.Text.Json.JsonSerializer.DeserializeAsync";
        var (_, route) = await RouterTargetAnalysis.ResolvePlatformAsync(
            target,
            new NuGetSourceOptions(),
            TestContext.Current.CancellationToken);
        var analysis = RouterResolvedTarget.From(
            Assert.IsType<PlatformTypeCatalogRouteOutcome.Resolved>(
                route!.Content));
        var routed = await Invoke(target, "-S", "Methods", "--markdown");
        var explicitMember = await Invoke(
            "member", analysis.TypeName,
            "--platform", analysis.Assembly.Name,
            "--framework", analysis.Framework,
            "-m", analysis.Member!.RequestedText,
            "-S", "Methods", "--markdown");
        Assert.Equal(0, routed.ExitCode);
        Assert.Equal(0, explicitMember.ExitCode);
        Assert.Equal(explicitMember.Output, routed.Output);
        Assert.Contains("| DeserializeAsync |", routed.Output);

        var directFqn = await Invoke(
            "member", target, "-S", "Overloads", "--count");
        Assert.Equal(0, directFqn.ExitCode);
        Assert.Equal("10", directFqn.Output.Trim());
    }

    [Fact]
    public async Task TypedInvocationDoesNotReinferTheOriginalQualifiedSpelling()
    {
        const string target =
            "System.Text.Json.JsonSerializer.DeserializeAsync";
        var (_, route) = await RouterTargetAnalysis.ResolvePlatformAsync(
            target,
            new NuGetSourceOptions(),
            TestContext.Current.CancellationToken);
        RouterResolvedTarget analysis = RouterResolvedTarget.From(
            Assert.IsType<PlatformTypeCatalogRouteOutcome.Resolved>(
                route!.Content));
        string[] rewritten =
        [
            "member", "Not.A.Real.Type.Or.Member",
            "--platform", analysis.Assembly.Name,
            "--framework", analysis.Framework,
            "-S", "Overloads", "--count",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        var result = await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeWithLineWindowAsync(
                root.Parse(rewritten), rewritten, analysis));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("10", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>.Add", "1")]
    [InlineData("System.Collections.Generic.Dictionary<TKey,TValue>.KeyCollection.CopyTo", "1")]
    public async Task GenericAndNestedMemberSelectionUsesResolvedBoundary(
        string target, string expectedCount)
    {
        var result = await Invoke(target, "-S", "Overloads", "--count");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expectedCount, result.Output.Trim());
    }

    [Fact]
    public async Task ExplicitQualifiedGenericMemberUsesSharedBoundary()
    {
        var result = await Invoke(
            "member", "System.Collections.Generic.List<T>.Add",
            "-S", "Overloads", "--count");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
    }

    [Theory]
    [InlineData("System.String..ctor:1", "\"name\": \"String\"")]
    [InlineData("System.Text.Json.JsonSerializer.DeserializeAsync<T>:1", "\"name\": \"JsonSerializer\"")]
    public async Task SpecialAndGenericAritySelectorsRetainMemberKind(
        string target, string typeName)
    {
        var result = await Invoke(target, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(typeName, result.Output);
    }

    private static async Task<(int ExitCode, string Output, string Error)> Invoke(
        params string[] arguments)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] prepared =
            CommandLineBuilder.PreprocessArgs(arguments, root);
        return await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(
                root.Parse(prepared), prepared));
    }
}
