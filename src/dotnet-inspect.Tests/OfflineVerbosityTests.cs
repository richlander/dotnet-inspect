using DotnetInspector.Packages;
using CoreFactory = DotnetInspector.Core.HttpClientFactory;

namespace DotnetInspector.Tests;

/// <summary>
/// Verifies that quiet and minimal verbosities are fully offline —
/// no network requests are made for type listings, single-type views,
/// or member views on platform assemblies. Tests pass --offline through
/// the same flow as Program.cs so any network attempt throws OfflineException.
/// </summary>
[Collection("Console")]
public class OfflineVerbosityTests : IDisposable
{
    public OfflineVerbosityTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    public void Dispose()
    {
        CoreFactory.Initialize(offline: false);
        CoreFactory.ResetSharedForTesting();
    }

    /// <summary>
    /// Mirrors the Program.cs entry point: strips --offline, initializes factory,
    /// preprocesses args, creates root command, and invokes.
    /// </summary>
    private static async Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
    {
        return await ConsoleCapture.RunAsync(async () =>
        {
            bool offline = args.Contains("--offline");
            if (offline)
                args = args.Where(a => a != "--offline").ToArray();

            CoreFactory.Initialize(offline);
            CoreFactory.ResetSharedForTesting();

            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await root.Parse(args).InvokeAsync();
        });
    }

    // ── type command: library listing ────────────────────────────────

    [Fact]
    public async Task TypeListing_Quiet_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-v:q", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task TypeListing_Minimal_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-v:m", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    // ── type command: single type ────────────────────────────────────

    [Fact]
    public async Task SingleType_Quiet_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "JsonSerializer", "--platform", "System.Text.Json", "-v:q", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("Methods", output);
    }

    [Fact]
    public async Task SingleType_Minimal_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "JsonSerializer", "--platform", "System.Text.Json", "-v:m", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        // Compact view: grouped by name with overload counts
        Assert.Contains("Overloads", output);
        // No documentation
        Assert.DoesNotContain("Reads the UTF-8", output);
    }

    // ── member command ───────────────────────────────────────────────

    [Fact]
    public async Task Member_Quiet_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-v:q", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Member_Minimal_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-v:m", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        // Minimal now shows full signatures with docs
        Assert.Contains("Signature", output);
    }

    // ── router -> qualified type name ─────────────────────────────────

    [Fact]
    public async Task Router_QualifiedType_Quiet_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "System.Text.Json.JsonSerializer", "-v:q", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Router_QualifiedType_Minimal_Offline_Succeeds()
    {
        var (exit, output, _) = await RunAppAsync(
            "System.Text.Json.JsonSerializer", "-v:m", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Overloads", output);
    }

    // ── type forwarder following ─────────────────────────────────────

    [Fact]
    public async Task TypeListing_FollowsForwarders_Offline()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Collections", "-v:q", "--offline");

        Assert.Equal(0, exit);
        // Native types (e.g., SortedSet) + forwarded types (e.g., HashSet)
        Assert.Contains("System.Collections", output);
    }

    [Fact]
    public async Task TypeListing_ForwardedTypeMatchesFilter_Offline()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Collections", "-t", "HashSet*", "-v:q", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("Types: 1", output);
    }

    [Fact]
    public async Task TypeListing_NativeAndForwardedTypes_Offline()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Collections", "-v:m", "--offline");

        Assert.Equal(0, exit);
        // Forwarded type
        Assert.Contains("HashSet", output);
        // Native type
        Assert.Contains("SortedSet", output);
    }

    [Fact]
    public async Task SingleType_ForwardedType_Offline()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "HashSet", "--platform", "System.Collections", "-v:q", "--offline");

        Assert.Equal(0, exit);
        Assert.Contains("HashSet", output);
    }
}
