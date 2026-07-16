using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Commands;
using Repl;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class ReplCommandTests
{
    [Fact]
    public async Task Repl_NavigatesPackageTypeMember_UsingExistingCliOperations()
    {
        List<string[]> calls = [];
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(arguments);
            io.Output.WriteLine($"ran {string.Join(" ", arguments)}");
            return Task.FromResult(0);
        }

        var result = await RunAsync(ReplCommand.CreateApp(ExecuteAsync), """
            package Example.Package
            libraries
            types
            type Example.Type
            members
            member Run
            source
            source 2
            show
            back
            back
            exit
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
        [
            "package Example.Package --tips q",
            "package Example.Package -S Libraries --tips q",
            "type --package Example.Package --tips q",
            "type --package Example.Package --tips q -- Example.Type",
            "member --package Example.Package --tips q -- Example.Type",
            "member --package Example.Package --member=Run --tips q -- Example.Type",
            "member --package Example.Package --member=Run -S Decompiled Source --tips q -- Example.Type",
            "member --package Example.Package --member=Run --index 2 -S Decompiled Source --tips q -- Example.Type",
            "member --package Example.Package --member=Run --tips q -- Example.Type",
        ],
        calls.Select(arguments => string.Join(" ", arguments)));
        Assert.Contains("[selected-package/selected-type/selected-member]>", result.Output);
        Assert.Contains("[selected-package/selected-type]>", result.Output);
        Assert.Contains("[selected-package]>", result.Output);
        Assert.DoesNotContain(Environment.NewLine + "0" + Environment.NewLine, result.Output);
    }

    [Fact]
    public async Task Repl_DefaultExecutor_TraversesLocalPackageTypeMemberAndSource()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("repl-integration-").FullName;
        try
        {
            var packageRoot = Path.Combine(tempDirectory, "package");
            var libraryDirectory = Path.Combine(packageRoot, "lib", "net11.0");
            Directory.CreateDirectory(libraryDirectory);
            File.Copy(
                typeof(ReplCommandTests).Assembly.Location,
                Path.Combine(libraryDirectory, "Repl.Integration.dll"));
            var packagePath = Path.Combine(tempDirectory, "Repl.Integration.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(packageRoot, packagePath);

            var result = await RunAsync(ReplCommand.CreateApp(), $$"""
                package "{{packagePath}}"
                type DotnetInspector.Tests.ReplCommandTests
                member Repl_OneShotActionFailure_PropagatesExitCode
                source 1
                exit
                """);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Contains("[selected-package/selected-type/selected-member]>", result.Output);
            Assert.Contains("Decompiled Source", result.Output);
            Assert.Contains("Repl_OneShotActionFailure_PropagatesExitCode", result.Output);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Repl_FailedContextValidation_IsRetried()
    {
        var callCount = 0;
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            callCount++;
            io.Error.WriteLine($"attempt {callCount}: {string.Join(" ", arguments)}");
            return Task.FromResult(callCount == 1 ? 1 : 0);
        }

        var result = await RunAsync(ReplCommand.CreateApp(ExecuteAsync), """
            package Missing.Package
            package Missing.Package
            exit
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, callCount);
        Assert.Contains("[selected-package]>", result.Output);
        Assert.Contains("attempt 1", result.Error);
        Assert.Contains("attempt 2", result.Error);
    }

    [Fact]
    public async Task Repl_OneShotActionFailure_PropagatesExitCode()
    {
        var callCount = 0;
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            callCount++;
            return Task.FromResult(7);
        }

        var result = await ConsoleCapture.RunAsync(
            () => ReplCommand.CreateApp(ExecuteAsync)
                .RunAsync(["package", "Example.Package"])
                .AsTask());

        Assert.Equal(1, callCount);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task Repl_ActionFailure_ProducesExplicitExitResult()
    {
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken) =>
            Task.FromResult(7);

        var result = await ReplCommand.ExecuteAsResultAsync(
            ["show"],
            new RecordingIoContext(),
            ExecuteAsync,
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task Repl_BackPreservesSelectorContainingWhitespace()
    {
        List<string[]> calls = [];
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(arguments);
            return Task.FromResult(0);
        }

        var result = await RunAsync(ReplCommand.CreateApp(ExecuteAsync), """
            package Example.Package
            type "Example Type"
            member Run
            back
            show
            exit
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, calls.Count(arguments => arguments.Length > 1 && arguments[0] == "type" && arguments.Contains("--") && arguments[^1] == "Example Type"));
        Assert.Contains("[selected-package/selected-type]>", result.Output);
    }

    [Fact]
    public async Task Repl_DistinctSlashDelimitedSelectors_DoNotShareContextState()
    {
        List<string[]> calls = [];
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(arguments);
            return Task.FromResult(0);
        }

        var result = await RunAsync(ReplCommand.CreateApp(ExecuteAsync), """
            package Example.Package
            type A/B
            member C
            back
            back
            type A
            member B/C
            exit
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(calls, arguments => arguments.Contains("--member=C"));
        Assert.Contains(calls, arguments => arguments.Contains("--member=B/C"));
    }

    [Fact]
    public async Task Repl_DashPrefixedType_UsesUnambiguousCliArguments()
    {
        List<string[]> calls = [];
        Task<int> ExecuteAsync(string[] arguments, IReplIoContext io, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(arguments);
            return Task.FromResult(0);
        }

        var result = await RunAsync(ReplCommand.CreateApp(ExecuteAsync), """
            package Example.Package
            type "-Foo"
            members
            member Run
            source
            exit
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(calls, arguments => arguments[0] == "type" && arguments.Contains("--") && arguments[^1] == "-Foo");
        Assert.All(calls.Where(arguments => arguments[0] == "member"), arguments =>
        {
            var separator = Array.IndexOf(arguments, "--");
            Assert.True(separator >= 0, string.Join(' ', arguments));
            Assert.Equal("-Foo", arguments[separator + 1]);
        });
    }

    [Fact]
    public async Task ReplFeatureGate_DisablesTrimmedAndRidAotBuilds()
    {
        (string Name, string[] Properties, bool Expected)[] cases =
        [
            ("managed development", [], true),
            ("managed any", ["-p:PublishAot=false", "-p:RuntimeIdentifier=any"], true),
            ("inferred RID AOT publish", ["-p:_IsPublishing=true"], false),
            ("RID AOT", ["-p:PublishAot=true", "-p:RuntimeIdentifier=linux-x64"], false),
            ("official RID AOT", ["-p:OfficialAotBuild=true", "-p:RuntimeIdentifier=linux-x64"], false),
            ("trimmed managed", ["-p:PublishAot=false", "-p:PublishTrimmed=true"], false),
        ];

        foreach (var testCase in cases)
        {
            var gate = await EvaluateExperimentalReplGateAsync(testCase.Properties);
            Assert.Equal(testCase.Expected, gate.Enabled);
            Assert.Equal(testCase.Expected, gate.HasCompilationSymbol);
            Assert.Equal(testCase.Expected, gate.HasPackageReference);
        }
    }

    [Fact]
    public async Task EntryPoint_IsolatedWithoutValueBeforeDoubleDash_Fails()
    {
        var result = await RunProductAsync(["--isolated", "--", "System.Private.CoreLib"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("Error: --isolated requires a session name before '--'.", result.Error.Trim());
    }

    [Fact]
    public async Task CliRouting_StreamsAndPreservesStdoutStderrInterleaving()
    {
        var io = new RecordingIoContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalOut = Console.Out;
        var originalError = Console.Error;

        var execution = ReplCommand.ExecuteWithConsoleRoutingAsync(io, TestContext.Current.CancellationToken, async _ =>
        {
            Console.Out.Write("first");
            Console.Error.Write("second");
            await release.Task;
            Console.Out.Write("third");
            return 0;
        });

        try
        {
            await io.FirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.False(execution.IsCompleted);
            Assert.Equal(["out:first", "err:second"], io.Events);
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.Equal(0, await execution);
        Assert.Equal(["out:first", "err:second", "out:third"], io.Events);
        Assert.Same(originalOut, Console.Out);
        Assert.Same(originalError, Console.Error);
    }

    [Fact]
    public async Task CliRouting_BoundsLargeOutputAndReportsTruncation()
    {
        var io = new RecordingIoContext();
        var payload = new string('x', ReplCommand.OutputCharacterLimit + 1);

        var exitCode = await ReplCommand.ExecuteWithConsoleRoutingAsync(
            io,
            TestContext.Current.CancellationToken,
            _ =>
            {
                Console.Out.Write(payload);
                return Task.FromResult(0);
            });

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ReplCommand.OutputCharacterLimit,
            io.Events.Where(entry => entry.StartsWith("out:", StringComparison.Ordinal)).Sum(entry => entry.Length - 4));
        Assert.Contains(io.Events, entry => entry.Contains("Repl output truncated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CliRouting_RestoresConsoleAndKeepsPartialOutputWhenOperationThrows()
    {
        var io = new RecordingIoContext();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReplCommand.ExecuteWithConsoleRoutingAsync(
                io,
                TestContext.Current.CancellationToken,
                async _ =>
                {
                    Console.Out.Write("partial");
                    Console.Error.Write("diagnostic");
                    await Task.Yield();
                    throw new InvalidOperationException("test failure");
                }));

        Assert.Equal(["out:partial", "err:diagnostic"], io.Events);
        Assert.Same(originalOut, Console.Out);
        Assert.Same(originalError, Console.Error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProductAsync(string[] arguments)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var runtimeConfig = Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.Tests.runtimeconfig.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot(),
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(runtimeConfig);
        startInfo.ArgumentList.Add(typeof(ReplCommand).Assembly.Location);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet-inspect.");
        var outputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<ExperimentalReplGate> EvaluateExperimentalReplGateAsync(string[] properties)
    {
        var repositoryRoot = FindRepositoryRoot();
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "src", "dotnet-inspect", "dotnet-inspect.csproj"));
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-getProperty:EnableExperimentalRepl,DefineConstants");
        startInfo.ArgumentList.Add("-getItem:PackageReference");
        foreach (var property in properties)
            startInfo.ArgumentList.Add(property);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
        var outputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"dotnet msbuild exited {process.ExitCode}: {error}");

        var jsonStart = output.IndexOf('{');
        Assert.True(jsonStart >= 0, $"Expected JSON from dotnet msbuild: {output}");

        using var document = JsonDocument.Parse(output[jsonStart..]);
        var propertiesElement = document.RootElement.GetProperty("Properties");
        var enabled = string.Equals(
            propertiesElement.GetProperty("EnableExperimentalRepl").GetString(),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var constants = propertiesElement.GetProperty("DefineConstants").GetString() ?? string.Empty;
        var hasCompilationSymbol = constants.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Contains("DOTNET_INSPECT_EXPERIMENTAL_REPL", StringComparer.Ordinal);
        var hasPackageReference = document.RootElement
            .GetProperty("Items")
            .GetProperty("PackageReference")
            .EnumerateArray()
            .Any(item => string.Equals(
                item.GetProperty("Identity").GetString(),
                "Repl.Core",
                StringComparison.OrdinalIgnoreCase));
        return new ExperimentalReplGate(enabled, hasCompilationSymbol, hasPackageReference);
    }

    private sealed record ExperimentalReplGate(
        bool Enabled,
        bool HasCompilationSymbol,
        bool HasPackageReference);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the dotnet-inspect repository root.");
    }

    private sealed class RecordingIoContext : IReplIoContext
    {
        private readonly List<string> _events = [];
        private readonly object _gate = new();

        public RecordingIoContext()
        {
            Output = new RecordingWriter("out", _events, _gate, FirstWrite);
            Error = new RecordingWriter("err", _events, _gate, FirstWrite);
        }

        public TaskCompletionSource FirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_gate)
                    return [.. _events];
            }
        }

        public TextReader Input => TextReader.Null;
        public TextWriter Output { get; }
        public TextWriter Error { get; }
        public bool IsHostedSession => false;
        public string? SessionId => null;
    }

    private sealed class RecordingWriter(
        string channel,
        List<string> events,
        object gate,
        TaskCompletionSource firstWrite) : StringWriter
    {
        public override void Write(string? value)
        {
            lock (gate)
                events.Add($"{channel}:{value}");
            firstWrite.TrySetResult();
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(CoreReplApp app, string input)
    {
        var originalIn = Console.In;
        Console.SetIn(new StringReader(input));
        try
        {
            return await ConsoleCapture.RunAsync(() => app.RunAsync([]).AsTask());
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }
}
