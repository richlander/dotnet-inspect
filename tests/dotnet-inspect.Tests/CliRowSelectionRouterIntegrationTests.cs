using System.Collections.Concurrent;
using System.CommandLine;

using DotnetInspector.CommandLine;
using DotnetInspector.Core;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class CliRowSelectionRouterIntegrationTests
{
    [Fact]
    public async Task InvalidImplicitPackageSelectionFailsBeforeRouterRewrite()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "System.CommandLine",
                "--versions",
                "-n",
                "0");

        Assert.Equal(1, invocation.ExitCode);
        Assert.Empty(invocation.Output);
        Assert.Equal(
            "Error: -n requires a positive whole number.",
            invocation.Error.Trim());
        Assert.Contains(
            invocation.Observations,
            observation =>
                observation.Stage == "router-row-selection"
                && observation.Detail == "LoweringFailure");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    [Fact]
    public async Task ExplicitPackageNeighborUsesSameDiagnostic()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "package",
                "System.CommandLine",
                "--versions",
                "-n",
                "0");

        Assert.Equal(1, invocation.ExitCode);
        Assert.Empty(invocation.Output);
        Assert.Equal(
            "Error: -n requires a positive whole number.",
            invocation.Error.Trim());
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-hit");
    }

    [Fact]
    public async Task CommonZeroArityDiagnosticPrecedesEnvelopeLowering()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "System.CommandLine",
                "--versions",
                "--rows",
                "2..3",
                "--tail",
                "false");

        Assert.Equal(1, invocation.ExitCode);
        Assert.Empty(invocation.Output);
        Assert.Equal(
            "Error: --tail does not accept a value.",
            invocation.Error.Trim());
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-row-selection");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    [Fact]
    public async Task CompatibilityDiagnosticPrecedesCommonZeroArityDiagnostic()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "System.CommandLine",
                "--versions",
                "--tail-lines",
                "false",
                "-n",
                "2",
                "--json",
                "--offline");

        Assert.Equal(1, invocation.ExitCode);
        Assert.Empty(invocation.Output);
        Assert.Equal(
            "Error: --lines and --tail-lines cannot be combined with JSON "
                + "output; use semantic -n to select complete JSON rows.",
            invocation.Error.Trim());
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-row-selection");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    [Fact]
    public async Task UniformlyUnsupportedRequestFailsBeforeRouterRewrite()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "NoSuchRouteTarget",
                "-n",
                "2");

        Assert.Equal(1, invocation.ExitCode);
        Assert.Empty(invocation.Output);
        Assert.Equal(
            "Error: -n is not available for this command.",
            invocation.Error.Trim());
        Assert.Contains(
            invocation.Observations,
            observation =>
                observation.Stage == "router-row-selection"
                && observation.Detail == "UnsupportedCapability");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    [Fact]
    public async Task AllLibrariesUnsupportedRequestFailsBeforeStructuralRoute()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "NoSuchRouteTarget",
                "--all-libraries",
                "-n",
                "2",
                "--offline");

        Assert.Equal(1, invocation.ExitCode);
        Assert.Empty(invocation.Output);
        Assert.Equal(
            "Error: -n is not available for this command.",
            invocation.Error.Trim());
        Assert.Contains(
            invocation.Observations,
            observation =>
                observation.Stage == "router-row-selection"
                && observation.Detail == "UnsupportedCapability");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-structural");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    [Fact]
    public async Task AllLibrariesWithoutRowRequestUsesStructuralRoute()
    {
        RouteInvocation invocation =
            await InvokeAsync(
                "NoSuchRouteTarget",
                "--all-libraries",
                "--offline");

        Assert.Contains(
            invocation.Observations,
            observation =>
                observation.Stage == "router-row-selection"
                && observation.Detail == "NoRequest");
        Assert.Contains(
            invocation.Observations,
            observation => observation.Stage == "router-structural");
        Assert.DoesNotContain(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    [Fact]
    public async Task MixedRealDeclarationsRequireExplicitCommand()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        CliRowSelectionRouteEnvelopeResult result =
            CliRowSelectionRouterPreflight.Evaluate(
                ["Target", "--versions", "-n", "2"],
                ImplicitRouteCommands(root));

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.ExplicitCommandRequired,
            result.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            result.RequestKind);
        var captured =
            await ConsoleCapture.RunAsync(
                () => Task.FromResult(
                    CliRowSelectionRouterPreflight.TryWriteFailure(result)
                        ? 1
                        : 0));
        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Equal(
            "Error: -n is not uniform across implicit routes; "
                + "use an explicit command.",
            captured.Error.Trim());
    }

    [Fact]
    public void RequiredValueDisagreementAcrossRealCandidatesDefers()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        CliRowSelectionRouteEnvelopeResult result =
            CliRowSelectionRouterPreflight.Evaluate(
                [
                    "Target",
                    "--versions",
                    "--index",
                    "-5",
                ],
                ImplicitRouteCommands(root));

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            result.Outcome);
        Assert.Equal([3], result.DeferredPositions);
    }

    [Fact]
    public void DeferredTypeOrMemberRewritePreservesBothCandidates()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        IReadOnlyList<Command> candidates =
            RouterCommandDefinition.GetRowSelectionCandidates(
                [
                    "System.String.Length",
                    "--platform",
                    "System.Runtime",
                ],
                root,
                ImplicitRouteCommands(root));

        Assert.Equal(
            ["type", "member"],
            candidates.Select(command => command.Name));
    }

    [Theory]
    [InlineData(
        "System.Runtime",
        "NoRequest")]
    [InlineData(
        "System.CommandLine --versions -n 2",
        "Success")]
    public async Task NonFailingPreflightContinuesToAuthoritativeRouting(
        string invocationText,
        string expectedOutcome)
    {
        RouteInvocation invocation =
            await InvokeAsync(
                [.. invocationText.Split(' '), "--offline"]);

        Assert.Contains(
            invocation.Observations,
            observation =>
                observation.Stage == "router-row-selection"
                && observation.Detail == expectedOutcome);
        Assert.Contains(
            invocation.Observations,
            observation => observation.Stage == "router-rewrite");
    }

    private static IReadOnlyList<Command> ImplicitRouteCommands(
        RootCommand root) =>
        [
            root.Subcommands.Single(command => command.Name == "package"),
            root.Subcommands.Single(command => command.Name == "library"),
            root.Subcommands.Single(command => command.Name == "type"),
            root.Subcommands.Single(command => command.Name == "member"),
        ];

    private static async Task<RouteInvocation> InvokeAsync(
        params string[] arguments)
    {
        var observations =
            new ConcurrentQueue<BreadcrumbObservation>();
        using var subscription =
            BreadcrumbTelemetry.Subscribe(
                new BreadcrumbObserver(observations));
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        string[] processed =
            CommandLineBuilder.PreprocessArgs(arguments, root);
        var captured =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeAsync(
                    root.Parse(processed),
                    processed));
        return new(
            captured.ExitCode,
            captured.Output,
            captured.Error,
            [.. observations]);
    }

    private sealed record RouteInvocation(
        int ExitCode,
        string Output,
        string Error,
        IReadOnlyList<BreadcrumbObservation> Observations);

    private sealed class BreadcrumbObserver(
        ConcurrentQueue<BreadcrumbObservation> observations)
        : IObserver<BreadcrumbObservation>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(BreadcrumbObservation value) =>
            observations.Enqueue(value);
    }
}
