using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class DescriptorCommandConsumerTests
{
    [Fact]
    public void TypeAnalysis_UsesDescriptorInsteadOfDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        var options = new ApiOptions
        {
            AssemblyReference = TestAssemblyReferences.Designated(path),
        };

        var index = ApiAnalysisInspection.OpenTypeAnalysisIndex(
            "/path-that-must-not-be-opened.dll",
            options: options);

        Assert.NotEmpty(index.Methods);
    }

    [Fact]
    public async Task MethodSource_UsesDescriptorInsteadOfDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path);
        using var httpClient = new HttpClient();

        ApiCommand.ResolvedMethodSource result =
            await ApiCommand.ResolveMethodSourceAsync(
                "/path-that-must-not-be-opened.dll",
                assembly,
                typeof(DescriptorCommandConsumerTests).FullName!,
                nameof(TypeAnalysis_UsesDescriptorInsteadOfDisplayPath),
                overloadIndex: 0,
                new ApiOptions(),
                httpClient,
                new VerboseLogger(false),
                fetchSource: false);

        Assert.False(result.MemberHasNoBody);
        Assert.Null(result.PdbSourceUnavailableReason);
    }
}
