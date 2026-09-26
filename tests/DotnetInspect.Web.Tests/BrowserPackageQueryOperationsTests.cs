using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;
using QuerySpace;
using QuerySpace.Rows;

using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition("Package query operations", DisableParallelization = true)]
public sealed class PackageQueryOperationCollection;

[Collection("Package query operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserPackageQueryOperationsTests
{
    [Fact]
    public void ProductionBindingUsesTheRegisteredPackageQueryRoute()
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    PackageQueryCapabilityBinding.Module,
                ]);

        Assert.Same(
            PackageQueryCapability.Route,
            PackageQueryCapabilityBinding.Binding.Route);
        Assert.Same(
            PackageQueryCapabilityBinding.Binding,
            Assert.Single(catalog.Bindings));
        Assert.Equal(
            InspectionConsumerKind.Cli,
            Assert.Single(catalog.AdoptionGaps).ConsumerKind);
        Assert.Contains(
            PackageQueryCapabilityBinding.Binding.ExposedQueryTerms,
            identity =>
                identity
                == "package-query.term.library-literal");
    }

    [Fact]
    public void LibraryLiteralPlan_UsesNormalPlanAndComposesWithOrdinaryTerms()
    {
        const string literal = " \r\nmarker ";
        var literalTerm = new PortableQueryTerm(
            PackageQuery.LibraryLiteralTermKey,
            PortableQueryOperator.Equal,
            literal);
        var licenseTerm = new PortableQueryTerm(
            PackageQuery.LicenseTermKey,
            PortableQueryOperator.Equal,
            "MIT");

        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "Contoso.*",
                [literalTerm, licenseTerm],
                maximumCandidates: 5,
                maximumMatches: 3,
                includePrerelease: false,
                targetFramework: "net8.0"));

        Assert.True(accepted.Plan.RequiresLibraryLiteralEvaluation);
        Assert.Equal(literal, accepted.Plan.LibraryLiteral);
        Assert.Equal("net8.0", accepted.Plan.LibraryTargetFramework);
        Assert.Same(
            literalTerm,
            accepted.Plan.Terms.Single(term =>
                term.Key == PackageQuery.LibraryLiteralTermKey));
        Assert.Same(
            licenseTerm,
            accepted.Plan.Terms.Single(term =>
                term.Key == PackageQuery.LicenseTermKey));
        Assert.Contains(
            accepted.Plan.Terms,
            term => term.Key == PackageQuery.LibraryTargetTermKey
                && term.Value == "net8.0");

        PackageQueryPlan prequalification =
            accepted.Plan.CreatePrequalificationPlan();
        Assert.Equal(
            [PackageQuery.LicenseTermKey],
            prequalification.Terms.Select(term => term.Key));
    }

    [Fact]
    public void Project_LibraryLiteralDocumentPreservesTypedAssessmentAndRootRequest()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageRootReacquisitionRequest rootRequest =
            RootRequest(source.Source.Producer.PortableKey);
        string rootToken = rootRequest.Encode();
        var selectedAsset = new PackageQueryLibraryLiteralSelectedAsset(
            new InertString(TextPolicy.Field, "lib/net10.0/Contoso.dll"),
            new InertString(TextPolicy.Field, "Contoso"),
            new InertString(TextPolicy.Field, "net10.0"),
            UnevaluatedSiblings: 2);
        var missedAsset = new PackageQueryLibraryLiteralSelectedAsset(
            new InertString(
                TextPolicy.Field,
                "lib/net10.0/Contoso.Missed.dll"),
            new InertString(TextPolicy.Field, "Contoso.Missed"),
            new InertString(TextPolicy.Field, "net10.0"),
            UnevaluatedSiblings: 2,
            Ordinal: 0);
        var package = new PackageProfileMatch(
            "Contoso.Package",
            "1.0.0-BETA",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest("Contoso.Package", "1.0.0-BETA", isToolPackage: false));
        var match = new PackageQueryMatch(
            package,
            PackageQueryAcquisitionTier.PackageContent,
            [],
            [])
        {
            LibraryLiteral = new(rootRequest, selectedAsset, []),
        };
        var assessment = new PackageQueryLibraryLiteralAssessment(
            CandidateOrdinal: 1,
            "Contoso.Package",
            "1.0.0-beta",
            source.Source,
            PackageQueryLibraryLiteralAssessmentKind.Matched)
        {
            RootRequest = rootRequest,
            SelectedAsset = selectedAsset,
            Message = "The selected library contains the literal.",
            Libraries =
            [
                new(
                    missedAsset,
                    PackageQueryLibraryLiteralLibraryAssessmentKind.NoMatch,
                    Occurrences: 0),
                new(
                    selectedAsset,
                    PackageQueryLibraryLiteralLibraryAssessmentKind.Matched,
                    Occurrences: 4),
            ],
        };
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Contoso."),
            source.Source,
            CandidateLimit: 5,
            MatchLimit: 3,
            Candidates: 1,
            Matches: 1,
            Failures: 0,
            PackageQueryCompletionKind.Exhausted)
        {
            EvaluatedCandidates = 1,
            SemanticMatches = 1,
            SemanticMisses = 0,
            NotApplicable = 0,
            NotEvaluatedCandidates = 0,
            Occurrences = 4,
            Scope =
                "All selected implementation libraries for one compatible target framework.",
        };
        var document = new PackageQueryDocument([match], [], summary)
        {
            LibraryLiteralAssessments = [assessment],
        };

        BrowserPackageQueryDocument projected =
            BrowserPackageQueryOperations.Project(document);

        Assert.Equal(rootToken, Assert.Single(projected.Results).RootRequest);
        BrowserPackageAssemblySemanticCandidateOutcome projectedAssessment =
            Assert.Single(projected.LibraryLiteralAssessments);
        Assert.Equal(
            BrowserPackageAssemblySemanticCandidateOutcomeKind.Matched,
            projectedAssessment.Kind);
        BrowserPackageAssemblySemanticResult projectedResult =
            Assert.IsType<BrowserPackageAssemblySemanticResult>(
                projectedAssessment.Result);
        Assert.Equal(rootToken, projectedResult.SelectedAsset.RootRequest);
        Assert.Equal(rootToken, projectedAssessment.RootRequest);
        Assert.Equal(rootToken, projectedAssessment.SelectedAsset!.RootRequest);
        Assert.Equal(
            "lib/net10.0/Contoso.dll",
            projectedAssessment.SelectedAsset.Path);
        Assert.Equal(2, projectedAssessment.SelectedAsset.UnevaluatedSiblings);
        Assert.Collection(
            projectedAssessment.Libraries,
            library =>
            {
                Assert.Equal(
                    BrowserPackageAssemblySemanticLibraryAssessmentKind.NoMatch,
                    library.Kind);
                Assert.Equal(
                    "lib/net10.0/Contoso.Missed.dll",
                    library.SelectedAsset.Path);
                Assert.Equal(0, library.Occurrences);
            },
            library =>
            {
                Assert.Equal(
                    BrowserPackageAssemblySemanticLibraryAssessmentKind.Matched,
                    library.Kind);
                Assert.Equal(
                    "lib/net10.0/Contoso.dll",
                    library.SelectedAsset.Path);
                Assert.Equal(4, library.Occurrences);
            });
        Assert.Equal(1, projected.Completion.EvaluatedCandidates);
        Assert.Equal(1, projected.Completion.SemanticMatches);
        Assert.Equal(4, projected.Completion.Occurrences);
    }

    [Theory]
    [InlineData("Newtonsoft.Json", false, "Newtonsoft.Json", 1)]
    [InlineData("Newtonsoft.*", true, "Newtonsoft.", 200)]
    [InlineData("Newtonsoft*", true, "Newtonsoft", 200)]
    public void PackagePlan_DispatchesExactAndLiteralPrefixInput(
        string text,
        bool prefix,
        string expected,
        int expectedCandidates)
    {
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                text,
                terms: null,
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: true,
                targetFramework: null));

        Assert.Equal(expectedCandidates, accepted.Plan.MaximumCandidates);
        Assert.True(accepted.Plan.IncludePrerelease);
        if (prefix)
        {
            var input = Assert.IsType<SourceSelector.PackagePrefix>(
                accepted.Plan.PackageInput);
            Assert.Equal(expected, input.Request.Prefix);
        }
        else
        {
            var input = Assert.IsType<SourceSelector.Package>(
                accepted.Plan.PackageInput);
            Assert.Equal(expected, input.Coordinate.PackageId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Newton*soft")]
    public void PackagePlan_RejectsBlankAndMalformedInputWithoutDiscovery(
        string text)
    {
        var rejected = Assert.IsType<PackageQueryPlanResult.Rejected>(
            BrowserPackageQueryOperations.Plan(
                text,
                terms: null,
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: false,
                targetFramework: null));

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            rejected.Failure.Reason);
    }

    [Fact]
    public void PackagePlan_BindsProductEcosystemMemberships()
    {
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "Aspire.Hosting.PostgreSQL",
                [
                    new PortableQueryTerm(
                        PackageQuery.DependsEcosystemTermKey,
                        PortableQueryOperator.Equal,
                        "ecosystem.aspire"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1,
                includePrerelease: false,
                targetFramework: null));

        Assert.True(accepted.Plan.RequiresManifest);
    }

    [Theory]
    [InlineData(
        "ecosystem.unknown",
        PackageQueryRequestFailureReason.UnknownEcosystem)]
    [InlineData(
        "ecosystem.platform",
        PackageQueryRequestFailureReason.UnknownEcosystem)]
    public void PackagePlan_RejectsUnknownProductEcosystemMemberships(
        string ecosystemId,
        PackageQueryRequestFailureReason expectedReason)
    {
        var rejected = Assert.IsType<PackageQueryPlanResult.Rejected>(
            BrowserPackageQueryOperations.Plan(
                "Contoso.Package",
                [
                    new PortableQueryTerm(
                        PackageQuery.DependsEcosystemTermKey,
                        PortableQueryOperator.Equal,
                        ecosystemId),
                ],
                maximumCandidates: 1,
                maximumMatches: 1,
                includePrerelease: false,
                targetFramework: null));

        Assert.Equal(expectedReason, rejected.Failure.Reason);
        Assert.Equal(ecosystemId, rejected.Failure.EcosystemId);
    }

    [Fact]
    public void PackagePlan_RejectsDistinctRepeatedLibraryLiterals()
    {
        var rejected = Assert.IsType<PackageQueryPlanResult.Rejected>(
            BrowserPackageQueryOperations.Plan(
                "Contoso.Package",
                [
                    new PortableQueryTerm(
                        PackageQuery.LibraryLiteralTermKey,
                        PortableQueryOperator.Equal,
                        "shared-literal-use-marker"),
                    new PortableQueryTerm(
                        PackageQuery.LibraryLiteralTermKey,
                        PortableQueryOperator.Equal,
                        "different-literal"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1,
                includePrerelease: false,
                targetFramework: "net10.0"));

        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            rejected.Failure.Reason);
    }

    [Fact]
    public void PackagePlan_UsesProductPortableInspectionTermBoundary()
    {
        PortableQueryTerm[] maximum = InspectionTerms(
            PackageQuery.MaximumInspectionTerms);
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "Contoso.*",
                maximum,
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: false,
                targetFramework: null));
        Assert.Equal(
            PortableQueryPayloadCodec.MaxTerms,
            accepted.Plan.Intent.Terms.Count);

        var rejected = Assert.IsType<PackageQueryPlanResult.Rejected>(
            BrowserPackageQueryOperations.Plan(
                "Contoso.*",
                InspectionTerms(PackageQuery.MaximumInspectionTerms + 1),
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: false,
                targetFramework: null));
        Assert.Equal(
            PackageQueryRequestFailureReason.TooManyTerms,
            rejected.Failure.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Project_ExactCompletionRetainsAuthoritativeSourceSelection(
        int sourceCandidates)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create());
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Missing.Package"),
            source.Source,
            CandidateLimit: 1,
            MatchLimit: 100,
            Candidates: sourceCandidates,
            Matches: sourceCandidates,
            Failures: 0,
            PackageQueryCompletionKind.ExactPackageComplete)
        {
            SourceCandidates = sourceCandidates,
        };

        BrowserPackageQueryCompletion completion =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Completed(summary)).Completion!;

        Assert.Equal(
            BrowserPackageQueryCompletionKind.ExactPackageComplete,
            completion.Kind);
        Assert.Equal(sourceCandidates, completion.Candidates);
        Assert.Equal(sourceCandidates, completion.Matches);
        Assert.Equal(sourceCandidates, completion.SourceCandidates);
    }

    [Fact]
    public void Catalog_MatchesProductPresetAndFreeTermOrderAndMetadata()
    {
        BrowserPackageQueryCatalog catalog =
            BrowserPackageQueryOperations.Catalog();

        var expectedPresets = PackageQuery.RegisteredTerms
            .Where(term =>
                term.Descriptor.Role
                    == PackageQueryTermRole.Inspection)
            .SelectMany(term =>
                term.Descriptor.Options.Select(option =>
                    (term, option)))
            .ToArray();
        Assert.Equal(expectedPresets.Length, catalog.Presets.Length);
        for (int index = 0; index < catalog.Presets.Length; index++)
        {
            (PackageQueryRegisteredTerm registered,
                PackageQueryTermOptionDescriptor option) = expectedPresets[index];
            PackageQueryTermDescriptor term = registered.Descriptor;
            BrowserPackageQueryPresetDescriptor actual = catalog.Presets[index];
            Assert.Equal(term.Key, actual.Key);
            Assert.Equal(
                PortableQueryModel.TextOf(
                    Assert.Single(registered.Operators)),
                actual.Operator);
            Assert.Equal(option.Value, actual.Value);
            Assert.Equal(option.Label, actual.Label);
            Assert.Equal(option.Summary, actual.Summary);
            Assert.Equal(term.Weight, actual.Weight);
            Assert.Equal(term.SelectionGroupId, actual.SelectionGroupId);
            Assert.Equal(
                term.CombinesWithinSelectionGroup,
                actual.CombinesWithinSelectionGroup);
            Assert.Equal(term.ReplacementGroupId, actual.ReplacementGroupId);
            Assert.Equal(term.DisplayGroupId, actual.DisplayGroupId);
            Assert.Equal(term.DisplayGroupLabel, actual.DisplayGroupLabel);
            Assert.Equal(
                BrowserTier(term.Tier),
                actual.Tier);
            Assert.Equal(
                term.ExecutionClass.ToString(),
                actual.ExecutionClass.ToString());
        }

        PackageQueryRegisteredTerm[] expectedTerms =
        [
            .. PackageQuery.RegisteredTerms.Where(term =>
                term.Descriptor.Role
                    == PackageQueryTermRole.Inspection
                && term.Descriptor.ControlKind
                    is PackageQueryTermControlKind.Input
                        or PackageQueryTermControlKind.MultilineInput),
        ];
        Assert.Equal(expectedTerms.Length, catalog.Terms.Length);
        for (int index = 0; index < expectedTerms.Length; index++)
        {
            PackageQueryRegisteredTerm registered = expectedTerms[index];
            PackageQueryTermDescriptor expected =
                registered.Descriptor;
            BrowserPackageQueryTermDescriptor actual = catalog.Terms[index];
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Label, actual.Label);
            Assert.Equal(expected.Summary, actual.Summary);
            Assert.Equal(expected.Weight, actual.Weight);
            Assert.Equal(
                registered.Operators.Select(
                    PortableQueryModel.TextOf),
                actual.Operators);
            Assert.Equal(expected.ValueKind, actual.ValueKind);
            Assert.Equal(expected.ExampleValue, actual.Example);
            Assert.Equal(BrowserTier(expected.Tier), actual.Tier);
            Assert.Equal(
                expected.ExecutionClass.ToString(),
                actual.ExecutionClass.ToString());
            Assert.Equal(
                expected.ControlKind
                    == PackageQueryTermControlKind.MultilineInput,
                actual.Multiline);
        }
    }

    [Fact]
    public void Plan_PreservesPortableTermsForProductBinding()
    {
        var term = new PortableQueryTerm(
            "depends",
            PortableQueryOperator.Equal,
            "Microsoft.Extensions.Hosting");

        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "Microsoft.*",
                [term],
                maximumCandidates: 200,
                maximumMatches: 100,
                includePrerelease: false,
                targetFramework: null));

        Assert.Same(term, Assert.Single(accepted.Plan.Terms));
        Assert.Equal(
            "Microsoft.Extensions.Hosting",
            Assert.Single(accepted.Plan.BoundTerms).Term.Value);
        Assert.Contains(
            accepted.Plan.Intent.Terms,
            item => item.Key == PackageQuery.PrefixTermKey
                && item.Value == "Microsoft.");
        Assert.Contains(
            accepted.Plan.Intent.Terms,
            item => item.Key == PackageQuery.PrereleaseTermKey
                && item.Value == "stable");
        Assert.Equal(
            RowSelectionStageKind.Head,
            Assert.Single(accepted.Plan.Intent.Stages).Kind);
    }

    [Fact]
    public void Plan_ReferencesTermReachesTheProductContentPlan()
    {
        var term = new PortableQueryTerm(
            PackageQuery.ReferencesTermKey,
            PortableQueryOperator.Equal,
            "Microsoft.Extensions.DependencyInjection.Abstractions");

        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "Microsoft.Extensions.*",
                [term],
                maximumCandidates:
                    PackageQuery.MaximumPackageContentCandidates,
                maximumMatches: 10,
                includePrerelease: false,
                targetFramework: null));

        BoundPackageQueryTerm bound =
            Assert.Single(accepted.Plan.BoundTerms);
        Assert.Same(term, Assert.Single(accepted.Plan.Terms));
        Assert.Equal(
            PackageQueryPredicateKind.AssemblyReference,
            bound.Predicate.Kind);
        Assert.True(accepted.Plan.RequiresPackageContent);
    }

    [Fact]
    public void Catalog_ProjectsDependenciesAsStableNuspecPresets()
    {
        BrowserPackageQueryPresetDescriptor[] dependencyPresets =
            BrowserPackageQueryOperations.Catalog().Presets
                .Where(candidate =>
                    candidate.Key == PackageQuery.DependenciesTermKey)
                .ToArray();
        Assert.Equal(
            ["none", "cross-prefix"],
            dependencyPresets.Select(candidate => candidate.Value));

        BrowserPackageQueryPresetDescriptor preset =
            dependencyPresets[1];

        Assert.Equal("eq", preset.Operator);
        Assert.Equal("cross-prefix", preset.Value);
        Assert.Equal(
            BrowserPackageQueryAcquisitionTier.Nuspec,
            preset.Tier);
        Assert.Equal(
            BrowserPackageQueryExecutionClass.Nuspec,
            preset.ExecutionClass);
    }

    [Fact]
    public void Catalog_ProjectsDependsStartsWithAsNuspecFreeOperator()
    {
        BrowserPackageQueryTermDescriptor term =
            Assert.Single(
                BrowserPackageQueryOperations.Catalog().Terms,
                candidate =>
                    candidate.Key == PackageQuery.DependsTermKey);

        Assert.Equal(["eq", "starts-with"], term.Operators);
        Assert.Equal("NuGet package ID or prefix", term.ValueKind);
        Assert.Equal(
            BrowserPackageQueryAcquisitionTier.Nuspec,
            term.Tier);
        Assert.Equal(
            BrowserPackageQueryExecutionClass.Nuspec,
            term.ExecutionClass);
    }

    [Fact]
    public void Catalog_ProjectsReferencesAsPackageContentFreeTerm()
    {
        BrowserPackageQueryTermDescriptor term =
            Assert.Single(
                BrowserPackageQueryOperations.Catalog().Terms,
                candidate =>
                    candidate.Key == PackageQuery.ReferencesTermKey);

        Assert.Equal("eq", Assert.Single(term.Operators));
        Assert.Equal("assembly simple name", term.ValueKind);
        Assert.Equal(
            BrowserPackageQueryAcquisitionTier.PackageContent,
            term.Tier);
        Assert.Equal(
            BrowserPackageQueryExecutionClass.Metadata,
            term.ExecutionClass);
    }

    [Fact]
    public void BrowserLowering_ProducesTheRegisteredCanonicalIntent()
    {
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "Contoso.*",
                [
                    new PortableQueryTerm(
                        PackageQuery.LicenseTermKey,
                        PortableQueryOperator.Equal,
                        "MIT"),
                ],
                maximumCandidates: 200,
                maximumMatches: 3,
                includePrerelease: true,
                targetFramework: null));

        PortableQueryIntent expected = PortableQueryIntent.Create(
            [
                new(
                    PackageQuery.LicenseTermKey,
                    PortableQueryOperator.Equal,
                    "MIT"),
                new(
                    PackageQuery.PrefixTermKey,
                    PortableQueryOperator.Equal,
                    "Contoso."),
                new(
                    PackageQuery.PrereleaseTermKey,
                    PortableQueryOperator.Equal,
                    "include"),
            ],
            [
                new("candidates", 200),
                new("matches", 3),
            ],
            [PortableQueryStage.Head(3)],
            []);
        Assert.Equal(
            PortableQueryPayloadCodec.Encode(
                expected,
                TestContext.Current.CancellationToken),
            PortableQueryPayloadCodec.Encode(
                accepted.Plan.Intent,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TryCreateTerms_ParsesCanonicalOperatorsWithoutChangingText()
    {
        Assert.True(BrowserPackageQueryOperations.TryCreateTerms(
            [
                new BrowserPackageQueryTerm(
                    "depends",
                    "eq",
                    "  Microsoft.Extensions.Hosting  "),
            ],
            out PortableQueryTerm[] terms,
            out string error));

        PortableQueryTerm term = Assert.Single(terms);
        Assert.Equal(PortableQueryOperator.Equal, term.Operator);
        Assert.Equal("  Microsoft.Extensions.Hosting  ", term.Value);
        Assert.Equal("", error);

        Assert.False(BrowserPackageQueryOperations.TryCreateTerms(
            [new BrowserPackageQueryTerm("depends", "=", "Contoso")],
            out terms,
            out error));
        Assert.Empty(terms);
        Assert.Contains("Unknown package-query operator", error);
    }

    [Fact]
    public void Project_PreservesFailureAndCompletionEvidence()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            source.Source,
            PackageQueryFailureKind.ManifestAcquisition,
            "manifest unavailable",
            PackageManifestFailureReason.InvalidDependencyContract);
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Contoso."),
            source.Source,
            CandidateLimit: 200,
            MatchLimit: 100,
            Candidates: 5,
            Matches: 2,
            Failures: 1,
            PackageQueryCompletionKind.CandidateLimitReached);

        BrowserPackageQueryEvent projectedFailure =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Failure(failure));
        BrowserPackageQueryEvent projectedCompletion =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Completed(summary));
        BrowserPackageQueryEvent projectedProgress =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Progress(
                    new PackageQueryProgress(
                        PackageQueryProgressPhase.Manifest,
                        Completed: 3,
                        Limit: 20)));
        var profile = new PackageProfileMatch(
            "Contoso.Package",
            "1.0.0",
            ["Contoso", "Fabrikam"],
            42,
            Verified: true,
            source.Source,
            new PackageManifestFacts(
                PackageSourceCoordinate.Create(
                    "Contoso.Package",
                    "1.0.0"),
                ManifestVersion: "nuspec",
                Description: new InertString(
                    TextPolicy.Field,
                    "Package description."),
                Authors: "Contoso; Fabrikam",
                Repository: "https://example.test/contoso/package",
                RepositoryType: "git",
                RepositoryCommit: "0123456789abcdef",
                License: "MIT",
                LicenseUrl: "https://example.test/licenses/mit",
                PackageTypes: ["DotnetTool"],
                IsToolPackage: true,
                ReadmeFile: "README.md",
                DependencyGroups:
                [
                    new DeclaredPackageDependencyGroup(
                        "net10.0",
                        [
                            new DeclaredPackageDependency(
                                "Contoso.Dependency",
                                "[2.0.0,3.0.0)"),
                        ]),
                ])
            {
                IconFile = "icon.png",
                IconUrl = "https://example.test/icon.png",
                IdentityProvenance =
                    PackageManifestIdentityProvenance.SelfAttested,
            });
        BrowserPackageQueryEvent projectedMatch =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(
                    new PackageQueryMatch(
                        profile,
                        PackageQueryAcquisitionTier.Nuspec,
                        [],
                        [])));
        string expectedProducer =
            source.Source.Producer.Display.ToString();

        Assert.Equal(
            "https://api.nuget.org:443/v3/index.json",
            expectedProducer);
        Assert.Equal(BrowserPackageQueryEventKind.Failure, projectedFailure.Kind);
        Assert.Equal(
            BrowserPackageQueryFailureKind.ManifestAcquisition,
            projectedFailure.Failure!.Kind);
        Assert.Equal("manifest unavailable", projectedFailure.Failure.Message);
        Assert.Equal(
            BrowserPackageQueryManifestFailureReason.InvalidDependencyContract,
            projectedFailure.Failure.ManifestFailureReason);
        Assert.Equal(
            expectedProducer,
            projectedFailure.Failure.Producer);
        Assert.Equal(
            BrowserPackageQueryEventKind.Completed,
            projectedCompletion.Kind);
        Assert.Equal(
            BrowserPackageQueryCompletionKind.CandidateLimitReached,
            projectedCompletion.Completion!.Kind);
        Assert.Equal(
            expectedProducer,
            projectedCompletion.Completion.Producer);
        Assert.Equal(200, projectedCompletion.Completion.CandidateLimit);
        Assert.Equal(100, projectedCompletion.Completion.MatchLimit);
        Assert.Equal(
            expectedProducer,
            projectedMatch.Row!.Producer);
        Assert.Equal(
            ["Contoso", "Fabrikam"],
            projectedMatch.Row.Owners);
        BrowserPackageQueryManifest projectedManifest =
            Assert.IsType<BrowserPackageQueryManifest>(
                projectedMatch.Row.Manifest);
        Assert.Equal("contoso.package", projectedManifest.PackageId);
        Assert.Equal("1.0.0", projectedManifest.Version);
        Assert.Equal("nuspec", projectedManifest.ManifestVersion);
        Assert.Equal("Package description.", projectedManifest.Description);
        Assert.Equal("Contoso; Fabrikam", projectedManifest.Authors);
        Assert.Equal(
            "https://example.test/contoso/package",
            projectedManifest.Repository);
        Assert.Equal("git", projectedManifest.RepositoryType);
        Assert.Equal(
            "0123456789abcdef",
            projectedManifest.RepositoryCommit);
        Assert.Equal("MIT", projectedManifest.License);
        Assert.Equal(
            "https://example.test/licenses/mit",
            projectedManifest.LicenseUrl);
        Assert.Equal(["DotnetTool"], projectedManifest.PackageTypes);
        Assert.True(projectedManifest.IsToolPackage);
        Assert.Equal("README.md", projectedManifest.ReadmeFile);
        BrowserPackageQueryDeclaredDependencyGroup projectedGroup =
            Assert.Single(projectedManifest.DependencyGroups);
        Assert.Equal("net10.0", projectedGroup.TargetFramework);
        Assert.False(projectedGroup.IsImplicitManifestGroup);
        BrowserPackageQueryDeclaredDependency projectedDependency =
            Assert.Single(projectedGroup.Dependencies);
        Assert.Equal("Contoso.Dependency", projectedDependency.Id);
        Assert.Equal("[2.0.0,3.0.0)", projectedDependency.VersionRange);
        Assert.Equal("icon.png", projectedManifest.IconFile);
        Assert.Equal(
            "https://example.test/icon.png",
            projectedManifest.IconUrl);
        Assert.Equal(
            BrowserPackageQueryManifestIdentityProvenance.SelfAttested,
            projectedManifest.IdentityProvenance);
        Assert.Equal(
            BrowserPackageQueryEventKind.Progress,
            projectedProgress.Kind);
        Assert.Equal(
            BrowserPackageQueryProgressPhase.Manifest,
            projectedProgress.Progress!.Phase);
        Assert.Equal(3, projectedProgress.Progress.Completed);
        Assert.Equal(20, projectedProgress.Progress.Limit);
    }

    [Fact]
    public void Project_PreservesPackageContentTierAndFailure()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageProfileMatch package = new(
            "Contoso.Tool",
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest(
                "Contoso.Tool",
                "1.0.0",
                isToolPackage: true));
        var match = new PackageQueryMatch(
            package,
            PackageQueryAcquisitionTier.PackageContent,
            [
                new PackageQueryAnswer(
                    PackageQuery.SkillTermKey,
                    new InertString(TextPolicy.Field, "true")),
            ],
            [
                new PackageQueryEvidence(PackageQuery.SkillTermKey)
                {
                    Scope = PackageQueryEvidenceScope.Package,
                    Summary = new PackageQueryEvidenceSummary(
                        2,
                        [
                            new InertString(TextPolicy.Field, "skills/SKILL.md"),
                            new InertString(
                                TextPolicy.Field,
                                "skills/build/SKILL.md"),
                        ]),
                },
                new PackageQueryEvidence("package.query.source-selection")
                {
                    Scope = PackageQueryEvidenceScope.Query,
                    Properties =
                    [
                        new(
                            "producer",
                            new InertString(TextPolicy.Field, "nuget.org")),
                    ],
                },
            ]);
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            source.Source,
            PackageQueryFailureKind.PackageContentAcquisition,
            "package payload unavailable");

        BrowserPackageQueryEvent projectedMatch =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(match));
        BrowserPackageQueryEvent projectedFailure =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Failure(failure));

        Assert.Equal(
            BrowserPackageQueryAcquisitionTier.PackageContent,
            projectedMatch.Row!.Tier);
        Assert.Equal(
            "true",
            Assert.Single(projectedMatch.Row.Answers).Value);
        Assert.Collection(
            projectedMatch.Row.Evidence,
            evidence =>
            {
                Assert.Equal(
                    BrowserPackageQueryEvidenceScope.Package,
                    evidence.Scope);
                Assert.Equal(2, evidence.Summary!.Count);
                Assert.Equal(
                    ["skills/SKILL.md", "skills/build/SKILL.md"],
                    evidence.Summary.Preview);
            },
            evidence =>
            {
                Assert.Equal(
                    BrowserPackageQueryEvidenceScope.Query,
                    evidence.Scope);
                Assert.Null(evidence.Summary);
            });
        Assert.Equal(
            BrowserPackageQueryFailureKind.PackageContentAcquisition,
            projectedFailure.Failure!.Kind);
    }

    [Fact]
    public void Project_PreservesStructuredTermAttribution()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageProfileMatch package = new(
            "Contoso.Library",
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest("Contoso.Library", "1.0.0", isToolPackage: false));
        var term = new PortableQueryTerm(
            "depends",
            PortableQueryOperator.Equal,
            "Microsoft.Extensions.Hosting");
        var match = new PackageQueryMatch(
            package,
            PackageQueryAcquisitionTier.Nuspec,
            [
                new PackageQueryAnswer(
                    "depends",
                    new InertString(
                        TextPolicy.Field,
                        "Microsoft.Extensions.Hosting"))
                {
                    Term = term,
                },
            ],
            [
                new PackageQueryEvidence("depends")
                {
                    Term = term,
                    Summary = new PackageQueryEvidenceSummary(
                        1,
                        [
                            new InertString(
                                TextPolicy.Field,
                                "any: Microsoft.Extensions.Hosting [10.0.0, )"),
                        ]),
                },
            ]);

        BrowserPackageQueryEvidence evidence = Assert.Single(
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(match)).Row!.Evidence);

        Assert.Equal(
            new BrowserPackageQueryTerm(
                "depends",
                "eq",
                "Microsoft.Extensions.Hosting"),
            evidence.Term);
    }

    [Fact]
    public async Task Serialize_RoundTripsThroughBrowserJsonContext()
    {
        var queryEvent = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "Contoso.",
                PackageProducerIdentity.NuGetOrg.Display.ToString(),
                CandidateLimit: 200,
                MatchLimit: 100,
                Candidates: 5,
                Matches: 2,
                Failures: 0,
                BrowserPackageQueryCompletionKind.Exhausted));

        BrowserPackageQueryOperations.StartSerializationPreparation();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        string json = BrowserPackageQueryOperations.Serialize(queryEvent);
        BrowserPackageQueryEvent? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);

        Assert.Equal(queryEvent, roundTripped);
    }

    [Fact]
    public async Task Serialize_AllowsOwnerValidUnicodeManifestToExpandPastDecodedBudget()
    {
        string packageType = new(
            '\u00E9',
            PackageManifestFactsQuery.MaxScalarCharacters);
        string[] packageTypes = Enumerable
            .Repeat(packageType, 8)
            .ToArray();
        var queryEvent = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Match,
            Row: new BrowserPackageQueryRow(
                "Contoso.Unicode",
                "1.0.0",
                BrowserPackageQueryAcquisitionTier.Nuspec,
                Answers: [],
                Evidence: [],
                TotalDownloads: null,
                Verified: null,
                Producer: PackageProducerIdentity.NuGetOrg.Display.ToString(),
                RootRequest: "root1:Contoso.Unicode@1.0.0")
            {
                Manifest = new BrowserPackageQueryManifest(
                    "Contoso.Unicode",
                    "1.0.0",
                    "nuspec",
                    Description: null,
                    Authors: null,
                    Repository: null,
                    RepositoryType: null,
                    RepositoryCommit: null,
                    License: null,
                    LicenseUrl: null,
                    packageTypes,
                    IsToolPackage: false,
                    ReadmeFile: null,
                    DependencyGroups: [],
                    IconFile: null,
                    IconUrl: null,
                    BrowserPackageQueryManifestIdentityProvenance.ExpectedCoordinate),
            },
            Failure: null,
            Completion: null);

        Assert.True(packageTypes.Length <= PackageManifestFactsQuery.MaxPackageTypes);
        Assert.True(
            packageTypes.Sum(value => value.Length)
            < PackageManifestFactsQuery.MaxManifestCharacters);

        BrowserPackageQueryOperations.StartSerializationPreparation();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        string json = BrowserPackageQueryOperations.Serialize(queryEvent);
        BrowserPackageQueryEvent? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);

        Assert.True(json.Length > 1_048_576);
        Assert.Contains("\\u00E9", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            packageTypes,
            roundTripped!.Row!.Manifest!.PackageTypes);
    }

    [Fact]
    public async Task ListCatalog_PreparesSerializationWithoutChangingCatalog()
    {
        string json = PackageExports.ListPackageQueryCatalog();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        BrowserPackageQueryCatalog? catalog = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryCatalog);

        Assert.NotNull(catalog);
        Assert.Equal(
            BrowserPackageQueryOperations.Catalog().Presets,
            catalog.Presets);
        BrowserPackageQueryTermDescriptor[] expectedTerms =
            BrowserPackageQueryOperations.Catalog().Terms;
        Assert.Equal(expectedTerms.Length, catalog.Terms.Length);
        for (int index = 0; index < expectedTerms.Length; index++)
        {
            Assert.Equal(expectedTerms[index].Key, catalog.Terms[index].Key);
            Assert.Equal(
                expectedTerms[index].Operators,
                catalog.Terms[index].Operators);
        }
    }

    [Fact]
    public async Task Coordinator_TargetsCancellationAndCreditByOperationId()
    {
        BrowserManagedOperationId firstId =
            BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        BrowserManagedOperationId secondId =
            BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        var releaseFirst =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReceivedCredit =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReceivedCredit =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondObservedCancellation =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<BrowserManagedOperationResult<int, string, string>> first =
            BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                firstId,
                initialMatchCredit: 1,
                eventCallback: null,
                async (credit, _, token) =>
                {
                    await credit.WaitAsync(token);
                    await credit.WaitAsync(token);
                    firstReceivedCredit.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                    return 1;
                });
        Task<BrowserManagedOperationResult<int, string, string>> second =
            BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                secondId,
                initialMatchCredit: 1,
                eventCallback: null,
                async (credit, _, token) =>
                {
                    await credit.WaitAsync(token);
                    try
                    {
                        await credit.WaitAsync(token);
                        secondReceivedCredit.SetResult();
                        return 2;
                    }
                    catch (OperationCanceledException)
                    {
                        secondObservedCancellation.SetResult();
                        await releaseSecond.Task;
                        throw;
                    }
                });

        var granted = Assert.IsType<
            BrowserPackageQueryMatchCreditRequestResult.Granted>(
                BrowserPackageQueryOperationCoordinator.RequestMatches(
                    firstId,
                    additionalMatchCredit: 1));
        Assert.Equal(1, granted.AdditionalMatchCredit);
        await firstReceivedCredit.Task;
        Assert.False(secondReceivedCredit.Task.IsCompleted);

        var requested = Assert.IsType<
            BrowserManagedCancellationRequestResult.Requested>(
                BrowserPackageQueryOperationCoordinator.RequestCancellation(
                    secondId,
                    BrowserManagedOperationCancelReason.User));
        Assert.Equal(BrowserManagedOperationCancelReason.User, requested.Reason);
        await secondObservedCancellation.Task;
        var repeated = Assert.IsType<
            BrowserManagedCancellationRequestResult.AlreadyRequested>(
                BrowserPackageQueryOperationCoordinator.RequestCancellation(
                    secondId,
                    BrowserManagedOperationCancelReason.Timeout));
        Assert.Equal(BrowserManagedOperationCancelReason.User, repeated.Reason);

        releaseSecond.SetResult();
        var canceled = Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Canceled>(
                await second);
        Assert.Equal(BrowserManagedOperationCancelReason.User, canceled.Reason);
        Assert.False(first.IsCompleted);

        releaseFirst.SetResult();
        Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Succeeded>(
                await first);
        Assert.IsType<BrowserPackageQueryMatchCreditRequestResult.NotActive>(
            BrowserPackageQueryOperationCoordinator.RequestMatches(firstId, 1));
        Assert.IsType<BrowserPackageQueryMatchCreditRequestResult.NotActive>(
            BrowserPackageQueryOperationCoordinator.RequestMatches(secondId, 1));
    }

    [Fact]
    public async Task Coordinator_RejectsDuplicateActiveIdAndReadmitsAfterRelease()
    {
        BrowserManagedOperationId id =
            BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        var release =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<BrowserManagedOperationResult<int, string, string>> original =
            BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                id,
                initialMatchCredit: 1,
                eventCallback: null,
                async (_, _, token) =>
                {
                    await release.Task.WaitAsync(token);
                    return 1;
                });

        var error = await Assert.ThrowsAsync<
            BrowserManagedOperationBoundaryException>(
                () => BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                    id,
                    initialMatchCredit: 1,
                    eventCallback: null,
                    (_, _, _) => Task.FromResult(2)));
        Assert.Equal("duplicate-active-operation", error.FailureKind);
        Assert.False(original.IsCompleted);

        release.SetResult();
        Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Succeeded>(
                await original);
        Assert.IsType<BrowserManagedCancellationRequestResult.NotActive>(
            BrowserPackageQueryOperationCoordinator.RequestCancellation(
                id,
                BrowserManagedOperationCancelReason.User));

        var readmitted = Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Succeeded>(
                await BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                    id,
                    initialMatchCredit: 1,
                    eventCallback: null,
                    (_, _, _) => Task.FromResult(3)));
        Assert.Equal(3, readmitted.Value);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidPlansBeforeStartingSourceWork()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BrowserPackageQueryOperations.ExecuteAsync(
                "Contoso.Package",
                [
                    new PortableQueryTerm(
                        "unknown",
                        PortableQueryOperator.Equal,
                        "true"),
                ],
                maximumCandidates: 200,
                maximumMatches: 100,
                includePrerelease: false,
                matchCredit: null,
                _ => { },
                TestContext.Current.CancellationToken));

        Assert.Contains(
            "does not define term 'unknown'",
            error.Message);
    }

    [Fact]
    public void ExpectedFailure_UsesTheVisibleExpectedRoute()
    {
        BrowserPackageQueryResult result =
            BrowserPackageQueryResult.ExpectedFailure(
                "A package-query term value is invalid.");

        Assert.Equal(BrowserPackageQueryResultKind.Failed, result.Kind);
        Assert.Equal(
            BrowserPackageQueryOperationFailureKind.Expected,
            result.FailureKind);
        Assert.Equal("A package-query term value is invalid.", result.Error);
        Assert.Equal("A package-query term value is invalid.", result.Diagnostic);
    }

    [Fact]
    public async Task PumpAsync_EmitsOnlyNonterminalEventsAndReturnsCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Progress progress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Search,
                Completed: 0,
                Limit: 1));
        PackageQueryEvent.Failure failure = new(
            new PackageQueryFailure(
                PackageId: null,
                Version: null,
                source.Source,
                PackageQueryFailureKind.Search,
                "search unavailable"));
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 1,
                PackageQueryCompletionKind.Failed));
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent returned =
            await BrowserPackageQueryOperations.PumpAsync(
                Events(progress, failure, completed),
                matchCredit: null,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                BrowserPackageQueryEventKind.Progress,
                BrowserPackageQueryEventKind.Failure,
            ],
            emitted.Select(item => item.Kind));
        Assert.Equal(BrowserPackageQueryEventKind.Completed, returned.Kind);
    }

    [Fact]
    public async Task EventObserverPublishesOnlyNonterminalContent()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Progress progress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Search,
                Completed: 0,
                Limit: 20));
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
        var emitted = new List<BrowserPackageQueryEvent>();
        var observer = new BrowserPackageQueryOperations.EventObserver(
                matchCredit: null,
                emitted.Add,
                deadline: null);

        await observer.ReportAsync(
            progress,
            TestContext.Current.CancellationToken);
        var envelope = new InspectionEnvelope<PackageQueryDocument>(
                new(
                    Results: [],
                    Failures: [],
                    completed.Value),
                new InspectionShare.NonProjectable(
                    "package-query/share",
                    "No canonical Workspace packet."));
        BrowserPackageQueryInspection inspection =
            BrowserPackageQueryOperations.Complete(envelope);

        Assert.Empty(inspection.Content.Results);
        Assert.False(inspection.Content.HasPackages);
        Assert.Empty(inspection.Content.Failures);
        Assert.Equal(
            BrowserPackageQueryCompletionKind.Exhausted,
            inspection.Content.Completion.Kind);
        Assert.Equal(
            BrowserInspectionShareKind.NonProjectable,
            inspection.Share.Kind);
        Assert.Equal("package-query/share", inspection.Share.Path);
        Assert.Empty(inspection.Diagnostics);
        Assert.Single(emitted);
        Assert.Equal(
            BrowserPackageQueryEventKind.Progress,
            emitted[0].Kind);
    }

    [Fact]
    public void OperationCompletionProjectsHasPackages()
    {
        PackageQueryEvent.Match match = MatchEvent("Contoso.One");
        var completed = Assert.IsType<PackageQueryEvent.Completed>(
            CompletedEvent());
        var envelope = new InspectionEnvelope<PackageQueryDocument>(
            new(
                Results: [match.Value],
                Failures: [],
                completed.Value),
            new InspectionShare.NonProjectable(
                "package-query/share",
                "No canonical Workspace packet."));

        BrowserPackageQueryInspection inspection =
            BrowserPackageQueryOperations.Complete(envelope);

        Assert.True(inspection.Content.HasPackages);
        Assert.Single(inspection.Content.Results);
    }

    [Fact]
    public void PackageQueryResultPreservesInspectionEnvelopeProjection()
    {
        var completed = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "Contoso.",
                PackageProducerIdentity.NuGetOrg.Display.ToString(),
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                BrowserPackageQueryCompletionKind.Exhausted));
        var inspection = new BrowserPackageQueryInspection(
            new BrowserPackageQueryDocument(
                Results: [],
                HasPackages: false,
                Failures: [],
                completed.Completion!),
            new BrowserInspectionShare(
                BrowserInspectionShareKind.NonProjectable,
                FullUrl: null,
                Packet: null,
                "package-query/share",
                "No canonical Workspace packet."),
            [
                new BrowserInspectionDiagnostic(
                    "package-query-note",
                    "Information",
                    "Package Query completed.",
                    Correspondence: null),
            ]);
        var result = BrowserPackageQueryResult.From(
            new BrowserManagedOperationResult<
                BrowserPackageQueryInspection,
                string,
                string>.Succeeded(inspection));

        string json = JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
        BrowserPackageQueryResult? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryResult);

        Assert.NotNull(roundTripped);
        Assert.Equal(3, roundTripped.Version);
        Assert.Null(roundTripped.Value);
        Assert.NotNull(roundTripped.Inspection);
        Assert.Equal(
            inspection.Content.Completion,
            roundTripped.Inspection.Content.Completion);
        Assert.Empty(roundTripped.Inspection.Content.Results);
        Assert.False(roundTripped.Inspection.Content.HasPackages);
        Assert.Empty(roundTripped.Inspection.Content.Failures);
        Assert.Equal(inspection.Share, roundTripped.Inspection.Share);
        Assert.Equal(
            inspection.Diagnostics,
            roundTripped.Inspection.Diagnostics);
    }

    [Fact]
    public async Task EventObserverPausesMatchDeliveryUntilCreditIsReplenished()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        var observer = new BrowserPackageQueryOperations.EventObserver(
            matchCredit,
            emitted.Add,
            deadline: null);

        await observer.ReportAsync(
            MatchEvent("Contoso.One"),
            TestContext.Current.CancellationToken);
        Task pending = observer.ReportAsync(
                MatchEvent("Contoso.Two"),
                TestContext.Current.CancellationToken)
            .AsTask();

        Assert.Single(emitted);
        Assert.False(pending.IsCompleted);
        Assert.True(matchCredit.TryAdd(1));
        await pending;
        Assert.Equal(
            ["Contoso.One", "Contoso.Two"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task EventObserverCallerCancellationReleasesWaitingMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        var observer = new BrowserPackageQueryOperations.EventObserver(
            matchCredit,
            emitted.Add,
            deadline: null);

        await observer.ReportAsync(
            MatchEvent("Contoso.One"),
            cancellation.Token);
        Task pending = observer.ReportAsync(
                MatchEvent("Contoso.Two"),
                cancellation.Token)
            .AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending);
        Assert.Equal(
            ["Contoso.One", "Contoso.Two"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task EventObserverActiveWorkExpiryDoesNotPublishWaitingMatch()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        using var callerCancellation = new CancellationTokenSource();
        var emitted = new List<BrowserPackageQueryEvent>();

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                async deadline =>
                {
                    var observer =
                        new BrowserPackageQueryOperations.EventObserver(
                            matchCredit,
                            emitted.Add,
                            deadline);
                    await observer.ReportAsync(
                        MatchEvent("Contoso.One"),
                        deadline.Token);
                    while (!deadline.HasExpired)
                        Thread.SpinWait(100);
                    await observer.ReportAsync(
                        MatchEvent("Contoso.Two"),
                        deadline.Token);
                    return 0;
                },
                TimeSpan.FromMilliseconds(100),
                callerCancellation.Token));

        Assert.Single(emitted);
        Assert.False(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task PumpAsync_RejectsAnEventAfterCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
        PackageQueryEvent.Progress lateProgress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Manifest,
                Completed: 1,
                Limit: 20));
        var emitted = new List<BrowserPackageQueryEvent>();

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageQueryOperations.PumpAsync(
                    Events(completed, lateProgress),
                    matchCredit: null,
                    emitted.Add,
                    TestContext.Current.CancellationToken));

        Assert.Contains("after completion", error.Message);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task PumpAsync_PausesMatchDeliveryUntilCreditIsReplenished()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 2);
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageQueryOperations.PumpAsync(
                CountedMatchEvents(() => established++),
                matchCredit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => established == 3);
        Assert.Equal(2, emitted.Count);
        Assert.False(pumping.IsCompleted);

        Assert.True(matchCredit.TryAdd(1));
        BrowserPackageQueryEvent completed = await pumping;

        Assert.Equal(3, emitted.Count);
        Assert.All(
            emitted,
            item => Assert.Equal(
                BrowserPackageQueryEventKind.Match,
                item.Kind));
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_ReadsCompletionWithoutAdditionalMatchCredit()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent completed =
            await BrowserPackageQueryOperations.PumpAsync(
                Events(MatchEvent("Contoso.One"), CompletedEvent()),
                matchCredit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Single(emitted);
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_AssemblyAssessmentDoesNotSpendMatchCredit()
    {
        var match = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Match,
            Row: new BrowserPackageQueryRow(
                "Contoso.Match", "1.0.0",
                BrowserPackageQueryAcquisitionTier.Assembly,
                [],
                [new BrowserPackageQueryEvidence(
                    "il-string-literal-contains",
                    BrowserPackageQueryEvidenceScope.Package,
                    null,
                    [new("value", "Matched.")],
                    null)],
                TotalDownloads: null,
                Verified: null,
                Producer: "nuget.org",
                RootRequest: "opaque-match-root"),
            Failure: null,
            Completion: null);
        var assessment = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Assessment,
            Row: null,
            Failure: null,
            Completion: null,
            Assessment: new BrowserPackageAssemblyAssessment(
                "Contoso.Other", "1.0.0",
                BrowserPackageAssemblyAssessmentKind.NoMatch,
                "No decoded literal matched in the selected assembly.",
                "lib/net10.0/Contoso.Other.dll",
                "opaque-assessment-root",
                []));
        var completion = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "", "nuget.org", CandidateLimit: 2, MatchLimit: 2,
                Candidates: 2, Matches: 1, Failures: 0,
                BrowserPackageQueryCompletionKind.ExplicitCandidatesComplete,
                SemanticMisses: 1, NotApplicable: 0,
                Scope: "Selector-issued primary implementation assemblies"));
        using var credit = new BrowserPackageQueryMatchCredit(initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent returned =
            await BrowserPackageQueryOperations.PumpAsync(
                BrowserEvents(match, assessment, completion),
                static item => item,
                credit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal([match, assessment], emitted);
        Assert.Same(completion, returned);
    }

    [Fact]
    public async Task PumpAsync_CancellationReleasesAWaitingMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageQueryOperations.PumpAsync(
                CountedMatchEvents(() => established++),
                matchCredit,
                emitted.Add,
                cancellation.Token);

        await WaitUntilAsync(() => established == 2);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pumping);
        Assert.Equal(
            ["Contoso.1", "Contoso.2"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task PumpAsync_ConsumerWaitOutlivesActiveWorkBudget()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() => { }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

        Assert.Single(emitted);
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        Assert.False(pumping.IsCompleted);
        Assert.Single(emitted);

        Assert.True(matchCredit.TryAdd(2));
        BrowserPackageQueryEvent completed = await pumping;
        Assert.Equal(3, emitted.Count);
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_ActiveWorkExpiryDoesNotPublishUncreditedMatch()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        using var callerCancellation = new CancellationTokenSource();
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() =>
                    {
                        if (++established == 2)
                        {
                            while (!deadline.HasExpired)
                                Thread.SpinWait(100);
                        }
                    }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromMilliseconds(100),
                callerCancellation.Token));

        Assert.Equal(2, established);
        Assert.Single(emitted);
        Assert.False(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task PumpAsync_CallerCancellationStillReleasesBudgetPausedWait()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() => { }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromSeconds(1),
                callerCancellation.Token);

        Assert.Single(emitted);
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pumping);
        Assert.Equal(
            ["Contoso.1", "Contoso.2"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task PackageOperation_ConsumerWaitDoesNotResetSpentBudget()
    {
        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                async deadline =>
                {
                    while (deadline.Remaining > TimeSpan.FromMilliseconds(500))
                        Thread.SpinWait(100);
                    TimeSpan remaining = deadline.Remaining;

                    await deadline.WaitForConsumerAsync(token =>
                        new ValueTask(Task.Delay(1200, token)));

                    Assert.True(deadline.Remaining <= remaining);
                    Assert.False(deadline.Token.IsCancellationRequested);
                    await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
                    return 0;
                },
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken));
    }

    static BrowserPackageQueryAcquisitionTier BrowserTier(
        PackageQueryAcquisitionTier tier) => tier switch
        {
            PackageQueryAcquisitionTier.Nuspec =>
                BrowserPackageQueryAcquisitionTier.Nuspec,
            PackageQueryAcquisitionTier.PackageContent =>
                BrowserPackageQueryAcquisitionTier.PackageContent,
            PackageQueryAcquisitionTier.SearchMetadata =>
                BrowserPackageQueryAcquisitionTier.SearchMetadata,
            _ => throw new InvalidOperationException(
                "Unknown package-query tier."),
        };

    static PortableQueryTerm[] InspectionTerms(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index =>
            new PortableQueryTerm(
                PackageQuery.DependsTermKey,
                PortableQueryOperator.Equal,
                $"Contoso.Dependency.{index:D2}")),
    ];

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    static async IAsyncEnumerable<PackageQueryEvent> CountedMatchEvents(
        Action established)
    {
        for (int index = 1; index <= 3; index++)
        {
            established();
            yield return MatchEvent($"Contoso.{index}");
        }
        yield return CompletedEvent(matches: 3);
        await Task.CompletedTask;
    }

    static async IAsyncEnumerable<PackageQueryEvent> Events(
        params PackageQueryEvent[] events)
    {
        await Task.CompletedTask;
        foreach (PackageQueryEvent queryEvent in events)
            yield return queryEvent;
    }

    static async IAsyncEnumerable<BrowserPackageQueryEvent> BrowserEvents(
        params BrowserPackageQueryEvent[] events)
    {
        await Task.CompletedTask;
        foreach (BrowserPackageQueryEvent queryEvent in events)
            yield return queryEvent;
    }

    static PackageQueryEvent.Match MatchEvent(string packageId)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var package = new PackageProfileMatch(
            packageId,
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest(packageId, "1.0.0", isToolPackage: false));
        return new PackageQueryEvent.Match(
            new PackageQueryMatch(
                package,
                PackageQueryAcquisitionTier.Nuspec,
                [],
                [
                    new PackageQueryEvidence(
                        "package.query.source-verified")
                    {
                        Properties =
                        [
                            new(
                                "value",
                                new InertString(
                                    TextPolicy.Field,
                                    "Matched")),
                        ],
                    },
                ]));
    }

    static PackageQueryEvent CompletedEvent(int matches = 1)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        return new PackageQueryEvent.Completed(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: matches,
                Matches: matches,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
    }

    static PackageManifestFacts Manifest(
        string packageId,
        string version,
        bool isToolPackage) =>
        new(
            PackageSourceCoordinate.Create(packageId, version),
            "nuspec",
            Description: null,
            Authors: null,
            Repository: null,
            RepositoryType: null,
            RepositoryCommit: null,
            License: null,
            LicenseUrl: null,
            PackageTypes: isToolPackage ? ["DotnetTool"] : [],
            IsToolPackage: isToolPackage,
            ReadmeFile: null,
            DependencyGroups: []);

    static PackageRootReacquisitionRequest RootRequest(string producer)
    {
        Assert.True(
            RealizedMemberCoordinate.Package.TryCreate(
                "contoso.package",
                "1.0.0",
                producer,
                "net10.0",
                runtimeIdentifier: null,
                out RealizedMemberCoordinate.Package? coordinate,
                out string? problem),
            problem);
        return new PackageRootReacquisitionRequest(
            PackageArtifactRootRequest.Create(
                coordinate,
                compileTargetFramework: "net10.0",
                selectionTargetFramework: "net10.0",
                selectionRuntimeIdentifier: null,
                hasSelectedImplementationUniverse: true,
                usesCompatibleImplementationSelection: false,
                allowsCompatibleTargetSelection: false));
    }
}
