using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
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
    public void MemberAnalysisAndExceptionRegions_UseDescriptorInsteadOfDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path);
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(
                assembly,
                includeAll: true));
        ApiType type = Assert.Single(
            api.Types,
            type => type.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        ApiMember method = Assert.Single(
            type.Members,
            member => member.Name == nameof(ExceptionRegionFixture));
        var options = new ApiOptions
        {
            AssemblyReference = assembly,
        };
        var inspection = new ApiMemberAnalysisInspection(
            "/path-that-must-not-be-opened.dll",
            [method],
            new HashSet<string> { SectionNames.ExceptionRegions },
            callerScopeAssemblies: null,
            options);

        Assert.NotEmpty(inspection.BodyIndex.Methods);
        Assert.NotEmpty(
            inspection.ResolveExceptionRegions(
                method.MetadataToken!.Value,
                out string? memberError));
        Assert.Null(memberError);
        Assert.NotEmpty(
            ApiAnalysisInspection.ResolveExceptionRegions(
                "/path-that-must-not-be-opened.dll",
                assembly,
                [method]));
    }

    [Fact]
    public void PathlessApiOwnership_DoesNotFallBackToDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path).WithoutLocalPath();
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(assembly));
        ApiType type = Assert.Single(
            api.Types,
            type => type.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        var loaded = new ApiServices.LoadedApiSurface(
            api,
            "/display-only.dll",
            "/display-only.dll",
            assembly,
            RuntimeAssemblyReference: null);

        Assert.Null(type.SourceAssemblyPath);
        Assert.Same(
            assembly,
            ApiServices.AssemblyReferenceForPath(
                loaded,
                type,
                "/display-only.dll"));
    }

    [Fact]
    public async Task SourceFileCollection_UsesDescriptorBackedApiSurface()
    {
        string path =
            FixtureCatalog.SourceLinkNormalized.AssemblyPath();
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path);
        using var service = SourceLinkService.Open(assembly);

        List<SourceFileInfo> files =
            await SourceFileCollector.CollectAsync(service, assembly);

        Assert.True(service.HasSourceLink);
        Assert.NotEmpty(files);
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

    static int ExceptionRegionFixture()
    {
        try
        {
            return 1;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}
