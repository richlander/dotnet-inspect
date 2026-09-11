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
    public void Catalog_CompleteSnapshotWitnessRetainsPurposePreservingTerms()
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

        ResourceEffectCatalog catalog = Build(
            Model(model, target, statements));

        var borrow = Assert.IsType<ResourceEffect.Borrow>(
            catalog.Declarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Borrow).Effect);
        Assert.Equal(ResourceBorrowMaterialization.None, borrow.Materialization);
        var pass = Assert.IsType<ResourceEffect.Pass>(
            catalog.Declarations.Single(declaration =>
                declaration.Effect is ResourceEffect.Pass
                {
                    Identity: ResourcePassIdentity.Preserve,
                }).Effect);
        Assert.Equal(ResourcePassIdentity.Preserve, pass.Identity);
        Assert.Equal(statements.Length, catalog.Declarations.Length);
    }

    [Fact]
    public void Catalog_SnapshotWitnessIsNonVacuousWhenCallbackDeclarationIsRemoved()
    {
        ResourceEffectModelIdentity model = new("example.snapshot");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SnapshotTarget(),
                    [
                        "borrow(source=receiver,target=callback[1].parameter[0],access=read,scope=callback[1],materialization=none)",
                        "pass(source=callback[1].return,target=return,identity=preserve)",
                    ]),
            ]);

        var rejected = Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedCallback,
            rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_RejectsAtomicModelWhenOneStatementIsMalformed()
    {
        ResourceEffectModelIdentity model = new("example.atomic");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "pass(source=parameter[0],target=return)",
                        "pass(source=parameter[0],target=return,unknown=value)",
                    ]),
            ]);

        var rejected = Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
        Assert.Equal(ResourceEffectDiagnosticKind.UnknownArgument, rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_RejectsUnknownLanguageAtomically()
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

        var rejected = Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(
            ResourceEffectCatalogBuilder.Build([definition]));
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnknownLanguage,
            rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_CoalescesSemanticEqualityAndRetainsEveryProvenance()
    {
        ResourceEffectModelIdentity model = new("example.coalescing");
        ResourceEffectTargetSelector target = SimpleOperationTarget();
        ResourceEffectCatalog catalog = Build(
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
                ]),
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
                                1,
                                "pass( target = return , source = parameter[0] )"),
                        ]),
                ]),
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                [],
                [],
                [
                    new NormalizedResourceEffectDeclaration(
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

        NormalizedResourceEffectDeclaration declaration = Assert.Single(catalog.Declarations);
        Assert.Equal(3, declaration.Provenances.Length);
        Assert.Equal(
            ["source.one", "source.two", "source.three"],
            declaration.Provenances.Select(provenance => provenance.SourceIdentity.ToString()));
    }

    [Fact]
    public void Catalog_ContentAndReceiptsAreDeterministicAcrossInputOrder()
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

        ResourceEffectCatalog left = Build(first, second);
        ResourceEffectCatalog right = Build(second, first);

        Assert.Equal(left.Receipt, right.Receipt);
        Assert.Equal(left.Receipt.SemanticHash, right.Receipt.SemanticHash);
        Assert.Equal(
            left.Declarations.Select(declaration => declaration.Effect),
            right.Declarations.Select(declaration => declaration.Effect));
        Assert.All(left.Receipt.Models, receipt => Assert.Equal(64, receipt.ContentHash.Length));
    }

    [Fact]
    public void Catalog_CoalescesCatalogGlobalResourceKindsAndRetainsProvenance()
    {
        ResourceEffectModelIdentity first = new("example.kind-first");
        ResourceEffectModelIdentity second = new("example.kind-second");

        ResourceEffectCatalog catalog = Build(
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

        ResourceKindDefinition kind = Assert.Single(catalog.ResourceKinds);
        Assert.Equal(2, kind.Provenances.Length);
        Assert.Equal(
            [first, second],
            kind.Provenances.Select(provenance => provenance.Model));
    }

    [Fact]
    public void Catalog_ModelBudgetsSucceedAtExactUseAndFailOneUnder()
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
            maxCatalogStatements: 2);
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build([model], exact));

        AssertLimit(
            ResourceEffectCatalogBuilder.Build(
                [model],
                new ResourceEffectWorkLimits(maxStatementsPerModel: 1)),
            ResourceEffectWorkLimitKind.ModelStatements);
        AssertLimit(
            ResourceEffectCatalogBuilder.Build(
                [model],
                new ResourceEffectWorkLimits(maxCatalogStatements: 1)),
            ResourceEffectWorkLimitKind.CatalogStatements);

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
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [twoDeclarations],
                new ResourceEffectWorkLimits(maxDeclarationsPerModel: 2)));
        AssertLimit(
            ResourceEffectCatalogBuilder.Build(
                [twoDeclarations],
                new ResourceEffectWorkLimits(maxDeclarationsPerModel: 1)),
            ResourceEffectWorkLimitKind.ModelDeclarations);

        ResourceEffectModelDefinition secondModel = Model(
            new ResourceEffectModelIdentity("example.second-budget-model"),
            SimpleOperationTarget(),
            ["pass(source=parameter[0],target=return)"]);
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [model, secondModel],
                new ResourceEffectWorkLimits(maxModels: 2)));
        AssertLimit(
            ResourceEffectCatalogBuilder.Build(
                [model, secondModel],
                new ResourceEffectWorkLimits(maxModels: 1)),
            ResourceEffectWorkLimitKind.Models);
    }

    [Fact]
    public void Catalog_ResolvesModelLocalFieldSelectors()
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

        ResourceEffectCatalog catalog = Build(definition);

        Assert.Contains(
            catalog.Declarations,
            declaration => declaration.Effect is ResourceEffect.Pass
            {
                Source: ResourceEffectLocation.ResolvedField,
            });
        Assert.Contains(
            catalog.ResourceKinds,
            kind => kind.Identity == new ResourceKindIdentity("example.child")
                && kind.Arity == 0);
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
    public void Catalog_RejectsUnresolvedModelLocalReferences(
        string statement,
        ResourceEffectDiagnosticKind expected)
    {
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    new ResourceEffectModelIdentity("example.references"),
                    SimpleOperationTarget(),
                    [statement]),
            ]);

        var rejected = Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
        Assert.Equal(expected, rejected.Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_RejectsUnresolvedResourceKindAndGlobalArityMismatch()
    {
        ResourceEffectModelIdentity model = new("example.resources");
        ResourceEffectCatalogOutcome unresolved = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    ["release(kind=example.missing,source=receiver,when=normal-return)"]),
            ]);
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedResourceKind,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(unresolved)
                .Diagnostics.Single().Diagnostic.Kind);

        ResourceEffectModelIdentity definitionModel = new("example.definition");
        ResourceKindDefinition kind = Kind(definitionModel, "example.shared", 1);
        ResourceEffectCatalogOutcome mismatch = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    definitionModel,
                    SimpleOperationTarget(),
                    [],
                    [kind]),
                Model(
                    new ResourceEffectModelIdentity("example.reference"),
                    SimpleOperationTarget(),
                    ["release(kind=example.shared,source=receiver,when=normal-return)"]),
            ]);
        var arityRejected =
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(mismatch);
        Assert.Equal(2, arityRejected.Diagnostics.Length);
        Assert.All(
            arityRejected.Diagnostics,
            diagnostic => Assert.Equal(
                ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                diagnostic.Diagnostic.Kind));
    }

    [Fact]
    public void Catalog_RejectsUnboundGenericVariables()
    {
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    new ResourceEffectModelIdentity("example.variables"),
                    SimpleOperationTarget(),
                    ["acquire(kind=example.resource<type[0]>,target=return,when=normal-return)"],
                    [Kind(new ResourceEffectModelIdentity("example.variables"), "example.resource", 1)]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.UnboundGenericVariable,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_RejectsInconsistentClosedDeclaringTypeVariables()
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

        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    target,
                    ["acquire(kind=example.resource<type[0]>,target=return,when=normal-return)"],
                    [Kind(model, "example.resource", 1)]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.InconsistentGenericVariable,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_RejectsOverlappingReleaseAndMove()
    {
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [TerminalModel(disjointOutcomes: false)]);

        var rejected = Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
        Assert.Equal(2, rejected.Diagnostics.Length);
        Assert.All(
            rejected.Diagnostics,
            diagnostic => Assert.Equal(
                ResourceEffectDiagnosticKind.ConflictingDeclaration,
                diagnostic.Diagnostic.Kind));
        Assert.Equal(
            2,
            rejected.Diagnostics
                .Select(diagnostic => diagnostic.Provenance)
                .Distinct()
                .Count());
    }

    [Fact]
    public void Catalog_AllowsTerminalEffectsWithDisjointOutcomes()
    {
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [TerminalModel(disjointOutcomes: true)]));
    }

    [Fact]
    public void Catalog_TreatsOppositeTestsOnDifferentSubjectsAsOverlapping()
    {
        ResourceEffectModelIdentity model = new("example.subjects");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    TwoParameterOperationTarget(),
                    [
                        "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                        "outcome(id=first,source=parameter[0],test=bool[true])",
                        "outcome(id=second,source=parameter[1],test=bool[false])",
                        "release(kind=example.resource,source=operation[0],when=outcome[first])",
                        "move(kind=example.resource,source=operation[0],target=return,when=outcome[second])",
                    ],
                    [Kind(model, "example.resource", 0)]),
            ]);

        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
    }

    [Fact]
    public void Catalog_AllowsContradictoryOperationFactsUnderDisjointExactTypeGuards()
    {
        ResourceEffectModelIdentity model = new("example.guards");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    TwoDifferentParameterOperationTarget(),
                    [
                        "operation(boundary=transparent,throws=never,guard=exact-type[parameter[0];signature-parameter[0]])",
                        "operation(boundary=ordinary,throws=possible,guard=exact-type[parameter[0];signature-parameter[1]])",
                    ]),
            ]);

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(outcome);
    }

    [Fact]
    public void Catalog_RejectsContradictoryOperationFactsWhenGuardSubjectsDiffer()
    {
        ResourceEffectModelIdentity model = new("example.guard-overlap");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    TwoDifferentParameterOperationTarget(),
                    [
                        "operation(boundary=transparent,throws=never,guard=exact-type[parameter[0];signature-parameter[0]])",
                        "operation(boundary=ordinary,throws=possible,guard=exact-type[parameter[1];signature-parameter[1]])",
                    ]),
            ]);

        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
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
    public void Catalog_ResolvesOutcomeIdsWithinEachAtomicModel()
    {
        ResourceEffectModelIdentity releaseModel = new("example.outcome-release");
        ResourceEffectModelIdentity moveModel = new("example.outcome-move");

        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
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

        ResourceEffectCatalog catalog =
            Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(outcome).Catalog;
        Assert.Contains(
            catalog.Declarations,
            declaration => declaration.Effect is ResourceEffect.Release
            {
                When: ResourceEffectCompletion.ResolvedOutcome
                {
                    Test: ResourceEffectOutcomeTest.Boolean { Value: true },
                },
            });
        Assert.Contains(
            catalog.Declarations,
            declaration => declaration.Effect is ResourceEffect.Move
            {
                When: ResourceEffectCompletion.ResolvedOutcome
                {
                    Test: ResourceEffectOutcomeTest.Boolean { Value: false },
                },
            });
    }

    [Fact]
    public void Catalog_CanonicalizesDifferentLocalNamesForTheSameField()
    {
        ResourceEffectModelIdentity releaseModel = new("example.field-release");
        ResourceEffectModelIdentity moveModel = new("example.field-move");

        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
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

        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
    }

    [Fact]
    public void Catalog_ScopesSameLocalFieldNameToEachModel()
    {
        ResourceEffectModelIdentity releaseModel = new("example.field-one");
        ResourceEffectModelIdentity moveModel = new("example.field-two");

        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
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

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(outcome);
    }

    [Fact]
    public void Catalog_ConservativelyUnifiesDifferentGenericResourceVariables()
    {
        ResourceEffectModelIdentity model = new("example.kind-unification");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    GenericMethodTarget(),
                    [
                        "release(kind=example.generic<method[0]>,source=parameter[0],when=normal-return)",
                        "move(kind=example.generic<method[1]>,source=parameter[0],target=return,when=normal-return)",
                    ],
                    [Kind(model, "example.generic", 1)]),
            ]);

        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
    }

    [Fact]
    public void Catalog_ConservativelyUnifiesDifferentGenericGuardVariables()
    {
        ResourceEffectModelIdentity model = new("example.guard-unification");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    GenericMethodTarget(),
                    [
                        "operation(boundary=transparent,throws=never,guard=exact-type[parameter[0];signature-parameter[0]])",
                        "operation(boundary=ordinary,throws=possible,guard=exact-type[parameter[0];signature-parameter[1]])",
                    ]),
            ]);

        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
    }

    [Fact]
    public void Catalog_RejectsTypedReleaseObservationInvariantViolations()
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
    public void Catalog_RejectsUndefinedTypedEffectEnumValues()
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
    public void Catalog_UsesUnambiguousLengthFramedStructuralReceipts()
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

        ResourceEffectCatalog first = Build(
            Model(
                model,
                namespaceDelimiter,
                ["pass(source=parameter[0],target=return)"]));
        ResourceEffectCatalog second = Build(
            Model(
                model,
                nameDelimiter,
                ["pass(source=parameter[0],target=return)"]));

        Assert.NotEqual(first.Receipt.SemanticHash, second.Receipt.SemanticHash);
        Assert.NotEqual(
            first.Receipt.Models.Single().ContentHash,
            second.Receipt.Models.Single().ContentHash);
    }

    [Fact]
    public void Catalog_ValidatesConsumeSourceOperationReferences()
    {
        ResourceEffectModelIdentity model = new("example.consume-source");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    ["consume(source=operation[9],target=operation[0])"]),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.UnresolvedOperation,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_EntryTerminalOverlapsLaterTerminalEffects()
    {
        ResourceEffectModelIdentity model = new("example.entry-overlap");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "release(source=parameter[0],when=entry)",
                        "move(source=parameter[0],target=return,when=normal-return)",
                    ]),
            ]);

        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome);
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
    public void Catalog_GuardUnificationOverlapsCompatibleAssemblyPolicies()
    {
        ResourceAssemblySelector anyVersion = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);
        ResourceAssemblySelector exactVersion = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Exact(new Version(1, 2, 3, 4)));
        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(
            GuardPolicyOutcome(anyVersion, exactVersion, "example.guard-version"));

        ResourceAssemblySelector unconstrainedToken = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);
        ResourceAssemblySelector exactToken = Assembly(
            publicKeyToken: "0011223344556677",
            ResourceAssemblyVersionPolicy.Any);
        Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(
            GuardPolicyOutcome(
                unconstrainedToken,
                exactToken,
                "example.guard-token-compatible"));

        ResourceAssemblySelector firstToken = Assembly(
            publicKeyToken: "0011223344556677",
            ResourceAssemblyVersionPolicy.Any);
        ResourceAssemblySelector secondToken = Assembly(
            publicKeyToken: "8899aabbccddeeff",
            ResourceAssemblyVersionPolicy.Any);
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            GuardPolicyOutcome(
                firstToken,
                secondToken,
                "example.guard-token-incompatible"));
    }

    [Fact]
    public void Catalog_ValidatesGenericVariablesInsideTypedResolvedFields()
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
                new ResourceEffectLocation.ResolvedField(
                    new ResourceEffectLocation.Receiver(),
                    field),
                new ResourceEffectLocation.Return(),
                null),
            SimpleOperationTarget(),
            ResourceEffectDiagnosticKind.UnboundGenericVariable);
    }

    [Fact]
    public void Catalog_CoalescesDuplicateOutcomesAfterFieldAliasResolution()
    {
        ResourceEffectCatalog catalog = Build(
            OutcomeAliasModel(
                new ResourceEffectModelIdentity("example.outcome-alias"),
                sameField: true));

        NormalizedResourceEffectDeclaration outcome = Assert.Single(
            catalog.Declarations.Where(declaration =>
                declaration.Effect is ResourceEffect.Outcome));
        Assert.Equal(2, outcome.Provenances.Length);
        Assert.IsType<ResourceEffectLocation.ResolvedField>(
            Assert.IsType<ResourceEffect.Outcome>(outcome.Effect).Source);
    }

    [Fact]
    public void Catalog_RejectsDuplicateOutcomeIdWithDifferentCanonicalSubjects()
    {
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                OutcomeAliasModel(
                    new ResourceEffectModelIdentity("example.outcome-subjects"),
                    sameField: false),
            ]);

        Assert.Equal(
            ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
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
    }

    [Fact]
    public void Catalog_RejectsOverlappingBorrowAndConsumeAtEntry()
    {
        ResourceEffectModelIdentity model = new("example.borrow-consume");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "borrow(source=parameter[0],target=parameter[0],access=read,scope=call)",
                        "consume(source=parameter[0],target=operation[0])",
                    ]),
            ]);

        AssertConflict(outcome);
    }

    [Fact]
    public void Catalog_RejectsMultipleConsumesToDifferentOperationSlots()
    {
        ResourceEffectModelIdentity model = new("example.multiple-consume");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    SimpleOperationTarget(),
                    [
                        "consume(source=parameter[0],target=operation[0])",
                        "consume(source=parameter[0],target=operation[1])",
                    ]),
            ]);

        AssertConflict(outcome);
    }

    [Fact]
    public void Catalog_AllowsConsumeFollowedByLaterOperationSlotSettlement()
    {
        ResourceEffectModelIdentity model = new("example.consume-release");

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
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
    public void Catalog_ConflictsAcrossCompatibleTargetAssemblyPolicies()
    {
        ResourceAssemblySelector any = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);
        ResourceAssemblySelector exact = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Exact(new Version(1, 0, 0, 0)));
        AssertConflict(
            TargetTerminalOutcome(
                any,
                exact,
                "example.target-version-overlap"));

        ResourceAssemblySelector noToken = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);
        ResourceAssemblySelector exactToken = Assembly(
            publicKeyToken: "0011223344556677",
            ResourceAssemblyVersionPolicy.Any);
        AssertConflict(
            TargetTerminalOutcome(
                noToken,
                exactToken,
                "example.target-token-overlap"));
    }

    [Fact]
    public void Catalog_AllowsTerminalEffectsOnProvenDisjointTargets()
    {
        ResourceAssemblySelector first = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Exact(new Version(1, 0, 0, 0)));
        ResourceAssemblySelector second = Assembly(
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Exact(new Version(2, 0, 0, 0)));

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            TargetTerminalOutcome(
                first,
                second,
                "example.target-version-disjoint"));
    }

    [Fact]
    public void Catalog_ConflictsAcrossCompatibleResolvedFieldSelectors()
    {
        ResourceEffectModelIdentity releaseModel =
            new("example.field-version-overlap.release");
        ResourceEffectModelIdentity moveModel =
            new("example.field-version-overlap.move");

        AssertConflict(
            ResourceEffectCatalogBuilder.Build(
                [
                    FieldPolicyTerminalModel(
                        releaseModel,
                        ResourceAssemblyVersionPolicy.Any,
                        "release(source=receiver.field[child],when=normal-return)"),
                    FieldPolicyTerminalModel(
                        moveModel,
                        ResourceAssemblyVersionPolicy.Exact(new Version(1, 0, 0, 0)),
                        "move(source=receiver.field[child],target=return,when=normal-return)"),
                ]));
    }

    [Fact]
    public void Catalog_AllowsTerminalEffectsOnProvenDisjointResolvedFields()
    {
        ResourceEffectModelIdentity releaseModel =
            new("example.field-version-disjoint.release");
        ResourceEffectModelIdentity moveModel =
            new("example.field-version-disjoint.move");

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [
                    FieldPolicyTerminalModel(
                        releaseModel,
                        ResourceAssemblyVersionPolicy.Exact(new Version(1, 0, 0, 0)),
                        "release(source=receiver.field[child],when=normal-return)"),
                    FieldPolicyTerminalModel(
                        moveModel,
                        ResourceAssemblyVersionPolicy.Exact(new Version(2, 0, 0, 0)),
                        "move(source=receiver.field[child],target=return,when=normal-return)"),
                ]));
    }

    [Fact]
    public void Catalog_DoesNotAssumeDistinctEnumNamesAreDisjoint()
    {
        ResourceEffectModelIdentity model = new("example.enum-alias");

        AssertConflict(
            ResourceEffectCatalogBuilder.Build(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [
                            "outcome(id=first,source=return,test=enum[Example.Disposition.Accepted])",
                            "outcome(id=second,source=return,test=enum[Example.Disposition.Success])",
                            "release(source=parameter[0],when=outcome[first])",
                            "move(source=parameter[0],target=return,when=outcome[second])",
                        ]),
                ]));
    }

    [Fact]
    public void Catalog_RejectsRepeatedTerminalReleaseAcrossCompletionPoints()
    {
        ResourceEffectModelIdentity model = new("example.repeated-release");

        AssertConflict(
            ResourceEffectCatalogBuilder.Build(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [
                            "release(source=parameter[0],when=entry)",
                            "release(source=parameter[0],when=normal-return)",
                        ]),
                ]));
    }

    [Fact]
    public void Catalog_CoalescesEquivalentReleaseAtOneCompletionPoint()
    {
        ResourceEffectModelIdentity first = new("example.release.first");
        ResourceEffectModelIdentity second = new("example.release.second");
        ResourceEffectCatalog catalog = Build(
            Model(
                first,
                SimpleOperationTarget(),
                ["release(source=parameter[0],when=normal-return)"]),
            Model(
                second,
                SimpleOperationTarget(),
                ["release(source=parameter[0],when=normal-return)"]));

        NormalizedResourceEffectDeclaration declaration =
            Assert.Single(catalog.Declarations);
        Assert.Equal(2, declaration.Provenances.Length);
    }

    [Fact]
    public void Catalog_ComposesNullAndExactTypeOutcomes()
    {
        ResourceEffectModelIdentity model = new("example.null-exact-type");

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [
                            "outcome(id=none,source=return,test=null)",
                            "outcome(id=some,source=return,test=type[Example.Result])",
                            "release(source=parameter[0],when=outcome[none])",
                            "move(source=parameter[0],target=return,when=outcome[some])",
                        ]),
                ]));
    }

    [Fact]
    public void Catalog_ComposesNullAndNonNullOutcomes()
    {
        ResourceEffectModelIdentity model = new("example.null-non-null");

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [
                    Model(
                        model,
                        SimpleOperationTarget(),
                        [
                            "outcome(id=none,source=return,test=null)",
                            "outcome(id=some,source=return,test=non-null)",
                            "release(source=parameter[0],when=outcome[none])",
                            "move(source=parameter[0],target=return,when=outcome[some])",
                        ]),
                ]));
    }

    [Fact]
    public void Catalog_ComposesIndependentModelLocalOperationSlots()
    {
        ResourceEffectModelIdentity poolModel = new("example.operation.pool");
        ResourceEffectModelIdentity sessionModel = new("example.operation.session");

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [
                    Model(
                        poolModel,
                        TwoDifferentParameterOperationTarget(),
                        [
                            "consume(kind=example.pool,source=parameter[0],target=operation[0])",
                            "release(source=operation[0],when=normal-return)",
                        ],
                        [Kind(poolModel, "example.pool", 0)]),
                    Model(
                        sessionModel,
                        TwoDifferentParameterOperationTarget(),
                        [
                            "consume(kind=example.session,source=parameter[1],target=operation[0])",
                            "move(source=operation[0],target=return,when=normal-return)",
                        ],
                        [Kind(sessionModel, "example.session", 0)]),
                ]));
    }

    [Fact]
    public void Catalog_CoalescesEquivalentOperationSlotsWithDifferentLocalIndexes()
    {
        ResourceEffectModelIdentity first = new("example.operation.first");
        ResourceEffectModelIdentity second = new("example.operation.second");
        ResourceEffectCatalog catalog = Build(
            Model(
                first,
                SimpleOperationTarget(),
                [
                    "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                    "release(source=operation[0],when=normal-return)",
                ],
                [Kind(first, "example.resource", 0)]),
            Model(
                second,
                SimpleOperationTarget(),
                [
                    "consume(kind=example.resource,source=parameter[0],target=operation[1])",
                    "release(source=operation[1],when=normal-return)",
                ],
                [Kind(second, "example.resource", 0)]));

        Assert.Equal(2, catalog.Declarations.Length);
        Assert.All(
            catalog.Declarations,
            declaration => Assert.Equal(2, declaration.Provenances.Length));
    }

    [Fact]
    public void Catalog_RejectsInconsistentModelLocalOperationSlotDefinitions()
    {
        ResourceEffectModelIdentity model = new("example.operation-inconsistent");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
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
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_ResolvesChainedModelLocalOperationSlots()
    {
        ResourceEffectModelIdentity model = new("example.operation-chain");

        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
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
    public void Catalog_RejectsCyclicModelLocalOperationSlotsWithoutThrowing()
    {
        ResourceEffectModelIdentity model = new("example.operation-cycle");
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
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
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_ReadmitsItsNormalizedFieldDeclarations()
    {
        ResourceEffectModelIdentity model = new("example.field-roundtrip");
        ResourceEffectCatalog original = Build(FieldRoundTripModel(model));
        ResourceEffectCatalog roundTrip = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                original.ResourceKinds,
                [],
                original.Declarations));

        Assert.Equal(original.Declarations, roundTrip.Declarations);
        Assert.Equal(original.Receipt.SemanticHash, roundTrip.Receipt.SemanticHash);
    }

    [Fact]
    public void Catalog_ReadmitsItsNormalizedOperationSlots()
    {
        ResourceEffectModelIdentity model = new("example.operation-roundtrip");
        ResourceEffectCatalog original = Build(
            Model(
                model,
                SimpleOperationTarget(),
                [
                    "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                    "release(source=operation[0],when=normal-return)",
                ],
                [Kind(model, "example.resource", 0)]));
        ResourceEffectCatalog roundTrip = Build(
            new ResourceEffectModelDefinition(
                ResourceEffectLanguageIdentity.Version1,
                model,
                original.ResourceKinds,
                [],
                original.Declarations));

        Assert.Equal(original.Declarations, roundTrip.Declarations);
        Assert.Equal(original.Receipt.SemanticHash, roundTrip.Receipt.SemanticHash);
    }

    [Fact]
    public void Catalog_RequiresFieldAliasesOnlyBeforeNormalization()
    {
        ResourceEffectModelIdentity parsedModel = new("example.field-parsed");
        ResourceEffectCatalogOutcome parsed = ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    parsedModel,
                    FieldTarget("Child"),
                    ["resource(kind=example.child,value=declared-field)"]),
            ]);
        Assert.Equal(
            ResourceEffectDiagnosticKind.InvalidTarget,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(parsed)
                .Diagnostics.Single().Diagnostic.Kind);

        ResourceEffectModelIdentity normalizedModel = new("example.field-normalized");
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [
                    new ResourceEffectModelDefinition(
                        ResourceEffectLanguageIdentity.Version1,
                        normalizedModel,
                        [],
                        [],
                        [
                            TypedFieldResource(
                                normalizedModel,
                                selector: null),
                        ]),
                ]));
        AssertTypedRejected(
            "example.field-normalized-alias",
            new ResourceEffect.Resource(
                new ResourceKindReference(
                    new ResourceKindIdentity("example.child")),
                ResourceDeclaredValueKind.DeclaredField,
                new ResourceEffectLocalIdentity("child")),
            FieldTarget("Child"),
            ResourceEffectDiagnosticKind.InvalidTarget);
    }

    [Fact]
    public void Catalog_RejectsFabricatedNormalizedOperationIdentity()
    {
        ResourceKindReference firstKind =
            new(new ResourceKindIdentity("example.first"));
        ResourceKindReference secondKind =
            new(new ResourceKindIdentity("example.second"));

        AssertTypedRejected(
            "example.operation-source-mismatch",
            new ResourceEffect.Consume(
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectLocation.ResolvedOperation(
                    new ResourceEffectLocation.Parameter(1),
                    firstKind),
                firstKind),
            TwoDifferentParameterOperationTarget(),
            ResourceEffectDiagnosticKind.InvalidTarget);
        AssertTypedRejected(
            "example.operation-kind-mismatch",
            new ResourceEffect.Consume(
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectLocation.ResolvedOperation(
                    new ResourceEffectLocation.Parameter(0),
                    secondKind),
                firstKind),
            TwoDifferentParameterOperationTarget(),
            ResourceEffectDiagnosticKind.InvalidTarget);
    }

    [Fact]
    public void Catalog_RejectsModelLocalAliasesInNormalizedDeclarations()
    {
        ResourceEffectModelIdentity operationModel =
            new("example.normalized-operation-alias");
        ResourceEffectCatalogOutcome operation = ResourceEffectCatalogBuilder.Build(
            [
                new ResourceEffectModelDefinition(
                    ResourceEffectLanguageIdentity.Version1,
                    operationModel,
                    [Kind(operationModel, "example.resource", 0)],
                    [
                        new ResourceEffectTargetDeclaration(
                            SimpleOperationTarget(),
                            [
                                Source(
                                    operationModel,
                                    operationModel.Value,
                                    0,
                                    "consume(kind=example.resource,source=parameter[0],target=operation[0])"),
                            ]),
                    ],
                    [
                        Typed(
                            operationModel,
                            SimpleOperationTarget(),
                            new ResourceEffect.Release(
                                new ResourceEffectLocation.Operation(0),
                                new ResourceEffectCompletion.NormalReturn(),
                                null,
                                null,
                                null),
                            1),
                    ]),
            ]);
        Assert.Equal(
            ResourceEffectDiagnosticKind.InvalidLocation,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(operation)
                .Diagnostics.Single().Diagnostic.Kind);

        ResourceEffectModelIdentity fieldModel =
            new("example.normalized-field-alias");
        ResourceEffectCatalogOutcome field = ResourceEffectCatalogBuilder.Build(
            [
                new ResourceEffectModelDefinition(
                    ResourceEffectLanguageIdentity.Version1,
                    fieldModel,
                    [],
                    [
                        new ResourceEffectTargetDeclaration(
                            FieldTarget("Child"),
                            [
                                Source(
                                    fieldModel,
                                    fieldModel.Value,
                                    0,
                                    "resource(kind=example.child,value=declared-field,selector=child)"),
                            ]),
                    ],
                    [
                        Typed(
                            fieldModel,
                            SimpleOperationTarget(),
                            new ResourceEffect.Pass(
                                new ResourceEffectLocation.Field(
                                    new ResourceEffectLocation.Receiver(),
                                    new ResourceEffectLocalIdentity("child")),
                                new ResourceEffectLocation.Return(),
                                null),
                            1),
                    ]),
            ]);
        Assert.Equal(
            ResourceEffectDiagnosticKind.InvalidLocation,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(field)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    [Fact]
    public void Catalog_RevalidatesParsedResolvedFieldVariablesAgainstOuterTarget()
    {
        ResourceEffectModelIdentity invalidModel = new("example.parsed-field-unbound");
        ResourceEffectCatalogOutcome invalid = ResourceEffectCatalogBuilder.Build(
            [
                ParsedGenericFieldModel(
                    invalidModel,
                    SimpleOperationTarget()),
            ]);
        Assert.Equal(
            ResourceEffectDiagnosticKind.UnboundGenericVariable,
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(invalid)
                .Diagnostics.Single().Diagnostic.Kind);

        ResourceEffectModelIdentity validModel = new("example.parsed-field-bound");
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [
                    ParsedGenericFieldModel(
                        validModel,
                        OpenGenericOperationTarget()),
                ]));
    }

    [Fact]
    public void Catalog_ChargesExplicitResourceKindsAfterCoalescing()
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
    public void Catalog_ChargesParsedResourceKindsAfterCoalescing()
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
    public void Catalog_ChargesTypedResourceKindsAfterCoalescing()
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
    public void Catalog_CoalescesExplicitParsedAndTypedKindsBeforeBudgeting()
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

        ResourceEffectCatalog catalog = Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [definition],
                new ResourceEffectWorkLimits(maxResourceKindsPerModel: 1))).Catalog;
        Assert.Single(catalog.ResourceKinds);
        Assert.Equal(3, catalog.ResourceKinds.Single().Provenances.Length);
    }

    static ResourceEffectModelDefinition TerminalModel(bool disjointOutcomes)
    {
        ResourceEffectModelIdentity model = new("example.terminal");
        string secondTest = disjointOutcomes ? "bool[false]" : "bool[true]";
        return Model(
            model,
            SimpleOperationTarget(),
            [
                "consume(kind=example.resource,source=parameter[0],target=operation[0])",
                "outcome(id=release,source=return,test=bool[true])",
                $"outcome(id=move,source=return,test={secondTest})",
                "release(kind=example.resource,source=operation[0],when=outcome[release])",
                "move(kind=example.resource,source=operation[0],target=return,when=outcome[move])",
            ],
            [Kind(model, "example.resource", 0)]);
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

    static ResourceEffectModelDefinition FieldPolicyTerminalModel(
        ResourceEffectModelIdentity model,
        ResourceAssemblyVersionPolicy fieldVersion,
        string terminal)
        => new(
            ResourceEffectLanguageIdentity.Version1,
            model,
            [],
            [
                new ResourceEffectTargetDeclaration(
                    FieldTarget("Child", fieldVersion),
                    [
                        Source(
                            model,
                            model.Value,
                            0,
                            "resource(kind=example.field-resource,value=declared-field,selector=child)"),
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

    static NormalizedResourceEffectDeclaration TypedResource(
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

    static NormalizedResourceEffectDeclaration Typed(
        ResourceEffectModelIdentity model,
        ResourceEffectTargetSelector target,
        ResourceEffect effect,
        int ordinal)
        => new(
            target,
            effect,
            [
                new ResourceDeclarationProvenance(
                    model,
                    ResourceDeclarationAuthority.ProductShipped,
                    new InertString(TextPolicy.Field, model.Value + ".typed"),
                    ordinal),
            ]);

    static NormalizedResourceEffectDeclaration TypedFieldResource(
        ResourceEffectModelIdentity model,
        ResourceEffectLocalIdentity? selector)
        => new(
            FieldTarget("Child"),
            new ResourceEffect.Resource(
                new ResourceKindReference(
                    new ResourceKindIdentity("example.child")),
                ResourceDeclaredValueKind.DeclaredField,
                selector),
            [
                new ResourceDeclarationProvenance(
                    model,
                    ResourceDeclarationAuthority.ProductShipped,
                    new InertString(TextPolicy.Field, model.Value + ".typed"),
                    0),
            ]);

    static void AssertResourceKindBoundary(
        ResourceEffectModelDefinition model,
        int exact)
    {
        Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(
                [model],
                new ResourceEffectWorkLimits(
                    maxResourceKindsPerModel: exact)));
        var exceeded = Assert.IsType<ResourceEffectCatalogOutcome.WorkLimitExceeded>(
            ResourceEffectCatalogBuilder.Build(
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
        var declaration = new NormalizedResourceEffectDeclaration(
            target ?? SimpleOperationTarget(),
            effect,
            [
                new ResourceDeclarationProvenance(
                    model,
                    ResourceDeclarationAuthority.ProductShipped,
                    new InertString(TextPolicy.Field, model.Value),
                    0),
            ]);
        ResourceEffectCatalogOutcome outcome = ResourceEffectCatalogBuilder.Build(
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
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome)
                .Diagnostics.Single().Diagnostic.Kind);
    }

    static ResourceEffectCatalogOutcome GuardPolicyOutcome(
        ResourceAssemblySelector first,
        ResourceAssemblySelector second,
        string modelValue)
    {
        ResourceEffectModelIdentity model = new(modelValue);
        return ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    model,
                    GuardTarget(
                        Named(first, "Example", "Value"),
                        Named(second, "Example", "Value")),
                    [
                        "operation(boundary=transparent,throws=never,guard=exact-type[parameter[0];signature-parameter[0]])",
                        "operation(boundary=ordinary,throws=possible,guard=exact-type[parameter[0];signature-parameter[1]])",
                    ]),
            ]);
    }

    static ResourceEffectCatalogOutcome TargetTerminalOutcome(
        ResourceAssemblySelector first,
        ResourceAssemblySelector second,
        string modelValue)
    {
        ResourceEffectModelIdentity firstModel = new(modelValue + ".first");
        ResourceEffectModelIdentity secondModel = new(modelValue + ".second");
        return ResourceEffectCatalogBuilder.Build(
            [
                Model(
                    firstModel,
                    OperationTargetForDeclaringAssembly(first),
                    ["release(source=parameter[0],when=normal-return)"]),
                Model(
                    secondModel,
                    OperationTargetForDeclaringAssembly(second),
                    ["move(source=parameter[0],target=return,when=normal-return)"]),
            ]);
    }

    static ResourceEffectCatalog Build(params ResourceEffectModelDefinition[] models)
        => Assert.IsType<ResourceEffectCatalogOutcome.Constructed>(
            ResourceEffectCatalogBuilder.Build(models)).Catalog;

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
            new ResourceDeclarationProvenance(
                model,
                ResourceDeclarationAuthority.ProductShipped,
                new InertString(TextPolicy.Field, source),
                ordinal));

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

    static ResourceEffectTargetSelector GenericMethodTarget()
        => new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                Named("Owner"),
                "Generic",
                ResourceEffectMemberKind.Method,
                isStatic: false,
                genericArity: 2,
                ResourceEffectCallingConvention.Default,
                hasThis: true,
                explicitThis: false,
                [
                    new ResourceEffectParameterSelector(
                        new ResourceTypeExpression.Variable(
                            new ResourceEffectGenericVariable(
                                ResourceEffectGenericVariableKind.Method,
                                0)),
                        ResourceEffectRefKind.Value),
                    new ResourceEffectParameterSelector(
                        new ResourceTypeExpression.Variable(
                            new ResourceEffectGenericVariable(
                                ResourceEffectGenericVariableKind.Method,
                                1)),
                        ResourceEffectRefKind.Value),
                ],
                Named("Object")));

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

    static ResourceEffectTargetSelector GuardTarget(
        ResourceTypeExpression first,
        ResourceTypeExpression second)
        => OperationTarget(
            "Guarded",
            first,
            second,
            returnType: Named("Object"));

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

    static ResourceEffectTargetSelector OperationTargetForDeclaringAssembly(
        ResourceAssemblySelector assembly)
        => OperationTargetWithDeclaring(
            Named(assembly, "Example", "Owner"),
            "Transform");

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

    static void AssertConflict(ResourceEffectCatalogOutcome outcome)
        => Assert.All(
            Assert.IsType<ResourceEffectCatalogOutcome.Rejected>(outcome).Diagnostics,
            diagnostic => Assert.Equal(
                ResourceEffectDiagnosticKind.ConflictingDeclaration,
                diagnostic.Diagnostic.Kind));

    static void AssertLimit(
        ResourceEffectParseOutcome outcome,
        ResourceEffectWorkLimitKind kind)
        => Assert.Equal(
            kind,
            Assert.IsType<ResourceEffectParseOutcome.WorkLimitExceeded>(outcome).LimitKind);

    static void AssertLimit(
        ResourceEffectCatalogOutcome outcome,
        ResourceEffectWorkLimitKind kind)
        => Assert.Equal(
            kind,
            Assert.IsType<ResourceEffectCatalogOutcome.WorkLimitExceeded>(outcome).LimitKind);
}
