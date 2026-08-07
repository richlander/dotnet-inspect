using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class InspectionViewDescriptorTests
{
    public static TheoryData<ApiMember, bool> BodyApplicabilityCases => new()
    {
        {
            new ApiMember { Name = "Run", Kind = "method", MetadataToken = 0x06000001 },
            true
        },
        {
            new ApiMember
            {
                Name = "Run",
                Kind = "method",
                MetadataToken = 0x06000001,
                IsAbstract = true
            },
            false
        },
        {
            new ApiMember { Name = "Value", Kind = "property", GetterToken = 0x06000002 },
            true
        },
        {
            new ApiMember { Name = "Value", Kind = "field" },
            false
        },
        {
            new ApiMember { Name = "Changed", Kind = "event", AdderToken = 0x06000003 },
            true
        }
    };

    [Theory]
    [MemberData(nameof(BodyApplicabilityCases))]
    public void MemberViews_ReflectExecutableBodyApplicability(ApiMember member, bool hasBodyViews)
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members = [member]
        };

        IReadOnlySet<string> ids = pipeline.GetInspectionViews(model)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.AnnotatedSource));
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.CallGraph));
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.Facts));
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.IL));
    }

    [Fact]
    public void MemberViewSelection_RoundTripsThroughOwningPipeline()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = "Run", Kind = "method", MetadataToken = 0x06000001 }
            ]
        };

        IReadOnlyList<InspectionViewDescriptor> views = pipeline.GetInspectionViews(model);
        InspectionViewDescriptor originalSource = Assert.Single(
            views,
            view => view.Id == SectionNames.OriginalSource);
        InspectionViewSelection selection = pipeline.ResolveInspectionViews(
            model,
            [SectionNames.IL, SectionNames.Signature]);

        Assert.Equal(SectionNames.OriginalSource, originalSource.Label);
        Assert.True(originalSource.MayUseNetwork);
        Assert.True(originalSource.MayFetchSourceContent);
        Assert.False(originalSource.MayDoExhaustiveWork);
        Assert.Equal(
            [SectionNames.Signature, SectionNames.IL],
            selection.Views.Select(view => view.Id));
        Assert.Equal(
            [SectionNames.Signature, SectionNames.IL],
            pipeline.GetEffectiveSections(
                model,
                Verbosity.Normal,
                new HashSet<string>(selection.SectionNames, StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void PackageViews_ExposeDefaultAndNetworkCostFromPackageCatalog()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Example.Package",
            Version = "1.0.0"
        };

        IReadOnlyList<InspectionViewDescriptor> views = pipeline.GetInspectionViews(model);
        InspectionViewDescriptor packageInfo = Assert.Single(
            views,
            view => view.Id == PackageSections.PackageInfo);
        InspectionViewDescriptor signals = Assert.Single(
            views,
            view => view.Id == PackageSections.Signals);
        InspectionViewDescriptor files = Assert.Single(
            pipeline.GetInspectionViews(
                new InspectionResult
                {
                    PackageName = "Example.Package",
                    Version = "1.0.0",
                    Files = [new PackageFile("lib/example.dll", 1, false, false)]
                }),
            view => view.Id == PackageSections.Files);

        Assert.True(packageInfo.IsDefault);
        Assert.True(packageInfo.IsHighValue);
        Assert.False(packageInfo.MayUseNetwork);
        Assert.True(signals.MayUseNetwork);
        Assert.False(signals.IsDefault);
        Assert.True(files.MayDoExhaustiveWork);
        Assert.False(files.MayUseNetwork);
        Assert.Contains(
            PackageSections.PackageInfo,
            pipeline.ResolveInspectionViews(model, [packageInfo.Id]).SectionNames);
    }

    [Fact]
    public void PlatformLibraryViews_UseLibraryCatalogApplicability()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            Source = "Platform (runtime)",
            PlatformVersion = "11.0.0",
            AssemblyInfo = new AssemblyInfo()
        };

        IReadOnlyList<InspectionViewDescriptor> views = pipeline.GetInspectionViews(model);
        InspectionViewDescriptor libraryInfo = Assert.Single(
            views,
            view => view.Id == SectionNames.LibraryInfo);

        Assert.True(libraryInfo.IsApplicable);
        Assert.True(libraryInfo.IsAvailable);
        Assert.True(libraryInfo.IsDefault);
        Assert.Contains(
            SectionNames.LibraryInfo,
            pipeline.ResolveInspectionViews(model, [libraryInfo.Id]).SectionNames);
    }

    [Fact]
    public void PlatformRuntimePackageViews_ExcludePackageDependencyQuery()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Microsoft.NETCore.App",
            Version = "11.0.0",
            Source = "localized display text",
            IsPlatformRuntime = true,
            DependencyGroups = [new DependencyGroup()]
        };

        IReadOnlySet<string> ids = pipeline.GetInspectionViews(model)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(PackageSections.Dependencies, ids);
    }

    [Fact]
    public void ViewSelection_RejectsUnknownAndInapplicableIds()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var field = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members = [new ApiMember { Name = "Value", Kind = "field" }]
        };
        var abstractMethod = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Run",
                    Kind = "method",
                    MetadataToken = 0x06000001,
                    IsAbstract = true
                }
            ]
        };

        Assert.Throws<ArgumentException>(
            () => pipeline.ResolveInspectionViews(field, ["missing-view"]));
        Assert.Throws<InvalidOperationException>(
            () => pipeline.ResolveInspectionViews(field, [SectionNames.IL]));
        InspectionViewDescriptor abstractIl = Assert.Single(
            pipeline.GetInspectionViews(abstractMethod, includeInapplicable: true),
            view => view.Id == SectionNames.IL);
        Assert.False(abstractIl.IsAvailable);
        Assert.True(abstractIl.CanRender);
        Assert.Throws<InvalidOperationException>(
            () => pipeline.ResolveInspectionViews(abstractMethod, [SectionNames.IL]));
    }
}
