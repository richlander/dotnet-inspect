using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class AuditSignalRefreshTests
{
    [Fact]
    public async Task AuditSignalRefresh_DoesNotReopenTheAssembly()
    {
        // Retarget the command path after every typed Signals input has run but before source
        // auditing and the final refresh. Reopening that path during refresh would silently
        // replace A's audit evidence with B's while the rest of the model still describes A.
        var pathA = typeof(AuditSignalRefreshTests).Assembly.Location;
        var pathB = typeof(AssemblyInspectionSession).Assembly.Location;
        var expectedA = PInvokeCount(pathA);
        var expectedB = PInvokeCount(pathB);
        Assert.NotEqual(expectedA, expectedB);
        CoreCache.Initialize("dotnet-inspect-test");

        var root = Path.Combine(Path.GetTempPath(), $"audit-refresh-{Guid.NewGuid():N}");
        var dirA = Path.Combine(root, "a");
        var dirB = Path.Combine(root, "b");
        var link = Path.Combine(root, "active");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        File.Copy(pathA, Path.Combine(dirA, "lib.dll"));
        File.Copy(pathB, Path.Combine(dirB, "lib.dll"));

        try
        {
            if (!TryLinkDirectory(link, dirA))
            {
                throw new InvalidOperationException(
                    $"Could not create a directory link at '{link}'. On Windows this needs " +
                    "Developer Mode, admin, or working `mklink /J`.");
            }

            var linkedAssembly = Path.Combine(link, "lib.dll");
            var retarget = new InspectionQuery<int>(
                "retarget assembly path after audit inputs",
                InspectionCost.NetworkFree);
            var registry = LibrarySections.CreateQueryRegistry()
                .Add(
                    retarget,
                    _ =>
                    {
                        if (!TryLinkDirectory(link, dirB))
                            throw new InvalidOperationException("Could not retarget the assembly path.");
                        return 0;
                    },
                    AssemblyReferencesQuery.Definition,
                    AuditMetadataQuery.Definition,
                    ClassifiedMethodsQuery.Definition);
            using var httpClient = new HttpClient();

            var inspection = await LibraryMetadataService.InspectAsync(
                linkedAssembly,
                new LibraryOptions(),
                new VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [retarget],
                queryRegistry: registry);

            Assert.NotNull(inspection);
            Assert.Equal(expectedB, PInvokeCount(linkedAssembly));
            Assert.Equal(expectedA, inspection.AuditMetadata?.PInvokeMethodCount);
            var signal = Assert.Single(
                inspection.AuditSignals!,
                candidate => candidate.Signal == "P/Invoke methods");
            Assert.Equal(expectedA.ToString(), signal.Value);
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    private static int PInvokeCount(string path)
    {
        using var session = AssemblyInspectionSession.Open(path);
        return Assert.IsType<AuditMetadataResult.Available>(
            AuditMetadataQuery.Execute(session)).Metadata.PInvokeMethodCount;
    }

    private static bool TryLinkDirectory(string link, string target)
    {
        if (Directory.Exists(link))
            Directory.Delete(link);

        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
    }
}
