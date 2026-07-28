using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspector.Output;

/// <summary>
/// Guards against a projection request being accepted by the parser and then silently
/// discarded by a render path that exits 0 with the unprojected payload.
/// </summary>
/// <remarks>
/// <para>
/// The payload-shaping flags (<c>--print</c>, <c>--value</c>, <c>--urls</c>, <c>--paths</c>,
/// <c>--count</c>) are honored at scattered dispatch points across the commands. A route that
/// forgets to dispatch does not fail: it renders the full section and exits 0, so the caller
/// receives a well-formed answer to a question it did not ask. That failure is invisible to
/// exit-code checks and to golden-output tests that only cover the unprojected path.
/// </para>
/// <para>
/// This audit closes the gap structurally rather than one site at a time. The request is
/// recorded from the <see cref="ParseResult"/> — by option name, so unaudited and future
/// commands are covered without registration — and the legitimate projection writers mark it
/// honored. <see cref="Verify"/> runs at the single invoke choke point shared by the product
/// entry point and the test harness. A route that drops a projection therefore fails loudly
/// instead of silently, and the failure surfaces in the existing test suite.
/// </para>
/// <para>
/// State is <see cref="AsyncLocal{T}"/> because in-process test runs invoke the CLI
/// concurrently; a static field would let one invocation's request observe another's honor.
/// </para>
/// </remarks>
public static class ProjectionAudit
{
    /// <summary>Payload-shaping option names, by the name each option is constructed with.</summary>
    private static readonly string[] ProjectionOptionNames =
        ["--print", "--value", "--urls", "--paths", "--count"];

    private sealed class Request
    {
        public required string Flag { get; init; }
        public bool Honored { get; set; }
    }

    private static readonly AsyncLocal<Request?> Current = new();

    /// <summary>
    /// Records the payload projection requested by this invocation, if any. Later calls replace
    /// the prior request, so a reused execution context never inherits a stale one.
    /// </summary>
    public static void BeginRequest(ParseResult parseResult)
    {
        Current.Value = null;

        // Help short-circuits rendering, so a projection flag alongside --help is not dropped.
        if (IsHelpRequested(parseResult))
            return;

        var command = parseResult.CommandResult.Command;
        foreach (var name in ProjectionOptionNames)
        {
            var option = command.Options.FirstOrDefault(
                o => string.Equals(o.Name, name, StringComparison.Ordinal));
            if (option is null)
                continue;

            // Implicit results come from defaults, not from the command line.
            if (parseResult.GetResult(option) is { Implicit: false } optionResult)
            {
                // Only bool flags shape the payload; a false value is not a request.
                if (optionResult.GetValueOrDefault<bool>() is false)
                    continue;

                Current.Value = new Request { Flag = name };
                return;
            }
        }
    }

    // Matched against raw tokens rather than a help option instance: the help option is
    // supplied by the parser rather than declared alongside the command's own options, so
    // token matching stays correct independent of how help is registered.
    private static bool IsHelpRequested(ParseResult parseResult)
        => parseResult.Tokens.Any(token => token.Value is "--help" or "-h" or "-?" or "/h" or "/?");

    /// <summary>
    /// Marks the recorded projection request as honored. Called by the projection writers —
    /// the paths that actually reduce the payload to the requested shape.
    /// </summary>
    public static void MarkHonored()
    {
        if (Current.Value is { } request)
            request.Honored = true;
    }

    /// <summary>
    /// Fails a successful invocation that accepted a projection request and never honored it.
    /// A non-zero exit code is left alone: the command already reported a problem, and
    /// rejecting an unsupported projection is a legitimate way to not honor one.
    /// </summary>
    public static int Verify(int exitCode)
    {
        var request = Current.Value;
        Current.Value = null;

        if (exitCode != 0 || request is null || request.Honored)
            return exitCode;

        Console.Error.WriteLine(
            $"Error: '{request.Flag}' was accepted but this command path produced unprojected output. " +
            "This is a bug in dotnet-inspect; please report it.");
        return 1;
    }

    /// <summary>Clears any recorded request. For tests that invoke writers directly.</summary>
    public static void ResetForTesting() => Current.Value = null;
}
