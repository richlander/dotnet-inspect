using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceResearchTargetDemoTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WorkspaceResearchTarget_DemoCoversForwardedAndMissingParticipant()
    {
        string repository = FindRepositoryRoot();
        string scratch = Path.Combine(repository, "artifacts", "workspace-target-demo-gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string config = Path.Combine(scratch, "NuGet.Config");
            // The test build already restored this reference graph. Never contact a feed here.
            await File.WriteAllTextAsync(config,
                "<configuration><packageSources><clear /></packageSources></configuration>",
                TestContext.Current.CancellationToken);
            string executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            string root = Environment.GetEnvironmentVariable("DOTNET_ROOT")
                ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
            var start = new ProcessStartInfo(Path.Combine(root, executable))
            {
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[]
            {
                "run", "eng/demo-workspace-target-composition.cs", "--configuration", "Release",
                $"--property:RestoreConfigFile={config}", "--property:NuGetAudit=false",
            })
                start.ArgumentList.Add(argument);
            start.Environment["DOTNET_ROOT"] = root;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the public workspace composition demo.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            string output = await stdout;
            string error = await stderr;
            Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\n{output}\n{error}");

            string forwarded = Section(output, "Forwarded both sides", "Missing participant");
            foreach (string side in new[] { "Before", "After" })
            {
                Assert.Contains($"{side} root: ContractsFacade", forwarded);
                Assert.Contains($"{side} local attempt: Unavailable / DeclaringTypeForwarded", forwarded);
                Assert.Contains($"{side} route: ContractsFacade -> ContractsImplementation", forwarded);
                Assert.Contains($"{side} effective target: ContractsImplementation", forwarded);
            }
            Assert.Contains("Effective-attempt pair: Paired", forwarded);
            Assert.Contains("Comparison work item: Paired", forwarded);
            string missing = Section(output, "Missing participant", "Divergent domains");
            Assert.Contains("After local attempt: Unavailable / DeclaringTypeForwarded", missing);
            Assert.Contains("After route: ContractsFacade -> ContractsImplementation", missing);
            Assert.Contains("After composition: Unavailable / UnboundBinding", missing);
            Assert.Contains("Effective target: none", missing);
            Assert.DoesNotContain("After address:", missing);
            string divergent = Section(output, "Divergent domains");
            Assert.Contains("Before effective domain: ContractsImplementation", divergent);
            Assert.Contains("After effective domain: ReplacementImplementation", divergent);
            Assert.Contains("Before-domain correspondence: BeforeOnly", divergent);
            Assert.Contains("After-domain correspondence: AfterOnly", divergent);
            Assert.Contains("Effective-attempt pair: none", divergent);
            Assert.Contains("Comparison work item: none", divergent);
            foreach (string section in new[] { forwarded, missing, divergent })
                Assert.Contains("Supplemental acquisition requests: 0", section);
            MatchCollection addresses = Regex.Matches(output, @"(?:Before|After) address: ([0-9a-f-]{36}):0x([0-9A-F]{8})");
            Assert.Equal(5, addresses.Count);
            foreach (Match address in addresses)
            {
                Assert.True(Guid.TryParseExact(address.Groups[1].Value, "D", out Guid mvid));
                Assert.NotEqual(Guid.Empty, mvid);
                Assert.Equal("06000001", address.Groups[2].Value);
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    static string Section(string output, string heading, string? next = null)
    {
        string[] parts = output.Split($"=== {heading} ===", StringSplitOptions.None);
        Assert.Equal(2, parts.Length);
        return next is null ? parts[1] : parts[1].Split($"=== {next} ===", StringSplitOptions.None)[0];
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
