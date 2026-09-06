using System.Collections.Immutable;
using System.Reflection;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class QueryComparisonPopulationTests
{
    [Fact]
    public void ComparisonPopulation_SealsImmutableInputAndSelectionSnapshots()
    {
        ImplementationComparisonBinding implementation = ImplementationBinding();
        List<ImplementationComparisonBinding?> before = [implementation];
        List<ImplementationComparisonBinding?> after = [];
        HashSet<string> types = new(StringComparer.Ordinal)
        {
            "Exact.Type",
            "exact.type",
            "",
        };
        HashSet<string> members = new(StringComparer.Ordinal)
        {
            "Exact.Type::Method()",
        };

        QueryComparisonPopulation<ImplementationComparisonBinding> population =
            Seal(new ImplementationComparisonPopulationRequest(
                before,
                after,
                types,
                members));

        before.Clear();
        before.Add(null);
        after.Add(implementation);
        types.Clear();
        types.Add("mutated");
        members.Clear();
        members.Add("mutated");

        Assert.Same(implementation, Assert.Single(population.Before).Binding);
        Assert.Empty(population.After);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Exact.Type",
                "exact.type",
                "",
            },
            population.TypeFilters);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Exact.Type::Method()",
            },
            population.MemberTargetIdentities);

        QueryComparisonPopulation<ImplementationComparisonBinding> omitted =
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation],
                [],
                TypeFilters: null,
                MemberTargetIdentities: null));
        QueryComparisonPopulation<ImplementationComparisonBinding> explicitlyEmpty =
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation],
                [],
                TypeFilters: new HashSet<string>(StringComparer.Ordinal),
                MemberTargetIdentities:
                    new HashSet<string>(StringComparer.Ordinal)));

        Assert.Null(omitted.TypeFilters);
        Assert.Null(omitted.MemberTargetIdentities);
        Assert.NotNull(explicitlyEmpty.TypeFilters);
        Assert.Empty(explicitlyEmpty.TypeFilters);
        Assert.NotNull(explicitlyEmpty.MemberTargetIdentities);
        Assert.Empty(explicitlyEmpty.MemberTargetIdentities);

        AssertEveryDeclaredSealingRejection(implementation);
    }

    [Fact]
    public void QueryPopulationBindings_AreIdlessBorrowedWrappers()
    {
        ImplementationComparisonBinding implementation = ImplementationBinding();

        Assert.Equal(
            new Dictionary<string, Type>
            {
                [nameof(ImplementationComparisonBinding.Assembly)] =
                    typeof(ResolvedAssemblyReference),
                [nameof(ImplementationComparisonBinding.Resolver)] =
                    typeof(IAssemblyReferenceResolver),
                [nameof(ImplementationComparisonBinding.BodyIndex)] =
                    typeof(LibraryBodyIndex),
            },
            PublicProperties(typeof(ImplementationComparisonBinding)));
        Type[] identityTypes =
        [
            typeof(QueryComparisonOperationId),
            typeof(QueryComparisonQuestionId),
            typeof(QueryComparisonInputId),
        ];
        foreach (Type bindingType in new[]
        {
            typeof(ImplementationComparisonBinding),
        })
        {
            Assert.True(bindingType.IsSealed);
            Assert.DoesNotContain(
                bindingType.GetProperties(
                    BindingFlags.Public
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly),
                property => identityTypes.Contains(property.PropertyType));
            Assert.DoesNotContain(
                bindingType.GetConstructors(),
                constructor => constructor.GetParameters()
                    .Any(parameter => identityTypes.Contains(parameter.ParameterType)));
        }

        QueryComparisonPopulation<ImplementationComparisonBinding> sealedImplementation =
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation],
                [],
                null,
                null));
        ImplementationComparisonBinding retainedImplementation =
            Assert.Single(sealedImplementation.Before).Binding;
        Assert.Same(implementation.Assembly, retainedImplementation.Assembly);
        Assert.Same(implementation.Resolver, retainedImplementation.Resolver);
        Assert.Same(implementation.BodyIndex, retainedImplementation.BodyIndex);
    }

    [Fact]
    public void ComparisonPopulation_MintsFreshParentedIdentitiesPerExecute()
    {
        ImplementationComparisonBinding implementation = ImplementationBinding();
        QueryComparisonPopulation<ImplementationComparisonBinding> firstImplementation =
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation, implementation],
                [implementation],
                null,
                null));
        QueryComparisonPopulation<ImplementationComparisonBinding> secondImplementation =
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation, implementation],
                [implementation],
                null,
                null));
        AssertFreshPopulation(firstImplementation, secondImplementation);
    }

    [Fact]
    public void ComparisonPopulation_SealsEverySubmittedOccurrenceWithDeclaredSide()
    {
        ImplementationComparisonBinding implementation = ImplementationBinding();
        QueryComparisonPopulation<ImplementationComparisonBinding> implementationPopulation =
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation, implementation],
                [implementation],
                null,
                null));
        AssertOccurrencePopulation(implementationPopulation, implementation);
    }

    [Fact]
    public void ResearchPopulationProjection_IsTotalAndBijective()
    {
        ImplementationComparisonBinding implementation = ImplementationBinding();
        QueryComparisonPopulation[] populations =
        [
            Seal(new ImplementationComparisonPopulationRequest(
                [implementation],
                [],
                null,
                null)),
            Seal(new ImplementationComparisonPopulationRequest(
                [],
                [implementation],
                null,
                null)),
            Seal(new ImplementationComparisonPopulationRequest(
                [],
                [],
                null,
                null)),
        ];

        foreach (QueryComparisonPopulation population in populations)
        {
            ProjectedQueryPopulation projected = AssertProjected(
                QueryPopulationProjection.Execute(population));
            QueryToResearchPopulationReceipt receipt = projected.Receipt;

            Assert.Equal(population.Profile, receipt.Profile);
            Assert.Same(population.Operation, receipt.Operation.Query);
            Assert.Same(projected.Admission.Operation, receipt.Operation.Research);

            ResearchAdmittedQuestion admittedQuestion =
                Assert.Single(projected.Admission.Questions);
            Assert.Single(receipt.Questions);
            Assert.True(receipt.Questions.TryGetValue(
                population.Question,
                out ResearchComparisonQuestionId? researchQuestion));
            Assert.Same(admittedQuestion.Id, researchQuestion);

            AssertReferenceSetEqual(
                population.InputIds,
                receipt.Inputs.Keys);
            AssertReferenceSetEqual(
                projected.Admission.Inputs.Select(input => input.Id),
                receipt.Inputs.Values.Select(pair => pair.Research));
            Assert.Equal(population.InputIds.Length, receipt.Inputs.Count);
            Assert.Equal(
                population.InputIds.Length,
                DistinctReferenceCount(
                    receipt.Inputs.Values.Select(pair => pair.Research)));

            foreach (QueryComparisonInputId queryId in population.InputIds)
            {
                QueryResearchInputCorrespondence pair = receipt.Inputs[queryId];
                Assert.Same(queryId, pair.Query);
                Assert.Same(population.Operation, pair.Query.Operation);
                Assert.Same(population.Question, pair.Query.Question);
                Assert.Equal(queryId.Side, pair.Side);
                Assert.Same(projected.Admission.Operation, pair.Research.Operation);
                Assert.Same(admittedQuestion.Id, pair.Research.Question);
                Assert.Equal(
                    QueryPopulationProjection.ResearchSide(queryId.Side),
                    pair.Research.Side);
            }
        }
    }

    [Fact]
    public void ResearchPopulationProjection_MapsEachReturnedIdentityToItsExactSealedAntecedent()
    {
        ImplementationComparisonBinding borrowed = ImplementationBinding();
        QueryComparisonPopulation<ImplementationComparisonBinding> population =
            Seal(new ImplementationComparisonPopulationRequest(
                [borrowed, borrowed],
                [borrowed],
                null,
                null));
        QueryPopulationProjection projection =
            QueryPopulationProjection.Prepare(population);
        ResearchAdmittedPopulation admitted = Admit(projection.Request);
        ProjectedQueryPopulation projected =
            AssertProjected(projection.Complete(admitted));

        Assert.Equal(3, projection.Occurrences.Count);
        Assert.Equal(3, projected.Receipt.Inputs.Count);
        Assert.Equal(
            3,
            DistinctReferenceCount(
                projection.Occurrences.Values));
        Assert.Equal(
            3,
            DistinctReferenceCount(
                projected.Receipt.Inputs.Values.Select(pair => pair.Research)));

        foreach (QueryComparisonInput<ImplementationComparisonBinding> input
            in population.Inputs)
        {
            ResearchComparisonInputOccurrence occurrence =
                projection.Occurrences[input.Id];
            var implementationOccurrence =
                Assert.IsType<ImplementationComparisonInputOccurrence>(
                    occurrence);
            Assert.Same(input.Binding.Assembly, implementationOccurrence.Assembly);
            Assert.Same(input.Binding.Resolver, implementationOccurrence.Resolver);
            Assert.Same(input.Binding.BodyIndex, implementationOccurrence.BodyIndex);

            ResearchAdmittedInput ownerInput = admitted.GetInput(occurrence);
            QueryResearchInputCorrespondence pair =
                projected.Receipt.Inputs[input.Id];
            Assert.Same(input.Id, pair.Query);
            Assert.Same(ownerInput.Id, pair.Research);
            Assert.Same(occurrence, ownerInput.Occurrence);
        }
    }

    [Fact]
    public void ResearchPopulationProjection_RejectsMissingExtraSubstitutedAndWrongSideMappings()
    {
        ImplementationComparisonBinding borrowed = ImplementationBinding();
        QueryComparisonPopulation<ImplementationComparisonBinding> population =
            Seal(new ImplementationComparisonPopulationRequest(
                [borrowed, borrowed],
                [borrowed],
                null,
                null));
        ProjectionParts valid = PrepareParts(population);

        AssertProjected(QueryToResearchPopulationReceipt.Create(
            valid.Projection,
            valid.Admission,
            valid.Operation,
            valid.Questions,
            valid.Inputs));

        QueryComparisonPopulation<ImplementationComparisonBinding> foreignPopulation =
            Seal(new ImplementationComparisonPopulationRequest(
                [borrowed, borrowed],
                [borrowed],
                null,
                null));
        ProjectionParts foreign = PrepareParts(foreignPopulation);

        AssertInputMappingRejected(
            valid,
            [.. valid.Inputs.Skip(1)]);
        AssertInputMappingRejected(
            valid,
            [.. valid.Inputs, foreign.Inputs[0]]);
        AssertInputMappingRejected(
            valid,
            valid.Inputs.SetItem(
                1,
                valid.Inputs[1] with { Query = valid.Inputs[0].Query }));
        AssertInputMappingRejected(
            valid,
            valid.Inputs.SetItem(
                1,
                valid.Inputs[1] with { Research = valid.Inputs[0].Research }));
        AssertInputMappingRejected(
            valid,
            valid.Inputs.SetItem(
                0,
                valid.Inputs[0] with { Query = foreign.Inputs[0].Query }));
        AssertInputMappingRejected(
            valid,
            valid.Inputs.SetItem(
                0,
                valid.Inputs[0] with { Research = foreign.Inputs[0].Research }));
        AssertInputMappingRejected(
            valid,
            valid.Inputs.SetItem(
                0,
                valid.Inputs[0] with { Side = QueryComparisonSide.After }));

        ImmutableArray<QueryResearchInputCorrespondence> sameSideSwap =
            valid.Inputs
                .SetItem(
                    0,
                    valid.Inputs[0] with
                    {
                        Research = valid.Inputs[1].Research,
                    })
                .SetItem(
                    1,
                    valid.Inputs[1] with
                    {
                        Research = valid.Inputs[0].Research,
                    });
        AssertInputMappingRejected(valid, sameSideSwap);

        ImmutableArray<QueryResearchInputCorrespondence> crossSideSwap =
            valid.Inputs
                .SetItem(
                    0,
                    valid.Inputs[0] with
                    {
                        Research = valid.Inputs[2].Research,
                    })
                .SetItem(
                    2,
                    valid.Inputs[2] with
                    {
                        Research = valid.Inputs[0].Research,
                    });
        AssertInputMappingRejected(valid, crossSideSwap);

        AssertProjectionRejected(
            QueryToResearchPopulationReceipt.Create(
                valid.Projection,
                valid.Admission,
                new(foreignPopulation.Operation, valid.Admission.Operation),
                valid.Questions,
                valid.Inputs),
            QueryPopulationProjectionRejection.OperationMismatch);
        AssertProjectionRejected(
            QueryToResearchPopulationReceipt.Create(
                valid.Projection,
                valid.Admission,
                new(population.Operation, foreign.Admission.Operation),
                valid.Questions,
                valid.Inputs),
            QueryPopulationProjectionRejection.OperationMismatch);
        AssertProjectionRejected(
            QueryToResearchPopulationReceipt.Create(
                valid.Projection,
                valid.Admission,
                valid.Operation,
                [new(foreignPopulation.Question,
                    valid.Admission.Questions.Single().Id)],
                valid.Inputs),
            QueryPopulationProjectionRejection.QuestionMappingMismatch);
        AssertProjectionRejected(
            QueryToResearchPopulationReceipt.Create(
                valid.Projection,
                valid.Admission,
                valid.Operation,
                [new(population.Question,
                    foreign.Admission.Questions.Single().Id)],
                valid.Inputs),
            QueryPopulationProjectionRejection.QuestionMappingMismatch);

        ResearchAdmittedPopulation bodySignalAdmission = Admit(
            new ResearchComparisonAdmissionRequest(
                ResearchComparisonProfile.BodySignal,
                [new ResearchComparisonAdmissionQuestion(
                    [
                        new BodySignalComparisonInputOccurrence(borrowed.BodyIndex),
                        new BodySignalComparisonInputOccurrence(borrowed.BodyIndex),
                    ],
                    [new BodySignalComparisonInputOccurrence(borrowed.BodyIndex)])]));
        AssertProjectionRejected(
            QueryToResearchPopulationReceipt.Create(
                valid.Projection,
                bodySignalAdmission,
                new(population.Operation, bodySignalAdmission.Operation),
                [new(population.Question,
                    bodySignalAdmission.Questions.Single().Id)],
                [
                    .. population.InputIds.Select(
                        (id, index) => new QueryResearchInputCorrespondence(
                            id,
                            bodySignalAdmission.Inputs[index].Id,
                            id.Side)),
                ]),
            QueryPopulationProjectionRejection.ProfileMismatch);
    }

    [Fact]
    public void QueryPopulationIdentities_AreOwnerIssuedAndNonConvertible()
    {
        QueryComparisonPopulation<ImplementationComparisonBinding> queryPopulation =
            Seal(new ImplementationComparisonPopulationRequest(
                [ImplementationBinding()],
                [],
                null,
                null));
        ProjectedQueryPopulation projected = AssertProjected(
            QueryPopulationProjection.Execute(queryPopulation));

        Type[] queryTypes =
        [
            typeof(QueryComparisonOperationId),
            typeof(QueryComparisonQuestionId),
            typeof(QueryComparisonInputId),
        ];
        Type[] researchTypes =
        [
            typeof(ResearchComparisonOperationId),
            typeof(ResearchComparisonQuestionId),
            typeof(ResearchComparisonInputId),
        ];

        foreach (Type type in queryTypes.Concat(researchTypes))
        {
            Assert.True(type.IsSealed, type.Name);
            Assert.Empty(type.GetConstructors());
            Assert.DoesNotContain(
                type.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.Static
                        | BindingFlags.DeclaredOnly),
                method => method.Name is "Parse" or "TryParse"
                    or "op_Implicit" or "op_Explicit");
        }

        for (int index = 0; index < queryTypes.Length; index++)
        {
            Assert.False(queryTypes[index].IsAssignableFrom(researchTypes[index]));
            Assert.False(researchTypes[index].IsAssignableFrom(queryTypes[index]));
        }
        Assert.False(typeof(QueryComparisonOperationId)
            .IsAssignableFrom(typeof(QueryComparisonQuestionId)));
        Assert.False(typeof(QueryComparisonQuestionId)
            .IsAssignableFrom(typeof(QueryComparisonInputId)));

        QueryComparisonInputId queryInput = Assert.Single(queryPopulation.InputIds);
        ResearchComparisonInputId researchInput =
            Assert.Single(projected.Admission.Inputs).Id;
        Assert.NotSame(queryPopulation.Operation, projected.Admission.Operation);
        Assert.NotSame(queryPopulation.Question, projected.Admission.Questions.Single().Id);
        Assert.NotSame(queryInput, researchInput);
        Assert.Same(queryPopulation.Operation, queryPopulation.Question.Operation);
        Assert.Same(queryPopulation.Question, queryInput.Question);
        Assert.Same(queryPopulation.Operation, queryInput.Operation);
    }

    [Fact]
    public void PopulationReceipt_DoesNotRetainBorrowedInputs()
    {
        ImplementationComparisonBinding borrowed = ImplementationBinding();
        QueryComparisonPopulation<ImplementationComparisonBinding> population =
            Seal(new ImplementationComparisonPopulationRequest(
                [borrowed],
                [borrowed],
                null,
                null));
        ProjectedQueryPopulation projected = AssertProjected(
            QueryPopulationProjection.Execute(population));
        QueryToResearchPopulationReceipt receipt = projected.Receipt;

        Assert.Equal(
            new Dictionary<string, Type>
            {
                [nameof(QueryToResearchPopulationReceipt.Profile)] =
                    typeof(QueryComparisonProfile),
                [nameof(QueryToResearchPopulationReceipt.Operation)] =
                    typeof(QueryResearchOperationCorrespondence),
                [nameof(QueryToResearchPopulationReceipt.Questions)] =
                    typeof(ImmutableDictionary<
                        QueryComparisonQuestionId,
                        ResearchComparisonQuestionId>),
                [nameof(QueryToResearchPopulationReceipt.Inputs)] =
                    typeof(ImmutableDictionary<
                        QueryComparisonInputId,
                        QueryResearchInputCorrespondence>),
            },
            NonPublicProperties(typeof(QueryToResearchPopulationReceipt)));

        Type[] forbidden =
        [
            typeof(ImplementationComparisonBinding),
            typeof(ResolvedAssemblyReference),
            typeof(IAssemblyReferenceResolver),
            typeof(LibraryBodyIndex),
            typeof(ResearchComparisonInputOccurrence),
            typeof(ResearchAdmittedPopulation),
        ];
        Type[] receiptState =
        [
            typeof(QueryToResearchPopulationReceipt),
            typeof(QueryResearchOperationCorrespondence),
            typeof(QueryResearchQuestionCorrespondence),
            typeof(QueryResearchInputCorrespondence),
        ];
        foreach (Type type in receiptState)
        {
            Assert.DoesNotContain(
                DeclaredInstanceStateComponentTypes(type),
                component => forbidden.Any(
                    forbiddenType => forbiddenType.IsAssignableFrom(component)));
        }

        Assert.Contains(
            DeclaredInstanceStateComponentTypes(
                typeof(QueryComparisonInput<ImplementationComparisonBinding>)),
            component => component == typeof(ImplementationComparisonBinding));
        Assert.Contains(
            DeclaredInstanceStateComponentTypes(typeof(ProjectedQueryPopulation)),
            component => component == typeof(ResearchAdmittedPopulation));

        Assert.Same(population.Operation, receipt.Operation.Query);
        Assert.Same(projected.Admission.Operation, receipt.Operation.Research);
        Assert.All(
            receipt.Inputs.Values,
            pair =>
            {
                Assert.Contains(pair.Query, population.InputIds);
                Assert.Contains(
                    pair.Research,
                    projected.Admission.Inputs.Select(input => input.Id));
            });
    }

    [Fact]
    public void PopulationProjection_IsCompanionInternalAndAbsentFromPublicResults()
    {
        Type[] companionInternal =
        [
            typeof(QueryPopulationProjection),
            typeof(QueryPopulationProjectionOutcome),
            typeof(ProjectedQueryPopulation),
            typeof(QueryToResearchPopulationReceipt),
            typeof(QueryResearchOperationCorrespondence),
            typeof(QueryResearchQuestionCorrespondence),
            typeof(QueryResearchInputCorrespondence),
        ];
        Assert.All(companionInternal, type => Assert.True(type.IsNotPublic, type.Name));

        Assert.DoesNotContain(
            typeof(QueryPopulationProjection).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance),
            method => method.DeclaringType == typeof(QueryPopulationProjection));

        MethodInfo implementationExecute = Assert.Single(
            typeof(ImplementationComparisonQuery).GetMethods(),
            method => method.Name == nameof(ImplementationComparisonQuery.Execute));
        MethodInfo bodySignalExecute = Assert.Single(
            typeof(BodySignalComparisonQuery).GetMethods(),
            method => method.Name == nameof(BodySignalComparisonQuery.Execute));
        Assert.Equal(typeof(ImplementationDiffResult), implementationExecute.ReturnType);
        Assert.Equal(typeof(ResearchComparison), bodySignalExecute.ReturnType);

        foreach (Type publicResult in new[]
        {
            implementationExecute.ReturnType,
            bodySignalExecute.ReturnType,
            typeof(QueryComparisonPopulation),
        })
        {
            Assert.DoesNotContain(
                PublicMemberComponentTypes(publicResult),
                component => companionInternal.Contains(component));
        }

        Type[] projectedComponents =
            DeclaredInstanceStateComponentTypes(typeof(ProjectedQueryPopulation))
                .ToArray();
        Assert.Contains(typeof(ResearchAdmittedPopulation), projectedComponents);
        Assert.Contains(typeof(QueryToResearchPopulationReceipt), projectedComponents);
    }

    [Fact]
    public void ComparisonPopulation_Demo()
    {
        ImplementationComparisonBinding borrowed = ImplementationBinding();
        QueryComparisonPopulation<ImplementationComparisonBinding> population =
            Seal(new ImplementationComparisonPopulationRequest(
                [borrowed, borrowed],
                [borrowed],
                null,
                null));
        ProjectionParts parts = PrepareParts(population);
        ProjectedQueryPopulation projected = AssertProjected(
            QueryToResearchPopulationReceipt.Create(
                parts.Projection,
                parts.Admission,
                parts.Operation,
                parts.Questions,
                parts.Inputs));
        QueryPopulationProjectionOutcome incomplete =
            QueryToResearchPopulationReceipt.Create(
                parts.Projection,
                parts.Admission,
                parts.Operation,
                parts.Questions,
                [.. parts.Inputs.Take(2)]);
        QueryPopulationProjectionOutcome.Rejected rejected =
            Assert.IsType<QueryPopulationProjectionOutcome.Rejected>(incomplete);

        Assert.Equal(3, population.InputIds.Length);
        Assert.Equal(
            3,
            DistinctReferenceCount(
                projected.Admission.Inputs.Select(input => input.Id)));
        Assert.Equal(3, projected.Receipt.Inputs.Count);
        Assert.Equal(
            QueryPopulationProjectionRejection.InputMappingMismatch,
            rejected.Reason);

        Console.WriteLine(
            "sealed: 3 occurrences (Before=2, After=1) from one borrowed binding");
        Console.WriteLine(
            "projected: 3 distinct Research input ids; exact maps=3");
        Console.WriteLine(
            "incomplete input map: InputMappingMismatch; receipt=none");
    }

    static void AssertEveryDeclaredSealingRejection(
        ImplementationComparisonBinding implementation)
    {
        HashSet<QueryPopulationRejectionKind> observed = [];

        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    null,
                    [],
                    null,
                    null)),
            QueryPopulationRejectionKind.MissingSide,
            QueryComparisonProfile.ImplementationComparison,
            QueryComparisonSide.Before,
            null);
        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    [null],
                    [],
                    null,
                    null)),
            QueryPopulationRejectionKind.MissingBinding,
            QueryComparisonProfile.ImplementationComparison,
            QueryComparisonSide.Before,
            0);
        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    [implementation with { Assembly = null! }],
                    [],
                    null,
                    null)),
            QueryPopulationRejectionKind.MissingAssembly,
            QueryComparisonProfile.ImplementationComparison,
            QueryComparisonSide.Before,
            0);
        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    [implementation with { Resolver = null! }],
                    [],
                    null,
                    null)),
            QueryPopulationRejectionKind.MissingResolver,
            QueryComparisonProfile.ImplementationComparison,
            QueryComparisonSide.Before,
            0);
        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    [],
                    [implementation with { BodyIndex = null! }],
                    null,
                    null)),
            QueryPopulationRejectionKind.MissingBodyIndex,
            QueryComparisonProfile.ImplementationComparison,
            QueryComparisonSide.After,
            0);
        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    [implementation],
                    [],
                    new HashSet<string> { null! },
                    null)),
            QueryPopulationRejectionKind.MissingTypeFilter,
            QueryComparisonProfile.ImplementationComparison,
            null,
            null);
        Observe(
            QueryComparisonPopulationSealer.Execute(
                new ImplementationComparisonPopulationRequest(
                    [implementation],
                    [],
                    null,
                    new HashSet<string> { null! })),
            QueryPopulationRejectionKind.MissingMemberTarget,
            QueryComparisonProfile.ImplementationComparison,
            null,
            null);

        Assert.Equal(
            Enum.GetValues<QueryPopulationRejectionKind>().ToHashSet(),
            observed);

        void Observe(
            QueryPopulationSealingOutcome outcome,
            QueryPopulationRejectionKind kind,
            QueryComparisonProfile profile,
            QueryComparisonSide? side,
            int? index)
        {
            QueryPopulationSealingOutcome.Rejected rejected =
                Assert.IsType<QueryPopulationSealingOutcome.Rejected>(outcome);
            Assert.Equal(kind, rejected.Rejection.Kind);
            Assert.Equal(profile, rejected.Rejection.Profile);
            Assert.Equal(side, rejected.Rejection.Side);
            Assert.Equal(index, rejected.Rejection.Index);
            Assert.Null(
                rejected.GetType().GetProperty(
                    nameof(QueryPopulationSealingOutcome.Sealed.Population)));
            observed.Add(rejected.Rejection.Kind);
        }
    }

    static void AssertFreshPopulation<TBinding>(
        QueryComparisonPopulation<TBinding> first,
        QueryComparisonPopulation<TBinding> second)
        where TBinding : class
    {
        Assert.NotSame(first.Operation, second.Operation);
        Assert.NotSame(first.Question, second.Question);
        Assert.Same(first.Operation, first.Question.Operation);
        Assert.Same(second.Operation, second.Question.Operation);
        Assert.Equal(first.InputIds.Length, second.InputIds.Length);
        Assert.Equal(
            first.InputIds.Length,
            DistinctReferenceCount(first.InputIds));
        Assert.Equal(
            second.InputIds.Length,
            DistinctReferenceCount(second.InputIds));

        for (int index = 0; index < first.InputIds.Length; index++)
        {
            Assert.NotSame(first.InputIds[index], second.InputIds[index]);
            Assert.Same(first.Question, first.InputIds[index].Question);
            Assert.Same(second.Question, second.InputIds[index].Question);
            Assert.Same(first.Operation, first.InputIds[index].Operation);
            Assert.Same(second.Operation, second.InputIds[index].Operation);
        }
    }

    static void AssertOccurrencePopulation<TBinding>(
        QueryComparisonPopulation<TBinding> population,
        TBinding borrowed)
        where TBinding : class
    {
        Assert.Equal(2, population.Before.Length);
        Assert.Single(population.After);
        Assert.Equal(3, population.Inputs.Length);
        Assert.Equal(3, population.InputIds.Length);
        Assert.Equal(3, DistinctReferenceCount(population.InputIds));
        Assert.All(population.Inputs, input => Assert.Same(borrowed, input.Binding));
        Assert.All(
            population.Before,
            input => Assert.Equal(QueryComparisonSide.Before, input.Id.Side));
        Assert.All(
            population.After,
            input => Assert.Equal(QueryComparisonSide.After, input.Id.Side));
        Assert.Equal(
            population.Inputs.Select(input => input.Id),
            population.InputIds);
        Assert.All(
            population.InputIds,
            id =>
            {
                Assert.Same(population.Operation, id.Operation);
                Assert.Same(population.Question, id.Question);
            });
    }

    static ProjectionParts PrepareParts(QueryComparisonPopulation population)
    {
        QueryPopulationProjection projection =
            QueryPopulationProjection.Prepare(population);
        ResearchAdmittedPopulation admitted = Admit(projection.Request);
        ResearchAdmittedQuestion admittedQuestion =
            Assert.Single(admitted.Questions);
        ImmutableArray<QueryResearchInputCorrespondence> inputs =
        [
            .. population.InputIds.Select(
                id => new QueryResearchInputCorrespondence(
                    id,
                    admitted.GetInput(projection.Occurrences[id]).Id,
                    id.Side)),
        ];
        return new(
            projection,
            admitted,
            new(population.Operation, admitted.Operation),
            [new(population.Question, admittedQuestion.Id)],
            inputs);
    }

    static void AssertInputMappingRejected(
        ProjectionParts parts,
        ImmutableArray<QueryResearchInputCorrespondence> inputs)
        => AssertProjectionRejected(
            QueryToResearchPopulationReceipt.Create(
                parts.Projection,
                parts.Admission,
                parts.Operation,
                parts.Questions,
                inputs),
            QueryPopulationProjectionRejection.InputMappingMismatch);

    static void AssertProjectionRejected(
        QueryPopulationProjectionOutcome outcome,
        QueryPopulationProjectionRejection expected)
    {
        QueryPopulationProjectionOutcome.Rejected rejected =
            Assert.IsType<QueryPopulationProjectionOutcome.Rejected>(outcome);
        Assert.Equal(expected, rejected.Reason);
        Assert.Null(
            rejected.GetType().GetProperty(
                nameof(QueryPopulationProjectionOutcome.Projected.Population)));
    }

    static ProjectedQueryPopulation AssertProjected(
        QueryPopulationProjectionOutcome outcome)
        => Assert.IsType<QueryPopulationProjectionOutcome.Projected>(outcome)
            .Population;

    static ResearchAdmittedPopulation Admit(
        ResearchComparisonAdmissionRequest request)
        => Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(request)).Population;

    static QueryComparisonPopulation<ImplementationComparisonBinding> Seal(
        ImplementationComparisonPopulationRequest request)
        => Assert.IsType<QueryComparisonPopulation<ImplementationComparisonBinding>>(
            Assert.IsType<QueryPopulationSealingOutcome.Sealed>(
                QueryComparisonPopulationSealer.Execute(request)).Population);

    static ImplementationComparisonBinding ImplementationBinding()
    {
        string path = FixtureCatalog.DiffPair.OldAssemblyPath();
        return new(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "query comparison population test")),
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            LibraryBodyIndex.Open(path));
    }

    static Dictionary<string, Type> PublicProperties(Type type)
        => type.GetProperties(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType);

    static Dictionary<string, Type> NonPublicProperties(Type type)
        => type.GetProperties(
                BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType);

    static IEnumerable<Type> DeclaredInstanceStateComponentTypes(Type type)
        => type.GetFields(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly)
            .SelectMany(field => ComponentTypes(field.FieldType));

    static IEnumerable<Type> PublicMemberComponentTypes(Type type)
    {
        const BindingFlags Flags =
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        foreach (FieldInfo field in type.GetFields(Flags))
        {
            foreach (Type component in ComponentTypes(field.FieldType))
                yield return component;
        }
        foreach (PropertyInfo property in type.GetProperties(Flags))
        {
            foreach (Type component in ComponentTypes(property.PropertyType))
                yield return component;
        }
        foreach (MethodInfo method in type.GetMethods(Flags))
        {
            foreach (Type component in ComponentTypes(method.ReturnType))
                yield return component;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                foreach (Type component in ComponentTypes(parameter.ParameterType))
                    yield return component;
            }
        }
    }

    static IEnumerable<Type> ComponentTypes(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (Type component in ComponentTypes(type.GetElementType()!))
                yield return component;
        }
        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type component in ComponentTypes(argument))
                    yield return component;
            }
        }
    }

    static int DistinctReferenceCount<T>(IEnumerable<T> values)
        where T : class
        => new HashSet<T>(values, ReferenceEqualityComparer.Instance).Count;

    static void AssertReferenceSetEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual)
        where T : class
    {
        HashSet<T> expectedSet = new(expected, ReferenceEqualityComparer.Instance);
        HashSet<T> actualSet = new(actual, ReferenceEqualityComparer.Instance);
        Assert.True(
            expectedSet.SetEquals(actualSet),
            $"Expected {expectedSet.Count} exact identities; actual {actualSet.Count}.");
    }

    sealed record ProjectionParts(
        QueryPopulationProjection Projection,
        ResearchAdmittedPopulation Admission,
        QueryResearchOperationCorrespondence Operation,
        ImmutableArray<QueryResearchQuestionCorrespondence> Questions,
        ImmutableArray<QueryResearchInputCorrespondence> Inputs);
}
