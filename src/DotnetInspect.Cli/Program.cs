using DotnetInspector.Cache;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Views;
using Markout;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// The .NET runtime is the one writer of this process's stderr that CommandError
// cannot own: an escaping exception is printed by the runtime at column 0, raw,
// with the message interpolated straight in. `--out "<dir>/x\nError: ..."`
// therefore forged a diagnostic line with no product code involved, and a
// hostile .nupkg reached the same printer through a zip-traversal or nuspec
// parse throw. Catching one exception type here (the --rows validation throw)
// stated the right intent with the wrong scope, which is the shape this whole
// change exists to stop; the guard has to cover everything that can throw.
try
{
    using var shareOutput = WorkspaceShareOutput.DeferSideOutput();

    // Parse --offline early (before command parsing) to configure HttpClientFactory
    bool offline = args.Contains("--offline")
        || string.Equals(Environment.GetEnvironmentVariable("DOTNET_INSPECT_OFFLINE"), "1");
    if (offline)
        args = args.Where(a => a != "--offline").ToArray();

    // Parse --isolated <name> and --no-nuget-cache early
    string? sessionName = null;
    var argList = new List<string>(args);
    int isolatedIdx = argList.IndexOf("--isolated");
    if (isolatedIdx >= 0 && isolatedIdx + 1 < argList.Count)
    {
        sessionName = argList[isolatedIdx + 1];
        argList.RemoveAt(isolatedIdx + 1);
        argList.RemoveAt(isolatedIdx);
    }
    else if (isolatedIdx >= 0)
    {
        argList.RemoveAt(isolatedIdx);
    }
    sessionName ??= Environment.GetEnvironmentVariable("DOTNET_INSPECT_ISOLATED");
    if (string.IsNullOrWhiteSpace(sessionName))
        sessionName = null;
    bool isolated = sessionName != null;
    args = argList.ToArray();

    bool noNuGetCache = args.Contains("--no-nuget-cache") || isolated;
    if (args.Contains("--no-nuget-cache"))
        args = args.Where(a => a != "--no-nuget-cache").ToArray();

    // Resolve cache base path: explicit env var > named session dir > default
    string? cacheBasePath = Environment.GetEnvironmentVariable("DOTNET_INSPECT_CACHE_DIR");
    if (isolated && cacheBasePath == null)
    {
        cacheBasePath = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-{sessionName}");
    }

    // Resolve the CLI-owned timeout before any library client can be constructed.
    var timeoutArguments = HttpTimeoutConfiguration.Extract(args);
    if (timeoutArguments.HasDuplicate)
    {
        CommandError.Write($"{HttpTimeoutConfiguration.Flag} may only be specified once.");
        return 1;
    }

    args = timeoutArguments.RemainingArgs;
    TimeSpan httpTimeout;
    if (timeoutArguments.ExplicitValue is not null)
    {
        // Rejected rather than clamped, and fatal rather than ignored: the operator typed this
        // one, so silently running with the 30 second default is the wrong kind of surprise.
        if (!HttpTimeoutConfiguration.TryParseSeconds(timeoutArguments.ExplicitValue, out httpTimeout))
        {
            CommandError.Write(
                $"{HttpTimeoutConfiguration.Flag} expects a whole number of seconds between 1 and 3600.",
                $"Got: '{timeoutArguments.ExplicitValue}'");
            return 1;
        }
    }
    else
    {
        httpTimeout = HttpTimeoutConfiguration.ResolveEnvironmentDefault(
            Environment.GetEnvironmentVariable(HttpTimeoutConfiguration.EnvironmentVariable));
    }

    // Initialize library configuration
    DotnetInspector.Networking.HttpClientFactory.Initialize(new HttpClientFactoryOptions
    {
        Offline = offline,
        DefaultTimeout = httpTimeout,
    });

    // Credential plugins are how a private feed is read without a password stored in nuget.config,
    // and NuGet ranks them as the most secure of the credential mechanisms. The provider defers
    // both discovery and process launch until a source actually answers 401.
    if (!offline)
    {
        var credentialProvider = new NuGetFetch.Plugins.PluginCredentialProvider();

        DotnetInspector.Networking.HttpClientFactory.SetAuthenticationDecorator(
            inner => new NuGetFetch.Plugins.PluginAuthenticationHandler(credentialProvider, inner));
    }
    NuGetCache.Initialize("dotnet-inspect", basePath: cacheBasePath, skipNuGetCache: noNuGetCache);
    // The IR invariant check is armed by default so any host that runs the
    // decompiler pipeline validates it (#3267). The shipped tool is the one
    // sanctioned opt-out: users are not developing the pipeline, so the per-pass
    // tree walk is pure overhead on the decompile hot path. Setting
    // DOTNET_INSPECT_IR_INVARIANTS=1 (or full) overrides this and arms the shipped
    // tool for debugging.
    ILInspector.Decompiler.Pipeline.IrInvariants.DisableForShippedTool();
    // Wire the tool-tier SourceLink index cache into the engine's dependency-inversion seam.
    ILInspector.SourceLink.SourceLinkService.DefaultCache = DotnetInspector.Services.CoreSourceLinkIndexCache.Instance;

    #if DEBUG
    // Log every managed HTTP request with its traffic kind; offline mode
    // enforces the no-network boundary separately.
    if (!offline)
        DotnetInspector.Networking.HttpClientFactory.EnableNetworkTrafficLogging(CSharpIdentifier.ContainRenderedText);
    #endif

    using var requestScope = RequestTelemetry.Scope(string.Join(' ', args), "cli invocation");

    // Handle --version explicitly to show short commit hash
    if (args.Length == 1 && args[0] == "--version")
    {
        Console.WriteLine(VersionInfo.Version);
        return 0;
    }

    // Handle --flavor to show build type (CoreCLR or NativeAOT)
    if (args.Length == 1 && args[0] == "--flavor")
    {
        Console.WriteLine(VersionInfo.FlavorVersion);
        return 0;
    }

    // Handle --release-notes to print release notes
    if (args.Length == 1 && args[0] == "--release-notes")
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("dotnet-inspect.release-notes.md");
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            Console.WriteLine(reader.ReadToEnd());
        }
        return 0;
    }

    var rootCommand = CommandLineBuilder.CreateRootCommand();

    if (CommandLineBuilder.TryGetRemovedCommandError(
            args,
            out var removedCommandError))
    {
        CommandError.Write(removedCommandError!);
        return 1;
    }

    if (CommandLineBuilder.TryGetStaleArgumentError(
            args,
            rootCommand,
            out var staleArgumentError))
    {
        CommandError.Write(staleArgumentError!);
        return 1;
    }

    // Pre-process args for implicit package command (also expands -NN → -n NN)
    args = CommandLineBuilder.PreprocessArgs(args, rootCommand);

    var result = rootCommand.Parse(args);

    int exitCode = await CommandLineBuilder.InvokeWithLineWindowAsync(
        result,
        args);

    _ = PersistentCache.CancelAndWaitForMaintenance(TimeSpan.FromMilliseconds(100));

    return exitCode;
}
catch (PackageSourceMappingException ex)
{
    CommandError.Write(ex.Message);
    return 1;
}
catch (NuGetFetch.UnsupportedSourceException ex)
{
    // Sources arrive from nuget.config as well as from options, so only resolution sees all of
    // them, and resolution runs after parsing. The option validators catch the common case
    // early with the same message; this reports the rest as the same clean error rather than
    // the stack trace the generic handler below would print.
    CommandError.Write(ex);
    return 1;
}
catch (OperationCanceledException)
{
    return 1;
}
catch (Exception ex)
{
    CommandError.WriteUnhandled(ex);
    return 1;
}
