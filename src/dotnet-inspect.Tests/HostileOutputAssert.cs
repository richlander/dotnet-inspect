using DotnetInspector.CommandLine;
using DotnetInspector.Packages;
using CoreFactory = DotnetInspector.Core.HttpClientFactory;
using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// The single oracle for "this rendered output carries nothing that can escape
/// the structure it was placed in" (issue #3319).
/// </summary>
/// <remarks>
/// This deliberately does not call the product's own hazard predicate. A gate
/// that asks the code under test what counts as a hazard agrees with that code
/// by construction and can only ever confirm it, so the set is written out here
/// independently.
///
/// It exists as one method because it previously existed as five copies, and a
/// review found that all five omitted U+2028 and U+2029 — so three real leaks
/// through those characters rendered while every gate stayed green. Copies drift;
/// one oracle cannot.
/// </remarks>
internal static class HostileOutputAssert
{
    /// <summary>
    /// Whether <paramref name="c"/> must never appear in rendered output.
    /// </summary>
    /// <remarks>
    /// TAB, CR, and LF are permitted because they are the structure itself.
    /// That is exactly why <see cref="NoLineSplit"/> exists: permitting LF here
    /// means a hazard scan alone cannot see a value that forged a new row, so
    /// the two assertions are always used together on table and tree output.
    /// </remarks>
    public static bool IsForbidden(char c)
        => c is not '\t' and not '\n' and not '\r'
            && (char.IsControl(c)
                || c is '\u061C' or '\u200E' or '\u200F' or '\u2028' or '\u2029'
                    or >= '\u202A' and <= '\u202E'
                    or >= '\u2066' and <= '\u2069');

    public static void NoRenderingHazard(string output, string channel)
    {
        for (int i = 0; i < output.Length; i++)
        {
            if (IsForbidden(output[i]))
            {
                var context = output.Substring(Math.Max(0, i - 40), Math.Min(80, output.Length - Math.Max(0, i - 40)));
                Assert.Fail(
                    $"rendered {channel} output carries U+{(int)output[i]:X4} at index {i}: ...{context.Replace(output[i], '?')}...");
            }
        }
    }

    /// <summary>
    /// Asserts every marker actually rendered, so a clean hazard scan means the
    /// channel was contained rather than never exercised.
    /// </summary>
    public static void MarkersRendered(string output, string channel, params string[] markers)
    {
        foreach (var marker in markers)
        {
            Assert.True(
                output.Contains(marker, StringComparison.Ordinal),
                $"'{marker}' never rendered in {channel}, so this gate proves nothing about that channel");
        }
    }

    /// <summary>
    /// Asserts no marker was pushed onto a line of its own.
    /// </summary>
    /// <remarks>
    /// <see cref="IsForbidden"/> permits LF by construction, so on its own it
    /// accepts the exact injection this issue is about: containment that
    /// rewrote a hazard as a newline would pass it. Correct containment leaves
    /// the marker welded to the text it was embedded in, so the character
    /// before it is never a line break.
    /// </remarks>
    public static void NoLineSplit(string output, params string[] markers)
    {
        foreach (var marker in markers)
        {
            int at = output.IndexOf(marker, StringComparison.Ordinal);
            while (at > 0)
            {
                char before = output[at - 1];
                Assert.False(
                    before is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029',
                    $"{marker} starts a new line: containment split the text it was embedded in");
                at = output.IndexOf(marker, at + marker.Length, StringComparison.Ordinal);
            }
        }
    }
}

/// <summary>
/// Runs the CLI in-process for a containment gate.
/// </summary>
/// <remarks>
/// Offline mode is a process-wide static on <see cref="CoreFactory"/>, not a
/// per-invocation option, so a gate that turned it on and walked away left it
/// on for every later test in the process. That is not hypothetical: it made
/// the network-backed <c>PackageVersionTests</c> fail in CI while every one of
/// them passed in isolation, which reads exactly like flakiness. The previous
/// state is therefore saved and restored, and this exists as one helper so a
/// fourth copy cannot reintroduce the leak.
/// </remarks>
internal static class HostileCli
{
    public static async Task<(int exit, string output, string error)> RunAsync(params string[] args)
    {
        var wasOffline = CoreFactory.IsOffline;
        try
        {
            return await ConsoleCapture.RunAsync(async () =>
            {
                CoreFactory.Initialize(offline: true);
                CoreFactory.ResetSharedForTesting();

                // Program.cs initializes the cache before dispatching, and the
                // resolution paths that produce "not found" diagnostics go
                // through it. Without this a gate on those diagnostics gets
                // "CoreCache.Initialize must be called" instead of the message
                // it means to inspect, and silently proves nothing. The
                // production app name is used so this does not repoint the
                // shared cache for later tests.
                NuGetCache.Initialize("dotnet-inspect");

                args = CommandLineBuilder.PreprocessArgs(args);
                var root = CommandLineBuilder.CreateRootCommand();
                return await root.Parse(args).InvokeAsync();
            });
        }
        finally
        {
            CoreFactory.Initialize(offline: wasOffline);
            CoreFactory.ResetSharedForTesting();
        }
    }
}
