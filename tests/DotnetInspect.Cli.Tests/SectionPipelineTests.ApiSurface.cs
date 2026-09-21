using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Findings;
using ILInspector.Research;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    // ===== API type-list pipeline tests =====

    [Fact]
    public void ApiTypePipeline_HasExpectedSectionCount()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        Assert.Equal(8, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void ApiTypePipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains(SectionNames.ApiInfo, names);
        Assert.Contains(SectionNames.TypeForwarders, names);
        Assert.Contains("Classes", names);
        Assert.Contains("Structs", names);
        Assert.Contains("Interfaces", names);
        Assert.Contains("Enums", names);
        Assert.Contains("Delegates", names);
        Assert.Contains(
            SectionNames.InspectionFailures,
            names);
    }

    [Fact]
    public void ApiTypePipeline_UsesAuthoredSurfaceCategoryWithoutComputedPoles()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();

        var category = Assert.Single(pipeline.GetCategoryMap());
        Assert.Equal(SectionCategoryNames.Surface, category.Key);
        Assert.Equal(pipeline.AllSectionNames, category.Value);
        Assert.Equal([SectionCategoryNames.Surface], pipeline.GetBaseCategoryDoors());
    }

    [Theory]
    [InlineData("Classes")]
    [InlineData("Structs")]
    [InlineData("Interfaces")]
    [InlineData("Enums")]
    [InlineData("Delegates")]
    [InlineData(SectionNames.TypeForwarders)]
    [InlineData(SectionNames.InspectionFailures)]
    public void ApiTypePipeline_SurfaceInventoriesDeclareMeasuredGrowth(
        string section)
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var include =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                section,
            };

        Assert.Contains(
            section,
            pipeline.GetCandidateSections(Verbosity.Minimal));
        Assert.DoesNotContain(
            section,
            pipeline.GetCandidateSections(Verbosity.Normal));
        Assert.Contains(
            section,
            pipeline.GetCandidateSections(Verbosity.Detailed));
        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(include));
    }

    [Fact]
    public void ApiTypePipeline_SurfaceInventoryDescriptorsAreVerbose()
    {
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.Classes.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.Structs.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.Interfaces.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.Enums.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.Delegates.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.TypeForwarders.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiTypeSectionDescriptors.InspectionFailures.SizeClass);
    }

    [Fact]
    public void ApiTypePipeline_ShowsClassesWhenPresent()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface { Types = [new ApiType { Name = "Foo", Kind = "class" }] };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Classes", effective);
        Assert.DoesNotContain("Structs", effective);
    }

    [Fact]
    public void ApiTypePipeline_MixedTypesAndForwardersShowsBoth()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface
        {
            Types = [new ApiType { Name = "Foo", Kind = "class" }],
            TypeForwarders =
            [
                new TypeForwarder
                {
                    TypeName = "Forwarded",
                    TargetAssembly = "Target",
                },
            ],
        };

        var effective = pipeline.GetEffectiveSections(
            model,
            Verbosity.Minimal);

        Assert.Contains("Classes", effective);
        Assert.Contains(SectionNames.TypeForwarders, effective);
    }

    [Fact]
    public void ApiTypePipeline_EmptyTypes_NoSections()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface { Types = [] };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Empty(effective);
    }

    [Fact]
    public void ApiTypePipeline_AllKindsPresent()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Equal(5, effective.Count);
    }

    [Fact]
    public void ApiTypePipeline_NormalOmitsVerboseSurfaceInventories()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
            ],
        };

        Assert.Empty(
            pipeline.GetEffectiveSections(model, Verbosity.Normal));
    }

    [Fact]
    public void ApiTypePipeline_ForwarderAndFailureRowsCanExceedInformativeRange()
    {
        var model = new ApiSurface
        {
            TypeForwarders =
            [
                .. Enumerable.Range(0, 31).Select(index =>
                    new TypeForwarder
                    {
                        TypeName = $"Forwarded{index}",
                        TargetAssembly = $"Target{index}",
                    }),
            ],
            InspectionFailures =
            [
                .. Enumerable.Range(0, 31).Select(index =>
                    new ApiSurfaceInspectionFailure(
                        "decode type",
                        0x02000001 + index,
                        MetadataTypeNameFailureMechanism.Metadata,
                        "MalformedMetadata",
                        $"Failure {index}")),
            ],
        };

        var (view, _) = ApiOutputFormatter.BuildFullApiView(
            model,
            new ApiOptions
            {
                Verbosity = Verbosity.Detailed,
            });

        Assert.Equal(31, view.TypeForwarders!.Count);
        Assert.Equal(31, view.InspectionFailures!.Count);
    }

    // ===== API member pipeline tests =====

    [Fact]
    public void ApiMemberPipeline_HasExpectedSectionCount()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        Assert.Equal(36, pipeline.AllSectionNames.Length);
        Assert.Contains(SectionNames.CloneCandidates, pipeline.AllSectionNames);
    }

    [Fact]
    public void LibraryPipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(["Library Info"], pipeline.InfoSectionNames);
    }

    [Fact]
    public void LibraryPipeline_RegistersEcosystemDependencies()
    {
        var catalog = LibrarySections.CreateCatalog().Sections;

        Assert.Contains(
            SectionNames.EcosystemDependencies,
            catalog.SelectableSectionNames);
        Assert.Contains(
            SectionNames.EcosystemDependencies,
            catalog.SelectionCategoryMap[SectionCategoryNames.Library]);
        Assert.Contains(
            SectionNames.EcosystemDependencies,
            catalog.SelectionCategoryMap[SectionCategoryNames.Dependencies]);
    }

    [Fact]
    public void ApiMemberPipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Type Info", names);
        Assert.Contains("Values", names);
        Assert.Contains("Type Parameters", names);
        Assert.Contains("Interfaces", names);
        Assert.Contains("Performance Triage", names);
        Assert.Contains("Body Shapes", names);
        Assert.Contains("Baseclass", names);
        Assert.Contains("Constructors", names);
        Assert.Contains("Fields", names);
        Assert.Contains("Properties", names);
        Assert.Contains("Method Groups", names);
        Assert.Contains("Methods", names);
        Assert.Contains("Operators", names);
        Assert.Contains("Explicit Interface Implementations", names);
        Assert.Contains("Extension Methods", names);
        Assert.Contains("Events", names);
        Assert.Contains("Source Files", names);
        Assert.Contains("IL", names);
        Assert.Contains("Decompiled Source", names);
        Assert.Contains("PDB Source", names);
        Assert.Contains("Source Diff", names);
        Assert.Contains("Custom Attributes", names);
        Assert.Contains("Called Types", names);
        Assert.Contains("Top Leverage", names);
    }

    [Fact]
    public void ApiMemberPipeline_DescriptorsDeclareAuditedGrowth()
    {
        Assert.Equal(
            SectionSizeClass.Fixed,
            ApiMemberSectionDescriptors.TypeInfo.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Values.SizeClass);
        Assert.Equal(
            SectionSizeClass.Informative,
            ApiMemberSectionDescriptors.TypeParameters.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.TypeInterfaces.SizeClass);
        Assert.Equal(
            SectionSizeClass.Fixed,
            ApiMemberSectionDescriptors.Baseclass.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Constructors.SizeClass);
        Assert.Equal(
            SectionSizeClass.Fixed,
            ApiMemberSectionDescriptors.Finalizer.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Fields.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Properties.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.MethodGroups.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Methods.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Operators.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors
                .ExplicitInterfaceImplementations
                .SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.ExtensionMethods.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.Events.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.MethodAttributes.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.DecompiledSource.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.PdbSource.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberSectionDescriptors.ILBody.SizeClass);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_MethodsDeclareAuditedGrowth()
    {
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberOverloadSectionDescriptors.Methods.SizeClass);
    }

    [Fact]
    public void ApiMemberDetailPipeline_DescriptorsDeclareAuditedGrowth()
    {
        Assert.Equal(
            SectionSizeClass.Fixed,
            ApiMemberDetailSectionDescriptors.Signature.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberDetailSectionDescriptors.MethodAttributes.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberDetailSectionDescriptors.DecompiledSource.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberDetailSectionDescriptors.PdbSource.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ApiMemberDetailSectionDescriptors.ILBody.SizeClass);
    }

    [Fact]
    public void
        ApiMemberDomainAndUncategorizedDescriptors_DeclareAuditedGrowth()
    {
        (string Name, SectionSizeClass SizeClass)[] verbose =
        [
            (ApiMemberSectionDescriptors.MemberIndex.Name,
                ApiMemberSectionDescriptors.MemberIndex.SizeClass),
            (ApiMemberSectionDescriptors.CostOverlay.Name,
                ApiMemberSectionDescriptors.CostOverlay.SizeClass),
            (ApiMemberSectionDescriptors.SemanticsOverlay.Name,
                ApiMemberSectionDescriptors.SemanticsOverlay.SizeClass),
            (ApiMemberSectionDescriptors.UnsafeMembers.Name,
                ApiMemberSectionDescriptors.UnsafeMembers.SizeClass),
            (ApiMemberSectionDescriptors.CloneCandidates.Name,
                ApiMemberSectionDescriptors.CloneCandidates.SizeClass),
            (ApiMemberSectionDescriptors.ExceptionRegions.Name,
                ApiMemberSectionDescriptors.ExceptionRegions.SizeClass),
            (ApiMemberSectionDescriptors.CalledTypes.Name,
                ApiMemberSectionDescriptors.CalledTypes.SizeClass),
            (ApiMemberSectionDescriptors.AllocationFacts.Name,
                ApiMemberSectionDescriptors.AllocationFacts.SizeClass),
            (ApiMemberSectionDescriptors.SafetyFacts.Name,
                ApiMemberSectionDescriptors.SafetyFacts.SizeClass),
            (ApiMemberSectionDescriptors.CostFacts.Name,
                ApiMemberSectionDescriptors.CostFacts.SizeClass),
            (ApiMemberSectionDescriptors.TopLeverage.Name,
                ApiMemberSectionDescriptors.TopLeverage.SizeClass),
            (ApiMemberSectionDescriptors.TypeMetrics.Name,
                ApiMemberSectionDescriptors.TypeMetrics.SizeClass),
            (ApiMemberSectionDescriptors.OptimizationOpportunities.Name,
                ApiMemberSectionDescriptors.OptimizationOpportunities.SizeClass),
            (ApiMemberSectionDescriptors.SourceLocations.Name,
                ApiMemberSectionDescriptors.SourceLocations.SizeClass),
            (ApiMemberSectionDescriptors.SourceFiles.Name,
                ApiMemberSectionDescriptors.SourceFiles.SizeClass),
            (ApiMemberSectionDescriptors.Facts.Name,
                ApiMemberSectionDescriptors.Facts.SizeClass),
            (ApiMemberDetailSectionDescriptors.AnnotatedSource.Name,
                ApiMemberDetailSectionDescriptors.AnnotatedSource.SizeClass),
            (ApiMemberDetailSectionDescriptors.AnnotatedSourceDocument.Name,
                ApiMemberDetailSectionDescriptors.AnnotatedSourceDocument.SizeClass),
            (ApiMemberDetailSectionDescriptors.FindingCensus.Name,
                ApiMemberDetailSectionDescriptors.FindingCensus.SizeClass),
            (ApiMemberDetailSectionDescriptors.FidelityCauses.Name,
                ApiMemberDetailSectionDescriptors.FidelityCauses.SizeClass),
            (ApiMemberDetailSectionDescriptors.AppliedTaste.Name,
                ApiMemberDetailSectionDescriptors.AppliedTaste.SizeClass),
            (ApiMemberDetailSectionDescriptors.CostOverlay.Name,
                ApiMemberDetailSectionDescriptors.CostOverlay.SizeClass),
            (ApiMemberDetailSectionDescriptors.SemanticsOverlay.Name,
                ApiMemberDetailSectionDescriptors.SemanticsOverlay.SizeClass),
            (ApiMemberDetailSectionDescriptors.SourceDiff.Name,
                ApiMemberDetailSectionDescriptors.SourceDiff.SizeClass),
            (ApiMemberDetailSectionDescriptors.ExceptionRegions.Name,
                ApiMemberDetailSectionDescriptors.ExceptionRegions.SizeClass),
            (ApiMemberDetailSectionDescriptors.Calls.Name,
                ApiMemberDetailSectionDescriptors.Calls.SizeClass),
            (ApiMemberDetailSectionDescriptors.Callers.Name,
                ApiMemberDetailSectionDescriptors.Callers.SizeClass),
            (ApiMemberDetailSectionDescriptors.CallGraph.Name,
                ApiMemberDetailSectionDescriptors.CallGraph.SizeClass),
            (ApiMemberDetailSectionDescriptors.UnsafeOperations.Name,
                ApiMemberDetailSectionDescriptors.UnsafeOperations.SizeClass),
            (ApiMemberDetailSectionDescriptors.BodyShapes.Name,
                ApiMemberDetailSectionDescriptors.BodyShapes.SizeClass),
            (ApiMemberDetailSectionDescriptors.BodyShapeSummary.Name,
                ApiMemberDetailSectionDescriptors.BodyShapeSummary.SizeClass),
            (ApiMemberDetailSectionDescriptors.Facts.Name,
                ApiMemberDetailSectionDescriptors.Facts.SizeClass),
        ];

        Assert.All(
            verbose,
            section => Assert.True(
                section.SizeClass == SectionSizeClass.Verbose,
                $"{section.Name} must declare Verbose growth."));
        Assert.Equal(
            SectionSizeClass.Fixed,
            ApiMemberDetailSectionDescriptors.SourceLocations.SizeClass);
    }

    [Fact]
    public void
        ApiMemberPipelines_DomainAndUncategorizedSelectionsUseAuditedGrowth()
    {
        AssertAuditedGrowth(
            ApiMemberSectionDescriptors.CreatePipeline(),
            new Dictionary<string, Verbosity>(
                StringComparer.OrdinalIgnoreCase));
        AssertAuditedGrowth(
            ApiMemberOverloadSectionDescriptors.CreatePipeline(),
            new Dictionary<string, Verbosity>(StringComparer.OrdinalIgnoreCase)
            {
                [SectionNames.Signature] = Verbosity.Minimal,
            });
        AssertAuditedGrowth(
            ApiMemberDetailSectionDescriptors.CreatePipeline(),
            new Dictionary<string, Verbosity>(StringComparer.OrdinalIgnoreCase)
            {
                [SectionNames.SourceLocations] = Verbosity.Normal,
            });

        static void AssertAuditedGrowth(
            SectionPipeline<ApiType> pipeline,
            IReadOnlyDictionary<string, Verbosity> bounded)
        {
            var categories = ApiMemberSectionPipelines.GetCategoryMap(pipeline);
            HashSet<string> categorized =
            [
                .. categories.SelectMany(pair => pair.Value),
            ];
            string[] audited =
            [
                .. categories
                    .Where(pair => pair.Key != SectionCategoryNames.Member)
                    .SelectMany(pair => pair.Value)
                    .Concat(pipeline.SelectableSectionNames.Where(
                        section => !categorized.Contains(section)))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];

            foreach (string section in audited)
            {
                Verbosity expected = bounded.GetValueOrDefault(
                    section,
                    Verbosity.Detailed);
                Assert.Equal(
                    expected,
                    pipeline.GetRequiredVerbosity(
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            section,
                        }));
            }
        }
    }

    [Theory]
    [InlineData(SectionNames.Values)]
    [InlineData(SectionNames.TypeInterfaces)]
    [InlineData(SectionNames.Constructors)]
    [InlineData(SectionNames.Fields)]
    [InlineData(SectionNames.Properties)]
    [InlineData(SectionNames.MethodGroups)]
    [InlineData(SectionNames.Operators)]
    [InlineData(SectionNames.ExplicitInterfaceImplementations)]
    [InlineData(SectionNames.ExtensionMethods)]
    [InlineData(SectionNames.Events)]
    public void
        ApiMemberPipeline_InfoVerboseSectionsStayMinimalSkipNormalReturnDetailed(
            string section)
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        Assert.Contains(
            section,
            pipeline.GetCandidateSections(Verbosity.Minimal));
        Assert.DoesNotContain(
            section,
            pipeline.GetCandidateSections(Verbosity.Normal));
        Assert.Contains(
            section,
            pipeline.GetCandidateSections(Verbosity.Detailed));
        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    section,
                }));
    }

    [Fact]
    public void ApiMemberPipeline_InformativeAndFixedSectionsStayBounded()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        foreach (string section in new[]
        {
            SectionNames.TypeParameters,
            SectionNames.Baseclass,
            SectionNames.Finalizer,
        })
        {
            Assert.Contains(
                section,
                pipeline.GetCandidateSections(Verbosity.Minimal));
            Assert.Contains(
                section,
                pipeline.GetCandidateSections(Verbosity.Normal));
            Assert.Contains(
                section,
                pipeline.GetCandidateSections(Verbosity.Detailed));
            Assert.Equal(
                Verbosity.Minimal,
                pipeline.GetRequiredVerbosity(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        section,
                    }));
        }
    }

    [Fact]
    public void ApiMemberPipeline_NonInfoVerboseSectionsRequireDetailed()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        foreach (string section in new[]
        {
            SectionNames.Methods,
            SectionNames.CustomAttributes,
            SectionNames.DecompiledSource,
            SectionNames.PdbSource,
            SectionNames.IL,
        })
        {
            Assert.Equal(
                Verbosity.Detailed,
                pipeline.GetRequiredVerbosity(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        section,
                    }));
        }

        Assert.DoesNotContain(
            SectionNames.Methods,
            pipeline.GetCandidateSections(Verbosity.Minimal));
        Assert.DoesNotContain(
            SectionNames.Methods,
            pipeline.GetCandidateSections(Verbosity.Normal));
        Assert.Contains(
            SectionNames.Methods,
            pipeline.GetCandidateSections(Verbosity.Detailed));
    }

    [Fact]
    public void
        ApiMemberPipeline_InfoVerboseSectionsPreserveAuthoredMinimalBehavior()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Interfaces = ["ISample"],
            Members =
            [
                new ApiMember { Name = "Field", Kind = "field" },
                new ApiMember { Name = ".ctor", Kind = "constructor" },
                new ApiMember { Name = "Property", Kind = "property" },
                new ApiMember { Name = "Method", Kind = "method" },
                new ApiMember { Name = "op_Addition", Kind = "operator" },
                new ApiMember
                {
                    Name = "ISample.Run",
                    Kind = "explicit-interface-implementation",
                },
                new ApiMember
                {
                    Name = "Extend",
                    Kind = "extension-method",
                },
                new ApiMember { Name = "Changed", Kind = "event" },
            ],
        };
        string[] sections =
        [
            SectionNames.TypeInterfaces,
            SectionNames.Constructors,
            SectionNames.Fields,
            SectionNames.Properties,
            SectionNames.MethodGroups,
            SectionNames.Operators,
            SectionNames.ExplicitInterfaceImplementations,
            SectionNames.ExtensionMethods,
            SectionNames.Events,
        ];

        var minimal =
            pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var normal =
            pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var detailed =
            pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        foreach (string section in sections)
        {
            Assert.Contains(section, minimal);
            Assert.DoesNotContain(section, normal);
            Assert.Contains(section, detailed);
        }
    }

    [Fact]
    public void ApiMemberPipelines_UseAuthoredCategoriesWithoutComputedPoles()
    {
        var broad = ApiMemberSectionDescriptors.CreatePipeline();
        var overload = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        var detail = ApiMemberDetailSectionDescriptors.CreatePipeline();
        string[] expectedCategories =
        [
            SectionCategoryNames.Member,
            SectionCategoryNames.Audit,
            SectionCategoryNames.Calls,
            SectionCategoryNames.Decompiler,
            SectionCategoryNames.Performance,
            SectionCategoryNames.Source,
            SectionCategoryNames.SourceLink,
        ];

        foreach (var pipeline in new[] { broad, overload, detail })
        {
            var categories = pipeline.GetCategoryMap();
            Assert.Equal(expectedCategories, categories.Keys);
            Assert.DoesNotContain(SectionPipeline<ApiType>.AllCategory, categories.Keys);
            Assert.DoesNotContain(SectionPipeline<ApiType>.HiddenCategory, categories.Keys);
        }

        Assert.Contains(SectionNames.MethodGroups, broad.BaseSectionNames);
        Assert.Contains(SectionNames.Methods, broad.BaseSectionNames);
        Assert.Contains(
            SectionNames.UnsafeMembers,
            broad.GetCategoryMap()[SectionCategoryNames.Audit]);
        Assert.Contains(
            SectionNames.CalledTypes,
            broad.GetCategoryMap()[SectionCategoryNames.Calls]);
        Assert.Contains(
            SectionNames.SourceFiles,
            broad.GetCategoryMap()[SectionCategoryNames.SourceLink]);

        Assert.Contains(SectionNames.Methods, overload.BaseSectionNames);
        Assert.Contains(
            SectionNames.SourceLocations,
            overload.GetCategoryMap()[SectionCategoryNames.SourceLink]);

        Assert.Equal(
            [
                SectionNames.Signature,
                SectionNames.CustomAttributes,
                SectionNames.DecompiledSource,
                SectionNames.PdbSource,
                SectionNames.IL,
            ],
            detail.BaseSectionNames);
        Assert.Contains(
            SectionNames.UnsafeOperations,
            detail.GetCategoryMap()[SectionCategoryNames.Audit]);
        Assert.Contains(
            SectionNames.CallGraph,
            detail.GetCategoryMap()[SectionCategoryNames.Calls]);
        Assert.Contains(
            SectionNames.Facts,
            detail.GetCategoryMap()[SectionCategoryNames.Decompiler]);

        string[] Uncategorized(SectionPipeline<ApiType> pipeline)
        {
            HashSet<string> categorized =
            [
                .. ApiMemberSectionPipelines
                    .GetCategoryMap(pipeline)
                    .SelectMany(pair => pair.Value),
            ];
            return
            [
                .. pipeline.SelectableSectionNames.Where(
                    section => !categorized.Contains(section)),
            ];
        }

        Assert.Equal(
            [
                SectionNames.MemberIndex,
                SectionNames.TypeMetrics,
                SectionNames.ApiDeclarations,
                SectionNames.CloneCandidates,
            ],
            Uncategorized(broad));
        Assert.Equal(
            [
                SectionNames.Signature,
                SectionNames.MemberIndex,
                SectionNames.CustomAttributes,
                SectionNames.FindingCensus,
                SectionNames.CloneCandidates,
                SectionNames.MemberMetrics,
            ],
            Uncategorized(overload));
        Assert.Equal(
            [
                SectionNames.FindingCensus,
                SectionNames.CloneCandidates,
                SectionNames.MemberMetrics,
            ],
            Uncategorized(detail));
    }

    [Fact]
    public void ApiMemberPipeline_EnumValues_PrimaryAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Color", Kind = "enum",
            Members = [new ApiMember { Name = "Red", Kind = "field", EnumValue = 0 }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var atNormal = pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var atDetailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        // Values is an authored minimal member overview for enums.
        Assert.Contains("Values", atMinimal);
        Assert.DoesNotContain("Values", atNormal);
        Assert.Contains("Values", atDetailed);
    }

    [Fact]
    public void ApiMemberPipeline_TypeParameters_ShowsThroughNormal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "List", Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var atNormal = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        // Generic identity is part of the authored member overview.
        Assert.Contains("Type Parameters", atMinimal);
        Assert.Contains("Type Parameters", atNormal);
    }

    [Fact]
    public void ApiMemberPipeline_Interfaces_SkipsGenericNormal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Interfaces = ["IDisposable"]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var atNormal = pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var atDetailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        // Interfaces is an authored minimal member overview.
        Assert.Contains("Interfaces", atMinimal);
        Assert.DoesNotContain("Interfaces", atNormal);
        Assert.Contains("Interfaces", atDetailed);
    }

    [Fact]
    public void ApiMemberPipeline_Baseclass_IsFixedAndRequiresNonTrivialBase()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Trivial base (System.Object) should not render
        var trivialModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "System.Object" };
        var trivialEffective = pipeline.GetEffectiveSections(trivialModel, Verbosity.Detailed);
        Assert.DoesNotContain("Baseclass", trivialEffective);

        // A real base is fixed identity evidence and remains in bounded views.
        var realModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "MyBase" };
        Assert.Contains(
            "Baseclass",
            pipeline.GetEffectiveSections(realModel, Verbosity.Minimal));
        Assert.Contains(
            "Baseclass",
            pipeline.GetEffectiveSections(realModel, Verbosity.Normal));
    }

    [Fact]
    public void ApiMemberPipeline_NormalOmitsGrowingMemberInventories()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Members =
            [
                new ApiMember { Name = ".ctor", Kind = "constructor" },
                new ApiMember { Name = "Count", Kind = "property" },
                new ApiMember { Name = "GetValue", Kind = "method" },
                new ApiMember { Name = "op_Equality", Kind = "operator" },
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Constructors", effective);
        Assert.DoesNotContain("Properties", effective);
        Assert.DoesNotContain("Method Groups", effective);
        Assert.DoesNotContain("Methods", effective);
        Assert.DoesNotContain("Operators", effective);
        Assert.DoesNotContain("Fields", effective);
        Assert.DoesNotContain("Events", effective);
    }

    [Fact]
    public void ApiMemberPipeline_FixedOverviewAddsApplicableIdentityRows()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            BaseType = "Base",
            Members =
            [
                new ApiMember { Name = ".ctor", Kind = "constructor" },
                new ApiMember { Name = "Finalize", Kind = "finalizer" },
            ],
        };

        Assert.Equal(
            [
                SectionNames.TypeInfo,
                SectionNames.Baseclass,
                SectionNames.Finalizer,
            ],
            pipeline.FixedOverviewSectionNames);
        Assert.Equal(
            [
                SectionNames.Baseclass,
                SectionNames.Finalizer,
            ],
            pipeline.BareSelectSectionNames);

        Assert.Equal(
            [
                SectionNames.Baseclass,
                SectionNames.Finalizer,
            ],
            pipeline.GetEffectiveSections(
                model,
                Verbosity.Normal,
                fixedOverview: true));
    }

    [Fact]
    public void ApiMemberPipeline_SourceLocations_AreMemberGroupAndDetailOnly()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        // Remote Source section was removed; SourceLink location rows live on the
        // member group/detail pipelines instead of the broad type/member-list view.
        Assert.DoesNotContain("Remote Source", names);
        Assert.DoesNotContain(SectionNames.SourceLocations, names);

        var overloadPipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        Assert.Contains(SectionNames.SourceLocations, overloadPipeline.AllSectionNames);
        Assert.DoesNotContain(SectionNames.SourceLocations, overloadPipeline.GetCostAnnotations());

        var detailPipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        Assert.Contains(SectionNames.SourceLocations, detailPipeline.AllSectionNames);
        Assert.DoesNotContain(SectionNames.SourceLocations, detailPipeline.GetCostAnnotations());
    }

    [Fact]
    public void ApiMemberPipeline_InfoPreset_UsesMethodGroups()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        Assert.Contains("Method Groups", pipeline.InfoSectionNames);
        Assert.Contains("Operators", pipeline.InfoSectionNames);
        Assert.Contains("Explicit Interface Implementations", pipeline.InfoSectionNames);
        Assert.Contains("Extension Methods", pipeline.InfoSectionNames);
        Assert.DoesNotContain("Methods", pipeline.InfoSectionNames);
        Assert.Equal("verbose", Assert.Contains("Methods", pipeline.GetCostAnnotations()));
    }

    [Fact]
    public void ApiMemberPipeline_AlternateMemberRows_UseExplicitApplicability()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = "Run", Kind = "method" },
                new ApiMember { Name = "op_Equality", Kind = "operator" },
                new ApiMember { Name = "IFoo.Bar", Kind = "explicit-interface-implementation" },
                new ApiMember { Name = "Ext", Kind = "extension-method" }
            ]
        };

        var explicitlyApplicable = pipeline.GetExplicitlyApplicableSections(model);

        Assert.Contains("Methods", explicitlyApplicable);
        Assert.Contains("Operators", explicitlyApplicable);
        Assert.Contains("Explicit Interface Implementations", explicitlyApplicable);
        Assert.Contains("Extension Methods", explicitlyApplicable);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_InfoPreset_UsesMethods()
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();

        Assert.Contains("Methods", pipeline.InfoSectionNames);
        Assert.DoesNotContain("Method Groups", pipeline.InfoSectionNames);
        Assert.Contains("Call Graph", pipeline.AllSectionNames);
        Assert.DoesNotContain("Caller Graph", pipeline.AllSectionNames);
        Assert.Contains("Unsafe Operations", pipeline.AllSectionNames);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_MethodsStayMinimalSkipNormalReturnDetailed()
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method" }],
        };

        Assert.Contains(
            SectionNames.Methods,
            pipeline.GetEffectiveSections(model, Verbosity.Minimal));
        Assert.DoesNotContain(
            SectionNames.Methods,
            pipeline.GetEffectiveSections(model, Verbosity.Normal));
        Assert.Contains(
            SectionNames.Methods,
            pipeline.GetEffectiveSections(model, Verbosity.Detailed));
        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.Methods,
                }));
    }

    [Theory]
    [InlineData(SectionNames.CustomAttributes)]
    [InlineData(SectionNames.DecompiledSource)]
    [InlineData(SectionNames.PdbSource)]
    [InlineData(SectionNames.IL)]
    public void
        ApiMemberOverloadPipeline_ReusedGrowingSectionsRequireDetailed(
            string section)
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();

        Assert.Contains(section, pipeline.AllSectionNames);
        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    section,
                }));
    }

    [Fact]
    public void ApiMemberDetailPipeline_NormalRetainsOnlySignature()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample", Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method" }]
        };

        var minimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var normal = pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var detailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Signature", minimal);
        Assert.DoesNotContain("Decompiled Source", minimal);
        Assert.DoesNotContain("PDB Source", minimal);
        Assert.Equal(
            [SectionNames.Summary, SectionNames.Signature],
            normal);
        Assert.DoesNotContain("Annotated Source", normal);
        Assert.DoesNotContain("Custom Attributes", normal);
        Assert.DoesNotContain("Decompiled Source", normal);
        Assert.DoesNotContain("IL", normal);
        Assert.Contains("Decompiled Source", detailed);
        Assert.Contains("PDB Source", detailed);
        Assert.Contains("IL", detailed);
        Assert.Contains("Custom Attributes", detailed);
        Assert.DoesNotContain("Annotated Source", detailed);
        var annotations = pipeline.GetCostAnnotations();
        Assert.DoesNotContain("Calls", annotations);
        Assert.DoesNotContain("Exception Regions", annotations);
        Assert.DoesNotContain("Callers", annotations);
        Assert.DoesNotContain("Call Graph", annotations);
        Assert.DoesNotContain("Facts", annotations);
        Assert.DoesNotContain("Unsafe Operations", annotations);
        Assert.DoesNotContain("Calls", normal);
        Assert.DoesNotContain("Exception Regions", normal);
        Assert.DoesNotContain("Callers", normal);
        Assert.DoesNotContain("Call Graph", normal);
        Assert.DoesNotContain("Facts", normal);
        Assert.DoesNotContain("Unsafe Operations", normal);
        Assert.DoesNotContain("Calls", detailed);
        Assert.DoesNotContain("Exception Regions", detailed);
        Assert.DoesNotContain("Callers", detailed);
        Assert.DoesNotContain("Call Graph", detailed);
        Assert.DoesNotContain("Facts", detailed);
        Assert.DoesNotContain("Unsafe Operations", detailed);
    }

    [Theory]
    [InlineData(SectionNames.CustomAttributes)]
    [InlineData(SectionNames.DecompiledSource)]
    [InlineData(SectionNames.PdbSource)]
    [InlineData(SectionNames.IL)]
    public void ApiMemberDetailPipeline_GrowingBaseSectionsRequireDetailed(
        string section)
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();

        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    section,
                }));
    }

    [Fact]
    public void ApiMemberDetailPipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();

        Assert.Equal([SectionNames.Signature], pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberDetailPipeline_FixedOverview_IsExactlySignature()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();

        Assert.Equal([SectionNames.Signature], pipeline.FixedOverviewSectionNames);
    }

    [Fact]
    public void ApiMemberPipeline_EventProjectionCanExceedInformativeRange()
    {
        var type = new ApiType
        {
            Name = "EventSource",
            Kind = "class",
            Members =
            [
                .. Enumerable.Range(0, 31).Select(index =>
                    new ApiMember
                    {
                        Name = $"Event{index}",
                        Kind = "event",
                        ReturnType = "System.EventHandler",
                    }),
            ],
        };
        var events = new EventsView();

        var (truncated, _) =
            ApiOutputFormatter.PopulateMemberSummarySections(
                new TypeView(),
                new MethodGroupsView(),
                events,
                type,
                new ApiOptions());

        Assert.Equal(0, truncated);
        Assert.Equal(31, events.SummaryRows!.Count);
    }

    [Fact]
    public void ApiMemberDetailPipeline_SourceCategory_MapsToSourceViews()
    {
        var categories = ApiMemberDetailSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.Equal(
            [
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.PdbSource,
                SectionNames.SourceDiff,
                SectionNames.IL
            ],
            categories[SectionCategoryNames.Source]);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_SourceCategory_MapsToSourceViews()
    {
        var categories = ApiMemberOverloadSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.Equal(
            [
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.PdbSource,
                SectionNames.SourceDiff,
                SectionNames.IL
            ],
            categories[SectionCategoryNames.Source]);
    }
}
