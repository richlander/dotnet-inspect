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
    // ===== Package pipeline tests =====

    [Fact]
    public void PackagePipeline_EverySelectableSectionBelongsToAnAuthoredCategory()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categorized = pipeline.GetCategoryMap()
            .SelectMany(pair => pair.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncategorized = pipeline.SelectableSectionNames
            .Where(name => !categorized.Contains(name))
            .ToArray();

        Assert.True(
            uncategorized.Length == 0,
            $"Package section(s) have no authored category: {string.Join(", ", uncategorized)}");
    }

    [Fact]
    public void PackagePipeline_BaseScopeIsDerivedFromPackageAndFilesCategories()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categories = pipeline.GetCategoryMap();

        Assert.Equal(
            [SectionCategoryNames.Files, SectionCategoryNames.Package],
            pipeline.GetBaseCategoryDoors().OrderBy(name => name, StringComparer.Ordinal));

        var expected = categories[SectionCategoryNames.Package]
            .Concat(categories[SectionCategoryNames.Files])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.BaseSectionNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void PackagePipeline_CategoryCompositionMatchesPackageEvidenceDomains()
    {
        var categories = PackageSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.Equal(
            [
                PackageSections.PackageInfo,
                PackageSections.Signals,
                PackageSections.Statistics,
                PackageSections.TargetFrameworks,
                PackageSections.Signature,
                PackageSections.Dependencies,
                PackageSections.EcosystemDependencies,
                PackageSections.Vulnerabilities,
                PackageSections.Manifest,
                PackageSections.RuntimeDependencies,
                PackageSections.Files
            ],
            categories[SectionCategoryNames.Package]);
        Assert.Equal(
            [
                PackageSections.DependencyHierarchy,
                PackageSections.Dependencies,
                PackageSections.EcosystemDependencies,
                PackageSections.RuntimeDependencies,
            ],
            categories[SectionCategoryNames.Dependencies]);
        Assert.Equal(
            [
                PackageSections.Signals,
                PackageSections.AuditArtifactText,
                PackageSections.AuditFindings,
                PackageSections.AuditIdentifierConfusion,
                PackageSections.Signature,
                PackageSections.Vulnerabilities,
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkMissingFiles,
                PackageSections.SourceLinkIntegrity
            ],
            categories[SectionCategoryNames.Audit]);
    }

    [Theory]
    [InlineData("ordinary text", false)]
    [InlineData("C:\\tmp\\package", false)]
    [InlineData("literal \\u202E text", false)]
    [InlineData("concerning\u202Etext", true)]
    public void PackagePipeline_ArtifactTextAuditEffectivenessUsesTypedConcerns(
        string packageName,
        bool expected)
    {
        var model = new InspectionResult
        {
            PackageName = packageName,
            Version = "1.0.0",
        };

        Assert.Equal(
            expected,
            PackageSectionDescriptors.AuditArtifactText.CanRender(model));
    }

    [Fact]
    public void PackagePipeline_BaseInventoriesFollowMeasuredGrowthClasses()
    {
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.TargetFrameworks.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.Dependencies.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.EcosystemDependencies.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.RuntimeDependencies.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.SkillFiles.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.NuspecFiles.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.Manifest.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.Vulnerabilities.SizeClass);
    }

    [Fact]
    public void PackagePipeline_DomainDescriptorsDeclareAuditedGrowth()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categories = pipeline.GetCategoryMap();
        HashSet<string> baseSections =
        [
            .. pipeline.GetBaseCategoryDoors()
                .SelectMany(category => categories[category]),
        ];
        string[] domainOnlySections =
        [
            .. categories
                .Where(pair => !pipeline.GetBaseCategoryDoors().Contains(
                    pair.Key,
                    StringComparer.OrdinalIgnoreCase))
                .SelectMany(static pair => pair.Value)
                .Where(section => !baseSections.Contains(section))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                PackageSections.AuditArtifactText,
                PackageSections.AuditFindings,
                PackageSections.AuditIdentifierConfusion,
                PackageSections.DependencyHierarchy,
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkFiles,
                PackageSections.SourceLinkIntegrity,
                PackageSections.SourceLinkMissingFiles,
            ],
            domainOnlySections);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.AuditArtifactText.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.AuditFindings.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.AuditIdentifierConfusion.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.DependencyHierarchy.SizeClass);
        Assert.Equal(
            SectionSizeClass.Fixed,
            PackageSectionDescriptors.SourceLinkAvailability.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.SourceFiles.SizeClass);
        Assert.Equal(
            SectionSizeClass.Fixed,
            PackageSectionDescriptors.SourceLinkIntegrity.SizeClass);
        Assert.Equal(
            SectionSizeClass.Verbose,
            PackageSectionDescriptors.SourceLinkMissingFiles.SizeClass);
    }

    [Fact]
    public void PackagePipeline_DomainSectionsRemainOutsideAutomaticScope()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categories = pipeline.GetCategoryMap();
        HashSet<string> automatic =
        [
            .. pipeline.GetCandidateSections(Verbosity.Detailed),
        ];
        HashSet<string> baseSections =
        [
            .. pipeline.GetBaseCategoryDoors()
                .SelectMany(category => categories[category]),
        ];
        string[] domainOnlySections =
        [
            .. categories
                .Where(pair => !pipeline.GetBaseCategoryDoors().Contains(
                    pair.Key,
                    StringComparer.OrdinalIgnoreCase))
                .SelectMany(static pair => pair.Value)
                .Where(section => !baseSections.Contains(section))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        Assert.All(
            domainOnlySections,
            section => Assert.DoesNotContain(section, automatic));
    }

    [Fact]
    public void PackagePipeline_PackageContentAuditRendersOnlyWithFindings()
    {
        var model = new InspectionResult
        {
            PackageContentAudit = new PackageContentAuditResult(
                [new PackageContentAuditFinding(
                    "README.md",
                    PackageContentFindingKind.NonGraphicText,
                    TextConcern.Format,
                    new InertString(TextPolicy.Field, "encoded"))],
                EligibleFiles: 1,
                ScannedFiles: 1,
                ScannedBytes: 7,
                Complete: true),
        };

        Assert.True(PackageSectionDescriptors.AuditFindings.CanRender(model));
        model.PackageContentAudit = model.PackageContentAudit with { Findings = [] };
        Assert.False(PackageSectionDescriptors.AuditFindings.CanRender(model));
    }

    [Theory]
    [InlineData("Contoso.Utilities", false)]
    [InlineData("C:\\tmp\\package", false)]
    [InlineData("Δelta.Tools", true)]
    [InlineData("Ѕystem.Text.Json", true)]
    public void PackagePipeline_IdentifierConfusionEffectivenessUsesTypedConcerns(
        string packageName,
        bool expected)
    {
        var model = new InspectionResult
        {
            PackageName = packageName,
            Version = "1.0.0",
        };

        Assert.Equal(
            expected,
            PackageSectionDescriptors.AuditIdentifierConfusion.CanRender(model));
    }

    [Fact]
    public void PackagePipeline_BaseCategoriesPreserveAutomaticCandidateSets()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(
            [PackageSections.Summary],
            pipeline.GetCandidateSections(Verbosity.Quiet));
        Assert.Equal(
            [PackageSections.PackageInfo],
            pipeline.GetCandidateSections(Verbosity.Minimal));
        Assert.Equal(
            new[]
            {
                PackageSections.Summary,
                PackageSections.PackageInfo,
                PackageSections.FilesReadme,
                PackageSections.Signature,
            }.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.GetCandidateSections(Verbosity.Normal)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                PackageSections.Summary,
                PackageSections.PackageInfo,
                PackageSections.FilesReadme,
                PackageSections.Signals,
                PackageSections.Statistics,
                PackageSections.TargetFrameworks,
                PackageSections.FilesNuspec,
                PackageSections.FilesSkills,
                PackageSections.Signature,
                PackageSections.Dependencies,
                PackageSections.EcosystemDependencies,
                PackageSections.Vulnerabilities,
                PackageSections.Manifest,
                PackageSections.RuntimeDependencies
            }.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.GetCandidateSections(Verbosity.Detailed)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            [
                PackageSections.PackageInfo,
                PackageSections.FilesReadme,
                PackageSections.Signature,
            ],
            pipeline.BareSelectSectionNames);
    }

    [Theory]
    [InlineData(PackageSections.TargetFrameworks)]
    [InlineData(PackageSections.Dependencies)]
    [InlineData(PackageSections.EcosystemDependencies)]
    [InlineData(PackageSections.RuntimeDependencies)]
    [InlineData(PackageSections.FilesSkills)]
    [InlineData(PackageSections.FilesNuspec)]
    [InlineData(PackageSections.Manifest)]
    [InlineData(PackageSections.Vulnerabilities)]
    public void PackagePipeline_MeasuredVerboseBaseInventoryRemainsExplicitlySelectable(
        string section)
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section };

        Assert.Equal(Verbosity.Detailed, pipeline.GetRequiredVerbosity(include));
        Assert.Equal([section], pipeline.GetCandidateSections(Verbosity.Detailed, include));
    }

    [Fact]
    public void PackagePipeline_HasExpectedSectionCount()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        Assert.Equal(24, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void PackagePipeline_SectionNamesMatchConstants()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Summary", names);
        Assert.Contains("Package Info", names);
        Assert.Contains("Package README file", names);
        Assert.Contains("Signals", names);
        Assert.Contains(PackageSections.AuditArtifactText, names);
        Assert.Contains(PackageSections.AuditFindings, names);
        Assert.Contains(PackageSections.AuditIdentifierConfusion, names);
        Assert.Contains("Target Frameworks", names);
        Assert.Contains("Package nuspec file", names);
        Assert.Contains("Package license files", names);
        Assert.Contains("Statistics", names);
        Assert.Contains(PackageSections.DependencyHierarchy, names);
        Assert.Contains("Dependencies", names);
        Assert.Contains(PackageSections.EcosystemDependencies, names);
        Assert.Contains("Package files", names);
        Assert.Contains("Package skill files", names);
        Assert.Contains(PackageSections.SourceLinkFiles, names);
        Assert.Contains(PackageSections.SourceLinkAvailability, names);
        Assert.Contains(PackageSections.SourceLinkMissingFiles, names);
        Assert.Contains(PackageSections.SourceLinkIntegrity, names);
        Assert.Contains("Vulnerabilities", names);
        Assert.Contains("Manifest", names);
        Assert.Contains("Runtime Dependencies", names);
    }

    [Fact]
    public void SigningSection_FieldCatalogMatchesCombinedRows()
    {
        var schema = InspectionContext.Default
            .GetSchemaInfo<InspectionResultView>()!
            .ToDocumentSchema()
            .GetSection(PackageSections.Signature);
        var section = new SigningSection
        {
            AuthorVerified = "Yes",
            Publisher = "Publisher",
            Repository = "Repository",
            RepositoryVerified = "Yes",
            Signed = "Yes",
            Status = "Status",
        };

        Assert.Equal(
            SigningSection.FieldNames,
            section.ToMarkoutFields().Select(field => field.Key));
        Assert.Equal(
            SigningSection.FieldNames,
            schema!.Items.Select(item => item.Name));
    }

    [Fact]
    public void PackagePipeline_Quiet_IncludesSummaryOnly()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Quiet);

        // Quiet includes the headless Summary section for compact field rendering
        Assert.Single(effective);
        Assert.Equal("Summary", effective[0]);
    }

    [Fact]
    public void PackagePipeline_Minimal_ShowsPackageAndConditionalSections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Package is always renderable at Minimal
        Assert.Contains("Package Info", effective);
        Assert.DoesNotContain("Summary", effective);
        // Statistics requires TotalDownloads (Normal verbosity anyway)
        Assert.DoesNotContain("Statistics", effective);
        // Target Frameworks requires target framework data
        Assert.DoesNotContain("Target Frameworks", effective);
        // Dependencies requires DependencyGroups (Normal verbosity anyway)
        Assert.DoesNotContain("Dependencies", effective);
        // Vulnerabilities is Detailed
        Assert.DoesNotContain("Vulnerabilities", effective);
        // Files is Detailed
        Assert.DoesNotContain("Package files", effective);
    }

    [Fact]
    public void PackagePipeline_CandidatesSeparateQuietSummaryFromMinimalInfo()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(
            [PackageSections.Summary],
            pipeline.GetCandidateSections(Verbosity.Quiet));
        Assert.Equal(
            [PackageSections.PackageInfo],
            pipeline.GetCandidateSections(Verbosity.Minimal));
    }

    [Fact]
    public void PackagePipeline_SignalsDoesNotShowAtMinimal()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        Assert.Contains("Package Info", effective);
        Assert.Contains("Package Info", effective);
        Assert.DoesNotContain("Signals", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsManifestWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Manifest", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsRuntimeDepsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeDependencies = [new PackageDependency { Id = "Dep", Version = "1.0.0" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Runtime Dependencies", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsStatisticsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Statistics", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_HidesStatistics()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Statistics", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsPackageDepsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Dependencies", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_HidesVulnerabilities()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsVulnerabilitiesWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_HidesVulnerabilitiesWhenEmpty()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.DoesNotContain("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_VerbosityAutoPromote_ForVulnerabilities()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Vulnerabilities" });

        Assert.Equal(Verbosity.Detailed, required);
    }

    [Fact]
    public void PackagePipeline_IdentifierConfusionAudit_DemandsRegistrationMetadata()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var options = new InspectionOptions
        {
            IncludeSections =
            [
                PackageSections.AuditIdentifierConfusion,
            ],
        };

        Assert.True(
            PackageCommand.RequiresPackageMetadata(options, pipeline));
        Assert.True(
            PackageCommand.AllowsVulnerabilityTraffic(options));
        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(options.IncludeSections));
        Assert.True(
            PackageCommand.RequiresPackageMetadata(
                options with
                {
                    IncludeSections = null,
                    Discover =
                    [
                        PackageSections.AuditIdentifierConfusion,
                    ],
                },
                pipeline));
    }

    [Fact]
    public void PackagePipeline_VerbosityAutoPromote_ForPackage()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Package Info" });

        // Curated ladder: Quiet renders only the headless Summary preamble, so the identity
        // table first becomes available at Minimal.
        Assert.Equal(Verbosity.Minimal, required);
    }

    [Fact]
    public void PackagePipeline_ComputeIncludeSections_FiltersExplicitOnlySections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0"],
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }],
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }],
            RuntimeDependencies = [new PackageDependency { Id = "Dep2", Version = "2.0" }],
            LibraryFiles = ["lib/net8.0/test.dll"],
            Files = [new PackageFile("lib/net8.0/test.dll", 1234)],
            SignatureResult = new DotnetInspector.Services.SignatureVerificationResult { RepositoryVerified = true, Repository = "nuget.org" },
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")],
            DependencyHierarchyProjection =
                CreateEmptyDependencyHierarchyProjection(),
        };

        // At Detailed with all default-renderable data populated, Unbounded-cost sections stay
        // filtered: the whole-package listing and the PDB-backed SourceLink listing are reachable
        // only by exact name or their category door, never by turning verbosity up.
        var include = pipeline.ComputeIncludeSections(model, Verbosity.Detailed);

        Assert.NotNull(include);
        Assert.DoesNotContain("Package files", include);
        Assert.DoesNotContain("SourceLink: Files", include);
        Assert.DoesNotContain(PackageSections.DependencyHierarchy, include);
    }

    [Fact]
    public void PackagePipeline_AllSelectorSections_DefaultFirstThenRemainingAlpha()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0"],
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            LibraryFiles = ["lib/net8.0/test.dll"],
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        var sections = pipeline.GetAllSelectorSections(model);

        Assert.Equal("Package Info", sections[0]);
        Assert.DoesNotContain("Summary", sections);
        // SourceLink: Files and Package files are reached through their door or by exact name,
        // so they are not members of the visible @All pole.
        Assert.Equal(["Dependencies", "Manifest", "Signals", "Statistics", "Target Frameworks"], sections.Skip(1).ToArray());
    }

    [Fact]
    public void PackagePipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(["Package Info"], pipeline.InfoSectionNames);
    }
}
