using System.Diagnostics;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsExportSdkCompileNegativeTests
{
    [Fact]
    public async Task PromiseReturningDelegate_IsRejectedBySdkGenerator()
    {
        string projectPath = Path.GetFullPath(
            Path.Combine(
                FindRepositoryRoot(),
                "tests",
                "ILInspector.JsExportSurface.AsyncDelegateCompileNegative",
                "ILInspector.JsExportSurface.AsyncDelegateCompileNegative.csproj"));
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--nologo");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output =
            await standardOutput
            + Environment.NewLine
            + await standardError;

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(
            "AsyncDelegateExports.cs",
            output,
            StringComparison.Ordinal);
        Assert.Contains("error SYSLIB1072", output, StringComparison.Ordinal);
        Assert.Contains(
            "System.Func<int, System.Threading.Tasks.Task<int>>",
            output,
            StringComparison.Ordinal);
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }
}
