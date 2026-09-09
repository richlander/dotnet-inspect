using System.Diagnostics;
using System.Text;
using DotnetInspector.Commands;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class WorkspaceStateCommandTests
{
    private const string CanonicalVector =
        "eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEwIiwibmV0MTAuMCIsbnVsbF0s"
        + "WyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpb"
        + "WzAsMV1dLCJhIjoxLCJ4IjowLCJ2IjoiYXBpIiwieSI6IlN5c3RlbS5UZXh0Lkpzb24u"
        + "SnNvblNlcmlhbGl6ZXIiLCJsIjpbIlN5c3RlbS5UZXh0Lkpzb24iXX0";

    private const string UnicodeVector =
        "eyJmIjoxLCJ0IjpbWyJDb250b3NvLkpzb24iLCIyLjAuMCIsIm5ldDEwLjAiLCJsaW51"
        + "eC14NjQiXV0sImciOltbMF1dLCJhIjowLCJ4IjowLCJ2IjoibWVtYmVyIiwieSI6IuS-"
        + "iy5Kc29uU2VyaWFsaXplcjxUPiIsInMiOiJEZXNlcmlhbGl6ZUFzeW5jKFN5c3RlbS5T"
        + "dHJpbmcsIFN5c3RlbS5UaHJlYWRpbmcuQ2FuY2VsbGF0aW9uVG9rZW4pIiwiYyI6IlNv"
        + "dXJjZSIsImwiOlsiQ29udG9zby5Kc29uIl19";

    private const string EquivalentJson =
        """
        {
          "x": 0,
          "a": 1,
          "g": [[0, 1]],
          "t": [
            [":Platform", "10.0.10", "net10.0", null],
            ["System.Text.Json", "10.0.0", "net10.0", null]
          ],
          "f": 1,
          "v": "\u0061pi",
          "y": "System.Text.Json.JsonSerializer",
          "l": ["System.Text.Json"]
        }
        """;

    [Fact]
    public async Task DecodeThenEncode_RoundTripsCanonicalPacket()
    {
        var decoded = await RunCliAsync(
            "workspace-state",
            "decode",
            CanonicalVector);

        Assert.Equal(0, decoded.ExitCode);
        Assert.Empty(decoded.Error);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            CanonicalVector,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            WorkspaceSharePacketCodec.SerializeJson(packet),
            decoded.Output.TrimEnd());

        var encoded = await RunCliAsync(
            "workspace-state",
            "encode",
            decoded.Output);

        Assert.Equal(0, encoded.ExitCode);
        Assert.Empty(encoded.Error);
        Assert.Equal(CanonicalVector, encoded.Output.TrimEnd());
    }

    [Theory]
    [InlineData(CanonicalVector)]
    [InlineData(UnicodeVector)]
    public async Task EncodeUrl_PreservesCanonicalPacket(string encoded)
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            encoded,
            TestContext.Current.CancellationToken);
        var result = await RunCliAsync(
            "workspace-state",
            "encode",
            WorkspaceSharePacketCodec.SerializeJson(packet),
            "--url");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            $"https://dotnet-inspect.net/?w={encoded}{Environment.NewLine}",
            result.Output);
        Assert.Equal(encoded, new Uri(result.Output.TrimEnd()).Query[3..]);
    }

    [Fact]
    public async Task Encode_AcceptsEquivalentJsonFromFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-workspace-state-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                EquivalentJson,
                TestContext.Current.CancellationToken);

            var result = await RunCliAsync(
                "workspace-state",
                "encode",
                "--file",
                path);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Equal(CanonicalVector, result.Output.TrimEnd());

            var url = await RunCliAsync(
                "workspace-state",
                "encode",
                "--file",
                path,
                "--url");

            Assert.Equal(0, url.ExitCode);
            Assert.Empty(url.Error);
            Assert.Equal(
                $"https://dotnet-inspect.net/?w={CanonicalVector}",
                url.Output.TrimEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Encode_RejectsNonUtf8File()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-workspace-state-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                EquivalentJson,
                Encoding.Unicode,
                TestContext.Current.CancellationToken);

            var result = await RunCliAsync(
                "workspace-state",
                "encode",
                "--file",
                path);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.StartsWith("Error:", result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Encode_RejectsEmptyFilePathWithoutStackTrace()
    {
        var result = await RunCliAsync(
            "workspace-state",
            "encode",
            "--file",
            "");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            "Error: Workspace-state file path cannot be empty.",
            result.Error.Trim());
        Assert.DoesNotContain("System.ArgumentException", result.Error);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData(" ")]
    public async Task Encode_InvalidFilePathDoesNotPrintStackTrace(string path)
    {
        var result = await RunCliAsync(
            "workspace-state",
            "encode",
            "--file",
            path);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("Error:", result.Error);
        Assert.DoesNotContain("System.ArgumentException", result.Error);
        Assert.DoesNotContain(" at System.IO.", result.Error);
    }

    [Fact]
    public async Task Dash_ReadsBoundedStandardInputInBothDirections()
    {
        using var encodeInput = Utf8Stream(EquivalentJson);
        var encoded = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                encodeInput));

        Assert.Equal(0, encoded.ExitCode);
        Assert.Empty(encoded.Error);
        Assert.Equal(CanonicalVector, encoded.Output.TrimEnd());

        using var decodeInput = Utf8Stream(
            CanonicalVector + Environment.NewLine);
        var decoded = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.DecodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                decodeInput));

        Assert.Equal(0, decoded.ExitCode);
        Assert.Empty(decoded.Error);
        Assert.Equal(
            WorkspaceSharePacketCodec.SerializeJson(
                WorkspaceSharePacketCodec.Decode(
                    CanonicalVector,
                    TestContext.Current.CancellationToken)),
            decoded.Output.TrimEnd());
    }

    [Fact]
    public async Task EncodeUrl_ReadsBoundedStandardInput()
    {
        using var input = Utf8Stream(EquivalentJson + "\r\n");
        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                input,
                url: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            $"https://dotnet-inspect.net/?w={CanonicalVector}",
            result.Output.TrimEnd());
    }

    [Fact]
    public async Task MaximumPacket_DecodePipeEncode_RoundTrips()
    {
        string json = CreateMaximumJson();
        Assert.Equal(
            WorkspaceSharePacketCodec.MaxDecodedUtf8Length,
            Encoding.UTF8.GetByteCount(json));

        var encoded = await RunCliAsync("workspace-state", "encode", json);
        Assert.Equal(0, encoded.ExitCode);
        Assert.Empty(encoded.Error);
        string packet = encoded.Output.TrimEnd();
        Assert.Equal(WorkspaceSharePacketCodec.MaxEncodedLength, packet.Length);

        var decoded = await RunCliAsync(
            "workspace-state",
            "decode",
            packet);
        Assert.Equal(0, decoded.ExitCode);
        Assert.Empty(decoded.Error);
        Assert.Equal(json, decoded.Output.TrimEnd());

        using var replayInput = Utf8Stream(decoded.Output);
        var replayed = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                replayInput));
        Assert.Equal(0, replayed.ExitCode);
        Assert.Empty(replayed.Error);
        Assert.Equal(packet, replayed.Output.TrimEnd());

        var url = await RunCliAsync(
            "workspace-state", "encode", json, "--url");
        Assert.Equal(0, url.ExitCode);
        Assert.Empty(url.Error);
        Assert.Equal(
            $"https://dotnet-inspect.net/?w={packet}",
            url.Output.TrimEnd());
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    public async Task RepeatedTerminalLineEndings_DoNotBypassLimits(
        string lineEndings)
    {
        using var jsonInput = Utf8Stream(CreateMaximumJson() + lineEndings);
        var encoded = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                jsonInput));
        Assert.Equal(1, encoded.ExitCode);
        Assert.Empty(encoded.Output);
        Assert.Contains(
            $"exceeds the {WorkspaceSharePacketCodec.MaxDecodedUtf8Length}-character read limit",
            encoded.Error);

        using var packetInput = Utf8Stream(CanonicalVector + lineEndings);
        var decoded = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.DecodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                packetInput));
        Assert.Equal(1, decoded.ExitCode);
        Assert.Empty(decoded.Output);
        Assert.StartsWith("Error:", decoded.Error);
    }

    [Fact]
    public async Task Encode_RejectsInvalidUtf8FromStandardInput()
    {
        byte[] input = Encoding.UTF8.GetBytes(
            "{\"f\":1,\"t\":[[\":Platform\",null,null,null]],"
            + "\"g\":[[0]],\"a\":0,\"x\":0,\"y\":\"a?b\"}");
        input[Array.IndexOf(input, (byte)'?')] = 0xFF;
        using var standardInput = new MemoryStream(input);

        var result = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                standardInput));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("Error:", result.Error);
    }

    [Fact]
    public async Task UnicodePacket_PipesAsUtf8UnderLegacyWindowsCodePage()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Windows console code pages do not apply on this platform.");

        string executable = Path.Combine(
            AppContext.BaseDirectory,
            "dotnet-inspect.exe");
        Assert.True(
            File.Exists(executable),
            $"Expected the built CLI at {executable}.");

        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-workspace-{Guid.NewGuid():N}.cmd");
        string script =
            """
            @echo off
            chcp 437 >nul || exit /b 91
            "%~1" workspace-state decode "%~2" | "%~1" workspace-state encode -
            """;
        await File.WriteAllTextAsync(
            scriptPath,
            script,
            Encoding.ASCII,
            TestContext.Current.CancellationToken);

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(executable);
        startInfo.ArgumentList.Add(UnicodeVector);

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start cmd.exe.");
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            Task<string> output = process.StandardOutput.ReadToEndAsync(
                cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(
                cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string outputText = await output;
            string errorText = await error;
            Assert.True(
                process.ExitCode == 0,
                $"Pipeline exited {process.ExitCode}. stderr: {errorText}");
            Assert.Empty(errorText);
            Assert.Equal(UnicodeVector, outputText.TrimEnd());
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Commands_RequireExactlyOneInputSource()
    {
        var missing = await RunCliAsync("workspace-state", "encode");
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains(
            "Provide <json>, '-' for stdin, or --file <path>.",
            missing.Error);

        var conflicting = await RunCliAsync(
            "workspace-state",
            "decode",
            CanonicalVector,
            "--file",
            "packet.txt");
        Assert.Equal(1, conflicting.ExitCode);
        Assert.Contains(
            "<packet> and --file are alternate input sources",
            conflicting.Error);

        var missingUrl = await RunCliAsync(
            "workspace-state", "encode", "--url");
        Assert.Equal(1, missingUrl.ExitCode);
        Assert.Empty(missingUrl.Output);
        Assert.Contains("Provide <json>", missingUrl.Error);

        var conflictingUrl = await RunCliAsync(
            "workspace-state", "encode", EquivalentJson,
            "--file", "workspace-state.json", "--url");
        Assert.Equal(1, conflictingUrl.ExitCode);
        Assert.Empty(conflictingUrl.Output);
        Assert.Contains(
            "<json> and --file are alternate input sources",
            conflictingUrl.Error);
    }

    [Fact]
    public async Task EncodeHelp_DoesNotRequireInput()
    {
        var result = await RunCliAsync(
            "workspace-state",
            "encode",
            "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output);
        Assert.Contains("--url", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task Decode_DoesNotAcceptUrlOption()
    {
        var result = await RunCliAsync(
            "workspace-state", "decode", CanonicalVector, "--url");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--url", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidAndOversizedInput_FailsWithoutOutput(bool url)
    {
        var invalid = await RunCliAsync(
            "workspace-state", "encode", "{}", $"--url={url}");
        Assert.Equal(1, invalid.ExitCode);
        Assert.Empty(invalid.Output);
        Assert.Contains("Error: Workspace share state requires", invalid.Error);

        using var oversizedInput = Utf8Stream(new string(
            ' ',
            WorkspaceSharePacketCodec.MaxDecodedUtf8Length + 3));
        var oversized = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                oversizedInput,
                url: url));
        Assert.Equal(1, oversized.ExitCode);
        Assert.Empty(oversized.Output);
        Assert.Contains(
            $"exceeds the {WorkspaceSharePacketCodec.MaxDecodedUtf8Length}-character read limit",
            oversized.Error);

        using var oversizedPacketInput = Utf8Stream(new string(
            'A',
            WorkspaceSharePacketCodec.MaxEncodedLength + 1));
        var oversizedPacket = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.DecodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                oversizedPacketInput));
        Assert.Equal(1, oversizedPacket.ExitCode);
        Assert.Empty(oversizedPacket.Output);
        Assert.Contains(
            $"exceeds the {WorkspaceSharePacketCodec.MaxEncodedLength}-character read limit",
            oversizedPacket.Error);
    }

    [Fact]
    public void WorkspaceState_IsReservedForExplicitRouting()
    {
        Assert.Contains(
            "workspace-state",
            CommandLineBuilder.KnownCommands);
    }

    private static string CreateMaximumJson()
    {
        const string prefix =
            "{\"f\":1,\"t\":[[\":Platform\",null,null,null]],"
            + "\"g\":[[0]],\"a\":0,\"x\":0,\"l\":[\"";
        const string suffix = "\"]}";
        return prefix
            + new string(
                'A',
                WorkspaceSharePacketCodec.MaxDecodedUtf8Length
                    - prefix.Length
                    - suffix.Length)
            + suffix;
    }

    private static MemoryStream Utf8Stream(string input) =>
        new(Encoding.UTF8.GetBytes(input));

    private static Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await CommandLineBuilder.InvokeAsync(root.Parse(args));
        });
}
