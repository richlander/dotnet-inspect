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
/// commands are covered without registration — and the legitimate projection writers report
/// which projection they honored. <see cref="Verify"/> runs at the single invoke choke point
/// shared by the product entry point and the test harness. A route that drops a projection
/// therefore fails loudly instead of silently, and the failure surfaces in the existing tests.
/// </para>
/// <para>
/// Honoring is reported per flag rather than as a bare acknowledgement. An untyped signal is
/// unsound: a writer reached for one reason (say <c>--bare</c> routing through the print
/// writer) would satisfy an unrelated recorded request such as <c>--count</c>, and the drop
/// would escape.
/// </para>
/// <para>
/// State is <see cref="AsyncLocal{T}"/> because in-process test runs invoke the CLI
/// concurrently; a static field would let one invocation's request observe another's honor.
/// </para>
/// </remarks>
public static class ProjectionAudit
{
    public const string Print = "--print";
    public const string Value = "--value";
    public const string Urls = "--urls";
    public const string Paths = "--paths";
    public const string Count = "--count";

    /// <summary>Payload-shaping option names, by the name each option is constructed with.</summary>
    private static readonly string[] ProjectionOptionNames = [Print, Value, Urls, Paths, Count];

    private sealed class Request
    {
        public required IReadOnlyList<string> Flags { get; init; }
        public HashSet<string> Honored { get; } = new(StringComparer.Ordinal);
    }

    private static readonly AsyncLocal<Request?> Current = new();

    /// <summary>
    /// Rejects more than one payload projection in a single invocation. Two projections cannot
    /// both shape one payload, so honoring either one silently discards the other. Commands
    /// already enforced this among <c>--value</c>/<c>--urls</c>/<c>--paths</c> individually;
    /// enforcing it centrally covers <c>--print</c> and <c>--count</c> too, and covers commands
    /// that never had the check.
    /// </summary>
    public static bool ValidateExclusive(ParseResult parseResult)
    {
        // Help short-circuits rendering, so no payload is shaped and the conflict is moot.
        // This must be checked here as well as in BeginRequest: rejecting the combination
        // would otherwise turn a legitimate help request into an error.
        if (IsHelpRequested(parseResult))
            return true;

        var requested = RequestedFlags(parseResult);
        if (requested.Count <= 1)
            return true;

        // Report in command-line order and in the wording the commands already use for this
        // conflict, so the central check tightens coverage without restating the contract.
        var ordered = requested
            .OrderBy(flag => FirstTokenIndex(parseResult, flag))
            .ToList();

        var conflicts = string.Join(", ", ordered.Skip(1));
        CommandError.Write($"{ordered[0]} cannot be combined with {conflicts}.");
        return false;
    }

    private static int FirstTokenIndex(ParseResult parseResult, string flag)
    {
        for (var i = 0; i < parseResult.Tokens.Count; i++)
            if (string.Equals(parseResult.Tokens[i].Value, flag, StringComparison.Ordinal))
                return i;

        return int.MaxValue;
    }

    /// <summary>
    /// Records the payload projections requested by this invocation. Disposing the returned
    /// scope restores whatever request was in flight before it.
    /// </summary>
    /// <remarks>
    /// Invocations nest: the bare-mode router invokes the command it rewrites to. Without the
    /// restore, an inner invocation would discard the outer one's request and the outer verify
    /// would then find nothing to check. That is unreachable today, because the router captures
    /// its tokens raw and so records nothing, but it is not a property worth depending on.
    /// </remarks>
    public static Scope BeginRequest(ParseResult parseResult)
    {
        var scope = new Scope(Current.Value);
        Current.Value = null;

        // Help short-circuits rendering, so a projection flag alongside --help is not dropped.
        if (IsHelpRequested(parseResult))
            return scope;

        var requested = RequestedFlags(parseResult);
        if (requested.Count > 0)
            Current.Value = new Request { Flags = requested };

        return scope;
    }

    /// <summary>Restores the request that was in flight when it was created.</summary>
    public readonly struct Scope(object? displaced) : IDisposable
    {
        public void Dispose() => Current.Value = displaced as Request;
    }

    private static List<string> RequestedFlags(ParseResult parseResult)
    {
        var requested = new List<string>();

        foreach (var name in ProjectionOptionNames)
        {
            // A projection can be declared by the executing command or by any ancestor:
            // `package --count search <id>` binds the flag to the parent `package` command,
            // and the parser accepts it. Inspecting only the executing command would miss a
            // projection that was accepted and is about to be dropped.
            if (CommandChain(parseResult).Any(command => IsExplicitlySet(parseResult, command, name)))
                requested.Add(name);
        }

        return requested;
    }

    private static IEnumerable<Command> CommandChain(ParseResult parseResult)
    {
        for (SymbolResult? scope = parseResult.CommandResult; scope is not null; scope = scope.Parent)
            if (scope is CommandResult commandResult)
                yield return commandResult.Command;
    }

    private static bool IsExplicitlySet(ParseResult parseResult, Command command, string name)
    {
        var option = command.Options.FirstOrDefault(
            o => string.Equals(o.Name, name, StringComparison.Ordinal));
        if (option is null)
            return false;

        // Implicit results come from defaults, not from the command line, and only a
        // true bool flag shapes the payload.
        return parseResult.GetResult(option) is { Implicit: false } result
            && result.GetValueOrDefault<bool>();
    }

    // Matched against option tokens rather than raw token text: '/h' can legitimately be the
    // *value* of another option (for example --type /h), and treating that as help would
    // silently disable the audit for the rest of the invocation.
    private static bool IsHelpRequested(ParseResult parseResult)
        => parseResult.Tokens.Any(token =>
            token.Type == TokenType.Option
            && token.Value is "--help" or "-h" or "-?" or "/h" or "/?");

    /// <summary>
    /// Records that <paramref name="flag"/> was honored — called by the writer that actually
    /// reduced the payload to that projection's shape. Reporting a flag that was not requested
    /// is harmless and expected: the print writer also serves <c>--bare</c>.
    /// </summary>
    public static void MarkHonored(string flag)
    {
        if (Current.Value is { } request)
            request.Honored.Add(flag);
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

        if (exitCode != 0 || request is null)
            return exitCode;

        var dropped = request.Flags.Where(flag => !request.Honored.Contains(flag)).ToList();
        if (dropped.Count == 0)
            return exitCode;

        CommandError.Write(
            $"{string.Join(", ", dropped.Select(flag => $"'{flag}'"))} " +
            "was accepted but this command path produced unprojected output. " +
            "This is a bug in dotnet-inspect; please report it.");
        return 1;
    }

    /// <summary>Clears any recorded request. For tests that invoke writers directly.</summary>
    public static void ResetForTesting() => Current.Value = null;
}
