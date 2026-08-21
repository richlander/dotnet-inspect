using DotnetInspector.Commands;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
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
            new ApiMember
            {
                Name = "Run",
                Kind = "method",
                MetadataToken = 0x06000001,
                HasMethodBody = true
            },
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
            new ApiMember
            {
                Name = "Value",
                Kind = "property",
                GetterToken = 0x06000002,
                HasMethodBody = true
            },
            true
        },
        {
            new ApiMember { Name = "Value", Kind = "field" },
            false
        },
        {
            new ApiMember
            {
                Name = "Changed",
                Kind = "event",
                AdderToken = 0x06000003,
                HasMethodBody = true
            },
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
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.DecompiledSource));
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.Facts));
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.IL));
        Assert.Equal(hasBodyViews, ids.Contains(SectionNames.OriginalSource));
    }

    [Fact]
    public void MemberViews_KeepSourceSectionsForUnknownBodyStateButNotKnownBodylessState()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var unknown = new ApiType
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
                }
            ],
        };
        var bodyless = new ApiType
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
                    HasMethodBody = false,
                }
            ],
        };

        IReadOnlySet<string> unknownIds = pipeline.GetInspectionViews(unknown)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> bodylessIds = pipeline.GetInspectionViews(bodyless)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(SectionNames.OriginalSource, unknownIds);
        Assert.Contains(SectionNames.SourceDiff, unknownIds);
        Assert.Contains(SectionNames.SourceLocations, unknownIds);
        Assert.DoesNotContain(SectionNames.IL, unknownIds);
        Assert.DoesNotContain(SectionNames.OriginalSource, bodylessIds);
        Assert.DoesNotContain(SectionNames.SourceDiff, bodylessIds);
        Assert.DoesNotContain(SectionNames.SourceLocations, bodylessIds);
    }

    [Fact]
    public void BodyShapesKnownBodylessGate_PreservesUnknownAccessorEligibility()
    {
        var knownBodylessAccessor = new ApiType
        {
            Name = "Sample",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    AccessorFacts =
                    [
                        new ApiAccessor
                        {
                            Kind = "get",
                            HasMethodBody = false,
                        },
                    ],
                },
            ],
        };
        var unknownLegacyAccessor = new ApiType
        {
            Name = "Sample",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    SetterToken = 0x06000002,
                    HasMethodBody = true,
                },
            ],
        };
        var executableMethod = new ApiType
        {
            Name = "Sample",
            Members =
            [
                new ApiMember
                {
                    Name = "Run",
                    Kind = "method",
                    MetadataToken = 0x06000003,
                    HasMethodBody = true,
                },
            ],
        };
        var options = new MemberOptions { OverloadIndex = 1 };

        Assert.True(MemberCommand.SelectedMemberDefinitelyHasNoBody(knownBodylessAccessor, options));
        Assert.False(MemberCommand.SelectedMemberDefinitelyHasNoBody(unknownLegacyAccessor, options));
        Assert.False(MemberCommand.SelectedMemberDefinitelyHasNoBody(executableMethod, options));
    }

    public static TheoryData<ApiMember, bool, bool> BodyAnalysisApplicabilityCases => new()
    {
        {
            new ApiMember
            {
                Name = "Run",
                Kind = "method",
                MetadataToken = 0x06000001,
                HasMethodBody = true
            },
            true,
            true
        },
        {
            new ApiMember
            {
                Name = "Extern",
                Kind = "method",
                MetadataToken = 0x06000002
            },
            true,
            true
        },
        {
            new ApiMember
            {
                Name = "Abstract",
                Kind = "method",
                MetadataToken = 0x06000003,
                IsAbstract = true
            },
            false,
            true
        },
        {
            new ApiMember { Name = "Value", Kind = "field" },
            false,
            false
        }
    };

    [Theory]
    [MemberData(nameof(BodyAnalysisApplicabilityCases))]
    public void MemberViews_ReflectBodyAnalysisTargetApplicability(
        ApiMember member,
        bool hasCallerViews,
        bool hasUnsafeOperations)
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

        Assert.Equal(hasCallerViews, ids.Contains(SectionNames.Callers));
        Assert.Equal(hasCallerViews, ids.Contains(SectionNames.CallGraph));
        Assert.Equal(hasUnsafeOperations, ids.Contains(SectionNames.UnsafeOperations));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, true)]
    public void MemberViews_UseSelectedAccessorBodyFacts(
        int accessorOrdinal,
        bool hasExecutableBodyViews,
        bool hasCallerViews)
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            SelectedAccessorOrdinal = accessorOrdinal,
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    SetterToken = 0x06000002,
                    HasMethodBody = true,
                    IsAbstract = true,
                    SignatureModel = new ApiSignature
                    {
                        Accessors =
                        [
                            new ApiAccessor
                            {
                                Kind = "get",
                                HasMethodBody = false,
                                IsAbstract = true
                            },
                            new ApiAccessor
                            {
                                Kind = "set",
                                HasMethodBody = true,
                                IsAbstract = false
                            }
                        ]
                    }
                }
            ]
        };

        IReadOnlySet<string> ids = pipeline.GetInspectionViews(model)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(hasExecutableBodyViews, ids.Contains(SectionNames.DecompiledSource));
        Assert.Equal(hasExecutableBodyViews, ids.Contains(SectionNames.IL));
        Assert.Equal(hasCallerViews, ids.Contains(SectionNames.Callers));
        Assert.Equal(hasCallerViews, ids.Contains(SectionNames.CallGraph));
        Assert.Contains(SectionNames.UnsafeOperations, ids);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void MemberViews_UseTokenOrderWhenPresentationOmitsAnAccessor(
        int accessorOrdinal,
        bool hasExecutableBodyViews)
    {
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            SelectedAccessorOrdinal = accessorOrdinal,
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    SetterToken = 0x06000002,
                    HasMethodBody = true,
                    SignatureModel = new ApiSignature
                    {
                        Accessors =
                        [
                            new ApiAccessor
                            {
                                Kind = "set",
                                HasMethodBody = false,
                                IsAbstract = true
                            }
                        ]
                    },
                    AccessorFacts =
                    [
                        new ApiAccessor
                        {
                            Kind = "get",
                            HasMethodBody = true,
                            IsAbstract = false
                        },
                        new ApiAccessor
                        {
                            Kind = "set",
                            HasMethodBody = false,
                            IsAbstract = true
                        }
                    ]
                }
            ]
        };

        IReadOnlySet<string> ids = ApiMemberDetailSectionDescriptors.CreatePipeline()
            .GetInspectionViews(model)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(hasExecutableBodyViews, ids.Contains(SectionNames.IL));
        Assert.Equal(hasExecutableBodyViews, ids.Contains(SectionNames.Facts));
    }

    [Fact]
    public void BodyExecution_SkipsSelectedAbstractAccessorWithoutShiftingOrdinal()
    {
        var type = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    SetterToken = 0x06000002,
                    HasMethodBody = true,
                    IsAbstract = true,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "int",
                        Accessors =
                        [
                            new ApiAccessor
                            {
                                Kind = "get",
                                HasMethodBody = false,
                                IsAbstract = true
                            },
                            new ApiAccessor
                            {
                                Kind = "set",
                                HasMethodBody = true,
                                IsAbstract = false
                            }
                        ]
                    }
                }
            ]
        };

        Assert.Empty(ApiOutputFormatter.ResolveBodyMethods(
            type,
            new HashSet<string> { SectionNames.Facts },
            selectedOrdinal: 1));
        Assert.Equal(2, ApiOutputFormatter.ResolveBodyMethods(
            type,
            new HashSet<string> { SectionNames.Facts },
            selectedOrdinal: 2).Count);
        Assert.Equal(2, ApiOutputFormatter.ResolveBodyMethods(
            type,
            new HashSet<string> { SectionNames.UnsafeOperations },
            selectedOrdinal: 1).Count);
    }

    [Fact]
    public void BodyExecution_UsesCompleteFactsWhenPresentationOmitsAccessor()
    {
        var type = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    SetterToken = 0x06000002,
                    HasMethodBody = true,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "int",
                        Accessors =
                        [
                            new ApiAccessor
                            {
                                Kind = "set",
                                HasMethodBody = true,
                                IsAbstract = false
                            }
                        ]
                    },
                    AccessorFacts =
                    [
                        new ApiAccessor
                        {
                            Kind = "get",
                            HasMethodBody = false,
                            IsAbstract = true
                        },
                        new ApiAccessor
                        {
                            Kind = "set",
                            HasMethodBody = true,
                            IsAbstract = false
                        }
                    ]
                }
            ]
        };
        HashSet<string> requested =
        [
            SectionNames.Facts,
            SectionNames.CallGraph,
            SectionNames.TopLeverage,
            SectionNames.PerformanceTriage,
            SectionNames.UnsafeOperations
        ];

        IReadOnlySet<string> executionSections =
            ApiOutputFormatter.ResolveExecutionSections(type, requested, selectedOrdinal: 1);

        Assert.Equal([SectionNames.UnsafeOperations], executionSections);
        var methods = ApiOutputFormatter.ResolveBodyMethods(
            type,
            executionSections,
            selectedOrdinal: 1);
        Assert.Equal(2, methods.Count);
        var method = methods[0];
        Assert.False(method.HasMethodBody);
        Assert.True(method.IsAbstract);
    }

    [Fact]
    public void DirectBodylessMethodKeepsExecutionSectionsAndScopesTheFactsDecision()
    {
        HashSet<string> requested =
        [
            SectionNames.Facts,
            SectionNames.Signature,
            SectionNames.SourceLocations,
            SectionNames.Calls,
            SectionNames.FidelityCauses,
            SectionNames.OriginalSource,
            SectionNames.SourceDiff,
        ];
        var abstractMethod = BodylessMethodType(isAbstract: true);
        var externMethod = BodylessMethodType(isAbstract: false);
        var unknown = new ApiType
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
                }
            ],
        };

        // The execution-section projection never filters a directly selected method: the sections
        // that report an absent body (Fidelity Causes here) must keep rendering for both shapes.
        foreach (var type in new[] { abstractMethod, externMethod, unknown })
        {
            Assert.Equal(
                requested.OrderBy(section => section, StringComparer.Ordinal),
                ApiOutputFormatter
                    .ResolveExecutionSections(type, requested, selectedOrdinal: 1)
                    .OrderBy(section => section, StringComparer.Ordinal));
        }

        // The Facts policy is the only thing scoped to the absent body. An abstract declaration is
        // dropped from the body projection, so only the sections that need no body target remain —
        // the declaration-only views plus the two source views that report the absence itself; a
        // concrete extern method is still projected and renders every requested body section.
        Assert.Equal(
            [
                SectionNames.OriginalSource,
                SectionNames.Signature,
                SectionNames.SourceDiff,
                SectionNames.SourceLocations,
            ],
            MemberCommand
                .ResolveBodylessRenderableSections(abstractMethod, requested, ordinal: 1)
                .OrderBy(section => section, StringComparer.Ordinal));
        Assert.Equal(
            requested.OrderBy(section => section, StringComparer.Ordinal),
            MemberCommand
                .ResolveBodylessRenderableSections(externMethod, requested, ordinal: 1)
                .OrderBy(section => section, StringComparer.Ordinal));

        // An unknown body fact is not evidence of absence: it stays a body target.
        Assert.Equal(
            requested.OrderBy(section => section, StringComparer.Ordinal),
            MemberCommand
                .ResolveBodylessRenderableSections(unknown, requested, ordinal: 1)
                .OrderBy(section => section, StringComparer.Ordinal));
    }

    [Fact]
    public void ConcreteBodylessAccessorKeepsAbsenceReportingSections()
    {
        var type = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001,
                    AccessorFacts =
                    [
                        new ApiAccessor
                        {
                            Kind = "get",
                            HasMethodBody = false,
                            IsAbstract = false,
                        }
                    ]
                }
            ]
        };
        HashSet<string> requested =
        [
            SectionNames.Facts,
            SectionNames.IL,
            SectionNames.FidelityCauses,
            SectionNames.AnnotatedSourceDocument,
            SectionNames.CostOverlay,
            SectionNames.SemanticsOverlay,
            SectionNames.Calls,
            SectionNames.ExceptionRegions,
            SectionNames.AllocationFacts,
            SectionNames.SafetyFacts,
            SectionNames.CostFacts,
            SectionNames.AppliedTaste,
        ];

        Assert.Equal(
            [
                SectionNames.AllocationFacts,
                SectionNames.AnnotatedSourceDocument,
                SectionNames.AppliedTaste,
                SectionNames.Calls,
                SectionNames.CostFacts,
                SectionNames.CostOverlay,
                SectionNames.FidelityCauses,
                SectionNames.SafetyFacts,
                SectionNames.SemanticsOverlay,
            ],
            ApiOutputFormatter
                .ResolveExecutionSections(type, requested, selectedOrdinal: 1)
                .OrderBy(section => section, StringComparer.Ordinal));
    }

    [Fact]
    public void LegacyMixedAccessorsKeepUnknownAbstractAndBodyState()
    {
        var member = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            GetterToken = 0x06000001,
            SetterToken = 0x06000002,
            IsAbstract = true,
        };
        var type = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            SelectedAccessorOrdinal = 1,
            Members = [member],
        };

        ApiMember[] accessors = ApiOutputFormatter.AccessorMethods(member, type).ToArray();

        Assert.Equal(2, accessors.Length);
        Assert.All(accessors, accessor =>
        {
            Assert.False(accessor.IsAbstract);
            Assert.Null(accessor.HasMethodBody);
        });
        Assert.Contains(
            SectionNames.Facts,
            ApiOutputFormatter.ResolveExecutionSections(
                type,
                new HashSet<string> { SectionNames.Facts },
                selectedOrdinal: 1));

        IReadOnlySet<string> viewIds = ApiMemberDetailSectionDescriptors.CreatePipeline()
            .GetInspectionViews(type)
            .Select(view => view.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(SectionNames.DecompiledSource, viewIds);
        Assert.Contains(SectionNames.Facts, viewIds);
        Assert.Contains(SectionNames.Callers, viewIds);
    }

    private static ApiType BodylessMethodType(bool isAbstract) => new()
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
                HasMethodBody = false,
                IsAbstract = isAbstract,
            }
        ],
    };

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
                new ApiMember
                {
                    Name = "Run",
                    Kind = "method",
                    MetadataToken = 0x06000001,
                    HasMethodBody = true
                }
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
    public void EmptyMemberViewSelection_UsesMinimalPreset()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
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
                    HasMethodBody = true
                }
            ]
        };

        InspectionViewSelection selection = pipeline.ResolveInspectionViews(model, null);

        Assert.Equal([SectionNames.Signature], selection.SectionNames);
    }

    [Fact]
    public void TypeSourceViews_DiscloseAcquisitionCapabilities()
    {
        var model = new ApiType
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
                    HasMethodBody = true
                }
            ]
        };
        IReadOnlyDictionary<string, InspectionViewDescriptor> views =
            ApiMemberSectionDescriptors.CreatePipeline()
                .GetInspectionViews(model)
                .ToDictionary(view => view.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources,
            views[SectionNames.SourceFiles].Capabilities);
        Assert.Equal(
            SectionCapabilities.MayDownloadPdb,
            views[SectionNames.DecompiledSource].Capabilities);
    }

    [Theory]
    [InlineData("property")]
    [InlineData("event")]
    public void AccessorOverloadViews_RoundTripThroughExecution(string kind)
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = kind,
                    GetterToken = kind == "property" ? 0x06000001 : null,
                    AdderToken = kind == "event" ? 0x06000001 : null,
                    HasMethodBody = true
                }
            ]
        };
        string[] requested =
        [
            SectionNames.DecompiledSource,
            SectionNames.OriginalSource,
            SectionNames.IL
        ];

        InspectionViewSelection selection = pipeline.ResolveInspectionViews(model, requested);
        IReadOnlyList<string> effective = pipeline.GetEffectiveSections(
            model,
            Verbosity.Normal,
            new HashSet<string>(selection.SectionNames, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(requested, selection.Views.Select(view => view.Id));
        Assert.Equal(requested, effective);
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
    public void SourcePlanCapabilities_MatchInspectionViewDescriptors()
    {
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasSourceLink = true
        };
        IReadOnlyDictionary<string, InspectionViewDescriptor> views =
            LibrarySections.CreatePipeline()
                .GetInspectionViews(model, includeInapplicable: true)
                .ToDictionary(view => view.Id, StringComparer.OrdinalIgnoreCase);

        foreach (LibrarySourceSectionPlan sourceSection in LibrarySourcePlans.Sections)
        {
            SectionCapabilities expected = sourceSection.DownloadPdb
                ? SectionCapabilities.MayDownloadPdb
                : SectionCapabilities.None;

            Assert.Equal(expected, views[sourceSection.Name].Capabilities);
        }
    }

    [Fact]
    public void CuratedViewListing_MatchesOwningPipelineCatalog()
    {
        AssertListingMatchesCatalog(
            LibrarySections.CreatePipeline(),
            new LibraryInspection
            {
                AssemblyInfo = new AssemblyInfo(),
                HasSourceLink = true
            });
        AssertListingMatchesCatalog(
            PackageSectionDescriptors.CreatePipeline(),
            new InspectionResult
            {
                PackageName = "Example.Package",
                Version = "1.0.0",
                AssemblyCount = 1
            });
    }

    [Fact]
    public void PackageSourceLinkViews_DiscloseAcquisitionCapabilities()
    {
        var model = new InspectionResult
        {
            PackageName = "Example.Package",
            Version = "1.0.0",
            AssemblyCount = 1
        };

        IReadOnlyDictionary<string, InspectionViewDescriptor> views =
            PackageSectionDescriptors.CreatePipeline()
                .GetInspectionViews(model)
                .ToDictionary(view => view.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            SectionCapabilities.MayDownloadPdb,
            views[PackageSections.SourceLinkFiles].Capabilities);
        Assert.Equal(
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayAuditSources,
            views[PackageSections.SourceLinkAvailability].Capabilities);
        Assert.Equal(
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayAuditSources,
            views[PackageSections.SourceLinkMissingFiles].Capabilities);
        Assert.Equal(
            SectionCapabilities.MayDownloadPdb | SectionCapabilities.MayFetchSources,
            views[PackageSections.SourceLinkIntegrity].Capabilities);
        foreach (string name in new[]
        {
            PackageSections.SourceLinkFiles,
            PackageSections.SourceLinkAvailability,
            PackageSections.SourceLinkMissingFiles,
            PackageSections.SourceLinkIntegrity
        })
        {
            Assert.True(views[name].MayUseNetwork);
        }
        Assert.True(views[PackageSections.SourceLinkIntegrity].MayFetchSourceContent);
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
        Assert.Throws<ArgumentException>(
            () => pipeline.ResolveInspectionViews(field, [""]));
        Assert.Throws<ArgumentException>(
            () => pipeline.ResolveInspectionViews(field, [" ", SectionNames.Signature]));
        Assert.Throws<InvalidOperationException>(
            () => pipeline.ResolveInspectionViews(field, [SectionNames.IL]));
        InspectionViewDescriptor abstractIl = Assert.Single(
            pipeline.GetInspectionViews(abstractMethod, includeInapplicable: true),
            view => view.Id == SectionNames.IL);
        Assert.True(abstractIl.IsApplicable);
        Assert.False(abstractIl.IsAvailable);
        Assert.True(abstractIl.CanRender);
        Assert.Throws<InvalidOperationException>(
            () => pipeline.ResolveInspectionViews(abstractMethod, [SectionNames.IL]));
    }

    [Fact]
    public void TypeDiscovery_OmitsMemberOnlyBodyBackedViews()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "IOnlyProperties",
            Kind = "interface",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    GetterToken = 0x06000001
                }
            ]
        };

        IReadOnlyList<string> discoverable = pipeline.GetDiscoverableSections(model);

        Assert.Contains(SectionNames.DecompiledSource, discoverable);
        Assert.DoesNotContain(SectionNames.IL, discoverable);
        Assert.DoesNotContain(SectionNames.OriginalSource, discoverable);
        Assert.DoesNotContain(SectionNames.SourceDiff, discoverable);
        Assert.DoesNotContain(SectionNames.Facts, discoverable);
        Assert.DoesNotContain(SectionNames.SourceFiles, discoverable);
    }

    [Fact]
    public void TypeDiscovery_PreservesUnsafeSignaturesWithoutExecutableBodies()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "NativeOnly",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Copy",
                    Kind = "method",
                    IsUnsafe = true
                }
            ]
        };

        IReadOnlyList<string> discoverable = pipeline.GetDiscoverableSections(model);
        InspectionViewSelection selection = pipeline.ResolveInspectionViews(
            model,
            [SectionNames.UnsafeMembers]);

        Assert.Contains(SectionNames.UnsafeMembers, discoverable);
        Assert.Equal([SectionNames.UnsafeMembers], selection.SectionNames);
    }

    [Fact]
    public void TypeDiscovery_OmitsUnsafeMembersForSafeBodylessMembers()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "ISafe",
            Kind = "interface",
            Members =
            [
                new ApiMember
                {
                    Name = "Run",
                    Kind = "method"
                }
            ]
        };

        IReadOnlyList<string> discoverable = pipeline.GetDiscoverableSections(model);

        Assert.DoesNotContain(SectionNames.UnsafeMembers, discoverable);
    }

    [Fact]
    public void TypeDiscovery_PreservesDegradedSignaturesWithoutExecutableBodies()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "IDegraded",
            Kind = "interface",
            Members =
            [
                new ApiMember
                {
                    Name = "Run",
                    Kind = "method",
                    SignatureDecodeStatus = SignatureDecodeStatus.Degraded
                }
            ]
        };

        IReadOnlyList<string> discoverable = pipeline.GetDiscoverableSections(model);

        Assert.Contains(SectionNames.UnsafeMembers, discoverable);
    }

    static void AssertListingMatchesCatalog<TModel>(
        SectionPipeline<TModel> pipeline,
        TModel model)
    {
        IReadOnlySet<string> hidden = pipeline.GetCatalogHiddenSections();

        Assert.All(
            pipeline.GetInspectionViews(model, includeInapplicable: true),
            view => Assert.Equal(!hidden.Contains(view.Id), view.IsListed));
    }
}
