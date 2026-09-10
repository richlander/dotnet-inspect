// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using DotnetInspector.Commands;
using DotnetInspector.Options;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the <c>ecosystem</c> command's section ladder, category door, and operand bound.
/// </summary>
[Collection("Console")]
public class EcosystemCommandTests
{
    static Task<(int Exit, string Output, string Error)> RunAsync(EcosystemOptions options) =>
        ConsoleCapture.RunAsync(() => Task.FromResult(EcosystemCommand.Execute(options)));

    [Fact]
    public async Task DefaultViewIsTheRegistryIndexAlone()
    {
        (int exit, string text, _) = await RunAsync(new EcosystemOptions());

        Assert.Equal(0, exit);
        Assert.Contains("## Ecosystems", text, StringComparison.Ordinal);
        Assert.Contains("ecosystem.platform", text, StringComparison.Ordinal);

        // Pruning is explicit-only, so it never joins an automatic view.
        Assert.DoesNotContain("## Pruning", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingPruningRendersTheSubsumedIdentities()
    {
        (int exit, string text, _) = await RunAsync(new EcosystemOptions { Select = ["Pruning"] });

        Assert.Equal(0, exit);
        Assert.Contains("## Pruning", text, StringComparison.Ordinal);
        Assert.Contains("System.Text.Json", text, StringComparison.Ordinal);

        // Both populations render, and the column that distinguishes them is what explains a
        // result rather than restating it.
        Assert.Contains("Live", text, StringComparison.Ordinal);
        Assert.Contains("Frozen", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlatformCategoryDoorExpandsToItsMembers()
    {
        (int exit, string text, _) = await RunAsync(new EcosystemOptions { Select = ["@Platform"] });

        Assert.Equal(0, exit);
        Assert.Contains("## Pruning", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOperandNarrowsToOneEcosystem()
    {
        (int exit, string text, _) = await RunAsync(new EcosystemOptions { Ecosystem = "aspire" });

        Assert.Equal(0, exit);
        Assert.Contains("ecosystem.aspire", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ecosystem.platform", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownOperandFailsVisibly()
    {
        // A failure that reports success is worse than no answer: a caller checking the exit
        // code must not read "unknown ecosystem" as an empty registry.
        (int exit, _, string error) = await RunAsync(new EcosystemOptions { Ecosystem = "not-an-ecosystem" });

        Assert.Equal(1, exit);
        Assert.Contains("not-an-ecosystem", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABareViewReadsNoPlatformPack()
    {
        // Explicit-only keeps Pruning out of automatic views; this keeps it out of the work.
        // A bogus framework would surface as a stderr diagnostic if the pack were read.
        (int exit, _, string error) = await RunAsync(new EcosystemOptions { Framework = "not-a-framework" });

        Assert.Equal(0, exit);
        Assert.Empty(error);
    }
}
