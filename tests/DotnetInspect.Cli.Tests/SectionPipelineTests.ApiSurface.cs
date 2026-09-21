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
        Assert.Equal(35, pipeline.AllSectionNames.Length);
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
                SectionNames.ImplementationProfiles,
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
                SectionNames.ImplementationProfiles,
            ],
            Uncategorized(overload));
        Assert.Equal(
            [
                SectionNames.FindingCensus,
                SectionNames.CloneCandidates,
                SectionNames.ImplementationProfiles,
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

        // Values is an authored minimal member overview for enums.
        Assert.Contains("Values", atMinimal);
        Assert.Contains("Values", atNormal);
    }

    [Fact]
    public void ApiMemberPipeline_TypeParameters_ShowsAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "List", Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Generic identity is part of the authored member overview.
        Assert.Contains("Type Parameters", atMinimal);
    }

    [Fact]
    public void ApiMemberPipeline_Interfaces_ShowsAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Interfaces = ["IDisposable"]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Interfaces is an authored minimal member overview.
        Assert.Contains("Interfaces", atMinimal);
    }

    [Fact]
    public void ApiMemberPipeline_Baseclass_RequiresDetailedAndNonTrivial()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Trivial base (System.Object) should not render
        var trivialModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "System.Object" };
        var trivialEffective = pipeline.GetEffectiveSections(trivialModel, Verbosity.Detailed);
        Assert.DoesNotContain("Baseclass", trivialEffective);

        // Real base should render at Detailed
        var realModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "MyBase" };
        var realEffective = pipeline.GetEffectiveSections(realModel, Verbosity.Detailed);
        Assert.Contains("Baseclass", realEffective);
    }

    [Fact]
    public void ApiMemberPipeline_MemberSections_AtNormal()
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

        Assert.Contains("Constructors", effective);
        Assert.Contains("Properties", effective);
        Assert.Contains("Method Groups", effective);
        Assert.Contains("Methods", effective);
        Assert.Contains("Operators", effective);
        Assert.DoesNotContain("Fields", effective);
        Assert.DoesNotContain("Events", effective);
    }

    [Fact]
    public void ApiMemberPipeline_VerbosityAutoPromote_ForInterfaces()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Interfaces is an authored overview section.
        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Interfaces" });

        Assert.Equal(Verbosity.Minimal, required);
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
    public void ApiMemberDetailPipeline_NormalIncludesLocalImplementationSections()
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
        Assert.Contains("Decompiled Source", normal);
        Assert.Contains("IL", normal);
        Assert.DoesNotContain("Annotated Source", normal);
        Assert.DoesNotContain("PDB Source", normal);
        Assert.Contains("Decompiled Source", detailed);
        Assert.Contains("PDB Source", detailed);
        Assert.Contains("IL", detailed);
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
