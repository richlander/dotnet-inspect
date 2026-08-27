using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ClassicAsyncReconstructionPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
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
        MethodRef? requested = null;
        var context = PassContext.ForImport(method =>
        {
            if (method.ExactDefinitionAddress is not null)
                requested = method;
            return IrImporter.Import(source, method);
        });

        IrPasses.Run(function, IrPasses.Default, context);

        Assert.NotNull(requested);
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        var resolved = Assert.IsType<StateMachineRelationshipResult.Resolved>(
            evidence.Relationship);
        Assert.True(resolved.Relationship.TryGetMethod(
            StateMachineMethodRole.MoveNext,
            out var moveNext));
        Assert.Equal(moveNext, requested.ExactDefinitionAddress);
        Assert.Same(
            evidence.AcquisitionGuard,
            requested.ExactDefinitionAcquisitionGuard);
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
            ClassicAsyncKickoffDisposition.ReplacedNarrowHandoff,
            outcome.KickoffDisposition);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            function.ClassicAsyncDeclarationDisposition);
        Assert.Single(function.Body.Blocks);
        Assert.Single(function.Body.Blocks[0].Children);
        Assert.Empty(function.Locals);
        Assert.DoesNotContain(
            originalStatements,
            statement => function.Body.Blocks[0]
                .Children
                .Select(SubtreeSignature)
                .Contains(statement));
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

        new ClassicAsyncReconstructionPass().Run(function, context);

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

    static MethodRef CaptureMoveNextRequest(MetadataSource source)
    {
        IrFunction function = ImportClassicFixture(source, "AwaitVoid");
        MethodRef? requested = null;
        var context = PassContext.ForImport(method =>
        {
            if (method.ExactDefinitionAddress is not null)
                requested = method;
            return null;
        });

        RunUntilClassicAsync(function, context);

        return Assert.IsType<MethodRef>(requested);
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
