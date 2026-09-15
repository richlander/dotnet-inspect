using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;

using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

/// <summary>
/// Gates for the Research-owned comparison admission boundary described by
/// <c>docs/design/implementation-diff.md</c> under
/// <c>Research admission and identity</c>.
/// </summary>
public class ResearchComparisonAdmissionTests
{
    [Fact]
    public void ResearchAdmission_MintsFreshParentedIdentitiesForEveryOccurrence()
    {
        foreach (ResearchComparisonProfile profile in
            Enum.GetValues<ResearchComparisonProfile>())
        {
            AdmissionFixture fixture = AdmissionFixture.TwoQuestions(profile);
            ResearchAdmittedPopulation population = Admit(fixture.Request);

            Assert.Equal(profile, population.Profile);
            Assert.Equal(fixture.Questions.Count, population.Questions.Length);
            Assert.Equal(fixture.Occurrences.Count, population.Inputs.Length);

            // Every question is parented by the one operation and is distinct.
            Assert.All(
                population.Questions,
                question => Assert.Same(population.Operation, question.Operation));
            Assert.Equal(
                population.Questions.Length,
                Distinct(population.Questions.Select(question => question.Id)));

            // Every input is freshly minted and parented by operation+question.
            Assert.Equal(
                population.Inputs.Length,
                Distinct(population.Inputs.Select(input => input.Id)));
            foreach (ResearchAdmittedQuestion question in population.Questions)
            {
                foreach (ResearchComparisonSide side in
                    Enum.GetValues<ResearchComparisonSide>())
                {
                    foreach (ResearchAdmittedInput input in question.Side(side))
                    {
                        Assert.Same(question.Id, input.Id.Question);
                        Assert.Same(population.Operation, input.Id.Operation);
                        Assert.Equal(side, input.Id.Side);
                        Assert.Equal(side, input.Side);
                    }
                }
            }

            // The admitted side set is derived from the side declaration.
            Assert.Equal(
                Enum.GetValues<ResearchComparisonSide>().ToHashSet(),
                population.Inputs.Select(input => input.Side).ToHashSet());
        }
    }

    [Fact]
    public void ResearchAdmission_ReturnsAtomicExactInputAssociations()
    {
        foreach (ResearchComparisonProfile profile in
            Enum.GetValues<ResearchComparisonProfile>())
        {
            AdmissionFixture fixture = AdmissionFixture.TwoQuestions(profile);
            ResearchAdmittedPopulation population = Admit(fixture.Request);

            // Every requested occurrence has exactly one admitted identity.
            Assert.Equal(fixture.Occurrences.Count, population.Inputs.Length);
            for (int i = 0; i < fixture.Occurrences.Count; i++)
            {
                (ResearchComparisonInputOccurrence occurrence,
                    int questionIndex,
                    ResearchComparisonSide side) = fixture.Occurrences[i];

                Assert.True(population.TryGetInput(occurrence, out var input));
                Assert.Same(occurrence, input.Occurrence);
                Assert.Same(input, population.GetInput(occurrence));
                Assert.Same(
                    population.Questions[questionIndex].Id,
                    input.Question);
                Assert.Equal(side, input.Side);
                Assert.Contains(
                    input,
                    population.Questions[questionIndex].Side(side));
            }

            // No admitted input exists without a requested occurrence.
            HashSet<ResearchComparisonInputOccurrence> requested = new(
                fixture.Occurrences.Select(entry => entry.Occurrence),
                ReferenceEqualityComparer.Instance);
            Assert.All(
                population.Inputs,
                input => Assert.Contains(input.Occurrence, requested));

            // An unadmitted occurrence has no association.
            ResearchComparisonInputOccurrence stranger = Occurrence(profile);
            Assert.False(population.TryGetInput(stranger, out _));
            Assert.Throws<ArgumentException>(
                () => population.GetInput(stranger));
        }
    }

    [Fact]
    public void ResearchAdmission_AdmitsEveryDeclaredProfile()
    {
        Dictionary<ResearchComparisonProfile, ResearchComparisonInputOccurrence>
            byProfile = new()
            {
                [ResearchComparisonProfile.ImplementationComparison] =
                    ImplementationOccurrence(),
                [ResearchComparisonProfile.BodySignal] =
                    BodySignalOccurrence(),
            };

        Assert.Equal(
            Enum.GetValues<ResearchComparisonProfile>().ToHashSet(),
            byProfile.Keys.ToHashSet());

        foreach ((ResearchComparisonProfile profile,
            ResearchComparisonInputOccurrence occurrence) in byProfile)
        {
            Assert.Equal(profile, occurrence.Profile);
            ResearchAdmittedPopulation population = Admit(
                Request(profile, Question([occurrence], [])));
            Assert.Equal(profile, population.Profile);
            ResearchAdmittedInput input = Assert.Single(population.Inputs);
            Assert.Same(occurrence, input.Occurrence);
            Assert.Equal(ResearchComparisonSide.Before, input.Side);
        }
    }

    [Fact]
    public void ResearchAdmission_ImplementationProfileBorrowsExactAssemblyEvidence()
    {
        ImplementationAssemblyInput borrowed = ImplementationInput();
        ImplementationComparisonInputOccurrence occurrence = new(borrowed);
        ResearchAdmittedPopulation population = Admit(
            Request(
                ResearchComparisonProfile.ImplementationComparison,
                Question([], [occurrence])));

        ResearchAdmittedInput input = Assert.Single(population.Inputs);
        var admitted = Assert.IsType<ImplementationComparisonInputOccurrence>(
            input.Occurrence);
        Assert.Same(borrowed, admitted.Input);
        Assert.Same(borrowed.Assembly, admitted.Assembly);
        Assert.Same(borrowed.Resolver, admitted.Resolver);
        Assert.Same(borrowed.BodyIndex, admitted.BodyIndex);
        Assert.Equal(ResearchComparisonSide.After, input.Side);
    }

    [Fact]
    public void ResearchAdmission_BodySignalProfileBorrowsExactBodyIndex()
    {
        LibraryBodyIndex borrowed = BodyIndex();
        BodySignalComparisonInputOccurrence occurrence = new(borrowed);
        ResearchAdmittedPopulation population = Admit(
            Request(
                ResearchComparisonProfile.BodySignal,
                Question([occurrence], [])));

        ResearchAdmittedInput input = Assert.Single(population.Inputs);
        var admitted = Assert.IsType<BodySignalComparisonInputOccurrence>(
            input.Occurrence);
        Assert.Same(borrowed, admitted.BodyIndex);
    }

    [Fact]
    public void ResearchAdmission_RepeatedBorrowedValuesRetainDistinctOccurrences()
    {
        ImplementationAssemblyInput borrowed = ImplementationInput();
        ImplementationComparisonInputOccurrence first = new(borrowed);
        ImplementationComparisonInputOccurrence second = new(borrowed);

        // The same borrowed value, wrapped twice, stays distinguishable.
        Assert.NotEqual<object>(first, second);
        Assert.Same(first.Input, second.Input);

        ResearchAdmittedPopulation population = Admit(
            Request(
                ResearchComparisonProfile.ImplementationComparison,
                Question([first, second], [new ImplementationComparisonInputOccurrence(borrowed)])));

        Assert.Equal(3, population.Inputs.Length);
        Assert.Equal(3, Distinct(population.Inputs.Select(input => input.Id)));
        Assert.Same(first, population.GetInput(first).Occurrence);
        Assert.Same(second, population.GetInput(second).Occurrence);
        Assert.NotSame(population.GetInput(first), population.GetInput(second));
        Assert.NotSame(
            population.GetInput(first).Id,
            population.GetInput(second).Id);
    }

    [Fact]
    public void ResearchAdmission_CopiesCallerOwnedCollections()
    {
        ImplementationComparisonInputOccurrence retained =
            ImplementationOccurrence();
        List<ResearchComparisonInputOccurrence?> before = [retained];
        List<ResearchComparisonInputOccurrence?> after = [];
        ResearchComparisonAdmissionQuestion question = new(before, after);
        List<ResearchComparisonAdmissionQuestion?> questions = [question];
        ResearchComparisonAdmissionRequest request = new(
            ResearchComparisonProfile.ImplementationComparison,
            questions);

        ResearchAdmittedPopulation population = Admit(request);

        // Mutating every caller-owned collection changes nothing admitted.
        before.Add(ImplementationOccurrence());
        before.Add(null);
        after.Add(ImplementationOccurrence());
        questions.Add(null);
        questions.Clear();

        Assert.Single(request.Questions);
        Assert.Single(question.Before);
        Assert.Empty(question.After);
        ResearchAdmittedQuestion admitted = Assert.Single(population.Questions);
        Assert.Same(retained, Assert.Single(admitted.Before).Occurrence);
        Assert.Empty(admitted.After);
        Assert.Single(population.Inputs);
    }

    [Fact]
    public void ResearchAdmission_InvalidProfileInputExposesNoPartialPopulation()
    {
        // A body-signal occurrence cannot enter an implementation admission.
        ResearchComparisonAdmissionRequest request = Request(
            ResearchComparisonProfile.ImplementationComparison,
            Question([ImplementationOccurrence()], []),
            Question([], [BodySignalOccurrence()]));

        ResearchAdmissionOutcome outcome =
            ResearchComparisonAdmission.Admit(request);

        var rejected = Assert.IsType<ResearchAdmissionOutcome.Rejected>(outcome);
        Assert.Equal(
            ResearchAdmissionRejectionKind.ProfileMismatch,
            rejected.Rejection.Kind);
        Assert.Equal(
            "ProfileMismatch Input[q=1,After,0]",
            Describe(rejected.Rejection));

        // The rejected arm's whole public surface -- properties, fields,
        // constructors, methods, events, parameters, by-ref and out
        // parameters, and return types, unwrapped through array elements and
        // generic arguments and followed transitively through every
        // Research-owned type it reaches -- exposes no admitted identity,
        // population, or input type.
        IReadOnlyCollection<Type> surface =
            PublicSurfaceClosure(typeof(ResearchAdmissionOutcome.Rejected));
        Assert.DoesNotContain(surface, IsAdmissionIdentity);

        // The permitted Research-owned rejection surface is exact, so no
        // wrapper type can be introduced to hide identity exposure behind a
        // type this walk would otherwise accept.
        Assert.Equal(
            new HashSet<Type>
            {
                typeof(ResearchAdmissionOutcome.Rejected),
                typeof(ResearchAdmissionRejection),
                typeof(ResearchAdmissionRejectionKind),
                typeof(ResearchAdmissionLocation),
                typeof(ResearchAdmissionLocation.Operation),
                typeof(ResearchAdmissionLocation.Question),
                typeof(ResearchAdmissionLocation.Input),
                typeof(ResearchComparisonProfile),
                typeof(ResearchComparisonSide),
            },
            surface.Where(IsResearchOwned).ToHashSet());

        // The walk is not vacuous: the admitted arm, and every indirect
        // exposure shape the rejected arm must never grow, are all reported.
        Assert.Contains(
            PublicSurfaceClosure(typeof(ResearchAdmissionOutcome.Admitted)),
            IsAdmissionIdentity);
        Assert.All(
            typeof(IdentityExposureProbe).GetMembers(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly),
            member => Assert.True(
                SignatureTypes(member).SelectMany(ComponentTypes)
                    .Any(IsAdmissionIdentity),
                member.Name));
        Assert.Contains(
            PublicSurfaceClosure(typeof(IdentityExposureProbe)),
            IsAdmissionIdentity);

        // An explicit interface implementation declares no public member, so
        // the walk must reach it through the implemented interface instead.
        Assert.Empty(
            typeof(ExplicitInterfaceExposureProbe).GetMembers(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly));
        Assert.Contains(
            PublicSurfaceClosure(typeof(ExplicitInterfaceExposureProbe)),
            IsAdmissionIdentity);

        // The rejected surface is exact, member by member. A hidden carrier
        // cannot be added without failing here, and no member is an opaque
        // object or non-generic collection. Members inherited from object are
        // not declared by these types, so they are absent by construction.
        Assert.Equal(
            new HashSet<string>
            {
                "property Rejection : ResearchAdmissionRejection",
                "method get_Rejection() : ResearchAdmissionRejection",
            },
            DeclaredPublicInstanceSurface(
                typeof(ResearchAdmissionOutcome.Rejected)).ToHashSet());
        Assert.Equal(
            new HashSet<string>
            {
                "property Kind : ResearchAdmissionRejectionKind",
                "method get_Kind() : ResearchAdmissionRejectionKind",
                "property Profile : ResearchComparisonProfile",
                "method get_Profile() : ResearchComparisonProfile",
                "property Location : ResearchAdmissionLocation",
                "method get_Location() : ResearchAdmissionLocation",
                "property Summary : String",
                "method get_Summary() : String",
            },
            DeclaredPublicInstanceSurface(typeof(ResearchAdmissionRejection))
                .ToHashSet());
        Assert.All(
            new[]
            {
                typeof(ResearchAdmissionOutcome.Rejected),
                typeof(ResearchAdmissionRejection),
            },
            type => Assert.All(
                type.GetMembers(
                    BindingFlags.Public
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly),
                member => Assert.All(
                    SignatureTypes(member),
                    signature => Assert.False(
                        IsOpaqueCarrier(signature),
                        $"{type.Name}.{member.Name}: {signature}"))));

        // The opaque-carrier check is not vacuous.
        Assert.All(
            new[]
            {
                typeof(object),
                typeof(object[]),
                typeof(System.Collections.IEnumerable),
                typeof(IReadOnlyList<object>),
                typeof(Delegate),
            },
            type => Assert.True(IsOpaqueCarrier(type), type.Name));
        Assert.All(
            new[]
            {
                typeof(string),
                typeof(ResearchAdmissionRejection),
                typeof(ResearchComparisonProfile),
                typeof(ImmutableArray<ResearchAdmittedInput>),
            },
            type => Assert.False(IsOpaqueCarrier(type), type.Name));

        // The outcome is a closed two-arm hierarchy.
        Assert.Equal(
            new HashSet<Type>
            {
                typeof(ResearchAdmissionOutcome.Admitted),
                typeof(ResearchAdmissionOutcome.Rejected),
            },
            typeof(ResearchAdmissionOutcome).Assembly
                .GetTypes()
                .Where(type => type.BaseType == typeof(ResearchAdmissionOutcome))
                .ToHashSet());
    }

    [Fact]
    public void ResearchAdmittedPopulation_RetainsOnlyImmutableState()
    {
        ImplementationComparisonInputOccurrence first = ImplementationOccurrence();
        ImplementationComparisonInputOccurrence second =
            new(first.Assembly, first.Resolver, first.BodyIndex);
        ResearchAdmittedPopulation population = Admit(
            Request(
                ResearchComparisonProfile.ImplementationComparison,
                Question([first], [second])));

        // No admitted type retains mutable state. The occurrence association
        // is a frozen private copy, not the minting dictionary.
        Type[] admitted =
        [
            typeof(ResearchAdmittedPopulation),
            typeof(ResearchAdmittedQuestion),
            typeof(ResearchAdmittedInput),
        ];
        foreach (Type type in admitted)
        {
            FieldInfo[] fields = type.GetFields(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotEmpty(fields);
            Assert.All(
                fields,
                field => Assert.True(
                    IsImmutableState(field.FieldType),
                    $"{type.Name}.{field.Name}: {field.FieldType}"));
            Assert.DoesNotContain(
                fields,
                field => IsGenericDefinition(field.FieldType, typeof(Dictionary<,>)));
        }

        FieldInfo[] maps =
        [
            .. typeof(ResearchAdmittedPopulation).GetFields(
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(
                    field => IsGenericDefinition(
                        field.FieldType,
                        typeof(FrozenDictionary<,>))),
        ];
        Assert.Equal(2, maps.Length);
        FieldInfo occurrenceMap = Assert.Single(
            maps,
            field => field.FieldType.GenericTypeArguments[0]
                == typeof(ResearchComparisonInputOccurrence));
        var byOccurrence =
            (FrozenDictionary<ResearchComparisonInputOccurrence, ResearchAdmittedInput>)
                occurrenceMap.GetValue(population)!;
        FieldInfo idMap = Assert.Single(
            maps,
            field => field.FieldType.GenericTypeArguments[0]
                == typeof(ResearchComparisonInputId));
        var byId =
            (FrozenDictionary<ResearchComparisonInputId, ResearchAdmittedInput>)
                idMap.GetValue(population)!;
        Assert.Same(ReferenceEqualityComparer.Instance, byOccurrence.Comparer);
        Assert.Same(ReferenceEqualityComparer.Instance, byId.Comparer);
        Assert.Equal(2, byOccurrence.Count);
        Assert.Equal(2, byId.Count);
        Assert.NotSame(population.GetInput(first), population.GetInput(second));
    }

    [Fact]
    public void ResearchAdmissionRequests_SeparateConstructionAndAdmissionNullContracts()
    {
        LibraryBodyIndex bodyIndex = BodyIndex();

        // A direct constructor argument is validated at construction, before
        // any admission and before any outcome exists.
        Assert.Throws<ArgumentNullException>(
            () => new BodySignalComparisonInputOccurrence(null!));
        Assert.Throws<ArgumentNullException>(
            () => new ImplementationComparisonInputOccurrence(
                (ImplementationAssemblyInput)null!));
        Assert.Throws<ArgumentNullException>(
            () => new ResearchComparisonAdmissionQuestion(null!, []));
        Assert.Throws<ArgumentNullException>(
            () => new ResearchComparisonAdmissionQuestion([], null!));
        Assert.Throws<ArgumentNullException>(
            () => new ResearchComparisonAdmissionRequest(
                ResearchComparisonProfile.BodySignal,
                null!));
        Assert.Throws<ArgumentNullException>(
            () => ResearchComparisonAdmission.Admit(null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResearchComparisonAdmissionRequest(
                (ResearchComparisonProfile)(-1),
                []));

        // The body-signal profile borrows exactly one value, taken as a direct
        // constructor argument, so that construction-time check is its whole
        // null contract: a constructed occurrence can never report missing
        // evidence, and admission never rejects one for evidence.
        BodySignalComparisonInputOccurrence bodySignal = new(bodyIndex);
        Assert.Same(bodyIndex, bodySignal.BodyIndex);
        Assert.Null(bodySignal.MissingEvidenceMember);
        Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(
                Request(
                    ResearchComparisonProfile.BodySignal,
                    Question([bodySignal], []))));

        // Every direct argument of the three-argument implementation
        // constructor is validated too.
        // ResearchAdmission_ImplementationOccurrenceValidatesEveryDirectArgument
        // covers one null per parameter, and this derives that case set from
        // the constructor's declaration, so a new or renamed parameter fails.
        Assert.Equal(
            new HashSet<string?> { "assembly", "resolver", "bodyIndex" },
            typeof(ImplementationComparisonInputOccurrence)
                .GetConstructors()
                .Single(constructor => constructor.GetParameters().Length == 3)
                .GetParameters()
                .Select(parameter => parameter.Name)
                .ToHashSet());

        // Nested borrowed evidence supplied as an already-constructed input is
        // deliberately not construction-validated. An incomplete
        // implementation input constructs, and admission turns it into a typed
        // rejection that exposes no identity and no partial population, rather
        // than an exception.
        ImplementationComparisonInputOccurrence incomplete =
            Incomplete(assembly: false);
        Assert.Null(incomplete.Assembly);
        Assert.Equal(
            nameof(ImplementationAssemblyInput.Assembly),
            incomplete.MissingEvidenceMember);
        var rejected = Assert.IsType<ResearchAdmissionOutcome.Rejected>(
            ResearchComparisonAdmission.Admit(
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([incomplete], []))));
        Assert.Equal(
            ResearchAdmissionRejectionKind.MissingInputEvidence,
            rejected.Rejection.Kind);

        // A null occurrence element is retained for the same reason.
        ResearchComparisonAdmissionQuestion question = new([null], []);
        Assert.Null(Assert.Single(question.Before));
    }

    [Theory]
    [InlineData("assembly")]
    [InlineData("resolver")]
    [InlineData("bodyIndex")]
    public void ResearchAdmission_ImplementationOccurrenceValidatesEveryDirectArgument(
        string parameter)
    {
        ResolvedAssemblyReference? assembly =
            parameter == "assembly" ? null : PathlessAssembly();
        IAssemblyReferenceResolver? resolver =
            parameter == "resolver" ? null : new UnusedResolver();
        LibraryBodyIndex? bodyIndex =
            parameter == "bodyIndex" ? null : BodyIndex();

        // The direct-argument overload validates before it constructs the
        // borrowed input record, so an incomplete implementation input is
        // unrepresentable through it.
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => new ImplementationComparisonInputOccurrence(
                    assembly!,
                    resolver!,
                    bodyIndex!));
        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void ResearchAdmission_RejectsEveryDeclaredInvalidShape()
    {
        ImplementationComparisonInputOccurrence withinSide =
            ImplementationOccurrence();
        ImplementationComparisonInputOccurrence acrossSides =
            ImplementationOccurrence();
        ImplementationComparisonInputOccurrence acrossQuestions =
            ImplementationOccurrence();

        // The location arms are a closed hierarchy, so the expected location
        // spelling below cannot go stale against a new arm.
        Assert.Equal(
            new HashSet<Type>
            {
                typeof(ResearchAdmissionLocation.Operation),
                typeof(ResearchAdmissionLocation.Question),
                typeof(ResearchAdmissionLocation.Input),
            },
            typeof(ResearchAdmissionLocation).Assembly
                .GetTypes()
                .Where(type => type.BaseType == typeof(ResearchAdmissionLocation))
                .ToHashSet());

        (string Name,
            ResearchComparisonAdmissionRequest Request,
            ResearchAdmissionRejectionKind Kind,
            string Location,
            string Summary)[] cases =
        [
            ("no questions",
                Request(ResearchComparisonProfile.BodySignal),
                ResearchAdmissionRejectionKind.MissingQuestions,
                "Operation",
                "at least one question"),

            ("null question first",
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.BodySignal,
                    [null]),
                ResearchAdmissionRejectionKind.MissingQuestion,
                "Question[0]",
                "question must not be null"),
            ("null question after a valid question",
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.BodySignal,
                    [Question([BodySignalOccurrence()], []), null]),
                ResearchAdmissionRejectionKind.MissingQuestion,
                "Question[1]",
                "question must not be null"),

            ("null Before occurrence",
                Request(
                    ResearchComparisonProfile.BodySignal,
                    Question([BodySignalOccurrence(), null], [])),
                ResearchAdmissionRejectionKind.MissingInput,
                "Input[q=0,Before,1]",
                "occurrence must not be null"),
            ("null After occurrence in a later question",
                Request(
                    ResearchComparisonProfile.BodySignal,
                    Question([BodySignalOccurrence()], []),
                    Question(
                        [BodySignalOccurrence()],
                        [BodySignalOccurrence(), null])),
                ResearchAdmissionRejectionKind.MissingInput,
                "Input[q=1,After,1]",
                "occurrence must not be null"),

            // Each borrowed implementation member is gated separately, so a
            // check that stops reporting one member fails here.
            ("missing assembly evidence",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([Incomplete(assembly: false)], [])),
                ResearchAdmissionRejectionKind.MissingInputEvidence,
                "Input[q=0,Before,0]",
                $"does not supply {nameof(ImplementationAssemblyInput.Assembly)}"),
            ("missing resolver evidence",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question(
                        [ImplementationOccurrence()],
                        [Incomplete(resolver: false)])),
                ResearchAdmissionRejectionKind.MissingInputEvidence,
                "Input[q=0,After,0]",
                $"does not supply {nameof(ImplementationAssemblyInput.Resolver)}"),
            ("missing body index evidence",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([ImplementationOccurrence()], []),
                    Question(
                        [],
                        [
                            ImplementationOccurrence(),
                            Incomplete(bodyIndex: false),
                        ])),
                ResearchAdmissionRejectionKind.MissingInputEvidence,
                "Input[q=1,After,1]",
                $"does not supply {nameof(ImplementationAssemblyInput.BodyIndex)}"),

            ("implementation occurrence in a body-signal request",
                Request(
                    ResearchComparisonProfile.BodySignal,
                    Question([ImplementationOccurrence()], [])),
                ResearchAdmissionRejectionKind.ProfileMismatch,
                "Input[q=0,Before,0]",
                "belongs to the ImplementationComparison profile"),
            ("body-signal occurrence in an implementation request",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([ImplementationOccurrence()], [BodySignalOccurrence()])),
                ResearchAdmissionRejectionKind.ProfileMismatch,
                "Input[q=0,After,0]",
                "belongs to the BodySignal profile"),

            // Duplication is rejected wherever the repeat occurs, because the
            // exact occurrence-to-identity association is request-wide.
            ("same occurrence twice within one side",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([withinSide, withinSide], [])),
                ResearchAdmissionRejectionKind.DuplicateOccurrence,
                "Input[q=0,Before,1]",
                "requested more than once"),
            ("same occurrence on both sides of one question",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([acrossSides], [ImplementationOccurrence(), acrossSides])),
                ResearchAdmissionRejectionKind.DuplicateOccurrence,
                "Input[q=0,After,1]",
                "requested more than once"),
            ("same occurrence in two questions",
                Request(
                    ResearchComparisonProfile.ImplementationComparison,
                    Question([acrossQuestions], []),
                    Question([ImplementationOccurrence()], [acrossQuestions])),
                ResearchAdmissionRejectionKind.DuplicateOccurrence,
                "Input[q=1,After,0]",
                "requested more than once"),
        ];

        // The expected set is derived from the declaration, and every location
        // arm is exercised.
        Assert.Equal(
            Enum.GetValues<ResearchAdmissionRejectionKind>().ToHashSet(),
            cases.Select(entry => entry.Kind).ToHashSet());
        Assert.Equal(
            new HashSet<string> { "Operation", "Question", "Input" },
            cases.Select(entry => entry.Location.Split('[')[0]).ToHashSet());

        foreach ((string name,
            ResearchComparisonAdmissionRequest request,
            ResearchAdmissionRejectionKind kind,
            string location,
            string summary) in cases)
        {
            ResearchAdmissionOutcome outcome =
                ResearchComparisonAdmission.Admit(request);
            var rejected =
                Assert.IsType<ResearchAdmissionOutcome.Rejected>(outcome);

            // Kind and the exact QuestionIndex/Side/Index are compared as one
            // value, so a wrong coordinate cannot pass on a right kind.
            Assert.Equal(
                $"{name}: {kind} {location}",
                $"{name}: {Describe(rejected.Rejection)}");
            Assert.Equal(request.Profile, rejected.Rejection.Profile);
            Assert.Contains(summary, rejected.Rejection.Summary);
        }
    }

    [Fact]
    public void ResearchAdmissionIdentities_AreOwnerIssuedReferenceIdentities()
    {
        Type[] identities =
        [
            typeof(ResearchComparisonOperationId),
            typeof(ResearchComparisonQuestionId),
            typeof(ResearchComparisonInputId),
        ];

        foreach (Type identity in identities)
        {
            Assert.True(identity.IsSealed, identity.Name);
            Assert.Empty(identity.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

            // Reference identity only: no value equality, parsing, string
            // conversion, or ordinal/name surrogate.
            Assert.Equal(
                typeof(object),
                identity.GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType);
            Assert.Equal(
                typeof(object),
                identity.GetMethod(nameof(GetHashCode), [])!.DeclaringType);
            Assert.Equal(
                typeof(object),
                identity.GetMethod(nameof(ToString), [])!.DeclaringType);
            Assert.Empty(
                identity.GetMethods(BindingFlags.Public | BindingFlags.Static));
            Assert.Empty(
                identity.GetMethods(BindingFlags.Public | BindingFlags.Static));
            Assert.All(
                identity.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                property => Assert.True(
                    IsAdmissionIdentity(property.PropertyType)
                        || property.PropertyType == typeof(ResearchComparisonSide),
                    $"{identity.Name}.{property.Name}"));
        }

        ResearchAdmittedPopulation first = Admit(
            Request(
                ResearchComparisonProfile.BodySignal,
                Question([BodySignalOccurrence()], [])));
        ResearchAdmittedPopulation second = Admit(
            Request(
                ResearchComparisonProfile.BodySignal,
                Question([BodySignalOccurrence()], [])));

        Assert.Equal(first.Operation, first.Operation);
        Assert.NotEqual(first.Operation, second.Operation);
        Assert.NotEqual(
            first.Inputs[0].Id,
            second.Inputs[0].Id);
    }

    [Fact]
    public void ResearchAdmission_NewAdmissionMintsFreshOperationAndPopulation()
    {
        // The same request, re-admitted, is a new admission: fresh operation,
        // fresh questions, fresh inputs. A retained population keeps its own.
        ImplementationComparisonInputOccurrence occurrence =
            ImplementationOccurrence();
        ResearchComparisonAdmissionRequest request = Request(
            ResearchComparisonProfile.ImplementationComparison,
            Question([occurrence], []));

        ResearchAdmittedPopulation first = Admit(request);
        ResearchAdmittedPopulation second = Admit(request);

        Assert.NotSame(first, second);
        Assert.NotSame(first.Operation, second.Operation);
        Assert.NotSame(first.Questions[0].Id, second.Questions[0].Id);
        Assert.NotSame(first.Inputs[0].Id, second.Inputs[0].Id);
        Assert.Same(occurrence, first.Inputs[0].Occurrence);
        Assert.Same(occurrence, second.Inputs[0].Occurrence);

        // Reading the retained population again preserves its identities.
        Assert.Same(first.Operation, first.Questions[0].Operation);
        Assert.Same(first.Inputs[0], first.GetInput(occurrence));
        Assert.Same(first.Inputs[0].Id, first.GetInput(occurrence).Id);
    }

    [Fact]
    public void ResearchAdmission_DoesNotOpenOrInspectBorrowedInputs()
    {
        // Behavioral evidence: every borrowed capability in the implementation
        // fixture throws when used. Admission completes, so it opened no
        // assembly and resolved no reference.
        AdmissionFixture implementation = AdmissionFixture.TwoQuestions(
            ResearchComparisonProfile.ImplementationComparison);
        ResearchAdmittedPopulation admitted = Admit(implementation.Request);

        Assert.Equal(implementation.Occurrences.Count, admitted.Inputs.Length);
        foreach (ResearchAdmittedInput input in admitted.Inputs)
        {
            var occurrence = Assert.IsType<ImplementationComparisonInputOccurrence>(
                input.Occurrence);
            Assert.Null(occurrence.Assembly.Path);
            Assert.Throws<InvalidOperationException>(
                () => occurrence.Assembly.OpenRead());
            Assert.Throws<InvalidOperationException>(
                () => occurrence.Resolver.Resolve(
                    new AssemblyReferenceIdentity("Probe", null, null, null),
                    AssemblyResolutionScope.Any));
        }

        // The body-signal profile borrows an index whose content cannot be
        // made to throw, so its evidence is the structural walk below plus the
        // exact borrowed value surviving admission unread.
        AdmissionFixture bodySignal = AdmissionFixture.TwoQuestions(
            ResearchComparisonProfile.BodySignal);
        ResearchAdmittedPopulation signals = Admit(bodySignal.Request);
        Assert.Equal(bodySignal.Occurrences.Count, signals.Inputs.Length);
        Assert.All(
            signals.Inputs,
            input => Assert.Same(
                Assert.IsType<BodySignalComparisonInputOccurrence>(
                        input.Occurrence)
                    .BodyIndex,
                Assert.IsType<BodySignalComparisonInputOccurrence>(
                        signals.GetInput(input.Occurrence).Occurrence)
                    .BodyIndex));

        // Structural evidence: no admission-reachable product method calls or
        // reads any borrowed-evidence member. The walk decodes real IL and
        // resolves real metadata tokens, so it sees indirect calls, delegates,
        // and compiler-generated helpers that source text would miss.
        IReadOnlyCollection<MethodBase> reachable = AdmissionReachableMethods();

        // The walk is seeded from the declared admission surface, so it covers
        // both occurrence profiles and the whole minting path.
        Assert.Contains(
            reachable,
            method => Describes(method, nameof(ResearchComparisonAdmission.Admit)));
        Assert.Contains(reachable, method => Describes(method, "Validate"));
        Assert.Contains(reachable, method => Describes(method, "Mint"));
        Assert.Contains(reachable, method => Describes(method, "MintSide"));
        foreach (Type profileType in new[]
        {
            typeof(ImplementationComparisonInputOccurrence),
            typeof(BodySignalComparisonInputOccurrence),
        })
        {
            Assert.Contains(
                reachable,
                method => method.DeclaringType == profileType
                    && method is ConstructorInfo);
            Assert.Contains(
                reachable,
                method => method.DeclaringType == profileType
                    && method.Name == "get_MissingEvidenceMember");
        }

        List<string> violations =
        [
            .. from method in reachable
               from member in ReferencedMembers(method)
               where IsBorrowedEvidence(member)
               select $"{Describe(method)} -> {Describe(member)}",
        ];
        Assert.Empty(violations);

        // The walk is not vacuous. Every forbidden shape -- opening, path
        // reads on both borrowed owners, resolution, and body-index content
        // reads and iteration -- is detected on a probe that performs it, and
        // the probes cover exactly the named forbidden members.
        MemberInfo[] forbidden =
        [
            typeof(ResolvedAssemblyReference).GetProperty(
                nameof(ResolvedAssemblyReference.OpenRead))!.GetMethod!,
            typeof(ResolvedAssemblyReference).GetProperty(
                nameof(ResolvedAssemblyReference.Path))!.GetMethod!,
            typeof(IAssemblyReferenceResolver).GetMethod(
                nameof(IAssemblyReferenceResolver.Resolve))!,
            typeof(LibraryBodyIndex).GetProperty(
                nameof(LibraryBodyIndex.Path))!.GetMethod!,
            typeof(LibraryBodyIndex).GetProperty(
                nameof(LibraryBodyIndex.Methods))!.GetMethod!,
            typeof(LibraryBodyIndex).GetProperty(
                nameof(LibraryBodyIndex.DirectCalls))!.GetMethod!,
            typeof(LibraryBodyIndex).GetMethod(
                nameof(LibraryBodyIndex.GetMethodSignals))!,
        ];
        Assert.All(
            forbidden,
            member => Assert.True(IsBorrowedEvidence(member), Describe(member)));

        MethodInfo[] probes = typeof(BorrowedEvidenceProbe).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.All(
            probes,
            probe => Assert.Contains(ReferencedMembers(probe), IsBorrowedEvidence));
        Assert.Equal(
            forbidden.Select(Describe).ToHashSet(),
            probes.SelectMany(ReferencedMembers)
                .Where(IsBorrowedEvidence)
                .Select(Describe)
                .ToHashSet());
    }

    /// <summary>
    /// Every product method one admission can reach: the declared methods and
    /// constructors of every Research-owned type on the admission surface,
    /// plus every Research-owned method they call, transitively, including
    /// virtual and interface targets and compiler-generated helpers.
    /// </summary>
    static IReadOnlyCollection<MethodBase> AdmissionReachableMethods()
    {
        HashSet<Type> seeds =
        [
            .. PublicSurfaceClosure(typeof(ResearchComparisonAdmission))
                .Concat(PublicSurfaceClosure(typeof(ResearchAdmissionOutcome)))
                .Where(IsResearchOwned)
                .Where(static type => !type.IsEnum),
        ];
        Assert.Contains(typeof(ImplementationComparisonInputOccurrence), seeds);
        Assert.Contains(typeof(BodySignalComparisonInputOccurrence), seeds);
        Assert.Contains(typeof(ResearchAdmittedPopulation), seeds);

        HashSet<MethodBase> reachable = [];
        Queue<MethodBase> pending = new();
        foreach (Type seed in seeds)
        {
            foreach (MethodBase method in DeclaredMethods(seed))
                Enqueue(method);
        }

        while (pending.Count > 0)
        {
            MethodBase method = pending.Dequeue();
            foreach (MemberInfo member in ReferencedMembers(method))
            {
                if (member is not MethodBase called
                    || called.DeclaringType is not Type declaring
                    || !IsResearchOwned(declaring))
                {
                    continue;
                }

                Enqueue(called);
                foreach (MethodBase implementation in Implementations(called))
                    Enqueue(implementation);
            }
        }

        return reachable;

        void Enqueue(MethodBase method)
        {
            if (reachable.Add(method))
                pending.Enqueue(method);
        }
    }

    static IEnumerable<MethodBase> DeclaredMethods(Type type)
        => type.GetMethods(DeclaredMembers)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(DeclaredMembers));

    /// <summary>
    /// Every Research-owned override of one virtual or interface method, so a
    /// virtual call into the admission surface reaches the code that runs.
    /// </summary>
    static IEnumerable<MethodBase> Implementations(MethodBase method)
    {
        if (method is not MethodInfo virtualMethod
            || !virtualMethod.IsVirtual
            || virtualMethod.DeclaringType is not Type declaring)
        {
            yield break;
        }

        MethodInfo baseDefinition = virtualMethod.GetBaseDefinition();
        foreach (Type type in ResearchAssembly.GetTypes())
        {
            if (type == declaring
                || type.IsInterface
                || !declaring.IsAssignableFrom(type))
            {
                continue;
            }

            foreach (MethodInfo candidate in type.GetMethods(DeclaredMembers))
            {
                if (candidate.GetBaseDefinition() == baseDefinition)
                    yield return candidate;
            }
        }
    }

    /// <summary>
    /// Every member one method references by metadata token: call, callvirt,
    /// newobj, ldftn, field access, and ldtoken operands, decoded from the
    /// method's real IL by the repository's instruction decoder.
    /// </summary>
    static IEnumerable<MemberInfo> ReferencedMembers(MethodBase method)
    {
        MethodBody? body = method.GetMethodBody();
        if (body?.GetILAsByteArray() is not byte[] il)
            yield break;

        Type[] typeArguments =
            method.DeclaringType is { IsGenericType: true } declaring
                ? declaring.GetGenericArguments()
                : [];
        Type[] methodArguments = method is MethodInfo { IsGenericMethod: true } generic
            ? generic.GetGenericArguments()
            : [];

        foreach (DecodedInstruction instruction in InstructionDecoder.Decode(il))
        {
            if (instruction.Operand
                is not (OperandKind.InlineMethod
                    or OperandKind.InlineField
                    or OperandKind.InlineTok))
            {
                continue;
            }

            MemberInfo? member = method.Module.ResolveMember(
                (int)instruction.OperandValue,
                typeArguments,
                methodArguments);
            if (member is not null)
                yield return member;
        }
    }

    /// <summary>
    /// A member of a borrowed input that admission must never touch: the
    /// assembly descriptor, its resolver, and the Analysis body index own
    /// every content, path, opening, and resolution capability.
    /// </summary>
    static bool IsBorrowedEvidence(MemberInfo member)
        => member.DeclaringType == typeof(ResolvedAssemblyReference)
            || member.DeclaringType == typeof(IAssemblyReferenceResolver)
            || member.DeclaringType == typeof(LibraryBodyIndex);

    static bool Describes(MethodBase method, string name)
        => method.DeclaringType == typeof(ResearchComparisonAdmission)
            && method.Name == name;

    static string Describe(MemberInfo member)
        => $"{member.DeclaringType?.Name}.{member.Name}";

    static int Distinct<T>(IEnumerable<T> values)
        where T : class
        => values.Distinct(ReferenceEqualityComparer.Instance).Count();

    const BindingFlags DeclaredMembers =
        BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

    static Assembly ResearchAssembly =>
        typeof(ResearchComparisonAdmission).Assembly;

    static bool IsAdmissionIdentity(Type type)
        => type == typeof(ResearchComparisonOperationId)
            || type == typeof(ResearchComparisonQuestionId)
            || type == typeof(ResearchComparisonInputId)
            || type == typeof(ResearchAdmittedPopulation)
            || type == typeof(ResearchAdmittedQuestion)
            || type == typeof(ResearchAdmittedInput);

    static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                yield return property.PropertyType;
                foreach (ParameterInfo parameter in property.GetIndexParameters())
                    yield return parameter.ParameterType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
            case MethodBase method:
                if (method is MethodInfo returning)
                    yield return returning.ReturnType;
                foreach (ParameterInfo parameter in method.GetParameters())
                    yield return parameter.ParameterType;
                break;
            case EventInfo @event when @event.EventHandlerType is Type handler:
                yield return handler;
                break;
            case Type nested:
                yield return nested;
                break;
        }
    }

    /// <summary>
    /// One signature type and every type it is built from: array and by-ref
    /// element types, pointer targets, and generic arguments, recursively.
    /// </summary>
    static IEnumerable<Type> ComponentTypes(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is Type element)
        {
            foreach (Type component in ComponentTypes(element))
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

    /// <summary>
    /// Every type reachable from one type's public surface: its properties,
    /// fields, constructors, methods, events, and nested types, unwrapped
    /// through <see cref="ComponentTypes"/> and followed transitively through
    /// every Research-owned type, implemented interface, and closed-hierarchy
    /// arm it reaches. Interfaces are walked whoever declares them, so an
    /// explicit interface implementation cannot hide an exposure.
    /// </summary>
    static IReadOnlyCollection<Type> PublicSurfaceClosure(Type root)
    {
        HashSet<Type> closure = [];
        HashSet<Type> walked = [];
        Queue<Type> pending = new();
        Enqueue(root);

        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();
            if (type != root && !IsResearchOwned(type) && !walked.Contains(type))
                continue;

            foreach (MemberInfo member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (Type signature in SignatureTypes(member))
                    Enqueue(signature);
            }

            // An implemented interface is part of the surface even when the
            // implementation is explicit and therefore not a public member.
            foreach (Type contract in type.GetInterfaces())
                Walk(contract);

            // A closed hierarchy is reachable by pattern match, so its arms
            // are part of the surface even when no signature names them.
            if (type.IsAbstract && !type.IsSealed)
            {
                foreach (Type arm in type.Assembly.GetTypes())
                {
                    if (arm != type && type.IsAssignableFrom(arm))
                        Enqueue(arm);
                }
            }
        }

        return closure;

        void Enqueue(Type type)
        {
            foreach (Type component in ComponentTypes(type))
            {
                if (closure.Add(component))
                    pending.Enqueue(component);
            }
        }

        void Walk(Type type)
        {
            Enqueue(type);
            if (walked.Add(type))
                pending.Enqueue(type);
        }
    }

    /// <summary>
    /// One type's exact declared public instance surface, spelled so that a
    /// new member, a changed member type, or a hidden carrier is a difference.
    /// Members inherited from <see cref="object"/> are not declared here and
    /// are therefore absent.
    /// </summary>
    static IReadOnlyCollection<string> DeclaredPublicInstanceSurface(Type type)
        =>
        [
            .. from member in type.GetMembers(
                   BindingFlags.Public
                       | BindingFlags.Instance
                       | BindingFlags.DeclaredOnly)
               select Spell(member),
        ];

    static string Spell(MemberInfo member)
        => member switch
        {
            PropertyInfo property =>
                $"property {property.Name} : {property.PropertyType.Name}",
            FieldInfo field => $"field {field.Name} : {field.FieldType.Name}",
            EventInfo @event =>
                $"event {@event.Name} : {@event.EventHandlerType?.Name}",
            ConstructorInfo constructor =>
                $"constructor ({Spell(constructor.GetParameters())})",
            MethodInfo method =>
                $"method {method.Name}({Spell(method.GetParameters())}) : "
                    + method.ReturnType.Name,
            _ => $"member {member.Name}",
        };

    static string Spell(ParameterInfo[] parameters)
        => string.Join(
            ", ",
            parameters.Select(parameter => parameter.ParameterType.Name));

    /// <summary>
    /// A member type that carries a value opaquely, so its declaration proves
    /// nothing about what it exposes.
    /// </summary>
    static bool IsOpaqueCarrier(Type type)
        => ComponentTypes(type).Any(static component =>
            component == typeof(object)
                || component == typeof(Array)
                || component == typeof(Delegate)
                || component == typeof(MulticastDelegate)
                || component == typeof(System.Collections.IEnumerable)
                || component == typeof(System.Collections.ICollection)
                || component == typeof(System.Collections.IList)
                || component == typeof(System.Collections.IDictionary));

    static bool IsResearchOwned(Type type)
        => type.Assembly == typeof(ResearchComparisonAdmission).Assembly;

    static bool IsImmutableState(Type type)
        => type.IsValueType
            || type == typeof(string)
            || IsResearchOwned(type)
            || IsGenericDefinition(type, typeof(FrozenDictionary<,>));

    static bool IsGenericDefinition(Type type, Type definition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == definition;

    static string Describe(ResearchAdmissionRejection rejection)
        => $"{rejection.Kind} {Describe(rejection.Location)}";

    static string Describe(ResearchAdmissionLocation location)
        => location switch
        {
            ResearchAdmissionLocation.Operation => "Operation",
            ResearchAdmissionLocation.Question question =>
                $"Question[{question.Index}]",
            ResearchAdmissionLocation.Input input =>
                $"Input[q={input.QuestionIndex},{input.Side},{input.Index}]",
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        };

    static ResearchAdmittedPopulation Admit(
        ResearchComparisonAdmissionRequest request)
        => Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(request)).Population;

    static ResearchComparisonAdmissionRequest Request(
        ResearchComparisonProfile profile,
        params ResearchComparisonAdmissionQuestion?[] questions)
        => new(profile, questions);

    static ResearchComparisonAdmissionQuestion Question(
        ResearchComparisonInputOccurrence?[] before,
        ResearchComparisonInputOccurrence?[] after)
        => new(before, after);

    static ResearchComparisonInputOccurrence Occurrence(
        ResearchComparisonProfile profile)
        => profile switch
        {
            ResearchComparisonProfile.ImplementationComparison =>
                ImplementationOccurrence(),
            ResearchComparisonProfile.BodySignal => BodySignalOccurrence(),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };

    static ImplementationComparisonInputOccurrence ImplementationOccurrence()
        => new(ImplementationInput());

    /// <summary>
    /// An implementation occurrence whose borrowed input omits exactly one
    /// member. Nested evidence is not construction-validated, so this shape
    /// reaches admission and becomes a typed rejection.
    /// </summary>
    static ImplementationComparisonInputOccurrence Incomplete(
        bool assembly = true,
        bool resolver = true,
        bool bodyIndex = true)
        => new(
            new ImplementationAssemblyInput(
                assembly ? PathlessAssembly() : null!,
                resolver ? new UnusedResolver() : null!,
                bodyIndex ? BodyIndex() : null!));

    static BodySignalComparisonInputOccurrence BodySignalOccurrence()
        => new(BodyIndex());

    static ImplementationAssemblyInput ImplementationInput()
        => new(PathlessAssembly(), new UnusedResolver(), BodyIndex());

    static LibraryBodyIndex BodyIndex()
        => LibraryBodyIndex.FromEvidence(
            [],
            [],
            moduleIdentity: new(
                new AssemblyReferenceIdentity(
                    "SyntheticAdmission",
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null),
                new Guid("f88df8d2-0474-4f48-811a-bf5cb2af203e")));

    static ResolvedAssemblyReference PathlessAssembly()
        => ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "AdmissionFixture",
                new Version(1, 0, 0, 0),
                null,
                null),
            path: null,
            openRead: static () => throw new InvalidOperationException(
                "Admission must not open a borrowed assembly."),
            provenance: AssemblyResolutionProvenance.Project(
                "AdmissionFixture",
                tfm: null,
                rid: null));

    /// <summary>
    /// Every indirect shape through which a rejection surface could hide an
    /// admitted identity. The no-partial-population walk must report all of
    /// them, so this probe keeps that walk non-vacuous.
    /// </summary>
    sealed class IdentityExposureProbe
    {
        public Dictionary<string, ResearchAdmittedPopulation> Map = null!;

        IdentityExposureProbe()
        {
        }

        public ResearchAdmittedInput[] Array => null!;

        public IReadOnlyList<ResearchAdmittedQuestion> List => null!;

        public ResearchComparisonInputId Method() => null!;

        public bool TryGet(out ResearchComparisonOperationId operation)
        {
            operation = null!;
            return false;
        }

        public void Accept(ImmutableArray<ResearchAdmittedInput> inputs) => _ = inputs;
    }

    /// <summary>
    /// One admitted-identity exposure hidden behind an explicit interface
    /// implementation, which declares no public member. The rejected-surface
    /// walk must still report it, through the implemented interface.
    /// </summary>
    interface IIdentityCarrier
    {
        ResearchAdmittedPopulation Population { get; }
    }

    sealed class ExplicitInterfaceExposureProbe : IIdentityCarrier
    {
        ExplicitInterfaceExposureProbe()
        {
        }

        ResearchAdmittedPopulation IIdentityCarrier.Population => null!;
    }

    sealed class UnusedResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => throw new InvalidOperationException(
                "Admission must not resolve a borrowed reference.");
    }

    /// <summary>
    /// Every borrowed-evidence access admission must never perform: opening,
    /// path reads on both borrowed owners, reference resolution, and
    /// body-index content reads and iteration. The IL walk must report all of
    /// them, so this probe keeps that walk non-vacuous.
    /// </summary>
    static class BorrowedEvidenceProbe
    {
        public static Stream Open(ResolvedAssemblyReference assembly)
            => assembly.OpenRead();

        public static string? ReadAssemblyPath(ResolvedAssemblyReference assembly)
            => assembly.Path;

        public static ResolvedAssemblyReference? Resolve(
            IAssemblyReferenceResolver resolver)
            => resolver.Resolve(
                new AssemblyReferenceIdentity("Probe", null, null, null),
                AssemblyResolutionScope.Any);

        public static string ReadBodyIndexPath(LibraryBodyIndex bodyIndex)
            => bodyIndex.Path;

        public static int ReadMethods(LibraryBodyIndex bodyIndex)
            => bodyIndex.Methods.Length;

        public static int IterateDirectCalls(LibraryBodyIndex bodyIndex)
        {
            int count = 0;
            foreach (DirectCall call in bodyIndex.DirectCalls)
                count += call.Callee.Name.Length;

            return count;
        }

        public static int ReadMethodSignals(LibraryBodyIndex bodyIndex)
            => bodyIndex.GetMethodSignals().Count;
    }

    sealed class AdmissionFixture
    {
        AdmissionFixture(
            ResearchComparisonAdmissionRequest request,
            IReadOnlyList<ResearchComparisonAdmissionQuestion> questions,
            IReadOnlyList<(ResearchComparisonInputOccurrence Occurrence,
                int QuestionIndex,
                ResearchComparisonSide Side)> occurrences)
        {
            Request = request;
            Questions = questions;
            Occurrences = occurrences;
        }

        public ResearchComparisonAdmissionRequest Request { get; }

        public IReadOnlyList<ResearchComparisonAdmissionQuestion> Questions { get; }

        public IReadOnlyList<(ResearchComparisonInputOccurrence Occurrence,
            int QuestionIndex,
            ResearchComparisonSide Side)> Occurrences { get; }

        /// <summary>
        /// Two questions whose second question repeats the first question's
        /// borrowed input value in a separate occurrence.
        /// </summary>
        public static AdmissionFixture TwoQuestions(
            ResearchComparisonProfile profile)
        {
            ResearchComparisonInputOccurrence firstBefore = Occurrence(profile);
            ResearchComparisonInputOccurrence firstAfter = Occurrence(profile);
            ResearchComparisonInputOccurrence secondBefore = Repeat(firstBefore);
            ResearchComparisonInputOccurrence secondAfterOne = Occurrence(profile);
            ResearchComparisonInputOccurrence secondAfterTwo = Occurrence(profile);

            ResearchComparisonAdmissionQuestion first =
                Question([firstBefore], [firstAfter]);
            ResearchComparisonAdmissionQuestion second =
                Question([secondBefore], [secondAfterOne, secondAfterTwo]);

            return new AdmissionFixture(
                Request(profile, first, second),
                [first, second],
                [
                    (firstBefore, 0, ResearchComparisonSide.Before),
                    (firstAfter, 0, ResearchComparisonSide.After),
                    (secondBefore, 1, ResearchComparisonSide.Before),
                    (secondAfterOne, 1, ResearchComparisonSide.After),
                    (secondAfterTwo, 1, ResearchComparisonSide.After),
                ]);
        }

        static ResearchComparisonInputOccurrence Repeat(
            ResearchComparisonInputOccurrence occurrence)
            => occurrence switch
            {
                ImplementationComparisonInputOccurrence implementation =>
                    new ImplementationComparisonInputOccurrence(
                        implementation.Input),
                BodySignalComparisonInputOccurrence bodySignal =>
                    new BodySignalComparisonInputOccurrence(
                        bodySignal.BodyIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(occurrence)),
            };
    }
}
