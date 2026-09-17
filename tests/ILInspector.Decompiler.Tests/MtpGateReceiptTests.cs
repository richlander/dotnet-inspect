using System.Diagnostics;
using System.Text.Json;
using ILInspector.Decompiler.Tests.Gating;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Collection(FidelityGateCollection.Name)]
public class MtpGateReceiptTests
{
    private const string TargetMethod =
        "ILInspector.Decompiler.Tests.GateArgumentExpanderTests."
        + "NoGateFlag_PassesArgumentsThroughUnchanged";

    [Fact]
    public async Task Host_RecordsStableDiscoveryAndExecutionIdentityExactlyOnce()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"decompiler-mtp-receipt-{Guid.NewGuid():N}");
        string discoveryPath = Path.Combine(directory, "discovery.jsonl");
        string executionPath = Path.Combine(directory, "execution.jsonl");

        try
        {
            ProcessResult discovery = await RunHostAsync(
                discoveryPath,
                MtpDiscoveryReceiptClient.ReceiptArgument,
                discoveryPath,
                "--filter-method",
                TargetMethod);
            ProcessResult execution = await RunHostAsync(
                executionPath,
                "--filter-method",
                TargetMethod);

            Assert.Equal(0, discovery.ExitCode);
            Assert.Equal(0, execution.ExitCode);

            ReceiptEntry discovered = Assert.Single(
                ReadReceipt(discoveryPath),
                entry => entry.State == "discovered");
            IReadOnlyList<ReceiptEntry> executed = ReadReceipt(executionPath);
            ReceiptEntry started = Assert.Single(
                executed,
                entry => entry.State == "started");
            ReceiptEntry passed = Assert.Single(
                executed,
                entry => entry.State == "passed");

            Assert.Equal(discovered.Uid, started.Uid);
            Assert.Equal(discovered.Uid, passed.Uid);
            Assert.Equal(discovered.Class, started.Class);
            Assert.Equal(discovered.Method, started.Method);
            Assert.Equal(discovered.MethodSignature, started.MethodSignature);
            Assert.Equal(
                "ILInspector.Decompiler.Tests.GateArgumentExpanderTests",
                discovered.Class);
            Assert.Equal(
                "NoGateFlag_PassesArgumentsThroughUnchanged",
                discovered.Method);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Host_UnmatchedMtpFilterReturnsZeroTestExitCode()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"decompiler-mtp-empty-{Guid.NewGuid():N}");
        string receiptPath = Path.Combine(directory, "execution.jsonl");

        try
        {
            ProcessResult result = await RunHostAsync(
                receiptPath,
                "--filter-method",
                "*DefinitelyMissingDecompilerTest*");

            Assert.Equal(8, result.ExitCode);
            Assert.Contains("Zero tests ran", result.Output);
            Assert.Empty(ReadReceipt(receiptPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunHostAsync(
        string receiptPath,
        params string[] args)
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (Path.GetFileNameWithoutExtension(processPath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        }

        if (!args.Contains(MtpDiscoveryReceiptClient.ReceiptArgument))
        {
            startInfo.Environment[MtpGateReceiptConsumer.ReceiptPathEnvironmentVariable] =
                receiptPath;
        }
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the decompiler test host.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await output + await error);
    }

    private static IReadOnlyList<ReceiptEntry> ReadReceipt(string path) =>
        File.ReadLines(path)
            .Select(line => JsonSerializer.Deserialize<ReceiptEntry>(
                line,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))
            .OfType<ReceiptEntry>()
            .ToList();

    private sealed record ReceiptEntry(
        string Schema,
        string Uid,
        string DisplayName,
        string? Class,
        string? Method,
        string? MethodSignature,
        string State);

    private sealed record ProcessResult(int ExitCode, string Output);
}
