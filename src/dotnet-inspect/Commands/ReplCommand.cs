#if DOTNET_INSPECT_EXPERIMENTAL_REPL
using System.CommandLine;
using System.Globalization;
using System.Text;
using Repl;

namespace DotnetInspector.Commands;

/// <summary>
/// Runs the experimental contextual inspection Repl.
/// </summary>
internal static class ReplCommand
{
    private const string CurrentPackageKey = "dotnet-inspect.repl.package";
    private const string CurrentTypeKey = "dotnet-inspect.repl.type";
    private const string CurrentMemberKey = "dotnet-inspect.repl.member";
    private const string PackageScope = "selected-package";
    private const string TypeScope = "selected-package selected-type";
    private const string MemberScope = "selected-package selected-type selected-member";
    internal const int OutputLineLimit = 10_000;
    internal const int OutputCharacterLimit = 1_000_000;
    private static readonly SemaphoreSlim CliExecutionLock = new(1, 1);

    internal delegate Task<int> ReplInspectionExecutor(
        string[] arguments,
        IReplIoContext io,
        CancellationToken cancellationToken);

    internal static Task<int> ExecuteAsync(CancellationToken cancellationToken = default) =>
        CreateApp().RunAsync([], cancellationToken).AsTask();

    internal static CoreReplApp CreateApp(ReplInspectionExecutor? executor = null)
    {
        executor ??= ExecuteExistingCliAsync;

        var app = CoreReplApp.Create()
            .WithDescription("Explore NuGet packages, .NET types, and members contextually.")
            .WithBanner("Experimental mode. Enter package <id> to begin; use help for commands and back to leave contexts.");

        Func<IReplSessionState, bool> hasPackage = state => state.TryGet<string>(CurrentPackageKey, out _);
        Func<IReplSessionState, bool> hasType = state => state.TryGet<string>(CurrentTypeKey, out _);
        Func<IReplSessionState, bool> hasMember = state => state.TryGet<string>(CurrentMemberKey, out _);

        app.Map("package {package}", async (
            string package,
            IReplSessionState state,
            IReplIoContext io,
            CancellationToken cancellationToken) =>
            await SelectContextAsync(
                CurrentPackageKey,
                package,
                [CurrentTypeKey, CurrentMemberKey],
                PackageScope,
                PackageArguments(package),
                state,
                io,
                executor,
                cancellationToken));

        app.Context(PackageScope, packageScope =>
        {
            packageScope.Map("show", async (
                IReplSessionState state,
                IReplIoContext io,
                CancellationToken cancellationToken) =>
                await ExecuteAsResultAsync(
                    PackageArguments(GetSelected(state, CurrentPackageKey, "package")),
                    io,
                    executor,
                    cancellationToken));
            packageScope.Map("libraries", async (
                IReplSessionState state,
                IReplIoContext io,
                CancellationToken cancellationToken) =>
                await ExecuteAsResultAsync(
                    PackageArguments(GetSelected(state, CurrentPackageKey, "package"), "-S", "Libraries"),
                    io,
                    executor,
                    cancellationToken));
            packageScope.Map("types", async (
                IReplSessionState state,
                IReplIoContext io,
                CancellationToken cancellationToken) =>
                await ExecuteAsResultAsync(
                    TypeArguments(GetSelected(state, CurrentPackageKey, "package")),
                    io,
                    executor,
                    cancellationToken));
            packageScope.Map("back", () => Results.NavigateUp());
            packageScope.Map("type {type}", async (
                string type,
                IReplSessionState state,
                IReplIoContext io,
                CancellationToken cancellationToken) =>
            {
                var package = GetSelected(state, CurrentPackageKey, "package");
                return await SelectContextAsync(
                    CurrentTypeKey,
                    type,
                    [CurrentMemberKey],
                    TypeScope,
                    TypeArguments(package, type),
                    state,
                    io,
                    executor,
                    cancellationToken);
            });

            packageScope.Context("selected-type", typeScope =>
            {
                typeScope.Map("show", async (
                    IReplSessionState state,
                    IReplIoContext io,
                    CancellationToken cancellationToken) =>
                    await ExecuteAsResultAsync(
                        TypeArguments(
                            GetSelected(state, CurrentPackageKey, "package"),
                            GetSelected(state, CurrentTypeKey, "type")),
                        io,
                        executor,
                        cancellationToken));
                typeScope.Map("members", async (
                    IReplSessionState state,
                    IReplIoContext io,
                    CancellationToken cancellationToken) =>
                    await ExecuteAsResultAsync(
                        MemberArguments(
                            GetSelected(state, CurrentPackageKey, "package"),
                            GetSelected(state, CurrentTypeKey, "type")),
                        io,
                        executor,
                        cancellationToken));
                typeScope.Map("back", () => Results.NavigateUp());
                typeScope.Map("member {member}", async (
                    string member,
                    IReplSessionState state,
                    IReplIoContext io,
                    CancellationToken cancellationToken) =>
                {
                    var package = GetSelected(state, CurrentPackageKey, "package");
                    var type = GetSelected(state, CurrentTypeKey, "type");
                    return await SelectContextAsync(
                        CurrentMemberKey,
                        member,
                        [],
                        MemberScope,
                        MemberArguments(package, type, member),
                        state,
                        io,
                        executor,
                        cancellationToken);
                });

                typeScope.Context("selected-member", memberScope =>
                {
                    memberScope.Map("show", async (
                        IReplSessionState state,
                        IReplIoContext io,
                        CancellationToken cancellationToken) =>
                        await ExecuteAsResultAsync(
                            MemberArguments(
                                GetSelected(state, CurrentPackageKey, "package"),
                                GetSelected(state, CurrentTypeKey, "type"),
                                GetSelected(state, CurrentMemberKey, "member")),
                            io,
                            executor,
                            cancellationToken));
                    memberScope.Map("source", async (
                        IReplSessionState state,
                        IReplIoContext io,
                        CancellationToken cancellationToken) =>
                        await ExecuteAsResultAsync(
                            MemberSourceArguments(
                                GetSelected(state, CurrentPackageKey, "package"),
                                GetSelected(state, CurrentTypeKey, "type"),
                                GetSelected(state, CurrentMemberKey, "member"),
                                overload: null),
                            io,
                            executor,
                            cancellationToken));
                    memberScope.Map("source {overload:int}", async (
                        int overload,
                        IReplSessionState state,
                        IReplIoContext io,
                        CancellationToken cancellationToken) =>
                        await ExecuteAsResultAsync(
                            MemberSourceArguments(
                                GetSelected(state, CurrentPackageKey, "package"),
                                GetSelected(state, CurrentTypeKey, "type"),
                                GetSelected(state, CurrentMemberKey, "member"),
                                overload),
                            io,
                            executor,
                            cancellationToken));
                    memberScope.Map("back", () => Results.NavigateUp());
                }, validation: hasMember);
            }, validation: hasType);
        }, validation: hasPackage);

        return app;
    }

    internal static async Task<IExitResult> ExecuteAsResultAsync(
        string[] arguments,
        IReplIoContext io,
        ReplInspectionExecutor executor,
        CancellationToken cancellationToken) =>
        Results.Exit(await executor(arguments, io, cancellationToken));

    internal static async Task<int> ExecuteWithConsoleRoutingAsync(
        IReplIoContext io,
        CancellationToken cancellationToken,
        Func<IReplIoContext, Task<int>> operation)
    {
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(operation);

        await CliExecutionLock.WaitAsync(cancellationToken);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var boundedIo = new BoundedReplIoContext(io);

        try
        {
            Console.SetOut(boundedIo.Output);
            Console.SetError(boundedIo.Error);
            cancellationToken.ThrowIfCancellationRequested();
            return await operation(boundedIo);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            try
            {
                if (boundedIo.LimitReached)
                {
                    io.Error.WriteLine(
                        $"Repl output truncated after {OutputLineLimit:N0} lines or " +
                        $"{OutputCharacterLimit:N0} characters per stream. " +
                        "Run the equivalent regular CLI command with explicit output controls.");
                }
            }
            finally
            {
                CliExecutionLock.Release();
            }
        }
    }

    private static string[] PackageArguments(string package, params string[] additionalArguments) =>
        ["package", package, .. additionalArguments, "--tips", "q"];

    private static string[] TypeArguments(string package, string? type = null) =>
        type is null
            ? ["type", "--package", package, "--tips", "q"]
            : ["type", "--package", package, "--tips", "q", "--", type];

    private static string[] MemberArguments(string package, string type, string? member = null) =>
        member is null
            ? ["member", "--package", package, "--tips", "q", "--", type]
            : ["member", "--package", package, $"--member={member}", "--tips", "q", "--", type];

    private static string[] MemberSourceArguments(string package, string type, string member, int? overload) =>
        overload is null
            ? ["member", "--package", package, $"--member={member}", "-S", "Decompiled Source", "--tips", "q", "--", type]
            : ["member", "--package", package, $"--member={member}", "--index", overload.Value.ToString(CultureInfo.InvariantCulture), "-S", "Decompiled Source", "--tips", "q", "--", type];

    private static string GetSelected(IReplSessionState state, string key, string name) =>
        state.Get<string>(key) ?? throw new InvalidOperationException($"No {name} is selected.");

    private static async Task<object> SelectContextAsync(
        string stateKey,
        string value,
        IReadOnlyList<string> staleStateKeys,
        string targetPath,
        string[] arguments,
        IReplSessionState state,
        IReplIoContext io,
        ReplInspectionExecutor executor,
        CancellationToken cancellationToken)
    {
        var exitCode = await executor(arguments, io, cancellationToken);
        if (exitCode != 0)
            return Results.Exit(exitCode);

        state.Set(stateKey, value);
        foreach (var staleStateKey in staleStateKeys)
            state.Remove(staleStateKey);

        return Results.NavigateTo(targetPath);
    }

    private sealed class BoundedReplIoContext : IReplIoContext
    {
        private readonly BoundedTextWriter _output;
        private readonly BoundedTextWriter _error;

        public BoundedReplIoContext(IReplIoContext inner)
        {
            Input = inner.Input;
            _output = new BoundedTextWriter(inner.Output, OutputLineLimit, OutputCharacterLimit);
            _error = new BoundedTextWriter(inner.Error, OutputLineLimit, OutputCharacterLimit);
            Output = TextWriter.Synchronized(_output);
            Error = TextWriter.Synchronized(_error);
            IsHostedSession = inner.IsHostedSession;
            SessionId = inner.SessionId;
        }

        public TextReader Input { get; }
        public TextWriter Output { get; }
        public TextWriter Error { get; }
        public bool IsHostedSession { get; }
        public string? SessionId { get; }
        public bool LimitReached => _output.LimitReached || _error.LimitReached;
    }

    private sealed class BoundedTextWriter(TextWriter inner, int maxLines, int maxCharacters) : TextWriter
    {
        private readonly object _gate = new();
        private int _charactersWritten;
        private int _linesWritten;

        public override Encoding Encoding => inner.Encoding;
        public bool LimitReached { get; private set; }

        public override void Write(char value)
        {
            lock (_gate)
            {
                if (LimitReached)
                    return;

                if (_charactersWritten >= maxCharacters || (value == '\n' && _linesWritten >= maxLines))
                {
                    LimitReached = true;
                    return;
                }

                inner.Write(value);
                _charactersWritten++;
                if (value == '\n')
                {
                    _linesWritten++;
                    if (_linesWritten >= maxLines)
                        LimitReached = true;
                }
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
                return;

            lock (_gate)
            {
                if (LimitReached)
                    return;

                var allowedLength = Math.Min(value.Length, maxCharacters - _charactersWritten);
                var lines = 0;
                for (var i = 0; i < allowedLength; i++)
                {
                    if (value[i] != '\n')
                        continue;

                    lines++;
                    if (_linesWritten + lines >= maxLines)
                    {
                        allowedLength = i + 1;
                        break;
                    }
                }

                if (allowedLength > 0)
                {
                    var allowed = value[..allowedLength];
                    inner.Write(allowed);
                    _charactersWritten += allowedLength;
                    _linesWritten += allowed.Count(character => character == '\n');
                }

                if (allowedLength < value.Length
                    || _charactersWritten >= maxCharacters
                    || _linesWritten >= maxLines)
                {
                    LimitReached = true;
                }
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write(NewLine);
        }

        public override void WriteLine() => Write(NewLine);
        public override void Flush() => inner.Flush();
    }

    private static Task<int> ExecuteExistingCliAsync(
        string[] arguments,
        IReplIoContext io,
        CancellationToken cancellationToken) =>
        ExecuteWithConsoleRoutingAsync(io, cancellationToken, async boundedIo =>
        {
            var processedArguments = CommandLineBuilder.PreprocessArgs(arguments);
            var root = CommandLineBuilder.CreateRootCommand();
            var invocationConfiguration = new InvocationConfiguration
            {
                Output = boundedIo.Output,
                Error = boundedIo.Error,
            };
            return await root.Parse(processedArguments)
                .InvokeAsync(invocationConfiguration, cancellationToken);
        });
}
#else
namespace DotnetInspector.Commands;

/// <summary>
/// Reports that the experimental Repl is unavailable in trimmed or NativeAOT builds.
/// </summary>
internal static class ReplCommand
{
    internal static Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.Error.WriteLine(
            "The experimental Repl is unavailable in trimmed or NativeAOT builds. " +
            "Use an untrimmed managed dotnet-inspect build or the managed 'any' tool package.");
        return Task.FromResult(1);
    }
}
#endif
