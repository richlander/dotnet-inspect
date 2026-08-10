using DotnetInspector.Inspectors;
using DotnetInspector.Queries;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

public sealed class AssemblySetInspectionWorkspaceTests
{
    [Fact]
    public async Task RunPerAssembly_RetainsOnlyCurrentParticipant()
    {
        string path =
            typeof(AssemblySetInspectionWorkspaceTests).Assembly.Location;
        using var httpClient = new HttpClient();
        using AssemblySet assemblySet =
            await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Assemblies = [path, path],
                });
        using var workspace = new AssemblySetInspectionWorkspace();
        int availableCount = 0;

        workspace.RunPerAssembly(
            assemblySet,
            AssemblyContextTypeInventoryQuery.Definition,
            group => AssemblyContextTypeInventoryQuery.Execute(
                group,
                includeAll: true),
            (_, entry) =>
            {
                Assert.IsType<
                    AssemblyContextEntry<
                        AssemblyTypeInventory>.Available>(entry);
                availableCount++;
            },
            (_, failure) => Assert.Fail(failure));

        Assert.Equal(2, availableCount);
        Assert.InRange(
            workspace.PeakRetainedImageBytes,
            1,
            new FileInfo(path).Length);
    }
}
