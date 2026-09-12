using System.Collections.Immutable;
using InertText;

namespace ILInspector.Analysis.Tests;

public class ResourceEffectLanguageTests
{
    public static TheoryData<string> Version1Statements { get; } = new()
    {
        "resource(kind=example.resource<type[0]>,value=declared-type)",
        "authority(kind=example.authority<type[0]>,target=return,key=singleton[type[0]])",
        "authority(kind=example.authority,target=receiver,key=value)",
        "authority(kind=example.authority,target=return,key=singleton[])",
        "acquire(kind=example.resource<type[0]>,target=return,when=normal-return,correspondence=receiver,lender=parameter[0])",
        "move(kind=example.resource<type[0]>,source=operation[0],target=return,when=outcome[accepted])",
        "consume(kind=example.resource<type[0]>,source=parameter[0],target=operation[0])",
        "release(kind=example.resource<type[0]>,source=receiver,when=successful-await,observation=return,correspondence=parameter[0])",
        "borrow(kind=example.resource<type[0]>,source=receiver,target=callback[1].parameter[0],access=read,scope=callback[1],lender=parameter[0],materialization=none)",
        "derive(source=parameter[0],target=constructed,relation=alias,guard=exact-type[parameter[0];signature-parameter[0]])",
        "pass(source=callback[1].return,target=return,identity=preserve)",
        "independent(source=receiver,target=callback[1].return)",
        "callback(delegate=parameter[1],scope=callback[1],execution=synchronous,cardinality=exactly-once)",
        "accept(kind=example.resource<type[0]>,source=operation[0],target=receiver.field[child],when=outcome[accepted],order=first)",
        "operation(boundary=transparent,throws=never,guard=exact-type[parameter[0];signature-parameter[0]])",
        "outcome(id=accepted,source=return,test=enum[Example.Accepted])",
    };

    [Theory]
    [MemberData(nameof(Version1Statements))]
    public void Parser_AcceptsEveryVersion1StatementForm(string statement)
    {
        Assert.IsType<ResourceEffectParseOutcome.Parsed>(
            ResourceEffectStatementParser.Parse(statement));
    }

    [Fact]
    public void Parser_RejectsUnknownDuplicateMissingAndTrailingTermsAtExactOffsets()
    {
        AssertRejected(
            "pass(source=receiver,mystery=return,target=return)",
            ResourceEffectDiagnosticKind.UnknownArgument,
            "mystery");
        AssertRejected(
            "pass(source=receiver,source=return,target=return)",
            ResourceEffectDiagnosticKind.DuplicateArgument,
            "source",
            occurrence: 2);

        var missing = Assert.IsType<ResourceEffectParseOutcome.Rejected>(
            ResourceEffectStatementParser.Parse("pass(source=receiver)"));
        Assert.Equal(ResourceEffectDiagnosticKind.MissingArgument, missing.Diagnostic.Kind);
        Assert.Equal("pass(source=receiver)".Length, missing.Diagnostic.Offset);

        const string trailing = "pass(source=receiver,target=return) trailing";
        var trailingResult = Assert.IsType<ResourceEffectParseOutcome.Rejected>(
            ResourceEffectStatementParser.Parse(trailing));
        Assert.Equal(ResourceEffectDiagnosticKind.TrailingText, trailingResult.Diagnostic.Kind);
        Assert.Equal(trailing.IndexOf("trailing", StringComparison.Ordinal), trailingResult.Diagnostic.Offset);
        Assert.NotEmpty(trailingResult.Diagnostic.Message.ToString());
    }

    [Theory]
    [InlineData("borrow(source=receiver,target=return,access=shared,scope=call)")]
    [InlineData("derive(source=receiver,target=return,relation=copy)")]
    [InlineData("pass(source=receiver,target=return,identity=copy)")]
    [InlineData("callback(delegate=parameter[0],scope=callback[0],execution=async,cardinality=exactly-once)")]
    [InlineData("operation(boundary=opaque,throws=never)")]
    [InlineData("outcome(id=value,source=return,test=bool[maybe])")]
    [InlineData("release(source=receiver,when=successful-await)")]
    [InlineData("release(source=receiver,when=normal-return,observation=return)")]
    [InlineData("release(source=receiver,when=successful-await,observation=parameter[0])")]
    public void Parser_RejectsValuesOutsideFiniteDomains(string statement)
    {
        Assert.IsType<ResourceEffectParseOutcome.Rejected>(
            ResourceEffectStatementParser.Parse(statement));
    }

    [Theory]
    [InlineData("bool[true]")]
    [InlineData("bool[false]")]
    [InlineData("enum[Example.Accepted]")]
    [InlineData("null")]
    [InlineData("non-null")]
    [InlineData("type[Example.Accepted]")]
    public void Parser_AcceptsEveryVersion1OutcomeTest(string test)
    {
        Assert.IsType<ResourceEffectParseOutcome.Parsed>(
            ResourceEffectStatementParser.Parse(
                $"outcome(id=value,source=return,test={test})"));
    }

    [Fact]
    public void Parser_BudgetsSucceedAtExactUseAndFailOneUnder()
    {
        const string statement =
            "borrow(kind=example.resource<type[0]>,source=receiver,target=callback[1].parameter[0],access=read,scope=callback[1],materialization=none)";
        var initial = Assert.IsType<ResourceEffectParseOutcome.Parsed>(
            ResourceEffectStatementParser.Parse(statement));

        ResourceEffectWorkLimits Exact(
            int? characters = null,
            int? tokens = null,
            int? nesting = null,
            int? arguments = null)
            => new(
                maxStatementCharacters: characters ?? initial.Receipt.CharacterCount,
                maxStatementTokens: tokens ?? initial.Receipt.TokenCount,
                maxNestingDepth: nesting ?? initial.Receipt.NestingDepth,
                maxStatementArguments: arguments ?? initial.Receipt.ArgumentCount);

        Assert.IsType<ResourceEffectParseOutcome.Parsed>(
            ResourceEffectStatementParser.Parse(statement, Exact()));
        AssertLimit(
            ResourceEffectStatementParser.Parse(
                statement,
                Exact(characters: initial.Receipt.CharacterCount - 1)),
            ResourceEffectWorkLimitKind.StatementCharacters);
        AssertLimit(
            ResourceEffectStatementParser.Parse(
                statement,
                Exact(tokens: initial.Receipt.TokenCount - 1)),
            ResourceEffectWorkLimitKind.StatementTokens);
        AssertLimit(
            ResourceEffectStatementParser.Parse(
                statement,
                Exact(nesting: initial.Receipt.NestingDepth - 1)),
            ResourceEffectWorkLimitKind.NestingDepth);
        AssertLimit(
            ResourceEffectStatementParser.Parse(
                statement,
                Exact(arguments: initial.Receipt.ArgumentCount - 1)),
            ResourceEffectWorkLimitKind.StatementArguments);
    }

    [Fact]
    public void Admission_CompleteSnapshotWitnessRetainsPurposePreservingTerms()
    {
        ResourceEffectModelIdentity model = new("example.snapshot");
        ResourceEffectTargetSelector target = SnapshotTarget();
        string[] statements =
        [
            "callback(delegate=parameter[1],scope=callback[1],execution=synchronous,cardinality=exactly-once)",
            "borrow(source=receiver,target=callback[1].parameter[0],access=read,scope=callback[1],materialization=none)",
            "pass(source=parameter[0],target=callback[1].parameter[1])",
            "independent(source=receiver,target=callback[1].return)",
            "pass(source=callback[1].return,target=return,identity=preserve)",
            "move(source=callback[1].return,target=return,when=normal-return)",
        ];

        AdmittedResourceEffectModel admitted = SingleModel(
            Build(
                Model(model, target, statements)));

        var borrow = Assert.IsType<ResourceEffect.Borrow>(
            admitted.Declarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Borrow).Effect);
        Assert.Equal(ResourceBorrowMaterialization.None, borrow.Materialization);
        var pass = Assert.IsType<ResourceEffect.Pass>(
            admitted.Declarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Pass
                {
                    Identity: ResourcePassIdentity.Preserve,
                }).Effect);
        Assert.Equal(ResourcePassIdentity.Preserve, pass.Identity);
        Assert.Equal(statements.Length, admitted.Declarations.Length);
    }

    [Fact]
    public void Admission_SnapshotWitnessIsNonVacuousWhenCallbackDeclarationIsRemoved()
    {
        ResourceEffectModelIdentity model = new("example.snapshot");
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SnapshotTarget(),
                    [
                        "borrow(source=receiver,target=callback[1].parameter[0],access=read,scope=callback[1],materialization=none)",
                        "pass(source=callback[1].return,target=return,identity=preserve)",
                    ]),
            ]);

        var rejected = Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome);
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedCallback,
            rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_RejectsAtomicModelWhenOneStatementIsMalformed()
    {
        ResourceEffectModelIdentity model = new("example.atomic");
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "pass(source=parameter[0],target=return)",
                        "pass(source=parameter[0],target=return,unknown=value)",
                    ]),
            ]);

        var rejected = Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome);
        Assert.Equal(ResourceEffectDiagnosticKind.UnknownArgument, rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_RejectsUnknownLanguageAtomically()
    {
        ResourceEffectModelIdentity model = new("example.unknown-language");
        var definition = new ResourceEffectModelDefinition(
            new ResourceEffectLanguageIdentity("resource-effects/2"),
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    SimpleOperationTarget(),
                    [Source(model, model.Value, 0, "pass(source=parameter[0],target=return)")]),
            ]);

        var rejected = Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(
            ResourceEffectAdmissionBuilder.Admit([definition]));
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnknownLanguage,
            rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_CoalescesSemanticEqualityAndRetainsEveryProvenance()
    {
        ResourceEffectModelIdentity model = new("example.coalescing");
        ResourceEffectTargetSelector target = SimpleOperationTarget();
        ResourceEffectAdmission admission = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [],
                [
                    new ResourceEffectTargetDeclaration(
                        target,
                        [
                            Source(
                                model,
                                "source.one",
                                0,
                                "pass(source=parameter[0],target=return)"),
                            Source(
                                model,
                                "source.two",
                                1,
                                "pass( target = return , source = parameter[0] )"),
                        ]),
                ],
                [
                    new ResourceEffectTypedDeclaration(
                        target,
                        new ResourceEffect.Pass(
                            new ResourceEffectLocation.Parameter(0),
                            new ResourceEffectLocation.Return(),
                            null),
                        [
                            new ResourceDeclarationProvenance(
                                model,
                                ResourceDeclarationAuthority.ProductShipped,
                                new InertString(TextPolicy.Field, "source.three"),
                                2),
                        ]),
                ]));

        AdmittedResourceEffectDeclaration declaration =
            Assert.Single(SingleModel(admission).Declarations);
        Assert.Equal(3, declaration.Provenances.Length);
        Assert.Equal(
            ["source.one", "source.two", "source.three"],
            declaration.Provenances.Select(provenance => provenance.SourceIdentity.ToString()));
    }

    [Fact]
    public void Admission_ReceiptsAreDeterministicAndPreserveInputOrder()
    {
        ResourceEffectModelDefinition first = Model(
            new ResourceEffectModelIdentity("example.first"),
            SimpleOperationTarget(),
            [
                "pass(source=parameter[0],target=return)",
                "independent(source=receiver,target=return)",
            ]);
        ResourceEffectModelDefinition second = Model(
            new ResourceEffectModelIdentity("example.second"),
            SimpleOperationTarget(),
            ["derive(source=parameter[0],target=return,relation=same-value)"]);

        ResourceEffectAdmission left = Build(first, second);
        ResourceEffectAdmission repeated = Build(first, second);
        ResourceEffectAdmission right = Build(second, first);

        Assert.Equal(left.Receipt, repeated.Receipt);
        Assert.NotEqual(left.Receipt.ContentHash, right.Receipt.ContentHash);
        Assert.Equal(
            [first.Identity, second.Identity],
            left.Models.Select(model => model.Identity));
        Assert.Equal(
            [second.Identity, first.Identity],
            right.Models.Select(model => model.Identity));
        Assert.All(left.Receipt.Models, receipt => Assert.Equal(64, receipt.ContentHash.Length));
    }

    [Fact]
    public void Admission_PreservesSharedResourceKindsInIndependentModels()
    {
        ResourceEffectModelIdentity first = new("example.kind-first");
        ResourceEffectModelIdentity second = new("example.kind-second");

        ResourceEffectAdmission admission = Build(
            Model(
                first,
                SimpleOperationTarget(),
                [],
                [Kind(first, "example.shared-kind", 0)]),
            Model(
                second,
                SimpleOperationTarget(),
                [],
                [Kind(second, "example.shared-kind", 0)]));

        Assert.Equal(2, admission.Models.Length);
        Assert.All(
            admission.Models,
            model =>
            {
                ResourceKindDefinition kind = Assert.Single(model.ResourceKinds);
                Assert.Equal(new ResourceKindIdentity("example.shared-kind"), kind.Identity);
                Assert.Single(kind.Provenances);
                Assert.Equal(model.Identity, kind.Provenances[0].Model);
            });
    }

    [Fact]
    public void Admission_AdmitsArrayPoolAndOwnershipModelsTogether()
    {
        ResourceEffectModelIdentity arrayPool = new("dotnet.array-pool");
        ResourceEffectModelIdentity ownership = new("dotnet-inspect.ownership");

        ResourceEffectAdmission admission = Build(
            Model(
                arrayPool,
                OpenGenericOperationTarget(),
                [
                    "acquire(kind=dotnet.array-pool-rental<type[0]>,target=return,when=normal-return)",
                ],
                [Kind(arrayPool, "dotnet.array-pool-rental", 1)]),
            Model(
                ownership,
                SimpleOperationTarget(),
                [
                    "borrow(kind=dotnet-inspect.assembly-session,source=receiver,target=return,access=read,scope=call)",
                ],
                [Kind(ownership, "dotnet-inspect.assembly-session", 0)]));

        Assert.Equal(
            [arrayPool, ownership],
            admission.Models.Select(model => model.Identity));
        Assert.All(admission.Models, model => Assert.Single(model.Declarations));
        Assert.Equal(2, admission.Receipt.Models.Length);
    }

    [Fact]
    public void Admission_SeparatesSemanticContentFromExactProvenance()
    {
        ResourceEffectModelIdentity model = new("example.provenance-receipt");
        ResourceEffectTargetSelector target = SimpleOperationTarget();
        ResourceEffectAdmission first = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [],
                [
                    new ResourceEffectTargetDeclaration(
                        target,
                        [
                            Source(
                                model,
                                "source.one",
                                0,
                                "pass(source=parameter[0],target=return)"),
                        ]),
                ]));
        ResourceEffectAdmission second = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [],
                [
                    new ResourceEffectTargetDeclaration(
                        target,
                        [
                            Source(
                                model,
                                "source.two",
                                0,
                                "pass(source=parameter[0],target=return)"),
                        ]),
                ]));

        Assert.Equal(
            first.Receipt.Models.Single().ContentHash,
            second.Receipt.Models.Single().ContentHash);
        Assert.NotEqual(first.Receipt.ContentHash, second.Receipt.ContentHash);
        Assert.Equal(
            "source.one",
            first.Receipt.Models.Single().Provenances.Single().SourceIdentity.ToString());
        Assert.Equal(
            "source.two",
            second.Receipt.Models.Single().Provenances.Single().SourceIdentity.ToString());
    }

    [Fact]
    public void Admission_ModelBudgetsSucceedAtExactUseAndFailOneUnder()
    {
        ResourceEffectModelDefinition model = Model(
            new ResourceEffectModelIdentity("example.budget"),
            SimpleOperationTarget(),
            [
                "pass(source=parameter[0],target=return)",
                "independent(source=receiver,target=return)",
            ]);
        var exact = new ResourceEffectWorkLimits(
            maxModels: 1,
            maxDeclarationsPerModel: 1,
            maxStatementsPerModel: 2,
            maxAdmissionStatements: 2);
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit([model], exact));

        AssertLimit(
            ResourceEffectAdmissionBuilder.Admit(
                [model],
                new ResourceEffectWorkLimits(maxStatementsPerModel: 1)),
            ResourceEffectWorkLimitKind.ModelStatements);
        AssertLimit(
            ResourceEffectAdmissionBuilder.Admit(
                [model],
                new ResourceEffectWorkLimits(maxAdmissionStatements: 1)),
            ResourceEffectWorkLimitKind.AdmissionStatements);

        ResourceEffectModelIdentity identity = new("example.declaration-budget");
        var twoDeclarations = new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            identity,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    SimpleOperationTarget(),
                    [Source(identity, identity.Value, 0, "pass(source=parameter[0],target=return)")]),
                new ResourceEffectTargetDeclaration(
                    TwoParameterOperationTarget(),
                    [Source(identity, identity.Value, 1, "pass(source=parameter[0],target=return)")]),
            ]);
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [twoDeclarations],
                new ResourceEffectWorkLimits(maxDeclarationsPerModel: 2)));
        AssertLimit(
            ResourceEffectAdmissionBuilder.Admit(
                [twoDeclarations],
                new ResourceEffectWorkLimits(maxDeclarationsPerModel: 1)),
            ResourceEffectWorkLimitKind.ModelDeclarations);

        ResourceEffectModelDefinition secondModel = Model(
            new ResourceEffectModelIdentity("example.second-budget-model"),
            SimpleOperationTarget(),
            ["pass(source=parameter[0],target=return)"]);
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [model, secondModel],
                new ResourceEffectWorkLimits(maxModels: 2)));
        AssertLimit(
            ResourceEffectAdmissionBuilder.Admit(
                [model, secondModel],
                new ResourceEffectWorkLimits(maxModels: 1)),
            ResourceEffectWorkLimitKind.Models);
    }

    [Fact]
    public void Admission_ResolvesModelLocalFieldSelectors()
    {
        ResourceEffectModelIdentity model = new("example.fields");
        var definition = new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    FieldTarget("Child"),
                    [
                        Source(
                            model,
                            model.Value,
                            0,
                            "resource(kind=example.child,value=declared-field,selector=child)"),
                    ]),
                new ResourceEffectTargetDeclaration(
                    SimpleOperationTarget(),
                    [
                        Source(
                            model,
                            model.Value,
                            1,
                            "pass(source=receiver.field[child],target=return)"),
                    ]),
            ]);

        AdmittedResourceEffectModel admitted = SingleModel(Build(definition));

        Assert.Contains(
            admitted.Declarations,
            declaration => declaration.Effect is ResourceEffect.Pass
            {
                Source: ResourceEffectLocation.StructuralField,
            });
        Assert.Contains(
            admitted.ResourceKinds,
            kind => kind.Identity == new ResourceKindIdentity("example.child")
                && kind.Arity == 0);
    }

    [Fact]
    public void Admission_ResolvesTypedModelLocalFieldSelectors()
    {
        ResourceEffectModelIdentity model = new("example.typed-fields");
        var selector = new ResourceEffectLocalIdentity("child");
        ResourceDeclarationProvenance fieldProvenance =
            Provenance(model, "typed.field", 0);
        ResourceDeclarationProvenance passProvenance =
            Provenance(model, "typed.pass", 1);
        ResourceEffectAdmission admission = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [],
                [],
                [
                    new ResourceEffectTypedDeclaration(
                        FieldTarget("Child"),
                        new ResourceEffect.Resource(
                            new ResourceKindReference(
                                new ResourceKindIdentity("example.child")),
                            ResourceDeclaredValueKind.DeclaredField,
                            selector),
                        [fieldProvenance]),
                    new ResourceEffectTypedDeclaration(
                        SimpleOperationTarget(),
                        new ResourceEffect.Pass(
                            new ResourceEffectLocation.Field(
                                new ResourceEffectLocation.Receiver(),
                                selector),
                            new ResourceEffectLocation.Return(),
                            null),
                        [passProvenance]),
                ]));

        Assert.Contains(
            SingleModel(admission).Declarations,
            declaration => declaration.Effect is ResourceEffect.Pass
            {
                Source: ResourceEffectLocation.StructuralField,
            });
    }

    [Theory]
    [InlineData(
        "move(source=parameter[0],target=return,when=outcome[missing])",
        ResourceEffectDiagnosticKind.UnresolvedOutcome)]
    [InlineData(
        "pass(source=callback[0].return,target=return)",
        ResourceEffectDiagnosticKind.UnresolvedCallback)]
    [InlineData(
        "move(source=operation[0],target=return,when=normal-return)",
        ResourceEffectDiagnosticKind.UnresolvedOperation)]
    [InlineData(
        "pass(source=receiver.field[missing],target=return)",
        ResourceEffectDiagnosticKind.UnresolvedField)]
    public void Admission_RejectsUnresolvedModelLocalReferences(
        string statement,
        ResourceEffectDiagnosticKind expected)
    {
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    new ResourceEffectModelIdentity("example.references"),
                    SimpleOperationTarget(),
                    [statement]),
            ]);

        var rejected = Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome);
        Assert.Equal(expected, rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_AllowsExternalKindsAndRejectsLocalArityMismatch()
    {
        ResourceEffectModelIdentity model = new("example.resources");
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    ["release(kind=example.missing,source=receiver,when=normal-return)"]),
            ]));

        ResourceEffectModelIdentity independent = new("example.independent");
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [],
                        [Kind(model, "example.shared", 1)]),
                    Model(
                        independent,
                        SimpleOperationTarget(),
                        ["release(kind=example.shared,source=receiver,when=normal-return)"]),
                ]));

        ResourceEffectAdmissionOutcome mismatch = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    ["release(kind=example.shared,source=receiver,when=normal-return)"],
                    [Kind(model, "example.shared", 1)]),
            ]);
        var arityRejected =
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(mismatch);
        Assert.Equal(2, arityRejected.Diagnostics.Length);
        Assert.All(
            arityRejected.Diagnostics,
            diagnostic => Assert.Equal(
                ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                diagnostic.Diagnostic.Kind));
    }

    [Fact]
    public void Admission_RejectsUnboundGenericVariables()
    {
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    new ResourceEffectModelIdentity("example.variables"),
                    SimpleOperationTarget(),
                    ["acquire(kind=example.resource<type[0]>,target=return,when=normal-return)"],
                    [Kind(new ResourceEffectModelIdentity("example.variables"), "example.resource", 1)]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.UnboundGenericVariable,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_RejectsInconsistentClosedDeclaringTypeVariables()
    {
        ResourceEffectModelIdentity model = new("example.closed-variable");
        var closedOwner = new ResourceTypeExpression.Named(
            new ResourceAssemblySelector(
                "Example",
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Example",
            [new ResourceTypeNameSegment("Owner", 1)],
            [Named("String")]);
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Member(
                new ResourceEffectMemberSelector(
                    closedOwner,
                    "Acquire",
                    ResourceEffectMemberKind.Method,
                    isStatic: false,
                    genericArity: 0,
                    ResourceEffectCallingConvention.Default,
                    hasThis: true,
                    explicitThis: false,
                    [],
                    Named("Object")));

        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    target,
                    ["acquire(kind=example.resource<type[0]>,target=return,when=normal-return)"],
                    [Kind(model, "example.resource", 1)]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.InconsistentGenericVariable,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void StructuralSelectorsUseImmutableValueEquality()
    {
        ResourceEffectTargetSelector first = TwoDifferentParameterOperationTarget();
        ResourceEffectTargetSelector second = TwoDifferentParameterOperationTarget();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Admission_ResolvesOutcomeIdsWithinEachAtomicModel()
    {
        ResourceEffectModelIdentity releaseModel = new("example.outcome-release");
        ResourceEffectModelIdentity moveModel = new("example.outcome-move");

        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    releaseModel,
                    SimpleOperationTarget(),
                    [
                        "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                        "outcome(id=done,source=return,test=bool[true])",
                        "release(kind=example.resource,source=operation[0],when=outcome[done])",
                    ],
                    [Kind(releaseModel, "example.resource", 0)]),
                Model(
                    moveModel,
                    SimpleOperationTarget(),
                    [
                        "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                        "outcome(id=done,source=return,test=bool[false])",
                        "move(kind=example.resource,source=operation[0],target=return,when=outcome[done])",
                    ],
                    [Kind(moveModel, "example.resource", 0)]),
            ]);

        ResourceEffectAdmission admission =
            Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;
        AdmittedResourceEffectModel admittedRelease =
            Assert.Single(admission.Models, model => model.Identity == releaseModel);
        AdmittedResourceEffectModel admittedMove =
            Assert.Single(admission.Models, model => model.Identity == moveModel);
        Assert.Contains(
            admittedRelease.Declarations,
            declaration => declaration.Effect is ResourceEffect.Release
            {
                When: ResourceEffectCompletion.OutcomeCase
                {
                    Test: ResourceEffectOutcomeTest.Boolean { Value: true },
                },
            });
        Assert.Contains(
            admittedMove.Declarations,
            declaration => declaration.Effect is ResourceEffect.Move
            {
                When: ResourceEffectCompletion.OutcomeCase
                {
                    Test: ResourceEffectOutcomeTest.Boolean { Value: false },
                },
            });
    }

    [Fact]
    public void Admission_AdmitsLatentCrossModelFieldConflict()
    {
        ResourceEffectModelIdentity releaseModel = new("example.field-release");
        ResourceEffectModelIdentity moveModel = new("example.field-move");

        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                FieldTerminalModel(
                    releaseModel,
                    "Child",
                    "release-child",
                    "release(kind=example.field-resource,source=receiver.field[release-child],when=normal-return)"),
                FieldTerminalModel(
                    moveModel,
                    "Child",
                    "move-child",
                    "move(kind=example.field-resource,source=receiver.field[move-child],target=return,when=normal-return)"),
            ]);

        ResourceEffectAdmission admission =
            Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;
        Assert.Equal(2, admission.Models.Length);
        Assert.Equal(
            admission.Models[0].Declarations
                .Select(declaration => declaration.Effect)
                .OfType<ResourceEffect.Release>()
                .Single()
                .Source,
            admission.Models[1].Declarations
                .Select(declaration => declaration.Effect)
                .OfType<ResourceEffect.Move>()
                .Single()
                .Source);
        Assert.Equal(2, admission.Receipt.Models.Length);
    }

    [Fact]
    public void Admission_ScopesSameLocalFieldNameToEachModel()
    {
        ResourceEffectModelIdentity releaseModel = new("example.field-one");
        ResourceEffectModelIdentity moveModel = new("example.field-two");

        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                FieldTerminalModel(
                    releaseModel,
                    "FirstChild",
                    "child",
                    "release(kind=example.field-resource,source=receiver.field[child],when=normal-return)"),
                FieldTerminalModel(
                    moveModel,
                    "SecondChild",
                    "child",
                    "move(kind=example.field-resource,source=receiver.field[child],target=return,when=normal-return)"),
            ]);

        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(outcome);
    }

    [Fact]
    public void Admission_RejectsTypedReleaseObservationInvariantViolations()
    {
        AssertTypedRejected(
            "example.typed-await-missing",
            new ResourceEffect.Release(
                new ResourceEffectLocation.Receiver(),
                new ResourceEffectCompletion.SuccessfulAwait(),
                null,
                null,
                null));
        AssertTypedRejected(
            "example.typed-await-location",
            new ResourceEffect.Release(
                new ResourceEffectLocation.Receiver(),
                new ResourceEffectCompletion.SuccessfulAwait(),
                null,
                null,
                new ResourceEffectLocation.Parameter(0)));
        AssertTypedRejected(
            "example.typed-normal-observation",
            new ResourceEffect.Release(
                new ResourceEffectLocation.Receiver(),
                new ResourceEffectCompletion.NormalReturn(),
                null,
                null,
                new ResourceEffectLocation.Return()));
    }

    [Fact]
    public void Admission_RejectsUndefinedTypedEffectEnumValues()
    {
        ResourceKindReference kind =
            new(new ResourceKindIdentity("example.typed-resource"));
        ResourceEffectTargetSelector typeTarget =
            new ResourceEffectTargetSelector.Type(Named("Resource"));
        (string Id, ResourceEffect Effect, ResourceEffectTargetSelector? Target)[] cases =
        [
            (
                "resource-value",
                new ResourceEffect.Resource(
                    kind,
                    (ResourceDeclaredValueKind)999,
                    null),
                typeTarget),
            (
                "borrow-access",
                new ResourceEffect.Borrow(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectLocation.Return(),
                    (ResourceBorrowAccess)999,
                    new ResourceBorrowScope.Call(),
                    null,
                    null,
                    null),
                null),
            (
                "borrow-materialization",
                new ResourceEffect.Borrow(
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectLocation.Return(),
                    ResourceBorrowAccess.Read,
                    new ResourceBorrowScope.Call(),
                    null,
                    null,
                    (ResourceBorrowMaterialization)999),
                null),
            (
                "derive-relation",
                new ResourceEffect.Derive(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceEffectLocation.Return(),
                    (ResourceDerivationRelation)999,
                    null),
                null),
            (
                "pass-identity",
                new ResourceEffect.Pass(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceEffectLocation.Return(),
                    (ResourcePassIdentity)999),
                null),
            (
                "callback-execution",
                new ResourceEffect.Callback(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceBorrowScope.Callback(0),
                    (ResourceCallbackExecution)999,
                    ResourceCallbackCardinality.ExactlyOnce),
                null),
            (
                "callback-cardinality",
                new ResourceEffect.Callback(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceBorrowScope.Callback(0),
                    ResourceCallbackExecution.Synchronous,
                    (ResourceCallbackCardinality)999),
                null),
            (
                "operation-boundary",
                new ResourceEffect.Operation(
                    (ResourceOperationBoundary)999,
                    ResourceOperationThrows.Never,
                    null),
                null),
            (
                "operation-throws",
                new ResourceEffect.Operation(
                    ResourceOperationBoundary.Transparent,
                    (ResourceOperationThrows)999,
                    null),
                null),
        ];

        foreach ((string id, ResourceEffect effect, ResourceEffectTargetSelector? target) in cases)
        {
            AssertTypedRejected(
                "example." + id,
                effect,
                target ?? SimpleOperationTarget());
        }
    }

    [Fact]
    public void TypedStructuralSelectorsRejectUndefinedOrInconsistentShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResourceEffectParameterSelector(
                Named("Object"),
                (ResourceEffectRefKind)999));
        Assert.Throws<ArgumentException>(
            () => MemberSelector(
                ResourceEffectMemberKind.Method,
                isStatic: true,
                hasThis: true,
                explicitThis: false,
                ResourceEffectCallingConvention.Default));
        Assert.Throws<ArgumentException>(
            () => MemberSelector(
                ResourceEffectMemberKind.Method,
                isStatic: false,
                hasThis: false,
                explicitThis: false,
                ResourceEffectCallingConvention.Default));
        Assert.Throws<ArgumentException>(
            () => MemberSelector(
                ResourceEffectMemberKind.Method,
                isStatic: true,
                hasThis: false,
                explicitThis: true,
                ResourceEffectCallingConvention.Default));
        Assert.Throws<ArgumentException>(
            () => MemberSelector(
                ResourceEffectMemberKind.Field,
                isStatic: false,
                hasThis: false,
                explicitThis: false,
                ResourceEffectCallingConvention.VarArgs));
    }

    [Fact]
    public void Admission_UsesUnambiguousLengthFramedStructuralReceipts()
    {
        ResourceEffectModelIdentity model = new("example.framing");
        ResourceEffectTargetSelector namespaceDelimiter =
            OperationTargetWithDeclaring(
                Named("A:B", "C"),
                "Transform");
        ResourceEffectTargetSelector nameDelimiter =
            OperationTargetWithDeclaring(
                Named("A", "B:C"),
                "Transform");

        ResourceEffectAdmission first = Build(
            Model(
                model,
                namespaceDelimiter,
                ["pass(source=parameter[0],target=return)"]));
        ResourceEffectAdmission second = Build(
            Model(
                model,
                nameDelimiter,
                ["pass(source=parameter[0],target=return)"]));

        Assert.NotEqual(first.Receipt.ContentHash, second.Receipt.ContentHash);
        Assert.NotEqual(
            first.Receipt.Models.Single().ContentHash,
            second.Receipt.Models.Single().ContentHash);
    }

    [Fact]
    public void Admission_ReceiptsPreserveDistinctUtf16CodeUnits()
    {
        ResourceEffectModelIdentity model = new("example.utf16-framing");
        ResourceEffectAdmission first = Build(
            Model(
                model,
                OperationTargetWithDeclaring(
                    Named("Example", "\uD800"),
                    "Transform"),
                ["pass(source=parameter[0],target=return)"]));
        ResourceEffectAdmission second = Build(
            Model(
                model,
                OperationTargetWithDeclaring(
                    Named("Example", "\uD801"),
                    "Transform"),
                ["pass(source=parameter[0],target=return)"]));

        Assert.NotEqual(first.Receipt.ContentHash, second.Receipt.ContentHash);
        Assert.NotEqual(
            first.Receipt.Models.Single().ContentHash,
            second.Receipt.Models.Single().ContentHash);
    }

    [Fact]
    public void Admission_ValidatesConsumeSourceOperationReferences()
    {
        ResourceEffectModelIdentity model = new("example.consume-source");
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    ["consume(source=operation[9],target=operation[0])"]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedOperation,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);

        ResourceEffectModelIdentity chainedModel =
            new("example.operation-canonical-chain-definition");
        ResourceEffectAdmission chained = Build(
            Model(
                chainedModel,
                SimpleOperationTarget(),
                [
                    "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                    "consume(kind=example.resource,source=operation[0],target=operation[1])",
                    "release(source=operation[1],when=normal-return)",
                ],
                [Kind(chainedModel, "example.resource", 0)]));
        ResourceEffectAdmissionOutcome missingInnerConsume =
            ResourceEffectAdmissionBuilder.Admit(
                [
                    new ResourceEffectModelDefinition(
                        ResourceEffectLanguageIdentity.Version1,
                        chainedModel,
                        SingleModel(chained).ResourceKinds,
                        [],
                        AsTyped(
                            SingleModel(chained),
                            declaration => declaration.Effect is not ResourceEffect.Consume
                            {
                                Source: ResourceEffectLocation.Parameter,
                            })),
                ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedOperation,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(missingInnerConsume)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void LocalIdentityGrammarMatchesTypedAndParsedInputs()
    {
        Assert.Throws<ArgumentException>(
            () => new ResourceEffectLocalIdentity("local.name"));
        Assert.IsType<ResourceEffectParseOutcome.Rejected>(
            ResourceEffectStatementParser.Parse(
                "outcome(id=local.name,source=return,test=null)"));
    }

    [Fact]
    public void Parser_RejectsMalformedDottedEnumWithoutThrowing()
    {
        var rejected = Assert.IsType<ResourceEffectParseOutcome.Rejected>(
            ResourceEffectStatementParser.Parse(
                "outcome(id=value,source=return,test=enum[A..B])"));
        Assert.Equal(
            ResourceEffectDiagnosticKind.InvalidOutcomeTest,
            rejected.Diagnostic.Kind);
    }

    [Fact]
    public void Admission_ValidatesGenericVariablesInsideTypedStructuralFields()
    {
        var field = new ResourceEffectMemberSelector(
            Named("Owner"),
            "State",
            ResourceEffectMemberKind.Field,
            isStatic: false,
            genericArity: 0,
            ResourceEffectCallingConvention.Default,
            hasThis: false,
            explicitThis: false,
            [],
            new ResourceTypeExpression.Variable(
                new ResourceEffectGenericVariable(
                    ResourceEffectGenericVariableKind.Method,
                    99)));

        AssertTypedRejected(
            "example.resolved-field-variable",
            new ResourceEffect.Pass(
                new ResourceEffectLocation.StructuralField(
                    new ResourceEffectLocation.Receiver(),
                    field),
                new ResourceEffectLocation.Return(),
                null),
            SimpleOperationTarget(),
            ResourceEffectDiagnosticKind.UnboundGenericVariable);
    }

    [Fact]
    public void Admission_CoalescesDuplicateOutcomesAfterFieldAliasResolution()
    {
        ResourceEffectAdmission admission = Build(
            OutcomeAliasModel(
                new ResourceEffectModelIdentity("example.outcome-alias"),
                sameField: true));

        AdmittedResourceEffectDeclaration outcome = Assert.Single(
            SingleModel(admission).Declarations.Where(declaration =>
                declaration.Effect is ResourceEffect.Outcome));
        Assert.Equal(2, outcome.Provenances.Length);
        Assert.IsType<ResourceEffectLocation.StructuralField>(
            Assert.IsType<ResourceEffect.Outcome>(outcome.Effect).Source);
    }

    [Fact]
    public void Admission_RejectsDuplicateOutcomeIdWithDifferentCanonicalSubjects()
    {
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                OutcomeAliasModel(
                    new ResourceEffectModelIdentity("example.outcome-subjects"),
                    sameField: false),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void ProvenanceRequiresNonEmptyStableSourceIdentity()
    {
        ResourceEffectModelIdentity model = new("example.provenance");

        Assert.Throws<ArgumentException>(
            () => new ResourceDeclarationProvenance(
                model,
                ResourceDeclarationAuthority.ProductShipped,
                default,
                0));
        Assert.Throws<ArgumentException>(
            () => new ResourceDeclarationProvenance(
                model,
                ResourceDeclarationAuthority.ProductShipped,
                InertString.Empty,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResourceDeclarationProvenance(
                model,
                (ResourceDeclarationAuthority)999,
                new InertString(TextPolicy.Field, "source"),
                0));
    }

    [Fact]
    public void Admission_AllowsBorrowFromConsumedOperationSlot()
    {
        ResourceEffectModelIdentity model = new("example.consume-callback-borrow");

        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [
                    Model(
                        model,
                        TwoParameterOperationTarget(),
                        [
                            "consume(source=parameter[0],target=operation[0])",
                            "callback(delegate=parameter[1],scope=callback[1],execution=synchronous,cardinality=exactly-once)",
                            "borrow(source=operation[0],target=callback[1].parameter[0],access=read,scope=callback[1])",
                            "release(source=operation[0],when=normal-return)",
                            "release(source=operation[0],when=exceptional-exit)",
                        ]),
                ]));
    }

    [Fact]
    public void Admission_CoalescesEquivalentConsumesThroughDifferentLocalSlots()
    {
        ResourceEffectModelIdentity model = new("example.multiple-consume");
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "consume(source=parameter[0],target=operation[0])",
                        "consume(source=parameter[0],target=operation[1])",
                    ]),
            ]);

        ResourceEffectAdmission admission =
            Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(outcome).Admission;
        AdmittedResourceEffectDeclaration consume = Assert.Single(
            SingleModel(admission).Declarations,
            declaration => declaration.Effect is ResourceEffect.Consume);
        Assert.Equal(2, consume.Provenances.Length);
    }

    [Fact]
    public void Admission_AllowsConsumeFollowedByLaterOperationSlotSettlement()
    {
        ResourceEffectModelIdentity model = new("example.consume-release");

        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [
                            "consume(source=parameter[0],target=operation[0])",
                            "release(source=operation[0],when=normal-return)",
                        ]),
                ]));
    }

    [Fact]
    public void Admission_RejectsInconsistentModelLocalOperationSlotDefinitions()
    {
        ResourceEffectModelIdentity model = new("example.operation-inconsistent");
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    TwoDifferentParameterOperationTarget(),
                    [
                        "consume(kind=example.first,source=parameter[0],target=operation[0])",
                        "consume(kind=example.second,source=parameter[1],target=operation[0])",
                    ],
                    [
                        Kind(model, "example.first", 0),
                        Kind(model, "example.second", 0),
                    ]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_ResolvesChainedModelLocalOperationSlots()
    {
        ResourceEffectModelIdentity model = new("example.operation-chain");

        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [
                            "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                            "consume(kind=example.resource,source=operation[0],target=operation[1])",
                            "release(source=operation[1],when=normal-return)",
                        ],
                        [Kind(model, "example.resource", 0)]),
                ]));
    }

    [Fact]
    public void Admission_RejectsCyclicModelLocalOperationSlotsWithoutThrowing()
    {
        ResourceEffectModelIdentity model = new("example.operation-cycle");
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "consume(kind=example.resource,source=operation[1],target=operation[0])",
                        "consume(kind=example.resource,source=operation[0],target=operation[1])",
                    ],
                    [Kind(model, "example.resource", 0)]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_ReadmitsItsCanonicalFieldDeclarations()
    {
        ResourceEffectModelIdentity model = new("example.field-roundtrip");
        ResourceEffectAdmission original = Build(FieldRoundTripModel(model));
        AdmittedResourceEffectModel originalModel = SingleModel(original);
        ResourceEffectAdmission roundTrip = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                originalModel.ResourceKinds,
                [],
                AsTyped(originalModel)));

        Assert.Equal(
            originalModel.Declarations,
            SingleModel(roundTrip).Declarations);
        Assert.Equal(original.Receipt.ContentHash, roundTrip.Receipt.ContentHash);
    }

    [Fact]
    public void Admission_RequiresDefiningConsumesForCanonicalOperationSlots()
    {
        ResourceEffectModelIdentity model =
            new("example.operation-canonical-definition");
        ResourceEffectAdmission original = Build(
            Model(
                model,
                SimpleOperationTarget(),
                [
                    "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                    "release(source=operation[0],when=normal-return)",
                ],
                [Kind(model, "example.resource", 0)]));
        AdmittedResourceEffectDeclaration release =
            Assert.Single(
                SingleModel(original).Declarations,
                declaration => declaration.Effect is ResourceEffect.Release);

        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                new ResourceEffectModelDefinition(
                    ResourceEffectLanguageIdentity.Version1,
                    model,
                    SingleModel(original).ResourceKinds,
                    [],
                    [AsTyped(release)]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedOperation,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Admission_ReadmitsItsCanonicalOperationSlots()
    {
        ResourceEffectModelIdentity model = new("example.operation-roundtrip");
        ResourceEffectAdmission original = Build(
            Model(
                model,
                SimpleOperationTarget(),
                [
                    "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                    "release(source=operation[0],when=normal-return)",
                ],
                [Kind(model, "example.resource", 0)]));
        AdmittedResourceEffectModel originalModel = SingleModel(original);
        ResourceEffectAdmission roundTrip = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                originalModel.ResourceKinds,
                [],
                AsTyped(originalModel)));

        Assert.Equal(
            originalModel.Declarations,
            SingleModel(roundTrip).Declarations);
        Assert.Equal(original.Receipt.ContentHash, roundTrip.Receipt.ContentHash);
    }

    [Fact]
    public void Admission_RevalidatesParsedStructuralFieldVariablesAgainstOuterTarget()
    {
        ResourceEffectModelIdentity invalidModel = new("example.parsed-field-unbound");
        ResourceEffectAdmissionOutcome invalid = ResourceEffectAdmissionBuilder.Admit(
            [
                ParsedGenericFieldModel(
                    invalidModel,
                    SimpleOperationTarget()),
            ]);
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnboundGenericVariable,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(invalid)
                .Diagnostics.Single().Diagnostic.Kind);

        ResourceEffectModelIdentity validModel = new("example.parsed-field-bound");
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [
                    ParsedGenericFieldModel(
                        validModel,
                        OpenGenericOperationTarget()),
                ]));
    }

    [Fact]
    public void Admission_ChargesExplicitResourceKindsAfterCoalescing()
    {
        ResourceEffectModelIdentity model = new("example.explicit-kind-budget");
        ResourceEffectModelDefinition definition = Model(
            model,
            SimpleOperationTarget(),
            [],
            [
                Kind(model, "example.first", 0),
                Kind(model, "example.second", 0),
            ]);

        AssertResourceKindBoundary(definition, exact: 2);
    }

    [Fact]
    public void Admission_ChargesParsedResourceKindsAfterCoalescing()
    {
        ResourceEffectModelIdentity model = new("example.parsed-kind-budget");
        ResourceEffectModelDefinition definition = Model(
            model,
            new ResourceEffectTargetSelector.Type(Named("Resource")),
            [
                "resource(kind=example.first)",
                "resource(kind=example.second)",
            ]);

        AssertResourceKindBoundary(definition, exact: 2);
    }

    [Fact]
    public void Admission_ChargesTypedResourceKindsAfterCoalescing()
    {
        ResourceEffectModelIdentity model = new("example.typed-kind-budget");
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Type(Named("Resource"));
        ResourceEffectModelDefinition definition = new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [],
            [
                TypedResource(model, target, "example.first", 0),
                TypedResource(model, target, "example.second", 1),
            ]);

        AssertResourceKindBoundary(definition, exact: 2);
    }

    [Fact]
    public void Admission_ChargesReferencedExternalResourceKinds()
    {
        ResourceEffectModelIdentity model = new("example.reference-kind-budget");
        ResourceEffectModelDefinition definition = Model(
            model,
            SimpleOperationTarget(),
            [
                "release(kind=example.first,source=receiver,when=normal-return)",
                "move(kind=example.second,source=receiver,target=return,when=normal-return)",
            ]);

        AssertResourceKindBoundary(definition, exact: 2);
        Assert.Empty(SingleModel(Build(definition)).ResourceKinds);
    }

    [Fact]
    public void Admission_CoalescesExplicitParsedAndTypedKindsBeforeBudgeting()
    {
        ResourceEffectModelIdentity model = new("example.coalesced-kind-budget");
        ResourceEffectTargetSelector target =
            new ResourceEffectTargetSelector.Type(Named("Resource"));
        ResourceEffectModelDefinition definition = new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [Kind(model, "example.shared", 0)],
            [
                new ResourceEffectTargetDeclaration(
                    target,
                    [Source(model, model.Value, 1, "resource(kind=example.shared)")]),
            ],
            [TypedResource(model, target, "example.shared", 2)]);

        ResourceEffectAdmission admission = Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [definition],
                new ResourceEffectWorkLimits(maxResourceKindsPerModel: 1))).Admission;
        Assert.Single(SingleModel(admission).ResourceKinds);
        Assert.Equal(
            3,
            SingleModel(admission).ResourceKinds.Single().Provenances.Length);
    }

    static ResourceEffectModelDefinition FieldTerminalModel(
        ResourceEffectModelIdentity model,
        string fieldName,
        string selector,
        string terminal)
        => new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    FieldTarget(fieldName),
                    [
                        Source(
                            model,
                            model.Value,
                            0,
                            $"resource(kind=example.field-resource,value=declared-field,selector={selector})"),
                    ]),
                new ResourceEffectTargetDeclaration(
                    SimpleOperationTarget(),
                    [Source(model, model.Value, 1, terminal)]),
            ]);

    static ResourceEffectModelDefinition FieldRoundTripModel(
        ResourceEffectModelIdentity model)
        => new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    FieldTarget("Child"),
                    [
                        Source(
                            model,
                            model.Value,
                            0,
                            "resource(kind=example.child,value=declared-field,selector=child)"),
                    ]),
                new ResourceEffectTargetDeclaration(
                    SimpleOperationTarget(),
                    [
                        Source(
                            model,
                            model.Value,
                            1,
                            "pass(source=receiver.field[child],target=return)"),
                    ]),
            ]);

    static ResourceEffectModelDefinition OutcomeAliasModel(
        ResourceEffectModelIdentity model,
        bool sameField)
    {
        ResourceEffectTargetSelector firstField = FieldTarget("FirstState");
        ResourceEffectTargetSelector secondField =
            sameField ? firstField : FieldTarget("SecondState");
        return new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    firstField,
                    [
                        Source(
                            model,
                            model.Value,
                            0,
                            "resource(kind=example.state,value=declared-field,selector=first)"),
                    ]),
                new ResourceEffectTargetDeclaration(
                    secondField,
                    [
                        Source(
                            model,
                            model.Value,
                            1,
                            "resource(kind=example.state,value=declared-field,selector=second)"),
                    ]),
                new ResourceEffectTargetDeclaration(
                    SimpleOperationTarget(),
                    [
                        Source(
                            model,
                            model.Value,
                            2,
                            "outcome(id=done,source=receiver.field[first],test=bool[true])"),
                        Source(
                            model,
                            model.Value,
                            3,
                            "outcome(id=done,source=receiver.field[second],test=bool[true])"),
                    ]),
            ]);
    }

    static ResourceEffectModelDefinition ParsedGenericFieldModel(
        ResourceEffectModelIdentity model,
        ResourceEffectTargetSelector operationTarget)
        => new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    GenericFieldTarget(),
                    [
                        Source(
                            model,
                            model.Value,
                            0,
                            "resource(kind=example.generic-field<type[0]>,value=declared-field,selector=state)"),
                    ]),
                new ResourceEffectTargetDeclaration(
                    operationTarget,
                    [
                        Source(
                            model,
                            model.Value,
                            1,
                            "pass(source=receiver.field[state],target=return)"),
                    ]),
            ]);

    static ResourceEffectTypedDeclaration TypedResource(
        ResourceEffectModelIdentity model,
        ResourceEffectTargetSelector target,
        string kind,
        int ordinal)
        => new(
            target,
            new ResourceEffect.Resource(
                new ResourceKindReference(new ResourceKindIdentity(kind)),
                null,
                null),
            [
                new ResourceDeclarationProvenance(
                    model,
                    ResourceDeclarationAuthority.ProductShipped,
                    new InertString(TextPolicy.Field, model.Value + ".typed"),
                    ordinal),
            ]);

    static void AssertResourceKindBoundary(
        ResourceEffectModelDefinition model,
        int exact)
    {
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(
                [model],
                new ResourceEffectWorkLimits(
                    maxResourceKindsPerModel: exact)));
        var exceeded = Assert.IsType<ResourceEffectAdmissionOutcome.WorkLimitExceeded>(
            ResourceEffectAdmissionBuilder.Admit(
                [model],
                new ResourceEffectWorkLimits(
                    maxResourceKindsPerModel: exact - 1)));
        Assert.Equal(ResourceEffectWorkLimitKind.ModelResourceKinds, exceeded.LimitKind);
        Assert.Equal(exact - 1, exceeded.Limit);
        Assert.Equal(exact, exceeded.Required);
    }

    static void AssertTypedRejected(
        string modelValue,
        ResourceEffect effect,
        ResourceEffectTargetSelector? target = null,
        ResourceEffectDiagnosticKind expected =
            ResourceEffectDiagnosticKind.InvalidTerm)
    {
        ResourceEffectModelIdentity model = new(modelValue);
        var declaration = new ResourceEffectTypedDeclaration(
            target ?? SimpleOperationTarget(),
            effect,
            [
                new ResourceDeclarationProvenance(
                    model,
                    ResourceDeclarationAuthority.ProductShipped,
                    new InertString(TextPolicy.Field, model.Value),
                    0),
            ]);
        ResourceEffectAdmissionOutcome outcome = ResourceEffectAdmissionBuilder.Admit(
            [
                new ResourceEffectModelDefinition(
                    ResourceEffectLanguageIdentity.Version1,
                    model,
                    [],
                    [],
                    [declaration]),
            ]);

        Assert.Equal(
            expected,
            Assert.IsType<ResourceEffectAdmissionOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    static ResourceEffectAdmission Build(params ResourceEffectModelDefinition[] models)
        => Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(models)).Admission;

    static AdmittedResourceEffectModel SingleModel(ResourceEffectAdmission admission)
        => Assert.Single(admission.Models);

    static ImmutableArray<ResourceEffectTypedDeclaration> AsTyped(
        AdmittedResourceEffectModel model,
        Func<AdmittedResourceEffectDeclaration, bool>? predicate = null)
        => [.. model.Declarations
            .Where(declaration => predicate?.Invoke(declaration) ?? true)
            .Select(AsTyped)];

    static ResourceEffectTypedDeclaration AsTyped(
        AdmittedResourceEffectDeclaration declaration)
        => new(
            declaration.Target,
            declaration.Effect,
            declaration.Provenances);

    static ResourceEffectModelDefinition Model(
        ResourceEffectModelIdentity identity,
        ResourceEffectTargetSelector target,
        IEnumerable<string> statements,
        ImmutableArray<ResourceKindDefinition> resourceKinds = default)
    {
        ImmutableArray<ResourceEffectSourceStatement> sources =
            [.. statements.Select((statement, index) =>
                Source(identity, identity.Value, index, statement))];
        return new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            identity,
            resourceKinds.IsDefault ? [] : resourceKinds,
            sources.IsEmpty
                ? []
                : [new ResourceEffectTargetDeclaration(target, sources)]);
    }

    static ResourceEffectSourceStatement Source(
        ResourceEffectModelIdentity model,
        string source,
        int ordinal,
        string statement)
        => new(
            statement,
            Provenance(model, source, ordinal));

    static ResourceDeclarationProvenance Provenance(
        ResourceEffectModelIdentity model,
        string source,
        int ordinal)
        => new(
            model,
            ResourceDeclarationAuthority.ProductShipped,
            new InertString(TextPolicy.Field, source),
            ordinal);

    static ResourceKindDefinition Kind(
        ResourceEffectModelIdentity model,
        string identity,
        int arity)
        => new(
            new ResourceKindIdentity(identity),
            arity,
            [
                new ResourceDeclarationProvenance(
                    model,
                    ResourceDeclarationAuthority.ProductShipped,
                    new InertString(TextPolicy.Field, model.Value + ".resources"),
                    0),
            ]);

    static ResourceEffectTargetSelector SnapshotTarget()
        => OperationTarget(
            "Snapshot",
            Named("State"),
            Named("SnapshotCallback"),
            returnType: Named("Result"));

    static ResourceEffectTargetSelector SimpleOperationTarget()
        => OperationTarget("Transform", Named("Value"), returnType: Named("Result"));

    static ResourceEffectTargetSelector TwoParameterOperationTarget()
        => OperationTarget(
            "TryAccept",
            Named("Boolean"),
            Named("Boolean"),
            returnType: Named("Boolean"));

    static ResourceEffectTargetSelector TwoDifferentParameterOperationTarget()
        => OperationTarget(
            "Convert",
            Named("String"),
            Named("Int32"),
            returnType: Named("Object"));

    static ResourceEffectTargetSelector OpenGenericOperationTarget()
        => OperationTargetWithDeclaring(
            OpenGenericOwner(),
            "Transform");

    static ResourceEffectTargetSelector GenericFieldTarget()
        => new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                OpenGenericOwner(),
                "State",
                ResourceEffectMemberKind.Field,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                [],
                new ResourceTypeExpression.Variable(
                    new ResourceEffectGenericVariable(
                        ResourceEffectGenericVariableKind.Type,
                        0))));

    static ResourceEffectTargetSelector OperationTarget(
        string name,
        params ResourceTypeExpression[] parameters)
        => OperationTarget(name, parameters, Named("Object"));

    static ResourceEffectTargetSelector OperationTarget(
        string name,
        ResourceTypeExpression parameter0,
        ResourceTypeExpression parameter1,
        ResourceTypeExpression returnType)
        => OperationTarget(name, [parameter0, parameter1], returnType);

    static ResourceEffectTargetSelector OperationTarget(
        string name,
        ResourceTypeExpression parameter0,
        ResourceTypeExpression returnType)
        => OperationTarget(name, [parameter0], returnType);

    static ResourceEffectTargetSelector OperationTarget(
        string name,
        ResourceTypeExpression[] parameters,
        ResourceTypeExpression returnType)
        => new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                Named("Owner"),
                name,
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [.. parameters.Select(parameter =>
                    new ResourceEffectParameterSelector(parameter, ResourceEffectRefKind.Value))],
                returnType));

    static ResourceEffectTargetSelector OperationTargetWithDeclaring(
        ResourceTypeExpression.Named declaringType,
        string name)
        => new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                name,
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        Named("Value"),
                        ResourceEffectRefKind.Value),
                ],
                Named("Result")));

    static ResourceEffectMemberSelector MemberSelector(
        ResourceEffectMemberKind kind,
        bool isStatic,
        bool hasThis,
        bool explicitThis,
        ResourceEffectCallingConvention callingConvention)
        => new(
            Named("Owner"),
            "Member",
            kind,
            isStatic,
            genericArity: 0,
            callingConvention,
            hasThis,
            explicitThis,
            [],
            Named("Object"));

    static ResourceEffectTargetSelector FieldTarget(string name)
        => FieldTarget(name, ResourceAssemblyVersionPolicy.Any);

    static ResourceEffectTargetSelector FieldTarget(
        string name,
        ResourceAssemblyVersionPolicy version)
        => new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                Named(
                    Assembly(publicKeyToken: null, version),
                    "Example",
                    "Owner"),
                name,
                ResourceEffectMemberKind.Field,
                isStatic: false,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                [],
                Named(
                    Assembly(publicKeyToken: null, version),
                    "Example",
                    "Object")));

    static ResourceTypeExpression.Named Named(string name)
        => Named("Example", name);

    static ResourceTypeExpression.Named Named(string @namespace, string name)
        => Named(Assembly(publicKeyToken: null, ResourceAssemblyVersionPolicy.Any), @namespace, name);

    static ResourceTypeExpression.Named Named(
        ResourceAssemblySelector assembly,
        string @namespace,
        string name)
        => new(
            assembly,
            @namespace,
            [new ResourceTypeNameSegment(name, 0)]);

    static ResourceTypeExpression.Named OpenGenericOwner()
        => new(
            Assembly(
                publicKeyToken: null,
                ResourceAssemblyVersionPolicy.Any),
            "Example",
            [new ResourceTypeNameSegment("Owner", 1)],
            [
                new ResourceTypeExpression.Variable(
                    new ResourceEffectGenericVariable(
                        ResourceEffectGenericVariableKind.Type,
                        0)),
            ]);

    static ResourceAssemblySelector Assembly(
        string? publicKeyToken,
        ResourceAssemblyVersionPolicy version)
        => new(
            "Example",
            publicKeyToken,
            version);

    static void AssertRejected(
        string statement,
        ResourceEffectDiagnosticKind kind,
        string token,
        int occurrence = 1)
    {
        var result = Assert.IsType<ResourceEffectParseOutcome.Rejected>(
            ResourceEffectStatementParser.Parse(statement));
        Assert.Equal(kind, result.Diagnostic.Kind);
        int offset = -1;
        for (int current = 0; current < occurrence; current++)
            offset = statement.IndexOf(token, offset + 1, StringComparison.Ordinal);
        Assert.Equal(offset, result.Diagnostic.Offset);
        Assert.Equal(token.Length, result.Diagnostic.Length);
    }

    static void AssertLimit(
        ResourceEffectParseOutcome outcome,
        ResourceEffectWorkLimitKind kind)
        => Assert.Equal(
            kind,
            Assert.IsType<ResourceEffectParseOutcome.WorkLimitExceeded>(outcome).LimitKind);

    static void AssertLimit(
        ResourceEffectAdmissionOutcome outcome,
        ResourceEffectWorkLimitKind kind)
        => Assert.Equal(
            kind,
            Assert.IsType<ResourceEffectAdmissionOutcome.WorkLimitExceeded>(outcome).LimitKind);
}
