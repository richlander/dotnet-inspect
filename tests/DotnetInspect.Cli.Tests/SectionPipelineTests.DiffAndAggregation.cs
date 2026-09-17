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
    [Fact]
    public void DiffQueryCatalog_RegistrationMatchesDeclaration()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();

        HashSet<InspectionQueryDefinition> closure =
            catalog.QueryCatalog.ExpandRequired(catalog.Sections.DeclaredQueries);

        Assert.Equal(
            closure.OrderBy(query => query.Name, StringComparer.Ordinal),
            catalog.QueryCatalog.RegisteredQueries.OrderBy(
                query => query.Name,
                StringComparer.Ordinal));
        Assert.Equal(
            [
                ApiComparisonQuery.Definition,
                BodySignalComparisonQuery.Definition,
                ImplementationComparisonQuery.Definition,
            ],
            catalog.Pipeline.DeclaredQueries);
    }

    [Fact]
    public void DiffSectionCatalog_UsesCompiledDomainLens()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();

        Assert.Same(DiffSections.Domain, catalog.Lens.Domain);
        Assert.Same(DiffSections.Lens, catalog.Lens);
        Assert.Same(DiffSections.QueryCatalog, catalog.QueryCatalog);
        Assert.Same(DiffSections.SectionCatalog, catalog.Sections);
    }

    [Fact]
    public void DiffPipeline_UsesAuthoredCategoryWithoutComputedPoles()
    {
        var pipeline = DiffSections.CreatePipeline();

        var category = Assert.Single(pipeline.GetCategoryMap());
        Assert.Equal(SectionCategoryNames.Diff, category.Key);
        Assert.Equal(
            [
                DiffSections.Changes.Name,
                DiffSections.AnalysisDiff.Name,
                DiffSections.ImplementationDiff.Name,
            ],
            category.Value);
        Assert.Equal(category.Value, pipeline.BaseSectionNames);
        Assert.DoesNotContain(
            DiffSections.FindingTransitions.Name,
            category.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiffComparisonSections_DemandTheirProducerQueriesAndCosts()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();
        var changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.Changes.Name,
        };
        var analysis = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.AnalysisDiff.Name,
        };
        var implementation = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.ImplementationDiff.Name,
        };

        Assert.Equal(
            [ApiComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(Verbosity.Minimal, changes));
        Assert.Equal(
            [ApiComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(Verbosity.Minimal));
        Assert.Equal(
            [BodySignalComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(Verbosity.Minimal, analysis));
        Assert.Equal(
            [ImplementationComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(
                Verbosity.Minimal,
                implementation));
        Assert.Equal(
            SectionCost.NetworkFree,
            Assert.Single(
                catalog.Pipeline.SectionCosts,
                section => section.Name == DiffSections.Changes.Name).Cost);
        Assert.Equal(
            SectionCost.Unbounded,
            Assert.Single(
                catalog.Pipeline.SectionCosts,
                section => section.Name == DiffSections.AnalysisDiff.Name).Cost);
        Assert.Equal(
            SectionCost.Unbounded,
            Assert.Single(
                catalog.Pipeline.SectionCosts,
                section => section.Name
                    == DiffSections.ImplementationDiff.Name).Cost);
    }

    [Fact]
    public void DiffQueryCatalog_RunsOnlySelectedSectionDemand()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();
        var analysis = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.AnalysisDiff.Name,
        };
        int analysisInputsCreated = 0;
        var analysisContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            () =>
            {
                analysisInputsCreated++;
                return new BodySignalComparisonInput([], []);
            });
        List<InspectionQueryDefinition> analysisExecuted = [];

        catalog.Lens.Plan(
            Verbosity.Minimal,
            analysis).Run(
            analysisContext,
            (query, _) => analysisExecuted.Add(query));

        Assert.Equal([BodySignalComparisonQuery.Definition], analysisExecuted);
        Assert.Equal(1, analysisInputsCreated);

        var changesContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            () => throw new InvalidOperationException(
                "Changes-only demand must not acquire Analysis indexes."),
            () => throw new InvalidOperationException(
                "Changes-only demand must not acquire Implementation inputs."));
        List<InspectionQueryDefinition> changesExecuted = [];
        catalog.Lens.Plan(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.Changes.Name,
                }).Run(
            changesContext,
            (query, _) => changesExecuted.Add(query));

        Assert.Equal([ApiComparisonQuery.Definition], changesExecuted);

        int implementationInputsCreated = 0;
        var implementationContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            createImplementationComparisonInput: () =>
            {
                implementationInputsCreated++;
                return new ImplementationComparisonInput([], []);
            });
        List<InspectionQueryDefinition> implementationExecuted = [];
        catalog.Lens.Plan(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.ImplementationDiff.Name,
                }).Run(
            implementationContext,
            (query, _) => implementationExecuted.Add(query));

        Assert.Equal(
            [ImplementationComparisonQuery.Definition],
            implementationExecuted);
        Assert.Equal(1, implementationInputsCreated);

        int analysisComposedInputsCreated = 0;
        int implementationComposedInputsCreated = 0;
        var composedContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            () =>
            {
                analysisComposedInputsCreated++;
                return new BodySignalComparisonInput([], []);
            },
            () =>
            {
                implementationComposedInputsCreated++;
                return new ImplementationComparisonInput([], []);
            });
        List<InspectionQueryDefinition> composedExecuted = [];
        catalog.Lens.Plan(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.Changes.Name,
                    DiffSections.AnalysisDiff.Name,
                    DiffSections.ImplementationDiff.Name,
                }).Run(
            composedContext,
            (query, _) => composedExecuted.Add(query));

        Assert.Equal(
            [
                ApiComparisonQuery.Definition,
                BodySignalComparisonQuery.Definition,
                ImplementationComparisonQuery.Definition,
            ],
            composedExecuted);
        Assert.Equal(1, analysisComposedInputsCreated);
        Assert.Equal(1, implementationComposedInputsCreated);
    }

    [Fact]
    public void DiffCommand_AllocRegressionsRequestsAnalysisWithoutUnusedChanges()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();

        CompiledInspectionPlan<DiffQueryContext> singleSection =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    AllocRegressionsOnly = true,
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.Changes.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> composedDocument =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    AllocRegressionsOnly = true,
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.Changes.Name,
                        DiffSections.AnalysisDiff.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> implementationSelection =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    AllocRegressionsOnly = true,
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.ImplementationDiff.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> implementationOnly =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.ImplementationDiff.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext>
            workspaceImplementationOnly =
                DiffCommand.GetRequestedQueryPlan(
                    catalog,
                    new DiffOptions
                    {
                        IncludeSections = new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            DiffSections.ImplementationDiff.Name,
                        },
                    },
                    workspaceImplementation: true);
        CompiledInspectionPlan<DiffQueryContext>
            workspaceComposedDocument =
                DiffCommand.GetRequestedQueryPlan(
                    catalog,
                    new DiffOptions
                    {
                        IncludeSections = new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            DiffSections.Changes.Name,
                            DiffSections.AnalysisDiff.Name,
                            DiffSections.ImplementationDiff.Name,
                        },
                    },
                    workspaceImplementation: true);
        CompiledInspectionPlan<DiffQueryContext> findingTransitionsOnly =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.FindingTransitions.Name,
                    },
                });

        Assert.Equal(
            [BodySignalComparisonQuery.Definition],
            singleSection.QueryPlan.Queries);
        Assert.Equal(
            [BodySignalComparisonQuery.Definition],
            implementationSelection.QueryPlan.Queries);
        Assert.Equal(
            [ImplementationComparisonQuery.Definition],
            implementationOnly.QueryPlan.Queries);
        Assert.Empty(
            workspaceImplementationOnly.QueryPlan.Queries);
        Assert.Equal(
            [
                ApiComparisonQuery.Definition,
                BodySignalComparisonQuery.Definition,
            ],
            composedDocument.QueryPlan.Queries);
        Assert.Equal(
            [ApiComparisonQuery.Definition],
            workspaceComposedDocument.QueryPlan.Queries);
        Assert.Empty(findingTransitionsOnly.RequestedQueries);
        Assert.Empty(findingTransitionsOnly.QueryPlan.Queries);
    }

    [Fact]
    public void DiffCommand_WorkspaceTypeSelectionPreservesShortSelectorSemantics()
    {
        var before = new ApiSurface
        {
            TypeForwarders =
            [
                new TypeForwarder
                {
                    DefinitionName =
                        Assert.IsType<
                            MetadataTypeDefinitionNameResult.Valid>(
                            MetadataTypeDefinitionName.Create(
                                "Microsoft.Extensions.DependencyInjection",
                                ["ServiceCollection"])).Name,
                    TypeName =
                        "Microsoft.Extensions.DependencyInjection.ServiceCollection",
                },
            ],
        };
        var after = new ApiSurface
        {
            TypeForwarders =
            [
                new TypeForwarder
                {
                    DefinitionName =
                        Assert.IsType<
                            MetadataTypeDefinitionNameResult.Valid>(
                            MetadataTypeDefinitionName.Create(
                                "Microsoft.Extensions.DependencyInjection",
                                ["ServiceCollection"])).Name,
                    TypeName =
                        "Microsoft.Extensions.DependencyInjection.ServiceCollection",
                },
            ],
        };

        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.ServiceCollection",
            DiffCommand.ResolveWorkspaceImplementationTypeName(
                before,
                after,
                "ServiceCollection")?.DisplayName);
        Assert.Equal(
            "Dispose",
            DiffCommand.LowerWorkspaceImplementationMemberSelector(
                "ServiceCollection.Dispose",
                "ServiceCollection",
                "Microsoft.Extensions.DependencyInjection.ServiceCollection"));
        Assert.Equal(
            "Dispose",
            DiffCommand.LowerWorkspaceImplementationMemberSelector(
                "ServiceCollection.Dispose",
                "Microsoft.Extensions.DependencyInjection.ServiceCollection",
                "Microsoft.Extensions.DependencyInjection.ServiceCollection"));
        Assert.Equal(
            "Dispose",
            DiffCommand.LowerWorkspaceImplementationMemberSelector(
                "OptionsMonitor<TOptions>.Dispose",
                "Microsoft.Extensions.Options.OptionsMonitor<TOptions>",
                "Microsoft.Extensions.Options.OptionsMonitor`1"));
        Assert.Equal(
            "MoveNext",
            DiffCommand.LowerWorkspaceImplementationMemberSelector(
                "ObjectEnumerator.MoveNext",
                "System.Text.Json.JsonElement+ObjectEnumerator",
                "System.Text.Json.JsonElement+ObjectEnumerator"));
    }

    [Fact]
    public void DiffCommand_WorkspaceTypeSelectionRetainsNestedIdentity()
    {
        MetadataTypeDefinitionName nestedName =
            Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Outer", "Inner"])).Name;
        var surface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "N",
                    Name = "Outer.Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = nestedName,
                },
            ],
        };

        DiffCommand.WorkspaceImplementationTypeSelection? selected =
            DiffCommand.ResolveWorkspaceImplementationTypeName(
                surface,
                surface,
                "N.Outer+Inner");

        Assert.NotNull(selected);
        Assert.Equal(nestedName, selected.DefinitionName);
        Assert.Equal("N.Outer+Inner", selected.DisplayName);
    }

    [Fact]
    public async Task PackageIntegrityExitCode_FailsForMismatchesAndAuditFailures()
    {
        var clean = new InspectionResult
        {
            SourceIntegrity = new PackageSourceIntegrity(
                1,
                1,
                Verified: 0,
                Mismatched: 0,
                LineEndingNormalized: 0,
                Unverifiable: 1,
                MismatchedFiles: null,
                UnavailableLibraries: null,
                FailedLibraries: null),
        };
        var mismatch = new InspectionResult
        {
            SourceIntegrity = clean.SourceIntegrity with { Mismatched = 1 },
        };
        var auditFailure = new InspectionResult
        {
            IdentifierConfusionFailure =
                IdentifierConfusionAuditFailureKind
                    .PackageMetadataUnavailable,
        };

        var (_, error) = await ConsoleCapture.RunAsync(() =>
        {
            Assert.Equal(0, PackageCommand.PackageIntegrityExitCode(clean));
            Assert.Equal(1, PackageCommand.PackageIntegrityExitCode(clean, mismatch));
            Assert.Equal(
                1,
                PackageCommand.PackageIntegrityExitCode(
                    clean,
                    auditFailure));
            Assert.Equal(1, PackageCommand.PackageIntegrityExitCode(1, clean));
            Assert.Equal(7, PackageCommand.PackageIntegrityExitCode(7, clean));
            Assert.Equal(1, PackageCommand.PackageIntegrityExitCode(0, mismatch));
        });

        Assert.Equal(
            "Warning: Identifier audit failed for package input #2: "
            + "package registry metadata unavailable"
            + Environment.NewLine,
            error);
    }

    [Theory]
    [InlineData(PackageSections.AuditIdentifierConfusion)]
    [InlineData(PackageSections.AuditArtifactText)]
    public void MultiPackageCount_CountsSelectedAuditRows(
        string section)
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-audit-count-{Guid.NewGuid():N}.txt");
        InspectionResult Result(string suffix) =>
            section == PackageSections.AuditIdentifierConfusion
                ? new InspectionResult
                {
                    PackageName = $"\u0405ystem.{suffix}",
                    Version = "1.0.0",
                }
                : new InspectionResult
                {
                    PackageName = $"Package.{suffix}",
                    Version = "1.0.0",
                    PackageFiles =
                    [
                        new PackageFile(
                            $"lib/{suffix}\u001b.dll",
                            1),
                    ],
                };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [Result("One"), Result("Two")],
                new InspectionOptions
                {
                    Count = true,
                    JsonOutput = true,
                    IncludeSections =
                        new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            section,
                        },
                    OutputPath = outputPath,
                },
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal(
                "2",
                File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_PreservesSelectedSectionMap()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-count-map-{Guid.NewGuid():N}.txt");
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            IncludeSections =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.PackageInfo,
                    PackageSections.TargetFrameworks,
                },
            OutputPath = outputPath,
        };
        var results = new[]
        {
            new InspectionResult
            {
                PackageName = "One",
                Version = "1.0.0",
                TargetFrameworks = ["net8.0"],
            },
            new InspectionResult
            {
                PackageName = "Two",
                Version = "1.0.0",
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                results,
                options,
                PackageSectionDescriptors.CreatePipeline());
            string output = File.ReadAllText(outputPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("| Section | Count |", output);
            Assert.Contains("| Package Info |", output);
            Assert.Contains("| Target Frameworks | 1 |", output);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_PreservesFixedOverviewMap()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-fixed-count-map-{Guid.NewGuid():N}.txt");
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult
                    {
                        PackageName = "One",
                        Version = "1.0.0",
                    },
                ],
                new InspectionOptions
                {
                    Count = true,
                    JsonOutput = true,
                    FixedOverview = true,
                    OutputPath = outputPath,
                },
                pipeline);
            string output = File.ReadAllText(outputPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("| Section | Count |", output);
            foreach (string section in pipeline.BareSelectSectionNames)
                Assert.Contains($"| {section} |", output);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_PreservesIntegrityMismatchExitCode()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"package-count-{Guid.NewGuid():N}.txt");
        var clean = new InspectionResult
        {
            PackageName = "Clean",
            SourceIntegrity = new PackageSourceIntegrity(
                1,
                1,
                Verified: 1,
                Mismatched: 0,
                LineEndingNormalized: 0,
                Unverifiable: 0,
                MismatchedFiles: null,
                UnavailableLibraries: null,
                FailedLibraries: null),
        };
        var mismatch = new InspectionResult
        {
            PackageName = "Mismatch",
            SourceIntegrity = clean.SourceIntegrity with { Mismatched = 1 },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [clean, mismatch],
                new InspectionOptions
                {
                    Count = true,
                    IncludeSections = new HashSet<string> { PackageSections.Files },
                    OutputPath = outputPath,
                },
                PackageSectionDescriptors.CreateCatalog().Pipeline);

            Assert.Equal(1, exitCode);
            Assert.Equal("2", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_AggregatesSelectedSignatureRows()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-signature-count-{Guid.NewGuid():N}.txt");
        var signature = new SignatureVerificationResult
        {
            AuthorVerified = true,
            Publisher = "Publisher",
            Repository = "nuget.org",
            RepositoryVerified = true,
        };
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            OutputPath = outputPath,
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.Signature,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult
                    {
                        PackageName = "First",
                        SignatureResult = signature,
                    },
                    new InspectionResult
                    {
                        PackageName = "Second",
                        SignatureResult = signature,
                    },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal("10", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_AppliesRowWindowToCombinedPackageInfoRows()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-info-count-{Guid.NewGuid():N}.txt");
        var options = new InspectionOptions
        {
            Count = true,
            OutputPath = outputPath,
            Rows = RowWindow.Head(1),
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.PackageInfo,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult { PackageName = "First" },
                    new InspectionResult { PackageName = "Second" },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal("1", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_JsonFileSelectionUsesCombinedRowShape()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-file-count-{Guid.NewGuid():N}.txt");
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            OutputPath = outputPath,
            Rows = RowWindow.Head(1),
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.FilesReadme,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult { PackageName = "First" },
                    new InspectionResult { PackageName = "Second" },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal("1", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_AggregatesMultipleSelectedSections()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-section-counts-{Guid.NewGuid():N}.txt");
        var signature = new SignatureVerificationResult
        {
            AuthorVerified = true,
            Publisher = "Publisher",
            Repository = "nuget.org",
            RepositoryVerified = true,
        };
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            OutputPath = outputPath,
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.PackageInfo,
                PackageSections.Signature,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult
                    {
                        PackageName = "First",
                        SignatureResult = signature,
                    },
                    new InspectionResult
                    {
                        PackageName = "Second",
                        SignatureResult = signature,
                    },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            string output = File.ReadAllText(outputPath);
            Assert.Contains("| Package Info |", output);
            Assert.Contains("| Signature | 10 |", output);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}
