using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ClassicAsyncReconstructionPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Task = TypeRef.CoreLib(
        "System.Threading.Tasks",
        "Task");
    static readonly TypeRef StateMachine = TypeRef.Definition(
        "Synthetic",
        "Samples",
        "Outer+<Fake>d__0");
    static readonly TypeRef Builder = TypeRef.Definition(
        "Synthetic",
        "Samples",
        "BuilderLike");
    static readonly MetadataMethodAddress KickoffAddress = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        MetadataTokens.MethodDefinitionHandle(1));
    static readonly MetadataMethodAddress ExecutionAddress = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        MetadataTokens.MethodDefinitionHandle(2));

    [Fact]
    public void UnstampedSupportLookalike_IsNotEdited()
    {
        IrFunction function = BuildSupportLookalike();
        string before = IrPrinter.Dump(function);

        new ClassicAsyncReconstructionPass().Run(
            function,
            PassContext.None);

        Assert.Equal(before, IrPrinter.Dump(function));
    }

    [Fact]
    public void UnstampedKickoffLookalike_DoesNotReachImport()
    {
        IrFunction function = BuildKickoffLookalike();
        bool attempted = false;
        var context = PassContext.ForImport(_ =>
        {
            attempted = true;
            return null;
        });

        new ClassicAsyncReconstructionPass().Run(function, context);

        Assert.False(attempted);
    }

    [Fact]
    public void ResolvedClassicKickoff_ImportsOwnerIssuedMoveNext()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, "AwaitVoid");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));

        IrPasses.Run(function, IrPasses.Default, context);

        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<StateMachineRelationshipResult.Resolved>(
            evidence.Relationship);
        Assert.True(resolved.Relationship.TryGetMethod(
            StateMachineMethodRole.MoveNext,
            out var moveNext));
        var decision = Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        Assert.Equal(moveNext, decision.Plan.Machine.Execution);
        Assert.Same(
            evidence.AcquisitionGuard,
            decision.Plan.Machine.AcquisitionGuard);
        Assert.Equal(1, PlanningSession(evidence).PreparationCount);
        Assert.True(function.RequiresAsyncBodyModifier);
    }

    [Fact]
    public void GenericStateMachine_UsesOwnerDefinitionIdentity()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, "AwaitGeneric");

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncOutcome.Reconstructed>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.IncludeAsync,
            function.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void GenericContainingTypeAndMethodMapFieldTypeParameters()
    {
        using var source = OpenClassicFixture();
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                "ILInspector.Decompiler.Fixtures.ClassicAsync.GenericAsyncFixtures`1",
                "AwaitGeneric"));

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncOutcome.Reconstructed>(
            function.ClassicAsyncOutcome);
        ClassicAsyncParameterBinding binding = Assert.Single(
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(
                    Assert.IsType<ClassicAsyncRelationshipEvidence>(
                        function.ClassicAsyncRelationship)))
                .Plan.Machine.ParameterBindings.Items);
        Assert.Equal("value", binding.FieldName);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            binding.FieldType.TypeArguments[0].Kind);
        Assert.Equal(0, binding.FieldType.TypeArguments[0]
            .GenericParameterIndex);
    }

    [Fact]
    public void AsyncVoid_DeclinesAsUnsupportedBuilder()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitAsyncVoid");

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.UnsupportedBuilder,
            outcome.Reason);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.ReplacedNarrowHandoff,
            outcome.KickoffDisposition);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            function.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void ResolvedClassicExecutionMethod_IsNotEdited()
    {
        using var source = OpenClassicFixture();
        IrFunction function = IrImporter.ImportAssembly(source)
            .Select(method => method.Function)
            .First(method => method.ClassicAsyncRelationship is
            {
                HostRole: ClassicAsyncHostRole.Execution,
                Relationship: StateMachineRelationshipResult.Resolved
                {
                    Relationship.Kind: StateMachineClaimKind.ClassicAsync,
                },
            });
        string before = IrPrinter.Dump(function);

        new ClassicAsyncReconstructionPass().Run(
            function,
            PassContext.ForImport(method => IrImporter.Import(source, method)));

        Assert.Equal(before, IrPrinter.Dump(function));
    }

    [Fact]
    public void UnsupportedResolvedClassic_PreservesKickoffAndNamesDecline()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitVoidThenReturn");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        IReadOnlyList<string> originalStatements = function.Body.Blocks[0]
            .Children
            .Select(SubtreeSignature)
            .ToList();

        new ClassicAsyncReconstructionPass().Run(function, context);

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
            outcome.Reason);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.PreservedOriginal,
            outcome.KickoffDisposition);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            function.ClassicAsyncDeclarationDisposition);
        Assert.Single(function.Body.Blocks);
        Assert.Equal(
            originalStatements,
            function.Body.Blocks[0]
                .Children
                .Skip(1)
                .Select(SubtreeSignature));
    }

    [Fact]
    public void NonNarrowDecline_PreservesEveryOriginalStatement()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitVoidThenReturn");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        var unexplained = new ExpressionStatement(new Call(
            new MethodRef(
                TypeRef.Definition("Synthetic", "Samples", "Effects"),
                "Observe",
                Void,
                [],
                HasThis: false),
            isVirtual: false,
            []));
        function.Body.Blocks[0].Add(unexplained);
        IReadOnlyList<string> originalStatements = function.Body.Blocks[0]
            .Children
            .Select(SubtreeSignature)
            .ToList();

        ClassicAsyncReconstructionPass.ApplyDecision(
            function,
            context,
            new ClassicAsyncDecision.Decline(
                ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
                ClassicAsyncKickoffDisposition.PreservedOriginal));

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.PreservedOriginal,
            outcome.KickoffDisposition);
        Assert.Equal(
            originalStatements,
            function.Body.Blocks[0]
                .Children
                .Skip(1)
                .Select(SubtreeSignature));
    }

    [Fact]
    public void ExactMoveNextAddress_IsBoundToItsAcquisition()
    {
        using var source = OpenClassicFixture();
        MethodRef requested = CaptureMoveNextRequest(source);
        using var otherSource = OpenClassicFixture();

        Assert.Null(IrImporter.Import(otherSource, requested));
    }

    [Fact]
    public void ExactMoveNextAddress_RejectsSymbolicSignatureMismatch()
    {
        using var source = OpenClassicFixture();
        MethodRef requested = CaptureMoveNextRequest(source);

        Assert.Null(IrImporter.Import(
            source,
            requested with { Name = "SetStateMachine" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KickoffLocalWithoutExactDefinitionIdentityDeclines(
        bool differentModule)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, "AwaitVoid");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        StoreField builderStore = Assert.Single(
            function.Descendants.OfType<StoreField>(),
            static store => store.Field.Name == "<>t__builder");
        var machineAddress = Assert.IsType<LoadLocalAddress>(
            builderStore.Instance);
        TypeRef machine = function.Locals[machineAddress.Index];
        Assert.NotNull(machine.DefinitionName);
        Assert.NotNull(machine.DefinitionModuleVersionId);
        TypeRef foreign = TypeRef.DefinitionWithResolution(
            machine.Assembly,
            machine.Namespace,
            machine.Name,
            machine.ValueTypeHint,
            machine.InlineArray,
            machine.EnclosingType,
            machine.DefinitionName,
            machine.ResolutionAssembly,
            definitionHandle: differentModule
                ? machine.DefinitionHandle
                : MetadataTokens.TypeDefinitionHandle(
                    MetadataTokens.GetRowNumber(
                        machine.DefinitionHandle)
                    + 1),
            definitionModuleVersionId: differentModule
                ? Guid.NewGuid()
                : machine.DefinitionModuleVersionId);
        function.ResetLocals(
            function.Locals.SetItem(
                machineAddress.Index,
                foreign),
            function.LocalNames);
        var evidence = Assert.IsType<
            ClassicAsyncRelationshipEvidence>(
                function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<
            StateMachineRelationshipResult.Resolved>(
                evidence.Relationship);

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                function,
                resolved.Relationship.StateMachineType,
                resolved.Relationship.StateMachineName,
                out _,
                out ClassicAsyncDeclineReason reason,
                out bool narrow));
        Assert.Equal(
            ClassicAsyncDeclineReason.KickoffMachineMismatch,
            reason);
        Assert.False(narrow);
    }

    [Fact]
    public void SwappedKickoffParameterCopiesDecline()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "TwoSequentialAwaits");
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        RunBeforeClassicAsync(function, context);
        StoreField first = Assert.Single(
            function.Descendants.OfType<StoreField>(),
            static store => store.Field.Name == "a");
        StoreField second = Assert.Single(
            function.Descendants.OfType<StoreField>(),
            static store => store.Field.Name == "b");
        var firstSource = Assert.IsType<LoadArgument>(first.Value);
        var secondSource = Assert.IsType<LoadArgument>(second.Value);
        firstSource.ReplaceWith(new LoadArgument(
            secondSource.Index,
            secondSource.Name,
            secondSource.Type)
        {
            IsDynamic = secondSource.IsDynamic,
            ArrayElementIsDynamic =
                secondSource.ArrayElementIsDynamic,
        });
        secondSource.ReplaceWith(new LoadArgument(
            firstSource.Index,
            firstSource.Name,
            firstSource.Type)
        {
            IsDynamic = firstSource.IsDynamic,
            ArrayElementIsDynamic =
                firstSource.ArrayElementIsDynamic,
        });
        var evidence = Assert.IsType<
            ClassicAsyncRelationshipEvidence>(
                function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<
            StateMachineRelationshipResult.Resolved>(
                evidence.Relationship);

        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                function,
                resolved.Relationship.StateMachineType,
                resolved.Relationship.StateMachineName,
                out _,
                out _,
                out bool narrow));
        Assert.False(narrow);
    }

    [Fact]
    public void ForeignCreateMakesKickoffNonNarrow()
    {
        using var source = OpenClassicFixture();
        IrFunction function = PreparedKickoff(source, "AwaitVoid");
        Call create = Assert.Single(
            function.Descendants.OfType<Call>(),
            static call => call.Callee.Name == "Create");

        ReplaceCallee(
            create,
            create.Callee with
            {
                DeclaringType = ForeignBuilderFactory(),
            });

        AssertKickoffIsNonNarrow(function);
        function.CheckInvariant();
    }

    [Fact]
    public void ForeignStartMakesKickoffNonNarrow()
    {
        using var source = OpenClassicFixture();
        IrFunction function = PreparedKickoff(source, "AwaitVoid");
        Call start = Assert.Single(
            function.Descendants.OfType<Call>(),
            static call => call.Callee.Name == "Start");

        ReplaceCallee(
            start,
            start.Callee with
            {
                DeclaringType = ForeignBuilderFactory(),
            });

        AssertKickoffIsNonNarrow(function);
        function.CheckInvariant();
    }

    [Fact]
    public void ForeignTaskAccessorMakesKickoffNonNarrow()
    {
        using var source = OpenClassicFixture();
        IrFunction function = PreparedKickoff(source, "AwaitVoid");
        LoadProperty task = Assert.Single(
            function.Descendants.OfType<LoadProperty>(),
            static property => property.PropertyName == "Task");
        IrExpression instance = Assert.IsAssignableFrom<IrExpression>(
            task.Instance);
        instance.Detach();
        var replacement = new LoadProperty(
            task.Accessor with
            {
                DeclaringType = ForeignBuilderFactory(),
            },
            instance,
            [])
        {
            IsVirtual = task.IsVirtual,
        };
        task.ReplaceWith(replacement);

        AssertKickoffIsNonNarrow(function);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Start")]
    [InlineData("get_Task")]
    public void CustomModifiedBuilderMemberMakesKickoffNonNarrow(
        string memberName)
    {
        using var source = OpenClassicFixture();
        IrFunction function = PreparedKickoff(source, "AwaitVoid");
        TypeRef modifier = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "IsVolatile");

        ReplaceBuilderMember(
            function,
            memberName,
            method => memberName switch
            {
                "Start" => method with
                {
                    ParameterTypes =
                    [
                        method.ParameterTypes[0]
                            .WithCustomModifier(
                                modifier,
                                isRequired: true),
                    ],
                },
                _ => method with
                {
                    ReturnType = method.ReturnType
                        .WithCustomModifier(
                            modifier,
                            isRequired: true),
                },
            });

        AssertKickoffIsNonNarrow(function);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Start")]
    [InlineData("get_Task")]
    public void ExactAddressedBuilderMemberMakesKickoffNonNarrow(
        string memberName)
    {
        using var source = OpenClassicFixture();
        IrFunction function = PreparedKickoff(source, "AwaitVoid");

        ReplaceBuilderMember(
            function,
            memberName,
            method => method with
            {
                ExactDefinitionAddress = new(
                    source.ModuleVersionId,
                    MetadataTokens.MethodDefinitionHandle(1)),
                ExactDefinitionAcquisitionGuard = new object(),
            });

        AssertKickoffIsNonNarrow(function);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InconsistentBuilderMemberProvenanceMakesKickoffNonNarrow(
        bool hasAddress,
        bool hasGuard)
    {
        using var source = OpenClassicFixture();
        IrFunction function = PreparedKickoff(source, "AwaitVoid");

        ReplaceBuilderMember(
            function,
            "Create",
            method => method with
            {
                ExactDefinitionAddress = hasAddress
                    ? new(
                        source.ModuleVersionId,
                        MetadataTokens.MethodDefinitionHandle(1))
                    : null,
                ExactDefinitionAcquisitionGuard =
                    hasGuard ? new object() : null,
            });

        AssertKickoffIsNonNarrow(function);
        function.CheckInvariant();
    }

    [Fact]
    public void SameExactTypeIncludesOrderedRecursiveCustomModifiers()
    {
        TypeRef firstModifier = TypeRef.Definition(
            "Synthetic",
            "ReviewRepro",
            "FirstModifier");
        TypeRef secondModifier = TypeRef.Definition(
            "Synthetic",
            "ReviewRepro",
            "SecondModifier");
        TypeRef required = TypeRef.ByRef(
            Int32.WithCustomModifier(
                firstModifier,
                isRequired: true));
        TypeRef optional = TypeRef.ByRef(
            Int32.WithCustomModifier(
                firstModifier,
                isRequired: false));
        TypeRef ordered = Int32
            .WithCustomModifier(
                firstModifier,
                isRequired: true)
            .WithCustomModifier(
                secondModifier,
                isRequired: false);
        TypeRef reversed = Int32
            .WithCustomModifier(
                secondModifier,
                isRequired: false)
            .WithCustomModifier(
                firstModifier,
                isRequired: true);
        MetadataTypeDefinitionName exactName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "ReviewRepro",
                    ["ExactModifier"]))
                .Name;
        TypeRef exactModifier = ExactModifier(Guid.NewGuid());
        TypeRef foreignExactModifier = ExactModifier(Guid.NewGuid());

        Assert.True(
            ClassicAsyncReconstructionPass.SameExactType(
                required,
                required));
        Assert.False(
            ClassicAsyncReconstructionPass.SameExactType(
                required,
                optional));
        Assert.False(
            ClassicAsyncReconstructionPass.SameExactType(
                ordered,
                reversed));
        Assert.False(
            ClassicAsyncReconstructionPass.SameExactType(
                Int32.WithCustomModifier(
                    exactModifier,
                    isRequired: true),
                Int32.WithCustomModifier(
                    foreignExactModifier,
                    isRequired: true)));

        TypeRef ExactModifier(Guid moduleVersionId)
            => TypeRef.DefinitionWithResolution(
                "Synthetic",
                "ReviewRepro",
                "ExactModifier",
                ValueTypeHint.ReferenceType,
                MetadataFactState.Unknown,
                enclosingType: null,
                definitionName: exactName,
                resolutionAssembly: null,
                definitionHandle:
                    MetadataTokens.TypeDefinitionHandle(1),
                definitionModuleVersionId: moduleVersionId);
    }

    [Fact]
    public void CompetingAwaiterDefinitionsDecline()
    {
        using var source = OpenClassicFixture();
        MethodRef request = CaptureMoveNextRequest(
            source,
            "TwoSequentialAwaits");
        IrFunction moveNext = Assert.IsType<IrFunction>(
            IrImporter.Import(source, request));
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));
        IrPasses.Run(
            moveNext,
            IrPasses.ForReconstruction<
                ClassicAsyncReconstructionPass>(),
            context);
        List<StoreLocal> awaiterStores =
        [
            .. moveNext.Descendants
                .OfType<StoreLocal>()
                .Where(static store => store.Value is Call
                {
                    Callee.Name: "GetAwaiter",
                }),
        ];
        Assert.True(awaiterStores.Count >= 2);
        StoreLocal first = awaiterStores[0];
        StoreLocal second = awaiterStores[1];
        var thenArm = new Block(0);
        thenArm.Add((StoreLocal)first.Clone());
        var elseArm = new Block(0);
        elseArm.Add(new StoreLocal(
            first.Index,
            first.Type,
            (IrExpression)second.Value.Clone()));
        first.ReplaceWith(new IfStatement(
            new Constant(
                true,
                TypeRef.CoreLib("System", "Boolean")),
            thenArm,
            elseArm));
        Call getResult = moveNext.Descendants
            .OfType<Call>()
            .First(static call =>
                call.Callee.Name == "GetResult");

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out _));
    }

    [Fact]
    public void AwaitSourceReachingDefinitionsRejectBackedgeAfterUse()
    {
        TypeRef awaiter = TypeRef.Definition(
            "Synthetic",
            "Samples",
            "TaskAwaiter");
        var getAwaiter = new MethodRef(
            Task,
            "GetAwaiter",
            awaiter,
            [Task],
            HasThis: false);
        var firstStore = new StoreLocal(
            0,
            awaiter,
            new Call(
                getAwaiter,
                isVirtual: false,
                [new LoadArgument(0, "a", Task)]));
        var alternateStore = new StoreLocal(
            0,
            awaiter,
            new Call(
                getAwaiter,
                isVirtual: false,
                [new LoadArgument(1, "b", Task)]));
        var getResult = new Call(
            new MethodRef(
                awaiter,
                "GetResult",
                Int32,
                [],
                HasThis: true),
            isVirtual: false,
            [new LoadLocalAddress(0, awaiter)]);
        var entry = new Block(0);
        entry.Add(firstStore);
        entry.Add(new Branch(4));
        var header = new Block(4);
        header.Add(new ExpressionStatement(getResult));
        header.Add(new ConditionalBranch(
            new Constant(true, Boolean),
            targetOffset: 12));
        var exit = new Block(8);
        exit.Add(new Return(null));
        var backedge = new Block(12);
        backedge.Add(alternateStore);
        backedge.Add(new Branch(4));
        var body = new BlockContainer();
        foreach (Block block in
            (Block[])[entry, header, exit, backedge])
        {
            body.Add(block);
        }
        var function = new IrFunction(
            "MoveNext",
            StateMachine,
            new MethodSignature(
                Void,
                [
                    new Parameter("a", Task),
                    new Parameter("b", Task),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [awaiter],
            body);

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                function,
                getResult,
                out _,
                out _));
        function.CheckInvariant();
    }

    [Fact]
    public void SequentialAwaiterLocalReuseHasUniqueReachingSources()
    {
        using var source = OpenClassicFixture();
        MethodRef request = CaptureMoveNextRequest(
            source,
            "TwoSequentialAwaits");
        IrFunction moveNext = Assert.IsType<IrFunction>(
            IrImporter.Import(source, request));
        IrPasses.Run(
            moveNext,
            IrPasses.ForReconstruction<
                ClassicAsyncReconstructionPass>(),
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        Call[] getResults =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>()
                .Where(static call =>
                    call.Callee.Name == "GetResult"),
        ];
        Assert.Equal(2, getResults.Length);

        Assert.True(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResults[0],
                out _,
                out IrExpression first));
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResults[1],
                out _,
                out IrExpression second));
        Assert.Equal("a", Assert.IsType<LoadField>(first).Field.Name);
        Assert.Equal("b", Assert.IsType<LoadField>(second).Field.Name);
        moveNext.CheckInvariant();
    }

    [Fact]
    public void HoistedResultRemappingRequiresExactFieldDefinition()
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "TwoSequentialAwaits");
        IrFunction kickoffFunction =
            PreparedKickoff(source, "TwoSequentialAwaits");
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoffFunction.ClassicAsyncRelationship)
                    .Relationship)
                .Relationship;
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoffFunction,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out ClassicAsyncReconstructionPass.Kickoff kickoff,
                out _,
                out _));
        Call keepAlive = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "KeepAlive");
        LoadField hoistedLoad = Assert.Single(
            keepAlive.Descendants.OfType<LoadField>());
        ExactFieldDefinitionAddress address =
            Assert.IsType<ExactFieldDefinitionAddress>(
                hoistedLoad.Field.ExactDefinitionAddress);
        Assert.NotNull(
            hoistedLoad.Field.ExactDefinitionAcquisitionGuard);
        var alias = hoistedLoad.Field with
        {
            ExactDefinitionAddress = address with
            {
                MetadataToken = address.MetadataToken + 1,
            },
        };
        var hoisted = new Dictionary<
            string,
            (FieldRef Field, int Index, TypeRef Type)>
        {
            [alias.Name] = (alias, 0, alias.Type),
        };
        Dictionary<int, (int Index, TypeRef Type)> locals =
            keepAlive.Descendants
                .OfType<LoadLocal>()
                .ToDictionary(
                    static load => load.Index,
                    static load => (load.Index, load.Type));

        Assert.Null(
            ClassicAsyncReconstructionPass.CloneAndRemap(
                keepAlive,
                kickoff,
                hoisted,
                locals));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void AwaitSourceRejectsDiamondWithTwoResumeDefinitions()
    {
        using var source = OpenClassicFixture();
        IrFunction original = PreparedMoveNext(source, "AwaitVoid");
        Call originalGetResult = Assert.Single(
            original.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "GetResult");
        var getResultAddress = Assert.IsType<LoadLocalAddress>(
            Assert.Single(originalGetResult.Arguments));
        StoreLocal candidate = Assert.Single(
            original.DescendantsOutsideNestedFunctions
                .OfType<StoreLocal>(),
            store => store.Index == getResultAddress.Index
                && store.Value is Call
                {
                    Callee.Name: "GetAwaiter",
                });
        StoreLocal resume = Assert.Single(
            original.DescendantsOutsideNestedFunctions
                .OfType<StoreLocal>(),
            store => store.Index == getResultAddress.Index
                && store.Value is LoadField
                {
                    Field.Name: var name,
                }
                && name.StartsWith(
                    "<>u__",
                    StringComparison.Ordinal));
        StoreField spill = Assert.Single(
            original.DescendantsOutsideNestedFunctions
                .OfType<StoreField>(),
            store => store.Value is LoadLocal
                {
                    Index: var index,
                }
                && index == getResultAddress.Index
                && store.Field.Name.StartsWith(
                    "<>u__",
                    StringComparison.Ordinal));
        ExpressionStatement callback = Assert.Single(
            original.DescendantsOutsideNestedFunctions
                .OfType<ExpressionStatement>(),
            static statement => statement.Expression is Call
            {
                Callee.Name:
                    "AwaitUnsafeOnCompleted"
                    or "AwaitOnCompleted",
            });

        var dispatch = new Block(0);
        dispatch.Add(new ConditionalBranch(
            new Constant(true, Boolean),
            targetOffset: 8));
        var splitResumes = new Block(4);
        splitResumes.Add(new ConditionalBranch(
            new Constant(true, Boolean),
            targetOffset: 16));
        var firstResume = new Block(12);
        firstResume.Add((StoreLocal)resume.Clone());
        firstResume.Add(new Branch(24));
        var secondResume = new Block(16);
        secondResume.Add((StoreLocal)resume.Clone());
        secondResume.Add(new Branch(24));
        var sourcePath = new Block(8);
        sourcePath.Add((StoreLocal)candidate.Clone());
        sourcePath.Add(new ConditionalBranch(
            new Constant(true, Boolean),
            targetOffset: 24));
        var suspension = new Block(20);
        suspension.Add((StoreField)spill.Clone());
        suspension.Add((ExpressionStatement)callback.Clone());
        suspension.Add(new Return(null));
        var use = new Block(24);
        var getResultStatement = new ExpressionStatement(
            (Call)originalGetResult.Clone());
        use.Add(getResultStatement);
        use.Add(new Return(null));
        var body = new BlockContainer();
        foreach (Block block in
            (Block[])
            [
                dispatch,
                splitResumes,
                firstResume,
                secondResume,
                sourcePath,
                suspension,
                use,
            ])
        {
            body.Add(block);
        }
        var function = new IrFunction(
            original.Name,
            original.DeclaringType,
            original.Signature,
            original.Locals,
            body);
        Call getResult =
            Assert.IsType<Call>(getResultStatement.Expression);

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                function,
                getResult,
                out _,
                out _));
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("GetAwaiter")]
    [InlineData("IsCompleted")]
    [InlineData("GetResult")]
    [InlineData("GetResultCustomModifier")]
    public void AwaitProtocolRequiresExactCorrelatedMembers(
        string memberName)
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "AwaitValue");
        Call getResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "GetResult");
        TypeRef foreign = TypeRef.Definition(
            "Foreign",
            "ReviewRepro",
            "AwaitProtocol");

        switch (memberName)
        {
            case "GetAwaiter":
                Call getAwaiter = Assert.Single(
                    moveNext.DescendantsOutsideNestedFunctions
                        .OfType<Call>(),
                    static call =>
                        call.Callee.Name == "GetAwaiter");
                TypeRef receiverType =
                    getAwaiter.Arguments[0].ResultType!;
                ReplaceCallee(
                    getAwaiter,
                    getAwaiter.Callee with
                    {
                        DeclaringType = foreign,
                        HasThis = false,
                        ParameterTypes = [receiverType],
                        ExactDefinitionAddress = null,
                        ExactDefinitionAcquisitionGuard = null,
                    });
                break;
            case "IsCompleted":
                LoadProperty completed = Assert.Single(
                    moveNext.DescendantsOutsideNestedFunctions
                        .OfType<LoadProperty>(),
                    static property =>
                        property.Accessor.Name
                            == "get_IsCompleted");
                IrExpression instance =
                    Assert.IsAssignableFrom<IrExpression>(
                        completed.Instance);
                instance.Detach();
                completed.ReplaceWith(new LoadProperty(
                    completed.Accessor with
                    {
                        DeclaringType = foreign,
                        ExactDefinitionAddress = null,
                        ExactDefinitionAcquisitionGuard = null,
                    },
                    instance,
                    [])
                {
                    IsVirtual = completed.IsVirtual,
                });
                break;
            case "GetResult":
                TypeRef resultReceiverType =
                    getResult.Arguments[0].ResultType!;
                getResult = ReplaceCallee(
                    getResult,
                    getResult.Callee with
                    {
                        DeclaringType = foreign,
                        HasThis = false,
                        ParameterTypes = [resultReceiverType],
                        ExactDefinitionAddress = null,
                        ExactDefinitionAcquisitionGuard = null,
                    });
                break;
            case "GetResultCustomModifier":
                TypeRef modifier = TypeRef.CoreLib(
                    "System.Runtime.CompilerServices",
                    "IsVolatile");
                getResult = ReplaceCallee(
                    getResult,
                    getResult.Callee with
                    {
                        ReturnType =
                            getResult.Callee.ReturnType
                                .WithCustomModifier(
                                    modifier,
                                    isRequired: true),
                    });
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(memberName));
        }

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out _));
        moveNext.CheckInvariant();
    }

    [Theory]
    [InlineData("AwaitVoid")]
    [InlineData("AwaitGeneric")]
    [InlineData("AwaitValueTask")]
    public void AuthenticatedAwaitProtocolIsAccepted(
        string methodName)
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, methodName);
        Call getResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "GetResult");

        Assert.True(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out _));
    }

    [Fact]
    public void CompletionBranchMustDefineAwaitCfgEdges()
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "AwaitVoid");
        Call getResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "GetResult");
        StoreLocal getAwaiter = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<StoreLocal>(),
            static store => store.Value is Call
            {
                Callee.Name: "GetAwaiter",
            });
        var sourceBlock = Assert.IsType<Block>(
            getAwaiter.Parent);
        var completion = Assert.IsType<ConditionalBranch>(
            sourceBlock.Children[^1]);
        sourceBlock.Add(new ConditionalBranch(
            new Constant(true, Boolean),
            completion.TargetOffset));

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out _));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void AwaitCompletionBranchRequiresMatchingPolarity()
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "AwaitVoid");
        Call getResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "GetResult");
        LoadProperty completed = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<LoadProperty>(),
            static property =>
                property.Accessor.Name
                    == "get_IsCompleted");
        completed.ReplaceWith(new LogicalNot(
            (IrExpression)completed.Clone()));

        Assert.False(
            ClassicAsyncReconstructionPass.TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out _));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void BuilderStorageMustBeCanonical()
    {
        TypeRef modifier = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "IsVolatile");
        TypeRef builder = TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                "AsyncTaskMethodBuilder")
            .WithCustomModifier(
                modifier,
                isRequired: true);

        Assert.False(
            ClassicAsyncReconstructionPass
                .TryAuthenticateBuilderStorage(
                    builder,
                    Task,
                    out _));
    }

    [Fact]
    public void CallbackBuilderMustMatchKickoffBuilder()
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "AwaitVoid");
        TypeRef valueTaskBuilder = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "AsyncValueTaskMethodBuilder");
        Call[] callbacks =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>()
                .Where(static call =>
                    call.Callee.Name is
                        "SetResult"
                        or "SetException"
                        or "AwaitUnsafeOnCompleted"
                        or "AwaitOnCompleted"),
        ];
        Assert.NotEmpty(callbacks);

        Call? setResult = null;
        foreach (Call callback in callbacks)
        {
            var receiver = Assert.IsType<LoadFieldAddress>(
                callback.Arguments[0]);
            IrExpression? instance = receiver.Instance;
            instance?.Detach();
            receiver.ReplaceWith(new LoadFieldAddress(
                receiver.Field with
                {
                    Type = valueTaskBuilder,
                },
                instance));
            Call replacement = ReplaceCallee(
                callback,
                callback.Callee with
                {
                    DeclaringType = valueTaskBuilder,
                });
            if (replacement.Callee.Name == "SetResult")
                setResult = replacement;
        }

        Assert.NotNull(setResult);
        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetExpectedBuilderCallbackSlots(
                    moveNext,
                    ExpectedStateMachineType(
                        source,
                        "AwaitVoid"),
                    ExpectedBuilderStorage(
                        source,
                        "AwaitVoid"),
                    setResult,
                    [
                        .. moveNext
                            .DescendantsOutsideNestedFunctions
                            .OfType<Call>()
                            .Where(static call =>
                                call.Callee.Name
                                    == "GetResult"),
                    ],
                    out _));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void NarrowKickoffRequiresProtocolOrder()
    {
        using var source = OpenClassicFixture();
        IrFunction kickoff =
            PreparedKickoff(source, "AwaitVoid");
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoff.ClassicAsyncRelationship)
                    .Relationship)
                .Relationship;
        Block block = Assert.Single(kickoff.Body.Blocks);
        IReadOnlyList<IrNode> statements =
            block.DetachChildren();
        var start = Assert.Single(
            statements.OfType<ExpressionStatement>(),
            static statement =>
                statement.Expression is Call
                {
                    Callee.Name: "Start",
                });
        var builder = Assert.Single(
            statements.OfType<StoreField>(),
            static store =>
                store.Field.Name == "<>t__builder");

        block.Add(builder);
        block.Add(start);
        foreach (IrNode statement in statements)
        {
            if (!ReferenceEquals(statement, builder)
                && !ReferenceEquals(statement, start))
            {
                block.Add(statement);
            }
        }

        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoff,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out _,
                out _,
                out bool narrow));
        Assert.False(narrow);
        kickoff.CheckInvariant();
    }

    [Fact]
    public void ParameterBindingRequiresExactFieldType()
    {
        using var source = OpenClassicFixture();
        IrFunction kickoffFunction =
            PreparedKickoff(source, "AwaitVoid");
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoffFunction.ClassicAsyncRelationship)
                    .Relationship)
                .Relationship;
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoffFunction,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out ClassicAsyncReconstructionPass.Kickoff kickoff,
                out _,
                out _));
        ClassicAsyncParameterBinding original =
            Assert.Single(kickoff.ParameterBindings.Items);
        TypeRef modifier = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "IsVolatile");
        TypeRef modifiedType =
            original.FieldType.WithCustomModifier(
                modifier,
                isRequired: true);
        var field = new FieldRef(
            kickoff.StateMachineType,
            original.FieldName,
            modifiedType)
        {
            ExactDefinitionAddress =
                original.FieldDefinitionAddress,
            ExactDefinitionAcquisitionGuard =
                original.FieldDefinitionAcquisitionGuard,
        };
        var argument = new LoadArgument(
            original.ArgumentIndex,
            original.ArgumentName,
            original.ArgumentType)
        {
            IsDynamic = original.IsDynamic,
            ArrayElementIsDynamic =
                original.ArrayElementIsDynamic,
        };

        Assert.False(
            ClassicAsyncReconstructionPass
                .TryCreateParameterBinding(
                    kickoffFunction,
                    kickoff.StateMachineType,
                    field,
                    argument,
                    out _));
    }

    [Fact]
    public void ParameterBindingRequiresExactFieldDefinition()
    {
        using var source = OpenClassicFixture();
        IrFunction kickoffFunction =
            PreparedKickoff(source, "AwaitVoid");
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoffFunction.ClassicAsyncRelationship)
                    .Relationship)
                .Relationship;
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoffFunction,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out ClassicAsyncReconstructionPass.Kickoff kickoff,
                out _,
                out _));
        ClassicAsyncParameterBinding binding =
            Assert.Single(kickoff.ParameterBindings.Items);
        ExactFieldDefinitionAddress address =
            Assert.IsType<ExactFieldDefinitionAddress>(
                binding.FieldDefinitionAddress);
        Assert.NotNull(
            binding.FieldDefinitionAcquisitionGuard);
        var alias = new FieldRef(
            kickoff.StateMachineType,
            binding.FieldName,
            binding.FieldType)
        {
            ExactDefinitionAddress = address with
            {
                MetadataToken = address.MetadataToken + 1,
            },
            ExactDefinitionAcquisitionGuard =
                binding.FieldDefinitionAcquisitionGuard,
        };

        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetParameterBinding(
                    kickoff,
                    alias,
                    out _));
    }

    [Theory]
    [InlineData("AwaitVoid", 1)]
    [InlineData("AwaitGeneric", 1)]
    [InlineData("TwoSequentialAwaits", 2)]
    [InlineData("AwaitInLoop", 1)]
    public void AcceptedRecipesOwnEveryCompletionCallback(
        string methodName,
        int suspensionCount)
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext = PreparedMoveNext(source, methodName);
        List<(ExpressionStatement Statement, Call Call)> callbacks =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<ExpressionStatement>()
                .Where(static statement =>
                    statement.Expression is Call)
                .Select(static statement =>
                    (statement, (Call)statement.Expression))
                .Where(static pair => pair.Item2.Callee.Name is
                    "AwaitUnsafeOnCompleted"
                    or "AwaitOnCompleted"
                    or "SetException"
                    or "SetResult"),
        ];
        Assert.Single(
            callbacks,
            static pair => pair.Call.Callee.Name == "SetResult");
        Assert.Single(
            callbacks,
            static pair => pair.Call.Callee.Name == "SetException");
        Assert.Equal(
            suspensionCount,
            callbacks.Count(static pair =>
                pair.Call.Callee.Name is
                    "AwaitUnsafeOnCompleted"
                    or "AwaitOnCompleted"));
        Assert.All(
            callbacks.Where(static pair =>
                pair.Call.Callee.Name is
                    "AwaitUnsafeOnCompleted"
                    or "AwaitOnCompleted"),
            static pair =>
            {
                Assert.Collection(
                    pair.Call.Arguments,
                    static receiver =>
                        Assert.IsType<LoadFieldAddress>(receiver),
                    static awaiter =>
                        Assert.IsType<LoadLocalAddress>(awaiter),
                    static machine =>
                        Assert.IsType<LoadArgument>(machine));
                Assert.Equal(2, pair.Call.Callee.TypeArguments.Length);
                Assert.Equal(2, pair.Call.Callee.ParameterTypes.Length);
            });
        Call[] getResults =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>()
                .Where(static call =>
                    call.Callee.Name == "GetResult"),
        ];
        Assert.Equal(suspensionCount, getResults.Length);
        Assert.Equal(suspensionCount + 2, callbacks.Count);
        IrFunction kickoff = ImportClassicFixture(source, methodName);
        var plan = Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoff.ClassicAsyncRelationship))).Plan;
        Assert.True(
            ClassicAsyncReconstructionPass.TryCaptureRegionIds(
                moveNext,
                ClassicAsyncRegionHost.Execution,
                plan.Machine.Execution,
                callbacks.Select(static pair =>
                    (IrNode)pair.Statement),
                out List<ClassicAsyncPhysicalRegionId> callbackRegions));
        Assert.All(
            callbackRegions,
            region => Assert.Contains(
                region,
                plan.RegionLedger.ConsumedRegions));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void ExtraExactMachineSetResultIsRejected()
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext = PreparedMoveNext(source, "AwaitVoid");
        Call setResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "SetResult");
        var statement = Assert.IsType<ExpressionStatement>(
            setResult.Parent);
        var block = Assert.IsType<Block>(statement.Parent);
        IReadOnlyList<IrNode> statements = block.DetachChildren();
        foreach (IrNode current in statements)
        {
            if (ReferenceEquals(current, statement))
                block.Add(statement.Clone());
            block.Add(current);
        }

        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetExpectedBuilderCallbackSlots(
                    moveNext,
                    ExpectedStateMachineType(source, "AwaitVoid"),
                    ExpectedBuilderStorage(source, "AwaitVoid"),
                    setResult,
                    [
                        .. moveNext
                            .DescendantsOutsideNestedFunctions
                            .OfType<Call>()
                            .Where(static call =>
                                call.Callee.Name == "GetResult"),
                    ],
                    out _));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void CallbackBuilderRequiresExactFieldDefinition()
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "AwaitGeneric");
        Call setResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "SetResult");
        var receiver = Assert.IsType<LoadFieldAddress>(
            setResult.Arguments[0]);
        ExactFieldDefinitionAddress address =
            Assert.IsType<ExactFieldDefinitionAddress>(
                receiver.Field.ExactDefinitionAddress);
        Assert.NotNull(
            receiver.Field.ExactDefinitionAcquisitionGuard);
        IrExpression? instance = receiver.Instance;
        instance?.Detach();
        receiver.ReplaceWith(new LoadFieldAddress(
            receiver.Field with
            {
                ExactDefinitionAddress = address with
                {
                    MetadataToken = address.MetadataToken + 1,
                },
            },
            instance));

        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetExpectedBuilderCallbackSlots(
                    moveNext,
                    ExpectedStateMachineType(
                        source,
                        "AwaitGeneric"),
                    ExpectedBuilderStorage(
                        source,
                        "AwaitGeneric"),
                    setResult,
                    [
                        .. moveNext
                            .DescendantsOutsideNestedFunctions
                            .OfType<Call>()
                            .Where(static call =>
                                call.Callee.Name
                                    == "GetResult"),
                    ],
                    out _));
        moveNext.CheckInvariant();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("overwritten")]
    [InlineData("outside-catch")]
    public void ExceptionCompletionRequiresCanonicalCaughtValue(
        string mutation)
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, "AwaitVoid");
        Call setException = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call =>
                call.Callee.Name == "SetException");
        var statement = Assert.IsType<ExpressionStatement>(
            setException.Parent);
        CatchClause clause = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<CatchClause>());
        var exception = Assert.IsType<LoadLocal>(
            setException.Arguments[1]);
        switch (mutation)
        {
            case "null":
                exception.ReplaceWith(new Constant(
                    null,
                    exception.Type));
                break;
            case "overwritten":
                {
                    var block = Assert.IsType<Block>(
                        statement.Parent);
                    IReadOnlyList<IrNode> children =
                        block.DetachChildren();
                    foreach (IrNode child in children)
                    {
                        if (ReferenceEquals(child, statement))
                        {
                            block.Add(new StoreLocal(
                                exception.Index,
                                exception.Type,
                                new Constant(
                                    null,
                                    exception.Type)));
                        }
                        block.Add(child);
                    }
                    break;
                }
            case "outside-catch":
                {
                    var catchBlock = Assert.IsType<Block>(
                        statement.Parent);
                    IReadOnlyList<IrNode> catchChildren =
                        catchBlock.DetachChildren();
                    foreach (IrNode child in catchChildren)
                    {
                        if (!ReferenceEquals(child, statement))
                            catchBlock.Add(child);
                    }
                    var tryCatch = Assert.IsType<TryCatch>(
                        clause.Parent);
                    var outerBlock = Assert.IsType<Block>(
                        tryCatch.Parent);
                    IReadOnlyList<IrNode> outerChildren =
                        outerBlock.DetachChildren();
                    foreach (IrNode child in outerChildren)
                    {
                        outerBlock.Add(child);
                        if (ReferenceEquals(child, tryCatch))
                            outerBlock.Add(statement);
                    }
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mutation));
        }

        Call setResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "SetResult");
        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetExpectedBuilderCallbackSlots(
                    moveNext,
                    ExpectedStateMachineType(
                        source,
                        "AwaitVoid"),
                    ExpectedBuilderStorage(
                        source,
                        "AwaitVoid"),
                    setResult,
                    [
                        .. moveNext
                            .DescendantsOutsideNestedFunctions
                            .OfType<Call>()
                            .Where(static call =>
                                call.Callee.Name
                                    == "GetResult"),
                    ],
                    out _));
        moveNext.CheckInvariant();
    }

    [Theory]
    [InlineData("foreign-machine-type")]
    [InlineData("foreign-machine-argument")]
    [InlineData("foreign-machine-instantiation")]
    [InlineData("foreign-awaiter-type")]
    [InlineData("foreign-awaiter-address")]
    [InlineData("custom-modifier")]
    [InlineData("exact-address")]
    public void AwaitCallbackRequiresExactAwaitPointCorrelation(
        string mutation)
    {
        using var source = OpenClassicFixture();
        string methodName =
            mutation == "foreign-machine-instantiation"
                ? "AwaitGeneric"
                : "AwaitVoid";
        IrFunction moveNext =
            PreparedMoveNext(source, methodName);
        Call callback = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name is
                "AwaitUnsafeOnCompleted"
                or "AwaitOnCompleted");
        var awaiterAddress = Assert.IsType<LoadLocalAddress>(
            callback.Arguments[1]);
        TypeRef foreign = TypeRef.Definition(
            "Foreign",
            "ReviewRepro",
            "Other");
        switch (mutation)
        {
            case "foreign-machine-type":
                ReplaceCallee(
                    callback,
                    callback.Callee with
                    {
                        TypeArguments =
                            callback.Callee.TypeArguments.SetItem(
                                1,
                                foreign),
                    });
                break;
            case "foreign-machine-argument":
                callback.Arguments[2].ReplaceWith(
                    new LoadArgument(0, "this", foreign));
                break;
            case "foreign-machine-instantiation":
                TypeRef machine =
                    callback.Callee.TypeArguments[1];
                TypeRef foreignInstantiation =
                    TypeRef.GenericInstance(
                        Assert.IsType<TypeRef>(
                            machine.ElementType),
                        [
                            TypeRef.CoreLib(
                                "System",
                                "String"),
                        ]);
                callback.Arguments[2].ReplaceWith(
                    new LoadArgument(
                        0,
                        "this",
                        foreignInstantiation));
                break;
            case "foreign-awaiter-type":
                callback.Arguments[1].ReplaceWith(
                    new LoadLocalAddress(
                        awaiterAddress.Index,
                        foreign));
                break;
            case "foreign-awaiter-address":
                int foreignIndex = moveNext.Locals.Length;
                moveNext.ResetLocals(
                    moveNext.Locals.Add(awaiterAddress.Type),
                    moveNext.LocalNames.Add(null));
                callback.Arguments[1].ReplaceWith(
                    new LoadLocalAddress(
                        foreignIndex,
                        awaiterAddress.Type));
                break;
            case "custom-modifier":
                TypeRef modifier = TypeRef.CoreLib(
                    "System.Runtime.CompilerServices",
                    "IsVolatile");
                ReplaceCallee(
                    callback,
                    callback.Callee with
                    {
                        ParameterTypes =
                            callback.Callee.ParameterTypes.SetItem(
                                0,
                                callback.Callee.ParameterTypes[0]
                                    .WithCustomModifier(
                                        modifier,
                                        isRequired: true)),
                    });
                break;
            case "exact-address":
                ReplaceCallee(
                    callback,
                    callback.Callee with
                    {
                        ExactDefinitionAddress = new(
                            source.ModuleVersionId,
                            MetadataTokens.MethodDefinitionHandle(1)),
                        ExactDefinitionAcquisitionGuard =
                            new object(),
                    });
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mutation));
        }

        Call setResult = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            static call => call.Callee.Name == "SetResult");
        Call[] getResults =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>()
                .Where(static call =>
                    call.Callee.Name == "GetResult"),
        ];
        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetExpectedBuilderCallbackSlots(
                    moveNext,
                    ExpectedStateMachineType(source, methodName),
                    ExpectedBuilderStorage(source, methodName),
                    setResult,
                    getResults,
                    out _));
        moveNext.CheckInvariant();
    }

    [Theory]
    [InlineData("SetResult", false)]
    [InlineData("SetResult", true)]
    [InlineData("SetException", false)]
    [InlineData("SetException", true)]
    public void CompletionCallbackRequiresExactExternalMemberIdentity(
        string callbackName,
        bool exactAddress)
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext = PreparedMoveNext(source, "AwaitValue");
        Call callback = Assert.Single(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>(),
            call => call.Callee.Name == callbackName);
        MethodRef mutation;
        if (exactAddress)
        {
            mutation = callback.Callee with
            {
                ExactDefinitionAddress = new(
                    source.ModuleVersionId,
                    MetadataTokens.MethodDefinitionHandle(1)),
                ExactDefinitionAcquisitionGuard = new object(),
            };
        }
        else
        {
            TypeRef modifier = TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                "IsVolatile");
            mutation = callback.Callee with
            {
                ReturnType = callback.Callee.ReturnType
                    .WithCustomModifier(
                        modifier,
                        isRequired: false),
            };
        }
        callback = ReplaceCallee(callback, mutation);

        Call setResult = callbackName == "SetResult"
            ? callback
            : Assert.Single(
                moveNext.DescendantsOutsideNestedFunctions
                    .OfType<Call>(),
                static call => call.Callee.Name == "SetResult");
        Call[] getResults =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>()
                .Where(static call =>
                    call.Callee.Name == "GetResult"),
        ];
        Assert.False(
            ClassicAsyncReconstructionPass
                .TryGetExpectedBuilderCallbackSlots(
                    moveNext,
                    ExpectedStateMachineType(source, "AwaitValue"),
                    ExpectedBuilderStorage(source, "AwaitValue"),
                    setResult,
                    getResults,
                    out _));
        moveNext.CheckInvariant();
    }

    [Fact]
    public void PostAwaitInvocationNodesAreUnverified()
    {
        TypeRef awaiter = TypeRef.Definition(
            "Synthetic",
            "ReviewRepro",
            "Awaiter");

        Call NewGetResult() => new(
            new MethodRef(
                awaiter,
                "GetResult",
                Int32,
                [],
                HasThis: true),
            isVirtual: false,
            [new LoadLocalAddress(0, awaiter)]);

        IrExpression[] invocations =
        [
            new NewObject(
                new MethodRef(
                    TypeRef.Definition(
                        "Synthetic",
                        "ReviewRepro",
                        "Result"),
                    ".ctor",
                    Void,
                    [Int32],
                    HasThis: true),
                [NewGetResult()]),
            new CallIndirect(
                new Constant(
                    0,
                    TypeRef.Pointer(Void)),
                [NewGetResult()],
                Int32,
                [Int32]),
            new LocalFunctionInvocation(
                "Consume",
                Int32,
                [NewGetResult()],
                [Int32],
                [ArgumentRefKind.Value]),
        ];
        foreach (IrExpression invocation in invocations)
        {
            Call getResult = Assert.Single(
                invocation.Descendants.Prepend(invocation)
                    .OfType<Call>());
            Assert.True(
                ClassicAsyncReconstructionPass
                    .HasUnverifiedPostAwaitResultUse(
                        invocation,
                        [getResult]));
        }

        var resultType = TypeRef.Definition(
            "Synthetic",
            "ReviewRepro",
            "Result");
        var constructor = new MethodRef(
            resultType,
            ".ctor",
            Void,
            [Int32],
            HasThis: true);
        var consume = new MethodRef(
            TypeRef.Definition(
                "Synthetic",
                "ReviewRepro",
                "Effects"),
            "Consume",
            Int32,
            [Int32],
            HasThis: false);
        IrExpression[] siblingInvocations =
        [
            new NewObject(
                constructor,
                [new Constant(1, Int32)]),
            new CallIndirect(
                new Constant(
                    0,
                    TypeRef.Pointer(Void)),
                [new Constant(1, Int32)],
                Int32,
                [Int32]),
            new LocalFunctionInvocation(
                "Consume",
                Int32,
                [new Constant(1, Int32)],
                [Int32],
                [ArgumentRefKind.Value]),
            new DelegateCreation(
                TypeRef.Definition(
                    "Synthetic",
                    "ReviewRepro",
                    "Consumer"),
                consume,
                isVirtual: false,
                new Constant(
                    null,
                    TypeRef.CoreLib("System", "Object"))),
            new ObjectInitializerExpression(
                new NewObject(
                    constructor,
                    [new Constant(1, Int32)]),
                isCollection: false,
                []),
            new WithExpression(
                new Constant(
                    null,
                    resultType),
                []),
            new AnonymousObject(
                TypeRef.Definition(
                    "Synthetic",
                    "ReviewRepro",
                    "Anonymous"),
                ["Value"],
                [new Constant(1, Int32)]),
            new InterpolatedStringExpression(
                [InterpolatedStringPart.LiteralText("value")],
                []),
            new DynamicGetMember(
                new Constant(
                    null,
                    TypeRef.CoreLib("System", "Object")),
                "Value"),
        ];
        foreach (IrExpression invocation in siblingInvocations)
        {
            Call getResult = NewGetResult();
            var tuple = new TupleExpression(
                TypeRef.Definition(
                    "Synthetic",
                    "ReviewRepro",
                    "ResultTuple"),
                [getResult, invocation]);
            Assert.True(
                ClassicAsyncReconstructionPass
                    .HasUnverifiedPostAwaitResultUse(
                        tuple,
                        [getResult]));
        }

        Call direct = NewGetResult();
        Assert.False(
            ClassicAsyncReconstructionPass
                .HasUnverifiedPostAwaitResultUse(
                    direct,
                    [direct]));
        Call convertedResult = NewGetResult();
        var conversion =
            new ILInspector.Decompiler.Pipeline.Convert(
            Int32,
            isChecked: false,
            isUnsigned: false,
            operand: convertedResult);
        Assert.False(
            ClassicAsyncReconstructionPass
                .HasUnverifiedPostAwaitResultUse(
                    conversion,
                    [convertedResult]));
        Call binaryResult = NewGetResult();
        var binary = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            left: binaryResult,
            right: new Constant(1, Int32));
        Assert.False(
            ClassicAsyncReconstructionPass
                .HasUnverifiedPostAwaitResultUse(
                    binary,
                    [binaryResult]));
    }

    [Theory]
    [InlineData("ConstructorAfterAwait")]
    [InlineData("ConstructorSiblingAfterAwait")]
    public void CompilerConstructorResultUseDeclines(string methodName)
    {
        using var source = OpenClassicFixture();
        IrFunction moveNext =
            PreparedMoveNext(source, methodName);
        Assert.NotEmpty(
            moveNext.DescendantsOutsideNestedFunctions
                .OfType<NewObject>());
        IrFunction kickoff = ImportClassicFixture(
            source,
            methodName);

        IrPasses.Run(
            kickoff,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            kickoff.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
            outcome.Reason);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            kickoff.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void CompilerProducedAcceptedPopulationIsExact()
    {
        using var source = OpenClassicFixture();
        var actual = new List<string>();
        foreach (var imported in IrImporter.ImportAssembly(source))
        {
            IrFunction function = imported.Function;
            if (function.ClassicAsyncRelationship is not
                {
                    HostRole: ClassicAsyncHostRole.DeclaredKickoff,
                    Relationship:
                        StateMachineRelationshipResult.Resolved
                        {
                            Relationship.Kind:
                                StateMachineClaimKind.ClassicAsync,
                        },
                })
            {
                continue;
            }

            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            if (function.ClassicAsyncOutcome is
                ClassicAsyncOutcome.Reconstructed)
            {
                string methodName = function.Name.StartsWith(
                    "<CallsClassicLocal>g__ClassicLocal|",
                    StringComparison.Ordinal)
                        ? "<CallsClassicLocal>g__ClassicLocal"
                        : function.Name;
                actual.Add(
                    $"{function.DeclaringType.ToDisplayString()}"
                    + $"::{methodName}");
            }
        }

        Assert.Equal(
            [
                "AsyncFixtures::<CallsClassicLocal>g__ClassicLocal",
                "AsyncFixtures::AwaitConditional",
                "AsyncFixtures::AwaitDelayConstant",
                "AsyncFixtures::AwaitGeneric",
                "AsyncFixtures::AwaitInLoop",
                "AsyncFixtures::AwaitInLoopChecked",
                "AsyncFixtures::AwaitInTryFinally",
                "AsyncFixtures::AwaitOrdinarySetMethod",
                "AsyncFixtures::AwaitValue",
                "AsyncFixtures::AwaitValueTask",
                "AsyncFixtures::AwaitVoid",
                "AsyncFixtures::DynamicArrayReferenceIdentity",
                "AsyncFixtures::DynamicReferenceIdentity",
                "AsyncFixtures::ObjectArrayReferenceIdentity",
                "AsyncFixtures::SequentialWithImplicitConversion",
                "AsyncFixtures::SequentialWithRealizedInitializer",
                "AsyncFixtures::SequentialWithRealizedWithExpression",
                "AsyncFixtures::TwoSequentialAwaits",
                "AsyncFixtures::TwoSequentialNamedAwaits",
                "GenericAsyncFixtures::AwaitGeneric",
            ],
            actual.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaisedAndLoweredShareOneDecisionWithoutAliasing(
        bool loweredFirst)
    {
        using var source = OpenClassicFixture();
        IrFunction raised = ImportClassicFixture(
            source,
            "AwaitValue");
        IrFunction lowered = ImportClassicFixture(
            source,
            "AwaitValue");
        var raisedEvidence =
            Assert.IsType<ClassicAsyncRelationshipEvidence>(
                raised.ClassicAsyncRelationship);
        var loweredEvidence =
            Assert.IsType<ClassicAsyncRelationshipEvidence>(
                lowered.ClassicAsyncRelationship);
        ClassicAsyncPlanningSession planningSession =
            PlanningSession(raisedEvidence);
        Assert.Same(
            planningSession,
            PlanningSession(loweredEvidence));

        Func<MethodRef, IrFunction?> import =
            method => IrImporter.Import(source, method);
        DecompilerResult raisedResult;
        DecompilerResult loweredResult;
        if (loweredFirst)
        {
            loweredResult = CSharpPrinter.PrintLowered(
                lowered,
                import);
            raisedResult = CSharpPrinter.PrintRaised(
                raised,
                import,
                typesProvablyDisjoint: source.AreProvablyDisjoint);
        }
        else
        {
            raisedResult = CSharpPrinter.PrintRaised(
                raised,
                import,
                typesProvablyDisjoint: source.AreProvablyDisjoint);
            loweredResult = CSharpPrinter.PrintLowered(
                lowered,
                import);
        }

        Assert.True(raisedResult.Succeeded);
        Assert.True(loweredResult.Succeeded);
        Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(raisedEvidence));
        var raisedStage = Assert.IsType<
            ClassicAsyncStageResult.Applied>(
                raised.ClassicAsyncStageResult);
        var loweredStage = Assert.IsType<
            ClassicAsyncStageResult.Applied>(
                lowered.ClassicAsyncStageResult);
        Assert.Equal(ClassicAsyncStage.Raised, raisedStage.Stage);
        Assert.Equal(ClassicAsyncStage.Lowered, loweredStage.Stage);
        Assert.Equal(
            1,
            planningSession.PreparationCount);
        Assert.Equal(
            1,
            planningSession.PublishedPreparationCount);
        Assert.NotSame(raised.Body, lowered.Body);
        Assert.NotSame(
            raised.Body.Blocks[0].Children[0],
            lowered.Body.Blocks[0].Children[0]);
        string loweredBefore = IrPrinter.Dump(lowered);

        raised.Body.DetachChildren();

        Assert.Equal(loweredBefore, IrPrinter.Dump(lowered));
        IrFunction later = ImportClassicFixture(
            source,
            "AwaitValue");
        DecompilerResult laterResult = CSharpPrinter.PrintRaised(
            later,
            import,
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        Assert.Equal(raisedResult.Output, laterResult.Output);
        Assert.NotSame(later.Body, lowered.Body);
    }

    [Fact]
    public async Task ConcurrentRequestsPublishOneDecisionWithoutDeadlock()
    {
        using var source = OpenClassicFixture();
        IrFunction[] functions = Enumerable.Range(0, 8)
            .Select(_ => ImportClassicFixture(
                source,
                "AwaitValue"))
            .ToArray();
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            functions[0].ClassicAsyncRelationship);
        Func<MethodRef, IrFunction?> import =
            method => IrImporter.Import(source, method);

        System.Threading.Tasks.Task<DecompilerResult>[] requests = functions
            .Select((function, index) => System.Threading.Tasks.Task.Run(() =>
                index % 2 == 0
                    ? CSharpPrinter.PrintRaised(
                        function,
                        import,
                        typesProvablyDisjoint:
                            source.AreProvablyDisjoint)
                    : CSharpPrinter.PrintLowered(
                        function,
                        import)))
            .ToArray();
        DecompilerResult[] results = await System.Threading.Tasks.Task.WhenAll(requests)
            .WaitAsync(
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        Assert.Equal(
            1,
            PlanningSession(evidence).PublishedPreparationCount);
    }

    [Fact]
    public void NestedLocalPreparationDoesNotPoisonTopLevelRequest()
    {
        using var source = OpenClassicFixture();
        IrFunction parent = ImportClassicFixture(
            source,
            "CallsClassicLocal");
        Func<MethodRef, IrFunction?> import =
            method => IrImporter.Import(source, method);

        CSharpPrinter.PrintRaised(
            parent,
            import,
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        IrFunction local = IrImporter.ImportAssembly(source)
            .Select(method => method.Function)
            .First(function => function.Name.Contains(
                "g__ClassicLocal",
                StringComparison.Ordinal));
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            local.ClassicAsyncRelationship);
        ClassicAsyncPlanningSession planningSession =
            PlanningSession(evidence);
        int preparationsAfterNestedRequest =
            planningSession.PreparationCount;

        CSharpPrinter.PrintRaised(
            local,
            import,
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        Assert.Equal(
            preparationsAfterNestedRequest,
            planningSession.PreparationCount);
        Assert.Equal(
            1,
            planningSession.PublishedPreparationCount);
    }

    [Fact]
    public void MixedRejectedClaimsClassifyEachKickoffExactly()
    {
        using var source = OpenClassicFixture();
        IrFunction classic = ImportClassicFixture(
            source,
            "RejectedClassicClaim");
        IrFunction iterator = ImportClassicFixture(
            source,
            "RejectedIteratorClaim");
        var classicEvidence =
            Assert.IsType<ClassicAsyncRelationshipEvidence>(
                classic.ClassicAsyncRelationship);
        var iteratorHandle = source.Reader.MethodDefinitions.Single(
            handle => source.Reader.GetString(
                source.Reader.GetMethodDefinition(handle).Name)
                == "RejectedIteratorClaim");
        ClassicAsyncRelationshipEvidence iteratorEvidence =
            source.ClassicAsyncRelationship(
                iteratorHandle,
                asyncClassification: null);
        iterator.ClassicAsyncRelationship = iteratorEvidence;

        var rejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                classicEvidence.Relationship);
        var iteratorRejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                iteratorEvidence.Relationship);
        Assert.Same(rejected.Failure, iteratorRejected.Failure);
        Assert.Equal(
            [
                new StateMachineRelationshipClaim(
                    classicEvidence.RequestedHost,
                    StateMachineClaimKind.ClassicAsync),
                new StateMachineRelationshipClaim(
                    iteratorEvidence.RequestedHost,
                    StateMachineClaimKind.Iterator),
            ],
            rejected.Failure.Claims);
        Assert.Equal(
            [
                StateMachineClaimKind.ClassicAsync,
                StateMachineClaimKind.Iterator,
            ],
            rejected.Failure.ClaimKinds);
        Assert.Equal(
            ClassicAsyncHostRole.DeclaredKickoff,
            classicEvidence.HostRole);
        Assert.Equal(
            ClassicAsyncHostRole.Ordinary,
            iteratorEvidence.HostRole);

        IrPasses.Run(
            classic,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        IrPasses.Run(
            iterator,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncStageResult.Applied>(
            classic.ClassicAsyncStageResult);
        var outcome = Assert.IsType<ClassicAsyncOutcome.Declined>(
            classic.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.RejectedRelationship,
            outcome.Reason);
        Assert.Equal(
            ClassicAsyncKickoffDisposition.PreservedOriginal,
            outcome.KickoffDisposition);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            classic.ClassicAsyncDeclarationDisposition);
        Assert.False(classic.RequiresAsyncBodyModifier);
        Assert.Contains(
            "owner rejected the classic state-machine relationship",
            CSharpPrinter.Print(classic).Output,
            StringComparison.Ordinal);
        Assert.IsType<ClassicAsyncStageResult.NotApplicable>(
            iterator.ClassicAsyncStageResult);
        Assert.Null(iterator.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.NoOpinion,
            iterator.ClassicAsyncDeclarationDisposition);
    }

    [Fact]
    public void BudgetFailureWithClassicClaimRemainsInputFailure()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitVoid");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        // Keep relationship construction Metadata-owned while injecting the
        // exact budget-plus-claim boundary that this consumer must prioritize.
        var failure =
            Assert.IsType<StateMachineRelationshipFailure>(
                Activator.CreateInstance(
                    typeof(StateMachineRelationshipFailure),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [
                        StateMachineRelationshipFailureKind.BudgetExceeded,
                        "focused budget boundary",
                        ImmutableArray.Create(evidence.RequestedHost),
                        ImmutableArray<
                            MetadataTypeDefinitionAddress>.Empty,
                        ImmutableArray<
                            MetadataTypeDefinitionName>.Empty,
                        ImmutableArray.Create(
                            new StateMachineRelationshipClaim(
                                evidence.RequestedHost,
                                StateMachineClaimKind.ClassicAsync)),
                    ],
                    culture: null));
        var rejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                Activator.CreateInstance(
                    typeof(
                        StateMachineRelationshipResult.Rejected),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [failure],
                    culture: null));
        function.ClassicAsyncRelationship = evidence with
        {
            Relationship = rejected,
        };

        new ClassicAsyncReconstructionPass().Run(
            function,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));

        Assert.IsType<ClassicAsyncStageResult.Failed>(
            function.ClassicAsyncStageResult);
        Assert.Null(function.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.NoOpinion,
            function.ClassicAsyncDeclarationDisposition);
        Assert.False(function.RequiresAsyncBodyModifier);
    }

    [Fact]
    public void KickoffPlanningPrefixIsDerivedFromRegisteredPipeline()
    {
        string[] expected = IrPasses.Default
            .TakeWhile(pass =>
                pass is not ClassicAsyncReconstructionPass)
            .Select(pass => pass.Name)
            .ToArray();

        Assert.Equal(
            expected,
            IrPasses.Before<ClassicAsyncReconstructionPass>()
                .Select(pass => pass.Name));
    }

    [Fact]
    public void IndependentlyPreparedPlansHaveValueSemantics()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitValue");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);

        ClassicAsyncPreparationResult first =
            ClassicAsyncReconstructionPass.Prepare(
                source,
                evidence);
        ClassicAsyncPreparationResult second =
            ClassicAsyncReconstructionPass.Prepare(
                source,
                evidence);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                Assert.IsType<
                    ClassicAsyncPreparationResult.Decided>(
                        first).Decision);
        Assert.Equal(
            "<>1__state",
            reconstruct.Plan.Machine.StateStorage.Name);
        Assert.Equal(
            "<>t__builder",
            reconstruct.Plan.Machine.BuilderStorage.Name);
        Assert.Contains(
            reconstruct.Plan.Machine.AwaiterStorages.Items,
            storage => storage.Name.StartsWith(
                "<>u__",
                StringComparison.Ordinal));
        Assert.Collection(
            reconstruct.Plan.Machine.ParameterBindings.Items,
            first =>
            {
                Assert.Equal("a", first.FieldName);
                Assert.Equal("a", first.ArgumentName);
                Assert.Equal(0, first.ArgumentIndex);
            },
            second =>
            {
                Assert.Equal("b", second.FieldName);
                Assert.Equal("b", second.ArgumentName);
                Assert.Equal(1, second.ArgumentIndex);
            });
    }

    [Fact]
    public void CheckedRegionHasOnePrimaryRealization()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitInLoopChecked");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);

        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion region = Assert.Single(
            ledger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.CheckedArithmetic);
        ClassicAsyncUserRegionRealization realization = Assert.Single(
            ledger.Realizations,
            realization => realization.UserRegion == region.Id);

        Assert.Equal(
            ClassicAsyncUserRegionKind.CheckedArithmetic,
            region.Semantics.Kind);
        Assert.Equal(region.Id, realization.UserRegion);
        Assert.Equal(
            region.Semantics,
            realization.PrimaryOutputNode.Semantics);
        Assert.Contains(
            region.PhysicalRegion,
            ledger.ConsumedRegions);
        Assert.DoesNotContain(
            region.PhysicalRegion,
            ledger.PreservedRegions);
    }

    [Theory]
    [InlineData("AwaitValue", 1)]
    [InlineData("AwaitVoid", 1)]
    [InlineData("AwaitDelayConstant", 1)]
    [InlineData("TwoSequentialAwaits", 2)]
    [InlineData("AwaitOrdinarySetMethod", 1)]
    [InlineData("AwaitConditional", 1)]
    [InlineData("AwaitInLoop", 1)]
    [InlineData("AwaitInTryFinally", 1)]
    public void AwaitedOperandsHaveOnePrimaryRealization(
        string methodName,
        int expectedCount)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion[] operands =
        [
            .. ledger.UserRegions.Where(static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.AwaitedOperand),
        ];

        Assert.Equal(expectedCount, operands.Length);
        Assert.All(
            operands,
            operand => Assert.Single(
                ledger.Realizations,
                realization =>
                    realization.UserRegion == operand.Id
                    && realization.PrimaryOutputNode.Semantics
                        == operand.Semantics));
    }

    [Fact]
    public void PredicateRegionHasOnePrimaryRealization()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitConditional");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion predicate = Assert.Single(
            ledger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.Predicate);
        ClassicAsyncUserRegionRealization realization = Assert.Single(
            ledger.Realizations,
            realization => realization.UserRegion == predicate.Id);

        Assert.Equal(
            predicate.Semantics,
            realization.PrimaryOutputNode.Semantics);
        Assert.Contains(
            "4:flag",
            predicate.Semantics.Discriminator,
            StringComparison.Ordinal);
        Assert.Contains(
            predicate.PhysicalRegion,
            ledger.ConsumedRegions);
        Assert.DoesNotContain(
            predicate.PhysicalRegion,
            ledger.PreservedRegions);
    }

    [Fact]
    public void RegionLedgerRejectsChangedPredicate()
    {
        var id = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.0");
        var predicate = new ClassicAsyncUserRegion(
            id,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.Predicate,
                "parameter|flag|System.Boolean",
                Occurrence: 0));
        var changed = new ClassicAsyncOutputNode(
            predicate.Semantics with
            {
                Discriminator = "parameter|other|System.Boolean",
            });

        Assert.False(TryCreateRegionLedger(
            [predicate],
            [new(id, changed)],
            out _));
    }

    [Fact]
    public void GuardedEffectRegionHasOnePrimaryRealization()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitInTryFinally");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncRegionLedger ledger =
            reconstruct.Plan.RegionLedger;
        ClassicAsyncUserRegion effect = Assert.Single(
            ledger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.GuardedEffect);
        ClassicAsyncUserRegionRealization realization = Assert.Single(
            ledger.Realizations,
            realization => realization.UserRegion == effect.Id);

        Assert.Equal(
            effect.Semantics,
            realization.PrimaryOutputNode.Semantics);
        Assert.Contains(
            "KeepAlive",
            effect.Semantics.Discriminator,
            StringComparison.Ordinal);
        Assert.Contains(effect.PhysicalRegion, ledger.ConsumedRegions);
        Assert.DoesNotContain(
            effect.PhysicalRegion,
            ledger.PreservedRegions);
    }

    [Fact]
    public void RegionLedgerRejectsChangedGuardedEffect()
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(
            source,
            "AwaitInTryFinally");
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncUserRegion effect = Assert.Single(
            reconstruct.Plan.RegionLedger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.GuardedEffect);
        BlockContainer output = reconstruct.Plan.Body.Materialize();
        Call keepAlive = Assert.Single(
            output.Descendants.OfType<Call>(),
            static call => call.Callee.Name == "KeepAlive");
        LoadArgument argument = Assert.IsType<LoadArgument>(
            Assert.Single(keepAlive.Arguments));
        argument.ReplaceWith(new LoadArgument(
            argument.Index + 1,
            "other",
            argument.Type));

        Assert.True(
            ClassicAsyncReconstructionPass.TryCaptureOutputNodes(
                output,
                out List<ClassicAsyncOutputNode> outputNodes));
        ClassicAsyncOutputNode changed = Assert.Single(
            outputNodes,
            static node =>
                node.Semantics.Kind
                    == ClassicAsyncUserRegionKind.GuardedEffect);
        Assert.NotEqual(effect.Semantics, changed.Semantics);

        Assert.False(TryCreateRegionLedger(
            [effect],
            [new(effect.Id, changed)],
            out _));
    }

    [Fact]
    public void OutputGuardedEffectInventoryRejectsNonCall()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var tryBlock = new Block(0);
        tryBlock.Add(new Return(new Constant(0, int32)));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);
        var finallyBlock = new Block(1);
        finallyBlock.Add(
            new ExpressionStatement(new Constant(1, int32)));
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);
        var outputBlock = new Block(2);
        outputBlock.Add(new TryFinally(tryBody, finallyBody));
        var output = new BlockContainer();
        output.Add(outputBlock);

        Assert.False(
            ClassicAsyncReconstructionPass.TryCaptureOutputNodes(
                output,
                out _));
    }

    [Theory]
    [InlineData("AwaitValue", "9:parameter1:01:a", "9:parameter1:01:a")]
    [InlineData(
        "AwaitOrdinarySetMethod",
        "11:set_GetTask",
        "9:parameter1:04:task")]
    [InlineData(
        "AwaitInLoop",
        "15:foreach-element5:tasks",
        "15:foreach-element5:tasks")]
    public void AwaitedOperandIdentityRetainsItsAuthoredSource(
        string methodName,
        string firstFragment,
        string secondFragment)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncUserRegion operand = Assert.Single(
            reconstruct.Plan.RegionLedger.UserRegions,
            static region =>
                region.Semantics.Kind
                    == ClassicAsyncUserRegionKind.AwaitedOperand);

        Assert.Contains(
            firstFragment,
            operand.Semantics.Discriminator,
            StringComparison.Ordinal);
        Assert.Contains(
            secondFragment,
            operand.Semantics.Discriminator,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegionLedgerRejectsChangedAwaitedOperand()
    {
        var id = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.0");
        var region = new ClassicAsyncUserRegion(
            id,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.AwaitedOperand,
                "parameter|a|System.Threading.Tasks.Task<int>",
                Occurrence: 0));
        var changed = new ClassicAsyncOutputNode(
            region.Semantics with
            {
                Discriminator =
                    "parameter|b|System.Threading.Tasks.Task<int>",
            });

        Assert.False(TryCreateRegionLedger(
            [region],
            [new(id, changed)],
            out _));

        var second = new ClassicAsyncUserRegion(
            new(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            new(
                ClassicAsyncUserRegionKind.AwaitedOperand,
                "parameter|b|System.Threading.Tasks.Task<int>",
                Occurrence: 1));
        Assert.False(TryCreateRegionLedger(
            [region, second],
            [
                new(
                    region.Id,
                    new(second.Semantics with { Occurrence = 0 })),
                new(
                    second.Id,
                    new(region.Semantics with { Occurrence = 1 })),
            ],
            out _));
    }

    [Fact]
    public void OutputAwaitInventoryRejectsUnrecognizedOperand()
    {
        TypeRef task = TypeRef.Definition(
            "Synthetic",
            "System.Threading.Tasks",
            "Task");
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new ExpressionStatement(new AwaitExpression(
            new LoadArgument(0, "task", task),
            resultType: Void)));
        block.Add(new ExpressionStatement(new AwaitExpression(
            new UnsupportedNode(
                0,
                "synthetic",
                "unrecognized awaited operand"),
            resultType: int32)));
        var body = new BlockContainer();
        body.Add(block);

        Assert.False(
            ClassicAsyncReconstructionPass.TryCaptureOutputNodes(
                body,
                out _));
    }

    [Fact]
    public void AwaitedIdentityIncludesTypedCallAndArgumentFacts()
    {
        TypeRef task = TypeRef.Definition(
            "Synthetic",
            "System.Threading.Tasks",
            "Task");
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        TypeRef firstOwner =
            TypeRef.Definition("First.Assembly", "Samples", "Factory");
        TypeRef secondOwner =
            TypeRef.Definition("Second.Assembly", "Samples", "Factory");
        var firstMethod = new MethodRef(
            firstOwner,
            "Create",
            task,
            [task],
            HasThis: false)
        {
            TypeArguments = [int32],
        };
        var stringInstantiation = firstMethod with
        {
            TypeArguments =
            [
                TypeRef.CoreLib("System", "String"),
            ],
        };
        var otherAssembly = firstMethod with
        {
            DeclaringType = secondOwner,
        };
        TypeRef requiredModifier = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "IsReadOnlyAttribute");
        var modifiedSignature = firstMethod with
        {
            ParameterTypes =
            [
                task.WithCustomModifier(
                    requiredModifier,
                    isRequired: true),
            ],
        };
        var argument = new LoadArgument(0, "task", task);
        var first = new Call(firstMethod, isVirtual: false, [argument])
        {
            ConstrainedTo = firstOwner,
        };
        var otherConstraint =
            new Call(
                firstMethod,
                isVirtual: false,
                [(IrExpression)argument.Clone()])
            {
                ConstrainedTo = secondOwner,
            };

        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    first,
                    out string firstKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new Call(
                        stringInstantiation,
                        isVirtual: false,
                        [(IrExpression)argument.Clone()]),
                    out string stringKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new Call(
                        otherAssembly,
                        isVirtual: false,
                        [(IrExpression)argument.Clone()]),
                    out string assemblyKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new Call(
                        modifiedSignature,
                        isVirtual: false,
                        [(IrExpression)argument.Clone()]),
                    out string modifiedKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    otherConstraint,
                    out string constraintKey));

        Assert.NotEqual(firstKey, stringKey);
        Assert.NotEqual(firstKey, assemblyKey);
        Assert.NotEqual(firstKey, modifiedKey);
        Assert.NotEqual(firstKey, constraintKey);

        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new LoadArgument(0, "value", task),
                    out string firstArgumentKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new LoadArgument(1, "value", task),
                    out string secondArgumentKey));
        Assert.True(
            ClassicAsyncReconstructionPass
                .TryGetSemanticExpressionKey(
                    new LoadArgument(0, "value", task)
                    {
                        IsDynamic = true,
                    },
                    out string dynamicArgumentKey));
        Assert.NotEqual(firstArgumentKey, secondArgumentKey);
        Assert.NotEqual(firstArgumentKey, dynamicArgumentKey);
    }

    [Theory]
    [InlineData("AwaitValue")]
    [InlineData("AwaitVoid")]
    [InlineData("TwoSequentialAwaits")]
    [InlineData("AwaitConditional")]
    [InlineData("AwaitInLoop")]
    [InlineData("AwaitInTryFinally")]
    public void AcceptedPlanPartitionsEveryPhysicalStatementSlot(
        string methodName)
    {
        using var source = OpenClassicFixture();
        IrFunction function = ImportClassicFixture(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var reconstruct =
            Assert.IsType<ClassicAsyncDecision.Reconstruct>(
                PublishedDecision(evidence));
        ClassicAsyncPlan plan = reconstruct.Plan;
        ClassicAsyncRegionLedger ledger = plan.RegionLedger;
        ClassicAsyncPhysicalRegionId[] physical =
        [
            .. ledger.PhysicalRegions.Select(static region => region.Id),
        ];

        Assert.NotEmpty(physical);
        Assert.Equal(
            physical.Length,
            ledger.ConsumedRegions.Count
                + ledger.PreservedRegions.Count);
        Assert.Empty(
            ledger.ConsumedRegions.Intersect(
                ledger.PreservedRegions));
        Assert.All(
            ledger.PhysicalRegions.Where(static region =>
                region.Id.Host == ClassicAsyncRegionHost.Kickoff),
            region => Assert.Contains(
                region.Id,
                ledger.ConsumedRegions));
        Assert.Contains(
            ledger.ConsumedRegions,
            region =>
                region.Host == ClassicAsyncRegionHost.Execution);
        Assert.Contains(
            ledger.PreservedRegions,
            region =>
                region.Host == ClassicAsyncRegionHost.Execution);
        Assert.All(
            ledger.UserRegions,
            region => Assert.Contains(
                region.PhysicalRegion,
                ledger.ConsumedRegions));
        Assert.All(
            ledger.PhysicalRegions,
            region => Assert.Equal(
                region.Id.Host == ClassicAsyncRegionHost.Kickoff
                    ? plan.Machine.Kickoff
                    : plan.Machine.Execution,
                region.Id.Method));
    }

    [Fact]
    public void RegionLedgerRejectsMissingDuplicateAndMismatchedRealizations()
    {
        var firstId = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.0");
        var secondId = new ClassicAsyncRegionId(
            ClassicAsyncRegionHost.Execution,
            "0.1");
        var first = new ClassicAsyncUserRegion(
            firstId,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.Throw,
                "throw",
                Occurrence: 0));
        var second = new ClassicAsyncUserRegion(
            secondId,
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            new(
                ClassicAsyncUserRegionKind.Break,
                "break",
                Occurrence: 0));
        var throwOutput = new ClassicAsyncOutputNode(
            first.Semantics);

        Assert.False(TryCreateRegionLedger(
            [first, second],
            [new(firstId, throwOutput)],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first],
            [
                new(firstId, throwOutput),
                new(firstId, throwOutput with
                {
                    Semantics = throwOutput.Semantics with
                    {
                        Occurrence = 1,
                    },
                }),
            ],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first],
            [new(firstId, throwOutput with
            {
                Semantics = second.Semantics,
            })],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first, second],
            [
                new(firstId, throwOutput),
                new(secondId, throwOutput),
            ],
            out _));
    }

    [Fact]
    public void RegionLedgerUsesOccurrenceToPairRepeatedSemantics()
    {
        var first = new ClassicAsyncUserRegion(
            new(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.0"),
            new(
                ClassicAsyncUserRegionKind.CheckedArithmetic,
                "Add|True|False",
                Occurrence: 0));
        var second = new ClassicAsyncUserRegion(
            new(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            PhysicalId(
                ClassicAsyncRegionHost.Execution,
                "0.1"),
            first.Semantics with { Occurrence = 1 });
        var firstOutput = new ClassicAsyncOutputNode(first.Semantics);
        var secondOutput = new ClassicAsyncOutputNode(second.Semantics);

        Assert.True(TryCreateRegionLedger(
            [first, second],
            [
                new(first.Id, firstOutput),
                new(second.Id, secondOutput),
            ],
            out _));
        Assert.False(TryCreateRegionLedger(
            [first, second],
            [
                new(first.Id, secondOutput),
                new(second.Id, firstOutput),
            ],
            out _));
    }

    [Fact]
    public void RegionLedgerRequiresCompleteDisjointPhysicalPartition()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        ClassicAsyncPhysicalRegion execution =
            Physical(ClassicAsyncRegionHost.Execution, "0.0");

        Assert.True(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id],
            [execution.Id]));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id, execution.Id],
            [execution.Id]));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [kickoff.Id, kickoff.Id],
            [execution.Id]));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, execution],
            [execution.Id],
            [kickoff.Id]));
    }

    [Fact]
    public void RegionLedgerRejectsRealizationFromPreservedMaterial()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        ClassicAsyncPhysicalRegion execution =
            Physical(ClassicAsyncRegionHost.Execution, "0.0");
        var semantics = new ClassicAsyncRegionSemantics(
            ClassicAsyncUserRegionKind.Throw,
            "throw",
            Occurrence: 0);
        var userRegion = new ClassicAsyncUserRegion(
            new(ClassicAsyncRegionHost.Execution, "0.0.0"),
            execution.Id,
            semantics);

        Assert.False(ClassicAsyncRegionLedger.TryCreate(
            KickoffAddress,
            ExecutionAddress,
            [kickoff, execution],
            [kickoff.Id],
            [execution.Id],
            [userRegion],
            [
                new(
                    userRegion.Id,
                    new ClassicAsyncOutputNode(semantics)),
            ],
            out _));
    }

    [Fact]
    public void RegionLedgerRejectsUnsupportedConsumedControlFlow()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        ClassicAsyncPhysicalRegion external = Physical(
            ClassicAsyncRegionHost.Execution,
            "0.0",
            hasExternalTarget: true);
        ClassicAsyncPhysicalRegion externalEntry = Physical(
            ClassicAsyncRegionHost.Execution,
            "0.1",
            hasExternalEntry: true);
        ClassicAsyncPhysicalRegion multiSuccessor = Physical(
            ClassicAsyncRegionHost.Execution,
            "0.2",
            successorMultiplicity: 3);

        Assert.False(TryCreatePhysicalLedger(
            [kickoff, external],
            [kickoff.Id, external.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, externalEntry],
            [kickoff.Id, externalEntry.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, multiSuccessor],
            [kickoff.Id, multiSuccessor.Id],
            []));
    }

    [Fact]
    public void PhysicalCensusRecordsExternalEntryAndSuccessorMultiplicity()
    {
        TypeRef boolean = TypeRef.CoreLib("System", "Boolean");
        var entered = new Block(0x20);
        entered.Add(new Return(null));
        var outer = new Block(0);
        outer.Add(new IfStatement(
            new Constant(true, boolean),
            entered,
            elseArm: null));
        outer.Add(new Branch(0x20));
        var externalBody = new BlockContainer();
        externalBody.Add(outer);
        var externalFunction = new IrFunction(
            "ExternalEntry",
            StateMachine,
            new MethodSignature(Void, [], true, 0),
            [],
            externalBody);

        Assert.True(
            ClassicAsyncReconstructionPass.TryCapturePhysicalRegions(
                externalFunction,
                ClassicAsyncRegionHost.Execution,
                ExecutionAddress,
                out var externalRegions));
        Assert.Contains(
            externalRegions,
            static region => region.HasExternalTarget);
        Assert.Contains(
            externalRegions,
            static region => region.HasExternalEntry);

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new Constant(0, TypeRef.CoreLib("System", "Int32")),
            [4, 8]));
        var fallthrough = new Block(4);
        fallthrough.Add(new Return(null));
        var alternate = new Block(8);
        alternate.Add(new Return(null));
        var switchBody = new BlockContainer();
        switchBody.Add(dispatch);
        switchBody.Add(fallthrough);
        switchBody.Add(alternate);
        var switchFunction = new IrFunction(
            "MultiSuccessor",
            StateMachine,
            new MethodSignature(Void, [], true, 0),
            [],
            switchBody);

        Assert.True(
            ClassicAsyncReconstructionPass.TryCapturePhysicalRegions(
                switchFunction,
                ClassicAsyncRegionHost.Execution,
                ExecutionAddress,
                out var switchRegions));
        Assert.Contains(
            switchRegions,
            static region => region.SuccessorMultiplicity == 3);
    }

    [Fact]
    public void RegionLedgerRejectsForeignAndNonCanonicalPhysicalPaths()
    {
        ClassicAsyncPhysicalRegion kickoff =
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0");
        var foreignId = new ClassicAsyncPhysicalRegionId(
            ClassicAsyncRegionHost.Execution,
            KickoffAddress,
            "0.0");
        var foreign = new ClassicAsyncPhysicalRegion(
            foreignId,
            EntryMultiplicity: 1,
            SuccessorMultiplicity: 1,
            HasExternalEntry: false,
            HasExternalTarget: false,
            LeavesRegion: false);
        ClassicAsyncPhysicalRegion unstable =
            Physical(ClassicAsyncRegionHost.Execution, "0.01");

        Assert.False(TryCreatePhysicalLedger(
            [kickoff, foreign],
            [kickoff.Id, foreign.Id],
            []));
        Assert.False(TryCreatePhysicalLedger(
            [kickoff, unstable],
            [kickoff.Id, unstable.Id],
            []));
    }

    static bool TryCreateRegionLedger(
        IReadOnlyList<ClassicAsyncUserRegion> userRegions,
        IReadOnlyList<ClassicAsyncUserRegionRealization> realizations,
        out ClassicAsyncRegionLedger ledger)
    {
        List<ClassicAsyncPhysicalRegion> physical =
        [
            Physical(ClassicAsyncRegionHost.Kickoff, "0.0"),
            .. userRegions
                .Select(static region => region.PhysicalRegion)
                .Distinct()
                .Select(static id => new ClassicAsyncPhysicalRegion(
                    id,
                    EntryMultiplicity: 1,
                    SuccessorMultiplicity: 1,
                    HasExternalEntry: false,
                    HasExternalTarget: false,
                    LeavesRegion: false)),
        ];
        ClassicAsyncPhysicalRegionId[] consumed =
        [
            .. physical.Select(static region => region.Id),
        ];
        return ClassicAsyncRegionLedger.TryCreate(
            KickoffAddress,
            ExecutionAddress,
            physical,
            consumed,
            [],
            userRegions,
            realizations,
            out ledger);
    }

    static bool TryCreatePhysicalLedger(
        IReadOnlyList<ClassicAsyncPhysicalRegion> physical,
        IReadOnlyList<ClassicAsyncPhysicalRegionId> consumed,
        IReadOnlyList<ClassicAsyncPhysicalRegionId> preserved)
        => ClassicAsyncRegionLedger.TryCreate(
            KickoffAddress,
            ExecutionAddress,
            physical,
            consumed,
            preserved,
            [],
            [],
            out _);

    static ClassicAsyncPhysicalRegion Physical(
        ClassicAsyncRegionHost host,
        string path,
        int entryMultiplicity = 1,
        int successorMultiplicity = 1,
        bool hasExternalEntry = false,
        bool hasExternalTarget = false,
        bool leavesRegion = false)
        => new(
            PhysicalId(host, path),
            entryMultiplicity,
            successorMultiplicity,
            hasExternalEntry,
            hasExternalTarget,
            leavesRegion);

    static ClassicAsyncPhysicalRegionId PhysicalId(
        ClassicAsyncRegionHost host,
        string path)
        => new(
            host,
            host == ClassicAsyncRegionHost.Kickoff
                ? KickoffAddress
                : ExecutionAddress,
            path);

    static MethodRef CaptureMoveNextRequest(
        MetadataSource source,
        string methodName = "AwaitVoid")
    {
        IrFunction function = ImportClassicFixture(
            source,
            methodName);
        var context = PassContext.ForImport(
            method => IrImporter.Import(source, method));

        RunUntilClassicAsync(function, context);

        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var decision = Assert.IsType<ClassicAsyncDecision.Reconstruct>(
            PublishedDecision(evidence));
        ClassicAsyncMachine machine = decision.Plan.Machine;
        return new MethodRef(
            machine.StateMachineType,
            "MoveNext",
            Void,
            [],
            HasThis: true)
        {
            ExactDefinitionAddress = machine.Execution,
            ExactDefinitionAcquisitionGuard =
                machine.AcquisitionGuard,
        };
    }

    static IrFunction PreparedKickoff(
        MetadataSource source,
        string methodName)
    {
        IrFunction function = ImportClassicFixture(source, methodName);
        RunBeforeClassicAsync(
            function,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        return function;
    }

    static TypeRef ExpectedStateMachineType(
        MetadataSource source,
        string methodName)
    {
        IrFunction kickoffFunction =
            PreparedKickoff(source, methodName);
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoffFunction.ClassicAsyncRelationship)
                    .Relationship)
                .Relationship;
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoffFunction,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out ClassicAsyncReconstructionPass.Kickoff kickoff,
                out _,
                out _));
        return kickoff.StateMachineType;
    }

    static ClassicAsyncStorage ExpectedBuilderStorage(
        MetadataSource source,
        string methodName)
    {
        IrFunction kickoffFunction =
            PreparedKickoff(source, methodName);
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                Assert.IsType<ClassicAsyncRelationshipEvidence>(
                    kickoffFunction.ClassicAsyncRelationship)
                    .Relationship)
                .Relationship;
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoffFunction,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out ClassicAsyncReconstructionPass.Kickoff kickoff,
                out _,
                out _));
        return kickoff.BuilderStorage;
    }

    static IrFunction PreparedMoveNext(
        MetadataSource source,
        string methodName)
    {
        IrFunction kickoffFunction =
            PreparedKickoff(source, methodName);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            kickoffFunction.ClassicAsyncRelationship);
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                evidence.Relationship).Relationship;
        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                kickoffFunction,
                relationship.StateMachineType,
                relationship.StateMachineName,
                out ClassicAsyncReconstructionPass.Kickoff kickoff,
                out _,
                out _));
        Assert.True(relationship.TryGetMethod(
            StateMachineMethodRole.MoveNext,
            out MetadataMethodAddress execution));
        var request = new MethodRef(
            kickoff.StateMachineType,
            "MoveNext",
            Void,
            [],
            HasThis: true)
        {
            ExactDefinitionAddress = execution,
            ExactDefinitionAcquisitionGuard =
                evidence.AcquisitionGuard,
        };
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, request));
        IrPasses.Run(
            function,
            IrPasses.ForReconstruction<
                ClassicAsyncReconstructionPass>(),
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        return function;
    }

    static void AssertKickoffIsNonNarrow(IrFunction function)
    {
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var resolved =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                evidence.Relationship);

        Assert.True(
            ClassicAsyncReconstructionPass.TryGetKickoff(
                function,
                resolved.Relationship.StateMachineType,
                resolved.Relationship.StateMachineName,
                out _,
                out _,
                out bool narrow));
        Assert.False(narrow);
    }

    static TypeRef ForeignBuilderFactory()
        => TypeRef.Definition(
            "Foreign",
            "ReviewRepro",
            "SideEffectingBuilderFactory");

    static void ReplaceBuilderMember(
        IrFunction function,
        string memberName,
        Func<MethodRef, MethodRef> mutate)
    {
        if (memberName != "get_Task")
        {
            Call call = Assert.Single(
                function.Descendants.OfType<Call>(),
                candidate => candidate.Callee.Name == memberName);
            ReplaceCallee(call, mutate(call.Callee));
            return;
        }

        LoadProperty property = Assert.Single(
            function.Descendants.OfType<LoadProperty>(),
            static candidate =>
                candidate.Accessor.Name == "get_Task");
        IrExpression instance = Assert.IsAssignableFrom<IrExpression>(
            property.Instance);
        instance.Detach();
        var replacement = new LoadProperty(
            mutate(property.Accessor),
            instance,
            [])
        {
            IsVirtual = property.IsVirtual,
        };
        property.ReplaceWith(replacement);
    }

    static Call ReplaceCallee(Call call, MethodRef callee)
    {
        List<IrExpression> arguments = [.. call.Arguments];
        foreach (IrExpression argument in arguments)
            argument.Detach();
        var replacement = new Call(
            callee,
            call.IsVirtual,
            arguments)
        {
            ConstrainedTo = call.ConstrainedTo,
        };
        call.ReplaceWith(replacement);
        return replacement;
    }

    static ClassicAsyncPlanningSession PlanningSession(
        ClassicAsyncRelationshipEvidence evidence)
        => Assert.IsType<ClassicAsyncPlanningSession>(
            evidence.PlanningSession);

    static ClassicAsyncDecision PublishedDecision(
        ClassicAsyncRelationshipEvidence evidence)
    {
        var prepared =
            Assert.IsType<ClassicAsyncPreparationResult.Decided>(
                PlanningSession(evidence).Prepare(evidence));
        return prepared.Decision;
    }

    static IrFunction BuildSupportLookalike()
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new LoadField(
            new FieldRef(StateMachine, "<>t__builder", Builder),
            new LoadArgument(0, "this", StateMachine))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(
                StateMachine,
                "SideEffect",
                Void,
                [],
                HasThis: false),
            isVirtual: false,
            [])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "MoveNext",
            StateMachine,
            new MethodSignature(
                Void,
                [],
                HasThis: true,
                GenericParameterCount: 0),
            [],
            body)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
    }

    static IrFunction BuildKickoffLookalike()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Outer");
        var block = new Block(0);
        block.Add(new StoreField(
            new FieldRef(StateMachine, "<>t__builder", Builder),
            new LoadLocalAddress(0, StateMachine),
            new Call(
                new MethodRef(
                    Builder,
                    "Create",
                    Builder,
                    [],
                    HasThis: false),
                isVirtual: false,
                [])));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(
                Builder,
                "Start",
                Void,
                [],
                HasThis: true),
            isVirtual: false,
            [])));
        block.Add(new Return(new LoadProperty(
            new MethodRef(
                Builder,
                "get_Task",
                Task,
                [],
                HasThis: true),
            new LoadLocalAddress(0, StateMachine),
            [])));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "KickoffMethod",
            owner,
            new MethodSignature(
                Task,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [StateMachine],
            body);
    }

    static MetadataSource OpenClassicFixture()
    {
        string configuration = new DirectoryInfo(
            AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        return MetadataSource.Open(path);
    }

    static IrFunction ImportClassicFixture(
        MetadataSource source,
        string methodName)
        => Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures",
            methodName));

    static void RunUntilClassicAsync(
        IrFunction function,
        PassContext context)
    {
        foreach (IIrPass pass in IrPasses.Default)
        {
            pass.Run(function, context);
            if (pass is ClassicAsyncReconstructionPass)
                return;
        }

        Assert.Fail("ClassicAsyncReconstructionPass is not registered.");
    }

    static void RunBeforeClassicAsync(
        IrFunction function,
        PassContext context)
    {
        foreach (IIrPass pass in IrPasses.Default)
        {
            if (pass is ClassicAsyncReconstructionPass)
                return;
            pass.Run(function, context);
        }

        Assert.Fail("ClassicAsyncReconstructionPass is not registered.");
    }

    static string SubtreeSignature(IrNode node)
        => string.Join(
            "\n",
            node.Descendants.Prepend(node).Select(current =>
                $"{current.GetType().Name}:{current.Describe()}"));
}
