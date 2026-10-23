using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries.Definitions;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ResourceExplanationCommandTests
{
    [Theory]
    [InlineData(".data", "explain-package-declaration_direct.json")]
    [InlineData(".hal", "explain-package-declaration_hal.json")]
    public async Task ComponentFile_PreservesTheSharedReadingAndUnavailableParent(
        string projection, string capture)
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(() => requests++));
        var result = await RunAsync("explain", "--component",
            ComponentExample("workspace-package-component.json"), projection, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument actual = JsonDocument.Parse(result.Output);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(
            ComponentExample(capture), TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ComponentParent_QualifiesTheRealSavedPacketWithoutAcquisition()
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(() => requests++));
        string packet = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            await File.ReadAllTextAsync(ComponentExample("workspace-package-replay.json"),
                TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        var result = await RunAsync("explain", "--component",
            ComponentExample("workspace-package-component.json"),
            "--parent-packet", packet, ".hal", "--json");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument actual = JsonDocument.Parse(result.Output);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(
            ComponentExample("explain-package-occurrence_hal.json"),
            TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ComponentParent_EqualLocalPathsDoNotCollapseDifferentPackets()
    {
        string json = await File.ReadAllTextAsync(ComponentExample("workspace-package-replay.json"),
            TestContext.Current.CancellationToken);
        string[] identities = new string[2];
        for (int i = 0; i < 2; i++)
        {
            string parentJson = i == 0 ? json : json.Replace("https://api.nuget.org/v3/index.json",
                "https://example.test/v3/index.json");
            string packet = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
                parentJson, TestContext.Current.CancellationToken));
            var result = await RunAsync("explain", "--component",
                ComponentExample("workspace-package-component.json"),
                "--parent-packet", packet, "--json");
            Assert.Equal(0, result.ExitCode);
            using JsonDocument data = JsonDocument.Parse(result.Output);
            Assert.Equal("packages/system.text.json@10.0.0/net10.0/~",
                data.RootElement.GetProperty("facts").GetProperty("component_path").GetString());
            identities[i] = data.RootElement.GetProperty("identity").GetProperty("value").GetString()!;
        }
        Assert.NotEqual(identities[0], identities[1]);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    [InlineData("--json")]
    public async Task ComponentFile_UsesExistingHumanAndJsonPresentations(string format)
    {
        var result = await RunAsync("explain", "--component",
            ComponentExample("workspace-package-component.json"), format);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("System.Text.Json", result.Output);
        Assert.Contains(format == "--json" ? "Unavailable" : "unavailable",
            result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("workspace/components/package", "2", "schema version is unsupported")]
    [InlineData("unfamiliar/components/package", "1", "contract is not supported")]
    [InlineData("workspace/components/package", "null", "Invalid Workspace Package component")]
    public async Task ComponentInput_OwnerFailuresDoNotFallThroughToSearch(
        string contract, string version, string error)
    {
        using var input = new ComponentInputFile(
            $$"""{"contract":"{{contract}}","schema_version":{{version}},"package_id":"System.Text.Json"}""");
        var result = await RunAsync("explain", "--component", input.Path, "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(error, result.Error);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("unfamiliar capability")]
    [InlineData("-n", "1")]
    [InlineData("-Q")]
    [InlineData("-Q", "version")]
    [InlineData("--envelope")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData(".hal", "--depth", "0", "--json")]
    [InlineData(".data", "--depth", "1", "--json")]
    [InlineData(".hal")]
    [InlineData(".contract")]
    public async Task ComponentOptions_AreRejectedBeforeOpeningInput(params string[] options)
    {
        // This path is deliberately absent; an input attempt would produce an I/O failure.
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "absent.json");
        var result = await RunAsync(["explain", "--component", missing, .. options]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("Could not find", result.Error);
        Assert.DoesNotContain("FileNotFound", result.Error);
    }

    [Fact]
    public async Task ParentPacket_RequiresComponentInput()
    {
        var result = await RunAsync("explain", "--parent-packet", "invalid", "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--parent-packet requires --component", result.Error);
    }

    [Fact]
    public async Task ComponentParent_RejectsConflictingClaimsAndInvalidPackets()
    {
        string json = await File.ReadAllTextAsync(ComponentExample("workspace-package-component.json"),
            TestContext.Current.CancellationToken);
        using var input = new ComponentInputFile(json.Replace("\"focused\": true", "\"focused\": false"));
        string parentJson = await File.ReadAllTextAsync(ComponentExample("workspace-package-replay.json"),
            TestContext.Current.CancellationToken);
        string parent = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            parentJson, TestContext.Current.CancellationToken));
        var mismatch = await RunAsync("explain", "--component", input.Path,
            "--parent-packet", parent, "--json");
        Assert.Equal(1, mismatch.ExitCode);
        Assert.Empty(mismatch.Output);
        Assert.Contains("does not match the supplied parent packet", mismatch.Error);
        var invalid = await RunAsync("explain", "--component", input.Path,
            "--parent-packet", "invalid", "--json");
        Assert.Equal(1, invalid.ExitCode);
        Assert.Empty(invalid.Output);
        Assert.NotEmpty(invalid.Error);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"contract\":\"workspace/components/package\",\"schema_version\":1,\"schema_version\":1}")]
    public async Task ComponentFailure_DoesNotTouchAnExistingDestination(string json)
    {
        using var input = new ComponentInputFile(json);
        string destination = input.Path + ".out";
        await File.WriteAllTextAsync(destination, "retained", TestContext.Current.CancellationToken);
        var result = await RunAsync("explain", "--component", input.Path,
            "--json", "--out", destination);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("retained", await File.ReadAllTextAsync(destination,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ComponentStdin_IsBoundedAndRejectsInvalidUtf8()
    {
        foreach (byte[] bytes in new[]
        {
            Encoding.UTF8.GetBytes(new string(' ', WorkspacePackageComponentExplanation.MaxUtf8ByteLength + 1)),
            new byte[] { 0xFF },
        })
        {
            using var stream = new MemoryStream(bytes);
            var result = await ConsoleCapture.RunAsync(() =>
                ResourceExplanationCommand.ExplainComponentAsync("-", null, 0,
                    OutputFormat.Json, null, ".hal",
                    TestContext.Current.CancellationToken, stream));
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
            Assert.True(stream.Position <= WorkspacePackageComponentExplanation.MaxUtf8ByteLength + 1);
        }
    }

    [Fact]
    public async Task ComponentStdin_UsesTheSameOwnerAndReadingAsFileInput()
    {
        using var input = File.OpenRead(ComponentExample("workspace-package-component.json"));
        var stdin = await ConsoleCapture.RunAsync(() =>
            ResourceExplanationCommand.ExplainComponentAsync("-", null, 0,
                OutputFormat.Json, null, ".hal", TestContext.Current.CancellationToken, input));
        var file = await RunAsync("explain", "--component",
            ComponentExample("workspace-package-component.json"), ".hal", "--json");
        Assert.Equal(0, stdin.ExitCode);
        Assert.Equal(file.Output, stdin.Output);
    }

    [Fact]
    public async Task ComponentStdin_ProductionParserReadsRedirectedInput()
    {
        string executable = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "explain", "--component", "-", ".hal", "--json" })
            start.ArgumentList.Add(argument);
        // Use the same runtime as this test host, including worktree-local SDKs.
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(
            Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        start.Environment["DOTNET_INSPECT_OFFLINE"] = "1";
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            string input = await File.ReadAllTextAsync(ComponentExample("workspace-package-component.json"),
                timeout.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await error);
            using JsonDocument actual = JsonDocument.Parse(await output);
            using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(
                ComponentExample("explain-package-declaration_hal.json"), timeout.Token));
            Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        }
        finally
        {
            if (!process.HasExited)
                OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ComponentContractProjection_UsesTheExistingTypedDocument()
    {
        var result = await RunAsync("explain", "--component",
            ComponentExample("workspace-package-component.json"), ".contract", "--json", "--depth", "1");
        Assert.Equal(0, result.ExitCode);
        using JsonDocument data = JsonDocument.Parse(result.Output);
        JsonElement resource = data.RootElement.GetProperty("resources")[0];
        Assert.Equal("Detached", resource.GetProperty("scope").GetString());
        Assert.Equal("System.Text.Json", TextFact(resource, "package_id"));
    }

    private static string ComponentExample(string name)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return Path.Combine(directory.FullName, "docs", "examples", name);
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class ComponentInputFile : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "component-explain-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }

        public ComponentInputFile(string content)
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "component.json");
            File.WriteAllText(Path, content);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
