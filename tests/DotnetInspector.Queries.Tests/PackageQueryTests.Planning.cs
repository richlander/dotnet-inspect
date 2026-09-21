using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspector.QueryOperations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    [Fact]
    public void TermDescriptors_HaveStableOrderedVocabulary()
    {
        Assert.Equal(
            [
                ("package", 10),
                ("prefix", 20),
                ("prerelease", 30),
                ("dependencies", 100),
                ("dependency-target", 150),
                ("depends", 200),
                ("depends-ecosystem", 210),
                ("depends-transitive", 220),
                ("dependency-depth", 225),
                ("license", 250),
                ("downloads", 300),
                ("readme", 400),
                ("tool", 500),
                ("tool-format", 510),
                ("references", 550),
                ("skill", 600),
            ],
            PackageQuery.Terms.Select(term =>
                (term.Key, term.Weight)));
        Assert.Equal(
            [
                PackageQueryTermRole.Population,
                PackageQueryTermRole.Population,
                PackageQueryTermRole.Population,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
                PackageQueryTermRole.Inspection,
            ],
            PackageQuery.Terms.Select(term => term.Role));
        Assert.Equal(
            ".NET Tool",
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.ToolTermKey).Label);
        Assert.Equal(
            "embedded SKILL.md",
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.SkillTermKey).Label);
        PackageQueryTermDescriptor toolFormat = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.ToolFormatTermKey);
        Assert.True(toolFormat.CombinesWithinSelectionGroup);
        Assert.Equal(
            PackageQuery.ToolReplacementGroupId,
            toolFormat.ReplacementGroupId);
        Assert.Equal(PackageQuery.ToolDisplayGroupId, toolFormat.DisplayGroupId);
        Assert.Equal(
            PackageQuery.ToolReplacementGroupId,
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.ToolTermKey).ReplacementGroupId);
        Assert.Equal(["v1", "v2"], toolFormat.Options.Select(option => option.Value));
        Assert.Equal(
            "downloads",
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DownloadsTermKey).SelectionGroupId);
    }

    [Fact]
    public void TermDescriptors_SeparateAcquisitionAndExecutionClass()
    {
        Assert.Equal(
            [
                ("package", PackageQueryAcquisitionTier.SearchMetadata, PackageQueryExecutionClass.SearchMetadata),
                ("prefix", PackageQueryAcquisitionTier.SearchMetadata, PackageQueryExecutionClass.SearchMetadata),
                ("prerelease", PackageQueryAcquisitionTier.SearchMetadata, PackageQueryExecutionClass.SearchMetadata),
                ("dependencies", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("dependency-target", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("depends", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("depends-ecosystem", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("depends-transitive", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.NuspecExpensive),
                ("dependency-depth", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.NuspecExpensive),
                ("license", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("downloads", PackageQueryAcquisitionTier.SearchMetadata, PackageQueryExecutionClass.SearchMetadata),
                ("readme", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("tool", PackageQueryAcquisitionTier.Nuspec, PackageQueryExecutionClass.Nuspec),
                ("tool-format", PackageQueryAcquisitionTier.PackageContent, PackageQueryExecutionClass.PackageContent),
                ("references", PackageQueryAcquisitionTier.PackageContent, PackageQueryExecutionClass.Metadata),
                ("skill", PackageQueryAcquisitionTier.PackageContent, PackageQueryExecutionClass.PackageContent),
            ],
            PackageQuery.Terms.Select(term =>
                (term.Key, term.Tier, term.ExecutionClass)));
        Assert.DoesNotContain(
            PackageQuery.Terms,
            term => term.ExecutionClass
                == PackageQueryExecutionClass.MetadataExpensive);
        Assert.Equal(
            [
                "search-metadata",
                "nuspec",
                "nuspec-expensive",
                "package-content",
                "metadata",
                "metadata-expensive",
            ],
            Enum.GetValues<PackageQueryExecutionClass>()
                .Select(PackageQuery.ExecutionClassIdentity));
    }

    [Fact]
    public void TermDescriptors_ExposeClosedAndFreeValueShapes()
    {
        PackageQueryTermDescriptor depends = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.DependsTermKey);
        Assert.Equal(
            [
                PortableQueryModel.TextOf(PortableQueryOperator.Equal),
                PortableQueryModel.TextOf(
                    PortableQueryOperator.StartsWith),
            ],
            depends.Operators);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, depends.Tier);
        Assert.Equal(PackageQueryTermControlKind.Input, depends.ControlKind);
        Assert.Equal(
            "NuGet package ID or prefix",
            depends.ValueKind);
        PackageQueryTermDescriptor dependsTransitive =
            PackageQuery.Terms.Single(
                term => term.Key == PackageQuery.DependsTransitiveTermKey);
        Assert.Equal(
            PackageQueryExecutionClass.NuspecExpensive,
            dependsTransitive.ExecutionClass);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            dependsTransitive.ControlKind);
        PackageQueryTermDescriptor dependencyDepth =
            PackageQuery.Terms.Single(
                term => term.Key == PackageQuery.DependencyDepthTermKey);
        Assert.Equal(
            ["2", "3", "4"],
            dependencyDepth.Options.Select(option => option.Value));
        Assert.Equal(
            PackageQueryTermControlKind.Choice,
            dependencyDepth.ControlKind);
        PackageQueryTermDescriptor dependencies =
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DependenciesTermKey);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, dependencies.Tier);
        Assert.Equal(
            PackageQueryTermControlKind.Choice,
            dependencies.ControlKind);
        Assert.Equal(
            ["none", "cross-prefix"],
            dependencies.Options.Select(option => option.Value));
        Assert.Equal(
            "dependencies",
            dependencies.SelectionGroupId);
        PackageQueryTermDescriptor dependsEcosystem =
            PackageQuery.Terms.Single(
                term => term.Key == PackageQuery.DependsEcosystemTermKey);
        Assert.Equal(
            "canonical ecosystem ID",
            dependsEcosystem.ValueKind);
        Assert.Equal(
            PackageQueryAcquisitionTier.Nuspec,
            dependsEcosystem.Tier);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            dependsEcosystem.ControlKind);
        PackageQueryTermDescriptor dependencyTarget =
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DependencyTargetTermKey);
        Assert.Equal(
            "all or NuGet target framework",
            dependencyTarget.ValueKind);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            dependencyTarget.ControlKind);
        Assert.Equal(
            "dependency-target",
            dependencyTarget.SelectionGroupId);
        Assert.Equal(
            ["10k", "100k", "1m"],
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DownloadsTermKey)
                .Options.Select(option => option.Value));
        PackageQueryTermDescriptor references =
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.ReferencesTermKey);
        Assert.Equal(
            PackageQueryAcquisitionTier.PackageContent,
            references.Tier);
        Assert.Equal(
            PackageQueryTermControlKind.Input,
            references.ControlKind);
        Assert.Equal("assembly simple name", references.ValueKind);
        PackageQueryTermDescriptor license = PackageQuery.Terms.Single(
            term => term.Key == PackageQuery.LicenseTermKey);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, license.Tier);
        Assert.Equal(PackageQueryTermControlKind.Choice, license.ControlKind);
        Assert.Equal(
            ["any", "MIT", "OSMF"],
            license.Options.Select(option => option.Value));
    }

    [Fact]
    public void OperationRoute_ProjectsTheCompleteExecutableVocabulary()
    {
        IQueryOperationRoute route = PackageQuery.OperationRoute;

        Assert.Equal(PackageQuery.OperationRouteIdentity, route.Identity);
        Assert.Equal(PackageQuery.OperationIdentity, route.OperationIdentity);
        Assert.Equal(
            PackageQuery.OperationSubjectRole,
            route.SubjectRole);
        Assert.Equal(
            PackageQuery.OperationResultGrain,
            route.ResultGrain);
        Assert.Equal(
            [PackageQuery.OperationPackagesRowSet],
            route.RowSets);
        Assert.Equal(
            PackageQuery.OperationProfileIdentity,
            route.ProfileIdentity);
        Assert.Equal(
            PackageQuery.VocabularyIdentity,
            route.Capabilities.Vocabulary);
        Assert.Equal(
            PackageQuery.Terms.Select(term => term.Key),
            route.Capabilities.Terms.Select(term =>
                term.Binding.Key));
        for (int index = 0; index < PackageQuery.Terms.Length; index++)
        {
            Assert.Equal(
                PackageQuery.Terms[index].Operators,
                route.Capabilities.Terms[index].Operators.Select(
                    PortableQueryModel.TextOf));
        }
        Assert.Equal(
            ["candidates", "matches"],
            route.Capabilities.Dimensions);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ],
            route.Capabilities.Stages);

        QueryOperationTermCapability packageContent =
            route.Capabilities.Terms.Single(term =>
                term.Binding.Key == PackageQuery.SkillTermKey);
        Assert.Contains(
            packageContent.Binding.Effects,
            effect =>
                effect.Kind == QueryOperationEffectKind.Capability
                && effect.Identity
                    == PackageQuery.PackageContentCapability);
        Assert.Contains(
            packageContent.Binding.Effects,
            effect =>
                effect.Kind == QueryOperationEffectKind.AcquisitionTier
                && effect.Identity == "package-content");
    }

    [Fact]
    public void RegisteredTerms_AreTheEffectiveOperationProjection()
    {
        Assert.Equal(
            PackageQuery.OperationRoute.Capabilities.Terms.Select(
                capability => capability.Binding.Key),
            PackageQuery.RegisteredTerms.Select(term =>
                term.Descriptor.Key));
        for (int index = 0;
             index < PackageQuery.RegisteredTerms.Length;
             index++)
        {
            Assert.Equal(
                PackageQuery.OperationRoute.Capabilities.Terms[index].Operators,
                PackageQuery.RegisteredTerms[index].Operators);
        }
        Assert.Equal(
            PackageQuery.RegisteredTerms.Select(term =>
                term.Descriptor),
            PackageQuery.Terms);
    }

    [Theory]
    [InlineData(
        "unknown",
        PortableQueryOperator.Equal,
        "Microsoft.Extensions.DependencyInjection",
        PackageQueryRequestFailureReason.UnknownTerm)]
    [InlineData(
        "depends",
        PortableQueryOperator.NotEqual,
        "Microsoft.Extensions.DependencyInjection",
        PackageQueryRequestFailureReason.TermOperatorNotAdmitted)]
    [InlineData(
        "depends",
        PortableQueryOperator.Equal,
        "not/a/package",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "depends",
        PortableQueryOperator.StartsWith,
        "not/a/prefix",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "depends",
        PortableQueryOperator.StartsWith,
        "Microsoft.*",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "dependencies",
        PortableQueryOperator.Equal,
        "other",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "license",
        PortableQueryOperator.Equal,
        "Apache-2.0",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "dependency-target",
        PortableQueryOperator.NotEqual,
        "net8.0",
        PackageQueryRequestFailureReason.TermOperatorNotAdmitted)]
    [InlineData(
        "dependency-target",
        PortableQueryOperator.Equal,
        "not/a/tfm",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "references",
        PortableQueryOperator.Equal,
        "System.Runtime, Version=10.0.0.0",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "references",
        PortableQueryOperator.Equal,
        "lib/System.Runtime",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "references",
        PortableQueryOperator.Equal,
        "System\\Runtime",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "references",
        PortableQueryOperator.Equal,
        " System.Runtime",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    [InlineData(
        "references",
        PortableQueryOperator.Equal,
        "",
        PackageQueryRequestFailureReason.InvalidTermValue)]
    public void PlanInput_RejectsInvalidTermsBeforeExecution(
        string key,
        PortableQueryOperator @operator,
        string value,
        PackageQueryRequestFailureReason reason)
    {
        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: [new PortableQueryTerm(key, @operator, value)]));

        Assert.Equal(reason, rejected.Reason);
    }

    [Fact]
    public void PlanInput_BindsAllAndCanonicalDependencyTargets()
    {
        PackageQueryPlan implicitAll = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms: [Term(PackageQuery.DependsTermKey, "Dependency")]));
        Assert.Equal(
            PackageQueryDependencyTargetKind.All,
            implicitAll.DependencyTarget.Kind);
        Assert.Null(
            implicitAll.DependencyTarget.RequestedTargetFramework);

        PackageQueryPlan explicitAll = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "ALL"),
                ]));
        Assert.Equal(
            PackageQueryDependencyTargetKind.All,
            explicitAll.DependencyTarget.Kind);
        Assert.Contains(
            explicitAll.Terms,
            term => term.Key == PackageQuery.DependencyTargetTermKey
                && term.Value == "ALL");

        PackageQueryPlan framework = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "NET8.0"),
                ]));
        Assert.Equal(
            PackageQueryDependencyTargetKind.TargetFramework,
            framework.DependencyTarget.Kind);
        Assert.Equal(
            "net8.0",
            framework.DependencyTarget.RequestedTargetFramework);

        PackageQueryPlan any = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "ANY"),
                ]));
        Assert.Equal(
            "any",
            any.DependencyTarget.RequestedTargetFramework);

        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "all"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net8.0"),
                ])).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "none"),
                    Term(PackageQuery.DependenciesTermKey, "CROSS-PREFIX"),
                ])).Reason);
    }

    [Fact]
    public void PlanInput_BindsDependencyStartsWithAndCollapsesCaseVariants()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "Microsoft.Extensions.",
                        PortableQueryOperator.StartsWith),
                    Term(
                        PackageQuery.DependsTermKey,
                        "microsoft.extensions.",
                        PortableQueryOperator.StartsWith),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "NET10.0"),
                ]));

        BoundPackageQueryTerm prefix = Assert.Single(
            plan.BoundTerms,
            term => term.Predicate.Kind
                == PackageQueryPredicateKind.DependsPrefix);
        Assert.Equal("Microsoft.Extensions.", prefix.Predicate.Text);
        Assert.Equal(
            "Microsoft.Extensions.",
            prefix.Predicate.PackagePrefix!.Prefix);
        Assert.Equal(
            "net10.0",
            plan.DependencyTarget.RequestedTargetFramework);
    }

    [Fact]
    public void PlanInput_DistinguishesDependencyOperators()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "Contoso.Dependency"),
                    Term(
                        PackageQuery.DependsTermKey,
                        "Contoso.Dependency",
                        PortableQueryOperator.StartsWith),
                ]));

        Assert.Equal(2, plan.BoundTerms.Length);
        Assert.Contains(
            plan.BoundTerms,
            term => term.Term.Operator == PortableQueryOperator.Equal
                && term.Predicate.Kind == PackageQueryPredicateKind.Depends);
        Assert.Contains(
            plan.BoundTerms,
            term => term.Term.Operator == PortableQueryOperator.StartsWith
                && term.Predicate.Kind
                    == PackageQueryPredicateKind.DependsPrefix);
    }

    [Fact]
    public void PlanInput_DependencyTargetRequiresDependencyPredicate()
    {
        PackageQueryRequestFailure failure = Rejected(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net8.0"),
                    Term(PackageQuery.DownloadsTermKey, "100k"),
                ]));

        Assert.Equal(
            PackageQueryRequestFailureReason
                .DependencyTargetRequiresDependencyPredicate,
            failure.Reason);
        Assert.Equal(
            [PackageQuery.DependencyTargetTermKey],
            failure.TermKeys);
    }

    [Fact]
    public void PlanInput_TransitiveDependencyRequiresExactTargetAndDepth()
    {
        PortableQueryTerm transitive = Term(
            PackageQuery.DependsTransitiveTermKey,
            "Contoso.Target");

        Assert.Equal(
            PackageQueryRequestFailureReason.TransitiveDependencyRequiresTarget,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    transitive,
                    Term(PackageQuery.DependencyDepthTermKey, "2"),
                ],
                maximumCandidates:
                    PackageQuery.MaximumNuspecExpensiveCandidates)).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.TransitiveDependencyRequiresTarget,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    transitive,
                    Term(PackageQuery.DependencyTargetTermKey, "all"),
                    Term(PackageQuery.DependencyDepthTermKey, "2"),
                ],
                maximumCandidates:
                    PackageQuery.MaximumNuspecExpensiveCandidates)).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.TransitiveDependencyRequiresDepth,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    transitive,
                    Term(PackageQuery.DependencyTargetTermKey, "net10.0"),
                ],
                maximumCandidates:
                    PackageQuery.MaximumNuspecExpensiveCandidates)).Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason
                .DependencyDepthRequiresTransitiveDependency,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Contoso.Target"),
                    Term(PackageQuery.DependencyDepthTermKey, "2"),
                ],
                maximumCandidates:
                    PackageQuery.MaximumNuspecExpensiveCandidates)).Reason);

        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            terms:
            [
                transitive,
                Term(PackageQuery.DependencyTargetTermKey, "NET10.0"),
                Term(PackageQuery.DependencyDepthTermKey, "2"),
            ],
            maximumCandidates: PackageQuery.MaximumNuspecExpensiveCandidates));
        Assert.True(plan.RequiresDependencyTraversal);
        Assert.Equal(2, plan.DependencyDepth);
        Assert.Equal(
            "net10.0",
            plan.DependencyTarget.RequestedTargetFramework);

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidCandidateLimit,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    transitive,
                    Term(PackageQuery.DependencyTargetTermKey, "net10.0"),
                    Term(PackageQuery.DependencyDepthTermKey, "2"),
                ],
                maximumCandidates:
                    PackageQuery.MaximumNuspecExpensiveCandidates + 1)).Reason);
    }

    [Fact]
    public void PlanInput_CollapsesEquivalentBoundTerms()
    {
        var exact = new PortableQueryTerm(
            PackageQuery.DependsTermKey,
            PortableQueryOperator.Equal,
            "Microsoft.Extensions.DependencyInjection");
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: [exact, exact]));
        Assert.Single(plan.Terms);

        PackageQueryPlan normalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    exact,
                    new(
                        PackageQuery.DependsTermKey,
                        PortableQueryOperator.Equal,
                        "microsoft.extensions.dependencyinjection"),
                ]));
        Assert.Single(normalized.Terms);

        PackageQueryPlan exclusiveNormalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                    Term(PackageQuery.DownloadsTermKey, "1M"),
                ]));
        Assert.Single(exclusiveNormalized.Terms);

        PackageQueryPlan reverseExclusiveNormalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    Term(PackageQuery.DownloadsTermKey, "1M"),
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                ]));
        Assert.Equal(
            exclusiveNormalized.Intent.Terms,
            reverseExclusiveNormalized.Intent.Terms);

        PackageQueryPlan structuralNormalized = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.DependencyInjection",
                terms:
                [
                    Term(
                        PackageQuery.PackageTermKey,
                        "microsoft.extensions.dependencyinjection"),
                ]));
        Assert.Single(
            structuralNormalized.Intent.Terms,
            term => term.Key == PackageQuery.PackageTermKey);

        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            Rejected(PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    Term(PackageQuery.DownloadsTermKey, "100k"),
                    Term(PackageQuery.DownloadsTermKey, "1m"),
                ])).Reason);
    }

    [Fact]
    public void PlanInput_EnforcesPortableInspectionTermBoundary()
    {
        PortableQueryTerm[] maximum = InspectionTerms(
            PackageQuery.MaximumInspectionTerms);
        PackageQueryPlan accepted = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: maximum));

        Assert.Equal(
            PortableQueryPayloadCodec.MaxTerms,
            accepted.Intent.Terms.Count);
        string payload = PortableQueryPayloadCodec.Encode(
            accepted.Intent,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            payload,
            PortableQueryPayloadCodec.Encode(
                PortableQueryPayloadCodec.Decode(
                    payload,
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken));

        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms: InspectionTerms(
                    PackageQuery.MaximumInspectionTerms + 1)));
        Assert.Equal(
            PackageQueryRequestFailureReason.TooManyTerms,
            rejected.Reason);
        Assert.Equal(
            PackageQuery.MaximumInspectionTerms + 1,
            rejected.Value);

        Assert.Equal(
            PackageQueryRequestFailureReason.TooManyTerms,
            Rejected(PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    .. Enumerable.Repeat(
                        maximum[0],
                        PackageQuery.MaximumInspectionTerms + 1),
                ])).Reason);
    }

    [Fact]
    public void PlanInput_RetainsStagesAndExplicitBounds()
    {
        RowSelectionIntent<string> selection = RowSelectionIntent<string>.Create(
        [
            RowSelectionIntentOperation<string>.Tail(4),
            RowSelectionIntentOperation<string>.Window(2, 3),
        ]);
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                maximumCandidates: 17,
                maximumMatches: null,
                rowSelection: selection));

        Assert.Equal(17, plan.MaximumCandidates);
        Assert.Null(plan.MaximumMatches);
        Assert.Equal(
            [RowSelectionStageKind.Tail, RowSelectionStageKind.Window],
            plan.RowSelection.Operations.Select(operation => operation.Kind));
        Assert.Equal(
            [RowSelectionStageKind.Tail, RowSelectionStageKind.Window],
            plan.Intent.Stages.Select(stage => stage.Kind));
        Assert.Contains(
            plan.Intent.Terms,
            term => term.Key == PackageQuery.PrefixTermKey
                && term.Value == "Microsoft.Extensions.");
        Assert.Contains(
            plan.Intent.Terms,
            term => term.Key == PackageQuery.PrereleaseTermKey
                && term.Value == "stable");
        Assert.Equal(
            [("candidates", 17)],
            plan.Intent.Bounds.Select(bound =>
                (bound.Dimension, bound.RequestedMaximum)));
    }

    [Theory]
    [InlineData("", PackageQueryRequestFailureReason.InvalidPackageInput)]
    [InlineData("\u202EContoso.", PackageQueryRequestFailureReason.InvalidPackageInput)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidCandidateLimit, 0, 1)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidCandidateLimit, PackageQuery.MaximumCandidates + 1, 1)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidMatchLimit, 1, 0)]
    [InlineData("Contoso.*", PackageQueryRequestFailureReason.InvalidMatchLimit, 1, PackageQuery.MaximumCandidates + 1)]
    public void Plan_RejectsInvalidScopeAndBoundsWithoutThrowing(
        string prefix,
        PackageQueryRequestFailureReason expected,
        int maximumCandidates = 1,
        int maximumMatches = 1)
    {
        PackageQueryPlanResult result = PackageQuery.Plan(
            new PackageQueryRequest(
                prefix,
                MaximumCandidates: maximumCandidates,
                MaximumMatches: maximumMatches));

        Assert.Equal(
            expected,
            Assert.IsType<PackageQueryPlanResult.Rejected>(result)
                .Failure.Reason);
    }

    [Fact]
    public void Plan_InvalidMatchLimitReportsTheAcceptedRange()
    {
        PackageQueryRequestFailure failure = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumMatches: PackageQuery.MaximumCandidates + 1)));

        Assert.Equal(
            $"The package-query match limit must be between 1 and {PackageQuery.MaximumCandidates}.",
            failure.Message);
    }

    [Fact]
    public void Plan_AcceptsScopeOnlyQuery()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("Contoso.*")));

        Assert.Empty(plan.Terms);
        Assert.Equal("Contoso.", plan.Prefix.ToString());
        Assert.Equal(PackageQuery.DefaultMaximumCandidates, plan.MaximumCandidates);
        Assert.Equal(PackageQuery.DefaultMaximumMatches, plan.MaximumMatches);
    }

    [Fact]
    public void Plan_PreservesAbsentMatchLimit()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 500,
                    MaximumMatches: null)));

        Assert.Equal(500, plan.MaximumCandidates);
        Assert.Null(plan.MaximumMatches);
    }

    [Fact]
    public void Plan_TreatsOneTrailingWildcardAsPrefixShorthand()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("System.*")));

        Assert.Equal("System.", plan.Prefix.ToString());
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            Rejected(PackageQuery.Plan(
                new PackageQueryRequest("System.*.Json")))
                .Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            Rejected(PackageQuery.Plan(
                new PackageQueryRequest("*")))
                .Reason);
    }

    [Fact]
    public void Plan_AcceptsMatchLimitAboveCandidateLimit()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 50)));

        Assert.Equal(50, plan.MaximumCandidates);
        Assert.Equal(PackageQuery.DefaultMaximumMatches, plan.MaximumMatches);
    }

    [Fact]
    public void Plan_AcceptsMaximumBounds()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: PackageQuery.MaximumCandidates,
                    MaximumMatches: PackageQuery.MaximumCandidates)));

        Assert.Equal(
            PackageQuery.MaximumCandidates,
            plan.MaximumCandidates);
        Assert.Equal(
            PackageQuery.MaximumCandidates,
            plan.MaximumMatches);
    }

    [Fact]
    public void Plan_RequiresThePackageContentCandidateBound()
    {
        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates + 1)));

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidCandidateLimit,
            rejected.Reason);

        PackageQueryPlan accepted = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            PackageQueryAcquisitionTier.PackageContent,
            Assert.Single(accepted.BoundTerms).Descriptor.Tier);
    }

    [Fact]
    public void Plan_RejectsUnknownAndIncompatibleTerms()
    {
        PackageQueryRequestFailure unknown = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term("unknown", "true")])));
        Assert.Equal(
            PackageQueryRequestFailureReason.UnknownTerm,
            unknown.Reason);

        PackageQueryRequestFailure incompatible = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolTermKey, "true"),
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                    ])));
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleTerms,
            incompatible.Reason);

        PackageQueryPlan toolVersions = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
                    ],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            ["v1", "v2"],
            toolVersions.Terms.Select(term => term.Value));
    }

    [Fact]
    public void ResolveIntent_RequiresPopulationPrereleaseAndCandidateBound()
    {
        PackageQueryRequestFailure missingPopulation = Rejected(
            PackageQuery.ResolveIntent(PortableQueryIntent.Create(
                [],
                [new("candidates", 10)],
                [],
                []),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageQueryRequestFailureReason.RequiredPopulationMissing,
            missingPopulation.Reason);

        PackageQueryRequestFailure missingBound = Rejected(
            PackageQuery.ResolveIntent(PortableQueryIntent.Create(
                [
                    Term(PackageQuery.PrefixTermKey, "Contoso."),
                    Term(PackageQuery.PrereleaseTermKey, "stable"),
                ],
                [],
                [],
                []),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageQueryRequestFailureReason.RequiredCandidateBoundMissing,
            missingBound.Reason);

        PackageQueryRequestFailure missingPrerelease = Rejected(
            PackageQuery.ResolveIntent(PortableQueryIntent.Create(
                [Term(PackageQuery.PrefixTermKey, "Contoso.")],
                [new("candidates", 10)],
                [],
                []),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageQueryRequestFailureReason.RequiredPrereleaseMissing,
            missingPrerelease.Reason);
    }
}
