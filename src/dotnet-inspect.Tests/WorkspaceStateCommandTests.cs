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
    public async Task Dash_ReadsBoundedStandardInputInBothDirections()
    {
        var encoded = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                new StringReader(EquivalentJson)));

        Assert.Equal(0, encoded.ExitCode);
        Assert.Empty(encoded.Error);
        Assert.Equal(CanonicalVector, encoded.Output.TrimEnd());

        var decoded = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.DecodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                new StringReader(CanonicalVector + Environment.NewLine)));

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
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task InvalidAndOversizedInput_FailsWithoutOutput()
    {
        var invalid = await RunCliAsync("workspace-state", "encode", "{}");
        Assert.Equal(1, invalid.ExitCode);
        Assert.Empty(invalid.Output);
        Assert.Contains("Error: Workspace share state requires", invalid.Error);

        var oversized = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.EncodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                new StringReader(new string(
                    ' ',
                    WorkspaceSharePacketCodec.MaxDecodedUtf8Length + 3))));
        Assert.Equal(1, oversized.ExitCode);
        Assert.Empty(oversized.Output);
        Assert.Contains(
            $"exceeds the {WorkspaceSharePacketCodec.MaxDecodedUtf8Length}-character read limit",
            oversized.Error);

        var oversizedPacket = await ConsoleCapture.RunAsync(
            () => WorkspaceStateCommand.DecodeAsync(
                "-",
                file: null,
                TestContext.Current.CancellationToken,
                new StringReader(new string(
                    'A',
                    WorkspaceSharePacketCodec.MaxEncodedLength + 1))));
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

    private static Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await CommandLineBuilder.InvokeAsync(root.Parse(args));
        });
}
