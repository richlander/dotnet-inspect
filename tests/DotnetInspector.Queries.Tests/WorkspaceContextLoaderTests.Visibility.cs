using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task Visibility_PresetsOverridesAndGeneratedNamesUseIndependentFacets()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(workspace, VisibilityFixtureImage());
        TypeDeclarationLocatorResult.Evaluated query =
            LocateAll(
                CaptureDeclarations(workspace, context),
                new TypeDeclarationLocatorRequest.Pattern("*"));

        var raw =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    query,
                    TypeDeclarationLocatorSectionPlan.All));
        TypeDeclarationLocatorSectionAnswer rawAnswer =
            Assert.Single(raw.Answers);
        Assert.Null(raw.Visibility);
        Assert.Null(rawAnswer.Visibility);
        Assert.Equal(14, rawAnswer.Candidates.Length);
        Assert.Equal(
            query.Answers[0].Candidates.Select(
                VisibilityCandidateName),
            rawAnswer.Candidates.Select(
                VisibilityCandidateName));
        Assert.Contains(
            rawAnswer.Candidates,
            static candidate =>
                candidate.Name.Segments is ["<Generated>d__0"]);
        Assert.Contains(
            rawAnswer.Candidates,
            static candidate =>
                candidate.Name.Segments is
                    ["PublicOuter", "__GeneratedNested"]);

        TypeDeclarationLocatorSectionCandidate publicNested =
            VisibilityCandidate(rawAnswer, "PublicOuter+NestedPublic");
        TypeDeclarationLocatorSectionCandidate privateNested =
            VisibilityCandidate(rawAnswer, "PublicOuter+NestedPrivate");
        TypeDeclarationLocatorSectionCandidate internalNested =
            VisibilityCandidate(rawAnswer, "InternalOuter+NestedPublic");
        Assert.True(publicNested.IsPublicSurface);
        Assert.False(privateNested.IsPublicSurface);
        Assert.False(internalNested.IsPublicSurface);
        TypeDeclarationLocatorSectionCandidate forwarder =
            VisibilityCandidate(rawAnswer, "Forwarded");
        Assert.True(forwarder.IsPublicSurface);
        Assert.Null(forwarder.DiscoveryAttributes);
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(true, true),
            VisibilityCandidate(rawAnswer, "PublicBoth")
                .DiscoveryAttributes);

        TypeDeclarationVisibilityPlan defaultPlan =
            TypeDeclarationVisibilityPlan.Default;
        TypeDeclarationLocatorSectionResult.Evaluated @default =
            ProjectVisibility(query, defaultPlan);
        Assert.Same(defaultPlan, @default.Visibility);
        Assert.True(@default.IsSuccess);
        Assert.Equal(
            TypeDeclarationVisibilityPreset.Default,
            defaultPlan.Preset);
        Assert.Equal(3, defaultPlan.EffectivePredicates.Length);
        Assert.True(defaultPlan.ExcludeCompilerGeneratedNames);
        TypeDeclarationLocatorSectionAnswer defaultAnswer =
            Assert.Single(@default.Answers);
        AssertVisibilityNames(
            defaultAnswer,
            "PublicOrdinary",
            "PublicOuter",
            "PublicOuter+NestedPublic");
        Assert.Equal(3, defaultAnswer.AvailableCandidateCount);
        Assert.False(defaultAnswer.IsComplete);
        TypeDeclarationVisibilityCoverage defaultCoverage =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                defaultAnswer.Visibility);
        Assert.Equal(14, defaultCoverage.InputCandidateCount);
        Assert.Equal(10, defaultCoverage.ExcludedCandidateCount);
        TypeDeclarationVisibilityUnknownCandidate defaultUnknown =
            Assert.Single(defaultCoverage.UnknownCandidates);
        Assert.Equal("Forwarded", VisibilityCandidateName(defaultUnknown.Candidate));
        Assert.Equal(
            [
                TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                TypeDeclarationVisibilityFacet.Obsolete,
            ],
            defaultUnknown.Facets);
        Assert.Equal(
            AssemblyTypeDeclarationKind.Forwarder,
            defaultUnknown.Candidate.DeclarationKind);
        Assert.True(defaultUnknown.Candidate.IsPublicSurface);
        Assert.Null(defaultUnknown.Candidate.DiscoveryAttributes);
        Assert.False(defaultCoverage.IsComplete);

        TypeDeclarationVisibilityPlan allPlan =
            TypeDeclarationVisibilityPlan.All;
        TypeDeclarationLocatorSectionResult.Evaluated all =
            ProjectVisibility(query, allPlan);
        Assert.Same(allPlan, all.Visibility);
        Assert.True(all.IsSuccess);
        Assert.Equal(
            TypeDeclarationVisibilityPreset.All,
            allPlan.Preset);
        Assert.Empty(allPlan.EffectivePredicates);
        Assert.True(allPlan.ExcludeCompilerGeneratedNames);
        TypeDeclarationLocatorSectionAnswer allAnswer =
            Assert.Single(all.Answers);
        AssertVisibilityNames(
            allAnswer,
            "Forwarded",
            "InternalObsolete",
            "InternalOrdinary",
            "InternalOuter",
            "InternalOuter+NestedPublic",
            "PublicBoth",
            "PublicHidden",
            "PublicObsolete",
            "PublicOrdinary",
            "PublicOuter",
            "PublicOuter+NestedPrivate",
            "PublicOuter+NestedPublic");
        Assert.Equal(12, allAnswer.AvailableCandidateCount);
        Assert.True(allAnswer.IsComplete);
        TypeDeclarationVisibilityCoverage allCoverage =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                allAnswer.Visibility);
        Assert.Equal(14, allCoverage.InputCandidateCount);
        Assert.Equal(2, allCoverage.ExcludedCandidateCount);
        Assert.Empty(allCoverage.UnknownCandidates);

        var publicFalsePlan =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.Default,
                [
                    new(
                        TypeDeclarationVisibilityFacet.PublicSurface,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.False),
                ]);
        TypeDeclarationLocatorSectionAnswer nonpublicAnswer =
            Assert.Single(
                ProjectVisibility(query, publicFalsePlan).Answers);
        AssertVisibilityNames(
            nonpublicAnswer,
            "InternalOrdinary",
            "InternalOuter",
            "InternalOuter+NestedPublic",
            "PublicOuter+NestedPrivate");
        Assert.Equal(
            [
                TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                TypeDeclarationVisibilityFacet.Obsolete,
                TypeDeclarationVisibilityFacet.PublicSurface,
            ],
            publicFalsePlan.EffectivePredicates.Select(
                static predicate => predicate.Facet));
        Assert.Empty(nonpublicAnswer.Visibility!.UnknownCandidates);
        Assert.True(nonpublicAnswer.IsComplete);

        var notPublicPlan =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.All,
                [
                    new(
                        TypeDeclarationVisibilityFacet.PublicSurface,
                        RowQueryOperator.NotEquals,
                        TypeDeclarationVisibilityValue.True),
                ]);
        TypeDeclarationLocatorSectionAnswer notPublicAnswer =
            Assert.Single(
                ProjectVisibility(query, notPublicPlan).Answers);
        AssertVisibilityNames(
            notPublicAnswer,
            "InternalObsolete",
            "InternalOrdinary",
            "InternalOuter",
            "InternalOuter+NestedPublic",
            "PublicOuter+NestedPrivate");
        Assert.True(notPublicAnswer.IsComplete);

        var obsoletePlan =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.Default,
                [
                    new(
                        TypeDeclarationVisibilityFacet.Obsolete,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.True),
                ]);
        TypeDeclarationLocatorSectionAnswer obsoleteAnswer =
            Assert.Single(
                ProjectVisibility(query, obsoletePlan).Answers);
        AssertVisibilityNames(obsoleteAnswer, "PublicObsolete");
        Assert.DoesNotContain(
            obsoleteAnswer.Candidates,
            static candidate =>
                candidate.Name.Segments is ["InternalObsolete"]
                    or ["PublicBoth"]);
        Assert.Equal(
            TypeDeclarationVisibilityPreset.Default,
            obsoletePlan.Preset);
        Assert.Equal(
            [
                TypeDeclarationVisibilityFacet.PublicSurface,
                TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                TypeDeclarationVisibilityFacet.Obsolete,
            ],
            obsoletePlan.EffectivePredicates.Select(
                static predicate => predicate.Facet));
        Assert.Equal(
            TypeDeclarationVisibilityValue.True,
            obsoletePlan.EffectivePredicates[2].Value);
        TypeDeclarationVisibilityUnknownCandidate obsoleteUnknown =
            Assert.Single(obsoleteAnswer.Visibility!.UnknownCandidates);
        Assert.Equal(
            [
                TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                TypeDeclarationVisibilityFacet.Obsolete,
            ],
            obsoleteUnknown.Facets);

        var unknownAttributesPlan =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.Default,
                [
                    new(
                        TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.Unknown),
                    new(
                        TypeDeclarationVisibilityFacet.Obsolete,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.Unknown),
                ]);
        TypeDeclarationLocatorSectionAnswer unknownAttributesAnswer =
            Assert.Single(
                ProjectVisibility(query, unknownAttributesPlan).Answers);
        AssertVisibilityNames(unknownAttributesAnswer, "Forwarded");
        Assert.Equal(
            [
                TypeDeclarationVisibilityValue.True,
                TypeDeclarationVisibilityValue.Unknown,
                TypeDeclarationVisibilityValue.Unknown,
            ],
            unknownAttributesPlan.EffectivePredicates.Select(
                static predicate => predicate.Value));
        Assert.Empty(
            unknownAttributesAnswer.Visibility!.UnknownCandidates);
        Assert.True(unknownAttributesAnswer.IsComplete);

        var availableAttributesPlan =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.All,
                [
                    new(
                        TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                        RowQueryOperator.NotEquals,
                        TypeDeclarationVisibilityValue.Unknown),
                ]);
        TypeDeclarationLocatorSectionAnswer availableAttributesAnswer =
            Assert.Single(
                ProjectVisibility(query, availableAttributesPlan).Answers);
        Assert.Equal(11, availableAttributesAnswer.Candidates.Length);
        Assert.DoesNotContain(
            availableAttributesAnswer.Candidates,
            static candidate =>
                candidate.DeclarationKind
                    is AssemblyTypeDeclarationKind.Forwarder);
        Assert.Empty(
            availableAttributesAnswer.Visibility!.UnknownCandidates);
        Assert.True(availableAttributesAnswer.IsComplete);
    }

    [Fact]
    public async Task Visibility_AttributeComparisonTruthTableCoversKnownAndUnavailable()
    {
        byte[] image =
            LocatorImage(
                "TruthTable",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Neither");
                    TypeDefinitionHandle editor =
                        LocatorDefinition(metadata, "N", "Editor");
                    LocatorDiscoveryAttribute(
                        metadata,
                        editor,
                        obsolete: false);
                    TypeDefinitionHandle obsolete =
                        LocatorDefinition(metadata, "N", "Obsolete");
                    LocatorDiscoveryAttribute(
                        metadata,
                        obsolete,
                        obsolete: true);
                    VisibilityForwarder(metadata, "N", "Forwarded");
                });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(workspace, image);
        TypeDeclarationLocatorResult.Evaluated query =
            LocateAll(
                CaptureDeclarations(workspace, context),
                new TypeDeclarationLocatorRequest.Exact(
                    LocatorName("N", "Neither")),
                new TypeDeclarationLocatorRequest.Exact(
                    LocatorName("N", "Editor")),
                new TypeDeclarationLocatorRequest.Exact(
                    LocatorName("N", "Obsolete")),
                new TypeDeclarationLocatorRequest.Exact(
                    LocatorName("N", "Forwarded")));

        foreach (TypeDeclarationVisibilityFacet facet in new[]
        {
            TypeDeclarationVisibilityFacet.EditorBrowsableNever,
            TypeDeclarationVisibilityFacet.Obsolete,
        })
        {
            int trueIndex =
                facet
                    is TypeDeclarationVisibilityFacet.EditorBrowsableNever
                    ? 1
                    : 2;
            (int Index, TypeDeclarationVisibilityValue Actual)[] actuals =
            [
                (0, TypeDeclarationVisibilityValue.False),
                (trueIndex, TypeDeclarationVisibilityValue.True),
                (3, TypeDeclarationVisibilityValue.Unknown),
            ];
            foreach (RowQueryOperator @operator in new[]
            {
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
            })
            {
                foreach (TypeDeclarationVisibilityValue expected
                    in Enum.GetValues<TypeDeclarationVisibilityValue>())
                {
                    var plan =
                        new TypeDeclarationVisibilityPlan(
                            TypeDeclarationVisibilityPreset.All,
                            [new(facet, @operator, expected)]);
                    TypeDeclarationLocatorSectionResult.Evaluated result =
                        ProjectVisibility(query, plan);
                    Assert.True(result.IsSuccess);
                    foreach ((int index, TypeDeclarationVisibilityValue actual)
                        in actuals)
                    {
                        TypeDeclarationLocatorSectionAnswer answer =
                            result.Answers[index];
                        VisibilitySelectionOutcome expectedOutcome =
                            ExpectedVisibilityOutcome(
                                actual,
                                @operator,
                                expected);
                        VisibilitySelectionOutcome actualOutcome =
                            VisibilityOutcome(answer);
                        TypeDeclarationVisibilityCoverage coverage =
                            Assert.IsType<
                                TypeDeclarationVisibilityCoverage>(
                                    answer.Visibility);
                        Assert.True(
                            expectedOutcome == actualOutcome,
                            $"{facet}: {actual} {@operator} {expected} "
                                + $"was {actualOutcome}, expected "
                                + $"{expectedOutcome}.");
                        Assert.Equal(
                            1,
                            coverage.InputCandidateCount);
                        Assert.Equal(
                            expectedOutcome
                                is VisibilitySelectionOutcome.Excluded
                                    ? 1
                                    : 0,
                            coverage.ExcludedCandidateCount);
                        Assert.Equal(
                            expectedOutcome
                                is VisibilitySelectionOutcome.Indeterminate
                                    ? 1
                                    : 0,
                            coverage.UnknownCandidates.Length);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Visibility_RepeatedPredicatesRemainConjunctiveAndKnownFalseDominatesUnknown()
    {
        byte[] image =
            LocatorImage(
                "Conjunction",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Ordinary");
                    VisibilityForwarder(metadata, "N", "Forwarded");
                });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(workspace, image);
        TypeDeclarationLocatorResult.Evaluated query =
            LocateAll(
                CaptureDeclarations(workspace, context),
                new TypeDeclarationLocatorRequest.Exact(
                    LocatorName("N", "Ordinary")),
                new TypeDeclarationLocatorRequest.Exact(
                    LocatorName("N", "Forwarded")));

        var contradiction =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.All,
                [
                    new(
                        TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.False),
                    new(
                        TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.True),
                ]);
        Assert.Equal(2, contradiction.Predicates.Length);
        Assert.Equal(2, contradiction.EffectivePredicates.Length);
        TypeDeclarationLocatorSectionResult.Evaluated contradicted =
            ProjectVisibility(query, contradiction);
        Assert.Equal(
            VisibilitySelectionOutcome.Excluded,
            VisibilityOutcome(contradicted.Answers[0]));
        Assert.Equal(
            VisibilitySelectionOutcome.Indeterminate,
            VisibilityOutcome(contradicted.Answers[1]));
        Assert.Equal(
            [TypeDeclarationVisibilityFacet.EditorBrowsableNever],
            Assert.Single(
                contradicted.Answers[1]
                    .Visibility!
                    .UnknownCandidates)
                .Facets);

        TypeDeclarationVisibilityPredicate editorTrue =
            new(
                TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                RowQueryOperator.Equals,
                TypeDeclarationVisibilityValue.True);
        TypeDeclarationVisibilityPredicate publicFalse =
            new(
                TypeDeclarationVisibilityFacet.PublicSurface,
                RowQueryOperator.Equals,
                TypeDeclarationVisibilityValue.False);
        foreach (TypeDeclarationVisibilityPredicate[] predicates in new[]
        {
            new[] { editorTrue, publicFalse },
            new[] { publicFalse, editorTrue },
        })
        {
            TypeDeclarationLocatorSectionResult.Evaluated dominated =
                ProjectVisibility(
                    query,
                    new(
                        TypeDeclarationVisibilityPreset.All,
                        predicates));
            Assert.All(
                dominated.Answers,
                static answer =>
                {
                    TypeDeclarationVisibilityCoverage coverage =
                        Assert.IsType<
                            TypeDeclarationVisibilityCoverage>(
                                answer.Visibility);
                    Assert.Empty(answer.Candidates);
                    Assert.Equal(
                        1,
                        coverage.ExcludedCandidateCount);
                    Assert.Empty(
                        coverage.UnknownCandidates);
                    Assert.True(coverage.IsComplete);
                });
        }
    }

    [Fact]
    public void Visibility_BindingAndDescriptorsAdvertiseOnlyImplementedPredicates()
    {
        Assert.Collection(
            TypeDeclarationVisibilityPlan.Facets,
            descriptor =>
            {
                Assert.Equal(
                    TypeDeclarationVisibilityFacet.PublicSurface,
                    descriptor.Facet);
                Assert.Equal(
                    [
                        RowQueryOperator.Equals,
                        RowQueryOperator.NotEquals,
                    ],
                    descriptor.Operators);
                Assert.Equal(
                    [
                        TypeDeclarationVisibilityValue.True,
                        TypeDeclarationVisibilityValue.False,
                    ],
                    descriptor.Values);
            },
            descriptor =>
            {
                Assert.Equal(
                    TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                    descriptor.Facet);
                Assert.Equal(
                    [
                        RowQueryOperator.Equals,
                        RowQueryOperator.NotEquals,
                    ],
                    descriptor.Operators);
                Assert.Equal(
                    [
                        TypeDeclarationVisibilityValue.True,
                        TypeDeclarationVisibilityValue.False,
                        TypeDeclarationVisibilityValue.Unknown,
                    ],
                    descriptor.Values);
            },
            descriptor =>
            {
                Assert.Equal(
                    TypeDeclarationVisibilityFacet.Obsolete,
                    descriptor.Facet);
                Assert.Equal(
                    [
                        RowQueryOperator.Equals,
                        RowQueryOperator.NotEquals,
                    ],
                    descriptor.Operators);
                Assert.Equal(
                    [
                        TypeDeclarationVisibilityValue.True,
                        TypeDeclarationVisibilityValue.False,
                        TypeDeclarationVisibilityValue.Unknown,
                    ],
                    descriptor.Values);
            });

        TypeDeclarationVisibilityBindingResult bound =
            TypeDeclarationVisibilityPlan.Bind(
                TypeDeclarationVisibilityPreset.Default,
                [
                    VisibilityIntent(
                        "publicsurface",
                        RowQueryOperator.NotEquals,
                        " FALSE "),
                    VisibilityIntent(
                        "EDITORBROWSABLENEVER",
                        RowQueryOperator.Equals,
                        "TrUe"),
                    VisibilityIntent(
                        "obsolete",
                        RowQueryOperator.Equals,
                        "UNKNOWN"),
                ]);
        Assert.True(bound.IsSuccess);
        Assert.Null(bound.Failure);
        TypeDeclarationVisibilityPlan plan =
            Assert.IsType<TypeDeclarationVisibilityPlan>(bound.Plan);
        Assert.Equal(TypeDeclarationVisibilityPreset.Default, plan.Preset);
        Assert.Collection(
            plan.Predicates,
            predicate =>
            {
                Assert.Equal(
                    TypeDeclarationVisibilityFacet.PublicSurface,
                    predicate.Facet);
                Assert.Equal(
                    RowQueryOperator.NotEquals,
                    predicate.Operator);
                Assert.Equal(
                    TypeDeclarationVisibilityValue.False,
                    predicate.Value);
            },
            predicate =>
            {
                Assert.Equal(
                    TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                    predicate.Facet);
                Assert.Equal(
                    TypeDeclarationVisibilityValue.True,
                    predicate.Value);
            },
            predicate =>
            {
                Assert.Equal(
                    TypeDeclarationVisibilityFacet.Obsolete,
                    predicate.Facet);
                Assert.Equal(
                    TypeDeclarationVisibilityValue.Unknown,
                    predicate.Value);
            });
        Assert.Equal(3, plan.EffectivePredicates.Length);

        AssertVisibilityBindingFailure(
            TypeDeclarationVisibilityPlan.Bind(
                TypeDeclarationVisibilityPreset.All,
                [
                    VisibilityIntent(
                        "Obsolete",
                        RowQueryOperator.Equals,
                        "false"),
                    VisibilityIntent(
                        "Accessibility",
                        RowQueryOperator.Equals,
                        "true"),
                    VisibilityIntent(
                        "EditorBrowsableNever",
                        RowQueryOperator.GreaterOrEqual,
                        "true"),
                ]),
            predicatePosition: 2,
            facet: null,
            reason: RowQueryFailureReason.UnknownKey);
        AssertVisibilityBindingFailure(
            TypeDeclarationVisibilityPlan.Bind(
                TypeDeclarationVisibilityPreset.All,
                [
                    VisibilityIntent(
                        "Obsolete",
                        RowQueryOperator.Equals,
                        "false"),
                    VisibilityIntent(
                        "EditorBrowsableNever",
                        RowQueryOperator.GreaterOrEqual,
                        "true"),
                    VisibilityIntent(
                        "Obsolete",
                        RowQueryOperator.Equals,
                        "sometimes"),
                ]),
            predicatePosition: 2,
            facet:
                TypeDeclarationVisibilityFacet.EditorBrowsableNever,
            reason:
                RowQueryFailureReason.UnsupportedPredicateOperator);
        AssertVisibilityBindingFailure(
            TypeDeclarationVisibilityPlan.Bind(
                TypeDeclarationVisibilityPreset.All,
                [
                    VisibilityIntent(
                        "Obsolete",
                        RowQueryOperator.Equals,
                        "false"),
                    VisibilityIntent(
                        "PublicSurface",
                        RowQueryOperator.Equals,
                        "unknown"),
                ]),
            predicatePosition: 2,
            facet: TypeDeclarationVisibilityFacet.PublicSurface,
            reason: RowQueryFailureReason.InvalidValue);
    }

    [Fact]
    public async Task TypeLocator_VisibilityRequiresAllDeclarationsAndDoesNotRunRows()
    {
        byte[] image =
            LocatorImage(
                "Admission",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Public");
                    LocatorDefinition(
                        metadata,
                        "N",
                        "Internal",
                        TypeAttributes.NotPublic);
                });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(workspace, image);
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(workspace, context);
        TypeDeclarationLocatorResult.Evaluated publicOnly =
            Locate(
                population,
                new TypeDeclarationLocatorRequest.Pattern("*"),
                new TypeDeclarationLocatorRequest.Pattern("Missing"));
        Assert.False(publicOnly.IncludeAll);
        TypeDeclarationLocatorCandidate publicCandidate =
            Assert.Single(publicOnly.Answers[0].Candidates);
        Assert.Empty(publicOnly.Answers[1].Candidates);
        Assert.Equal("Public", VisibilityCandidateName(publicCandidate));
        Assert.True(publicCandidate.IsPublicSurface);

        var raw =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    publicOnly,
                    TypeDeclarationLocatorSectionPlan.All));
        var plan =
            new TypeDeclarationLocatorSectionPlan(
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(
                            2,
                            2),
                    ]),
                TypeDeclarationVisibilityPlan.Default);
        var failed =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    publicOnly,
                    plan));

        Assert.False(failed.IsSuccess);
        Assert.False(failed.IncludeAll);
        Assert.Equal(
            TypeDeclarationVisibilityInputFailure.AllDeclarationsRequired,
            failed.VisibilityFailure);
        Assert.Same(TypeDeclarationVisibilityPlan.Default, failed.Visibility);
        Assert.Null(failed.RowSelectionFailure);
        Assert.Equal(
            raw.Contexts.Select(
                static contextCoverage =>
                    (contextCoverage.ContextOrder,
                        contextCoverage.IsRealized,
                        contextCoverage.Failures.Length)),
            failed.Contexts.Select(
                static contextCoverage =>
                    (contextCoverage.ContextOrder,
                        contextCoverage.IsRealized,
                        contextCoverage.Failures.Length)));
        Assert.Equal(
            raw.Members.Select(
                static member =>
                    (member.Outcome,
                        member.IsComplete,
                        member.Observation.ContextOrder,
                        member.Observation.MemberOrder)),
            failed.Members.Select(
                static member =>
                    (member.Outcome,
                        member.IsComplete,
                        member.Observation.ContextOrder,
                        member.Observation.MemberOrder)));
        Assert.Equal(2, failed.Answers.Length);
        TypeDeclarationLocatorSectionAnswer answer =
            failed.Answers[0];
        Assert.Empty(answer.Candidates);
        Assert.Equal(0, answer.AvailableCandidateCount);
        Assert.Equal(
            publicOnly.Answers[0].IsEvaluationComplete,
            answer.IsEvaluationComplete);
        Assert.False(answer.IsComplete);
        TypeDeclarationVisibilityCoverage visibility =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                answer.Visibility);
        Assert.Equal(1, visibility.InputCandidateCount);
        Assert.Equal(0, visibility.ExcludedCandidateCount);
        Assert.Empty(visibility.UnknownCandidates);
        Assert.False(visibility.IsEvaluated);
        Assert.False(visibility.IsComplete);
        TypeDeclarationVisibilityCoverage emptyVisibility =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                failed.Answers[1].Visibility);
        Assert.Empty(failed.Answers[1].Candidates);
        Assert.Equal(0, failed.Answers[1].AvailableCandidateCount);
        Assert.Equal(0, emptyVisibility.InputCandidateCount);
        Assert.False(emptyVisibility.IsEvaluated);
        Assert.False(emptyVisibility.IsComplete);
        Assert.All(
            failed.Answers,
            static projectedAnswer =>
                Assert.False(projectedAnswer.IsComplete));

        TypeDeclarationLocatorView view =
            TypeDeclarationLocatorView.Create(failed);
        Assert.Equal("Visibility selection failed", view.Status);
        Assert.Equal("Incomplete", view.Completion);
        TypeDeclarationLocatorGapRowView gap =
            Assert.Single(view.Gaps);
        Assert.Equal("Visibility selection", gap.Kind);
        Assert.Equal("AllDeclarationsRequired", gap.Detail);

        TypeDeclarationLocatorResult rejectedQuery =
            TypeDeclarationLocatorQuery.Execute(
                population,
                [],
                includeAll: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var rejected =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Rejected>(
                TypeDeclarationLocatorSection.Project(
                    rejectedQuery,
                    plan));
        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.EmptyRequests,
            rejected.RejectionKind);
    }

    [Fact]
    public async Task Visibility_FiltersBeforeWindowsAndKeepsUnknownEvidenceOnStrictFailure()
    {
        byte[] image =
            LocatorImage(
                "Windows",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Alpha");
                    LocatorDefinition(metadata, "N", "Beta");
                    VisibilityForwarder(metadata, "N", "Forwarded");
                });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(workspace, image);
        TypeDeclarationLocatorResult.Evaluated query =
            LocateAll(
                CaptureDeclarations(workspace, context),
                new TypeDeclarationLocatorRequest.Pattern("*"));
        TypeDeclarationLocatorSectionResult.Evaluated unwindowed =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.Default);
        TypeDeclarationLocatorSectionAnswer unwindowedAnswer =
            Assert.Single(unwindowed.Answers);
        Assert.Equal(2, unwindowedAnswer.AvailableCandidateCount);
        Assert.Equal(2, unwindowedAnswer.Candidates.Length);
        TypeDeclarationVisibilityUnknownCandidate unknown =
            Assert.Single(
                unwindowedAnswer.Visibility!.UnknownCandidates);

        TypeDeclarationLocatorSectionResult.Evaluated windowed =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.Default,
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(
                            2,
                            2),
                    ]));
        Assert.True(windowed.IsSuccess);
        TypeDeclarationLocatorSectionAnswer windowedAnswer =
            Assert.Single(windowed.Answers);
        Assert.Equal(2, windowedAnswer.AvailableCandidateCount);
        Assert.Single(windowedAnswer.Candidates);
        Assert.Equal(
            unwindowedAnswer.Candidates[1],
            windowedAnswer.Candidates[0]);
        Assert.Equal(
            VisibilityUnknownKey(unknown),
            VisibilityUnknownKey(
                Assert.Single(
                    windowedAnswer.Visibility!.UnknownCandidates)));
        Assert.True(windowedAnswer.IsEvaluationComplete);
        Assert.False(windowedAnswer.IsComplete);
        Assert.True(windowed.IsSuccess);

        TypeDeclarationLocatorSectionResult.Evaluated strict =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.Default,
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(
                            3,
                            3),
                    ]));
        Assert.False(strict.IsSuccess);
        TypeDeclarationLocatorSectionAnswer strictAnswer =
            Assert.Single(strict.Answers);
        Assert.Empty(strictAnswer.Candidates);
        Assert.Equal(2, strictAnswer.AvailableCandidateCount);
        Assert.Equal(
            VisibilityUnknownKey(unknown),
            VisibilityUnknownKey(
                Assert.Single(
                    strictAnswer.Visibility!.UnknownCandidates)));
        Assert.True(strictAnswer.IsEvaluationComplete);
        Assert.False(strictAnswer.IsComplete);
        TypeDeclarationLocatorRowSelectionFailure failure =
            Assert.IsType<TypeDeclarationLocatorRowSelectionFailure>(
                strict.RowSelectionFailure);
        Assert.Equal(3, failure.RequiredPosition);
        Assert.Equal(2, failure.AvailableCount);
        Assert.Equal(
            query.Answers[0].IsEvaluationComplete,
            strictAnswer.IsEvaluationComplete);
        Assert.Equal(
            unwindowed.Contexts.Select(
                static coverage =>
                    (coverage.ContextOrder,
                        coverage.IsRealized,
                        coverage.Failures.Length)),
            strict.Contexts.Select(
                static coverage =>
                    (coverage.ContextOrder,
                        coverage.IsRealized,
                        coverage.Failures.Length)));

        TypeDeclarationLocatorView successfulView =
            TypeDeclarationLocatorView.Create(windowed);
        Assert.Equal("Evaluated", successfulView.Status);
        Assert.Equal("Incomplete", successfulView.Completion);
        Assert.Single(
            successfulView.Gaps,
            static gap => gap.Kind == "Visibility unknown");
        TypeDeclarationLocatorView strictView =
            TypeDeclarationLocatorView.Create(strict);
        Assert.Equal("Row selection failed", strictView.Status);
        Assert.Equal("Incomplete", strictView.Completion);
        Assert.Equal(2, strictView.Gaps.Count);
        Assert.Contains(
            strictView.Gaps,
            static gap => gap.Kind == "Row selection");
        Assert.Contains(
            strictView.Gaps,
            static gap => gap.Kind == "Visibility unknown");
    }

    [Fact]
    public async Task TypeLocator_VisibilityResidentReusePreservesOccurrencesAndDetachedPlansAfterClose()
    {
        byte[] image =
            LocatorImage(
                "ResidentVisibility",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Ordinary");
                    TypeDefinitionHandle hidden =
                        LocatorDefinition(metadata, "N", "Hidden");
                    LocatorDiscoveryAttribute(
                        metadata,
                        hidden,
                        obsolete: false);
                    VisibilityForwarder(metadata, "N", "Forwarded");
                });
        await using var workspace = new InspectionWorkspace();
        _ = await LocatorContext(workspace, image);
        _ = await LocatorContext(workspace, image);
        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator();
        TypeDeclarationLocatorResult.Evaluated query =
            Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
                await locator.ExecuteAsync(
                    [new TypeDeclarationLocatorRequest.Pattern("*")],
                    includeAll: true,
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Equal(6, query.Answers[0].Candidates.Length);

        TypeDeclarationLocatorSectionResult.Evaluated @default =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.Default);
        TypeDeclarationLocatorSectionResult.Evaluated all =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.All);
        var hiddenPlan =
            new TypeDeclarationVisibilityPlan(
                TypeDeclarationVisibilityPreset.Default,
                [
                    new(
                        TypeDeclarationVisibilityFacet.EditorBrowsableNever,
                        RowQueryOperator.Equals,
                        TypeDeclarationVisibilityValue.True),
                ]);
        TypeDeclarationLocatorSectionResult.Evaluated hidden =
            ProjectVisibility(query, hiddenPlan);
        Assert.Equal(2, locator.InventoryReadCount);

        TypeDeclarationLocatorSectionCandidate[] ordinary =
        [
            .. @default.Answers[0].Candidates.Where(
                static candidate =>
                    candidate.Name.Segments is ["Ordinary"]),
        ];
        Assert.Equal(2, ordinary.Length);
        Assert.Equal(ordinary[0].Coordinate, ordinary[1].Coordinate);
        Assert.Equal(
            2,
            ordinary.Select(
                static candidate =>
                    candidate.Observation.ContextOrder)
                .Distinct()
                .Count());
        Assert.Equal(
            ordinary.Select(
                static candidate =>
                    candidate.Observation.ContextOrder)
                .Order(),
            ordinary.Select(
                static candidate =>
                    candidate.Observation.ContextOrder));
        Assert.Equal(
            2,
            @default.Answers[0].Visibility!
                .UnknownCandidates.Length);
        Assert.Equal(
            2,
            hidden.Answers[0].Candidates.Length);
        Assert.All(
            hidden.Answers[0].Candidates,
            static candidate =>
                Assert.Equal(
                    "Hidden",
                    candidate.Name.Segments[0]));
        Assert.Equal(
            VisibilityObservationSequence(query),
            VisibilityObservationSequence(all));

        Assert.True((await workspace.CloseAsync()).Succeeded);
        TypeDeclarationLocatorSectionResult.Evaluated afterClose =
            ProjectVisibility(
                query,
                TypeDeclarationVisibilityPlan.Default);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Equal(
            VisibilityObservationSequence(@default),
            VisibilityObservationSequence(afterClose));
        TypeDeclarationVisibilityCoverage beforeCoverage =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                @default.Answers[0].Visibility);
        TypeDeclarationVisibilityCoverage afterCoverage =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                afterClose.Answers[0].Visibility);
        Assert.Equal(
            (
                beforeCoverage.InputCandidateCount,
                beforeCoverage.ExcludedCandidateCount,
                beforeCoverage.IsEvaluated),
            (
                afterCoverage.InputCandidateCount,
                afterCoverage.ExcludedCandidateCount,
                afterCoverage.IsEvaluated));
        Assert.Equal(
            beforeCoverage.UnknownCandidates.Select(
                static unknown =>
                    (
                        string.Join(
                            "+",
                            unknown.Candidate.Name.Segments),
                        unknown.Candidate.Observation.ContextOrder,
                        unknown.Candidate.Observation.MemberOrder,
                        string.Join(",", unknown.Facets))),
            afterCoverage.UnknownCandidates.Select(
                static unknown =>
                    (
                        string.Join(
                            "+",
                            unknown.Candidate.Name.Segments),
                        unknown.Candidate.Observation.ContextOrder,
                        unknown.Candidate.Observation.MemberOrder,
                        string.Join(",", unknown.Facets))));
    }

    static TypeDeclarationLocatorResult.Evaluated LocateAll(
        WorkspaceDeclarationPopulation population,
        params TypeDeclarationLocatorRequest[] requests) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            TypeDeclarationLocatorQuery.Execute(
                population,
                [.. requests],
                includeAll: true,
                cancellationToken:
                    TestContext.Current.CancellationToken));

    static TypeDeclarationLocatorSectionResult.Evaluated ProjectVisibility(
        TypeDeclarationLocatorResult.Evaluated query,
        TypeDeclarationVisibilityPlan visibility,
        RowSelectionIntent<string>? rowSelection = null) =>
        Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
            TypeDeclarationLocatorSection.Project(
                query,
                new TypeDeclarationLocatorSectionPlan(
                    rowSelection ?? RowSelectionIntent<string>.Empty,
                    visibility)));

    static byte[] VisibilityFixtureImage() =>
        LocatorImage(
            "Visibility",
            metadata =>
            {
                LocatorDefinition(metadata, "N", "PublicOrdinary");
                LocatorDefinition(
                    metadata,
                    "N",
                    "InternalOrdinary",
                    TypeAttributes.NotPublic);
                TypeDefinitionHandle internalObsolete =
                    LocatorDefinition(
                        metadata,
                        "N",
                        "InternalObsolete",
                        TypeAttributes.NotPublic);
                LocatorDiscoveryAttribute(
                    metadata,
                    internalObsolete,
                    obsolete: true);
                TypeDefinitionHandle hidden =
                    LocatorDefinition(metadata, "N", "PublicHidden");
                LocatorDiscoveryAttribute(
                    metadata,
                    hidden,
                    obsolete: false);
                TypeDefinitionHandle obsolete =
                    LocatorDefinition(metadata, "N", "PublicObsolete");
                LocatorDiscoveryAttribute(
                    metadata,
                    obsolete,
                    obsolete: true);
                TypeDefinitionHandle both =
                    LocatorDefinition(metadata, "N", "PublicBoth");
                LocatorDiscoveryAttribute(
                    metadata,
                    both,
                    obsolete: false);
                LocatorDiscoveryAttribute(
                    metadata,
                    both,
                    obsolete: true);

                TypeDefinitionHandle publicOuter =
                    LocatorDefinition(metadata, "N", "PublicOuter");
                TypeDefinitionHandle publicNested =
                    LocatorDefinition(
                        metadata,
                        "",
                        "NestedPublic",
                        TypeAttributes.NestedPublic);
                metadata.AddNestedType(publicNested, publicOuter);
                TypeDefinitionHandle privateNested =
                    LocatorDefinition(
                        metadata,
                        "",
                        "NestedPrivate",
                        TypeAttributes.NestedPrivate);
                metadata.AddNestedType(privateNested, publicOuter);

                TypeDefinitionHandle internalOuter =
                    LocatorDefinition(
                        metadata,
                        "N",
                        "InternalOuter",
                        TypeAttributes.NotPublic);
                TypeDefinitionHandle internalNested =
                    LocatorDefinition(
                        metadata,
                        "",
                        "NestedPublic",
                        TypeAttributes.NestedPublic);
                metadata.AddNestedType(internalNested, internalOuter);

                LocatorDefinition(
                    metadata,
                    "N",
                    "<Generated>d__0");
                TypeDefinitionHandle generatedNested =
                    LocatorDefinition(
                        metadata,
                        "",
                        "__GeneratedNested",
                        TypeAttributes.NestedPublic);
                metadata.AddNestedType(generatedNested, publicOuter);
                VisibilityForwarder(metadata, "N", "Forwarded");
            });

    static void VisibilityForwarder(
        MetadataBuilder metadata,
        string @namespace,
        string name)
    {
        AssemblyReferenceHandle target =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("ForwardTarget"),
                new Version(1, 0, 0, 0),
                default,
                default,
                0,
                default);
        metadata.AddExportedType(
            (TypeAttributes)0x00200000,
            metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(name),
            target,
            0);
    }

    static string VisibilityCandidateName(
        TypeDeclarationLocatorCandidate candidate) =>
        string.Join("+", candidate.Name.Segments);

    static string VisibilityCandidateName(
        TypeDeclarationLocatorSectionCandidate candidate) =>
        string.Join("+", candidate.Name.Segments);

    static TypeDeclarationLocatorSectionCandidate VisibilityCandidate(
        TypeDeclarationLocatorSectionAnswer answer,
        string name) =>
        Assert.Single(
            answer.Candidates,
            candidate =>
                VisibilityCandidateName(candidate) == name);

    static void AssertVisibilityNames(
        TypeDeclarationLocatorSectionAnswer answer,
        params string[] expected) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            answer.Candidates
                .Select(VisibilityCandidateName)
                .Order(StringComparer.Ordinal));

    static RowQueryPredicateIntent VisibilityIntent(
        string field,
        RowQueryOperator @operator,
        string value) =>
        new(field, @operator, new(value));

    static void AssertVisibilityBindingFailure(
        TypeDeclarationVisibilityBindingResult result,
        int predicatePosition,
        TypeDeclarationVisibilityFacet? facet,
        RowQueryFailureReason reason)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        TypeDeclarationVisibilityBindingFailure failure =
            Assert.IsType<TypeDeclarationVisibilityBindingFailure>(
                result.Failure);
        Assert.Equal(predicatePosition, failure.PredicatePosition);
        Assert.Equal(facet, failure.Facet);
        Assert.Equal(reason, failure.Reason);
    }

    static VisibilitySelectionOutcome VisibilityOutcome(
        TypeDeclarationLocatorSectionAnswer answer)
    {
        TypeDeclarationVisibilityCoverage visibility =
            Assert.IsType<TypeDeclarationVisibilityCoverage>(
                answer.Visibility);
        if (answer.Candidates.Length == 1)
            return VisibilitySelectionOutcome.Match;
        if (visibility.UnknownCandidates.Length == 1)
            return VisibilitySelectionOutcome.Indeterminate;
        Assert.Empty(answer.Candidates);
        Assert.Empty(visibility.UnknownCandidates);
        return VisibilitySelectionOutcome.Excluded;
    }

    static VisibilitySelectionOutcome ExpectedVisibilityOutcome(
        TypeDeclarationVisibilityValue actual,
        RowQueryOperator @operator,
        TypeDeclarationVisibilityValue expected)
    {
        if (actual is TypeDeclarationVisibilityValue.Unknown
            && expected is not TypeDeclarationVisibilityValue.Unknown)
        {
            return VisibilitySelectionOutcome.Indeterminate;
        }

        bool equal = actual == expected;
        bool match =
            @operator is RowQueryOperator.Equals
                ? equal
                : !equal;
        return match
            ? VisibilitySelectionOutcome.Match
            : VisibilitySelectionOutcome.Excluded;
    }

    static (string Name, int ContextOrder, int MemberOrder, string Facets)
        VisibilityUnknownKey(
            TypeDeclarationVisibilityUnknownCandidate unknown) =>
        (
            VisibilityCandidateName(unknown.Candidate),
            unknown.Candidate.Observation.ContextOrder,
            unknown.Candidate.Observation.MemberOrder,
            string.Join(",", unknown.Facets));

    static (string Name, int ContextOrder, int MemberOrder,
        AssemblyReferenceIdentity Assembly)[] VisibilityObservationSequence(
            TypeDeclarationLocatorResult.Evaluated result) =>
        [
            .. result.Answers[0].Candidates.Select(
                static candidate =>
                    (string.Join("+", candidate.Name.Segments),
                        candidate.Observation.Occurrence.ContextOrder,
                        candidate.Observation.Occurrence.MemberOrder,
                        candidate.Observation.AssemblyIdentity)),
        ];

    static (string Name, int ContextOrder, int MemberOrder,
        AssemblyReferenceIdentity Assembly)[] VisibilityObservationSequence(
            TypeDeclarationLocatorSectionResult.Evaluated result) =>
        [
            .. result.Answers[0].Candidates.Select(
                static candidate =>
                    (string.Join("+", candidate.Name.Segments),
                        candidate.Observation.ContextOrder,
                        candidate.Observation.MemberOrder,
                        candidate.Observation.AssemblyIdentity)),
        ];

    enum VisibilitySelectionOutcome
    {
        Match,
        Excluded,
        Indeterminate,
    }
}
