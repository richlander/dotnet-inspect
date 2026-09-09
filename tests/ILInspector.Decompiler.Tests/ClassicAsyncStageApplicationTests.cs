using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.DecompilerHarness;
using DotnetInspector.Fixtures;
using MetadataStateMachineFixtures =
    ILInspector.Metadata.StateMachineFixtures.StateMachineFixtures;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
[Trait("Speed", "Fast")]
public sealed class ClassicAsyncStageApplicationTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";
    const string FixtureMethod = "TwoSequentialAwaits";

    [Fact]
    public void ExactClassicExecutionRole_PreservesImportedStageSnapshot()
    {
        using var source = OpenFixture();
        IrFunction function = ImportRole(
            source,
            StateMachineMethodRole.MoveNext,
            ClassicAsyncHostRole.Execution);

        AssertStageSnapshotPreserved(function);
    }

    [Fact]
    public void ExactClassicSupportRole_PreservesImportedStageSnapshot()
    {
        using var source = OpenFixture();
        IrFunction function = ImportRole(
            source,
            StateMachineMethodRole.SetStateMachine,
            ClassicAsyncHostRole.Support);

        AssertStageSnapshotPreserved(function);
    }

    [Theory]
    [InlineData(StateMachineMethodRole.MoveNext)]
    [InlineData(StateMachineMethodRole.SetStateMachine)]
    public void ExplicitMethodImplRole_PreservesImportedStageSnapshot(
        StateMachineMethodRole role)
    {
        ClassicAsyncHostRole expectedHostRole =
            role == StateMachineMethodRole.MoveNext
                ? ClassicAsyncHostRole.Execution
                : ClassicAsyncHostRole.Support;
        using var source = MetadataSource.Open(
            typeof(MetadataStateMachineFixtures).Assembly.Location);
        IrFunction function = ImportRole(
            source,
            typeof(MetadataStateMachineFixtures).FullName!,
            nameof(MetadataStateMachineFixtures.ExplicitAsync),
            role,
            expectedHostRole);

        Assert.NotEqual(role.ToString(), function.Name);
        AssertStageSnapshotPreserved(function);
    }

    [Fact]
    public void AsyncIteratorExecutionRole_HasNoClassicMutationAuthority()
    {
        using var source = MetadataSource.Open(
            typeof(MetadataStateMachineFixtures).Assembly.Location);
        IrFunction kickoff = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(MetadataStateMachineFixtures).FullName!,
                nameof(MetadataStateMachineFixtures.AsyncIterator)));
        var kickoffEvidence = Assert.IsType<
            ClassicAsyncRequestAdapterResult.Filtered>(
                kickoff.ClassicAsyncRequest).Evidence;
        var resolved = Assert.IsType<
            StateMachineRelationshipResult.Resolved>(
                kickoffEvidence.Relationship);
        Assert.Equal(
            StateMachineClaimKind.AsyncIterator,
            resolved.Relationship.Kind);
        var moveNext = Assert.IsType<StateMachineRoleDisposition.Present>(
            resolved.Relationship.GetRole(
                StateMachineMethodRole.MoveNext));
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, moveNext.Method.Handle));
        var filtered = Assert.IsType<
            ClassicAsyncRequestAdapterResult.Filtered>(
                function.ClassicAsyncRequest);
        Assert.Equal(
            ClassicAsyncHostRole.Execution,
            filtered.Evidence.HostRole);
        Assert.Equal(
            ClassicAsyncStageApplicationKind.NoOpinion,
            ClassicAsyncStageApplication.Decide(
                function.ClassicAsyncRequest));

        AssertStageSnapshotPreserved(function);
    }

    [Fact]
    public void ExactDeclaredKickoff_IsTheEvaluationArm()
    {
        using var source = OpenFixture();
        IrFunction kickoff = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                FixtureType,
                FixtureMethod));

        Assert.Equal(
            ClassicAsyncStageApplicationKind.EvaluateDeclaredKickoff,
            ClassicAsyncStageApplication.Decide(
                kickoff.ClassicAsyncRequest));
    }

    [Fact]
    public void PreserveArm_ReturnsBeforeKickoffAcquisition()
    {
        using var source = OpenFixture();
        IrFunction kickoff = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                FixtureType,
                FixtureMethod));
        IrFunction execution = ImportRole(
            source,
            StateMachineMethodRole.MoveNext,
            ClassicAsyncHostRole.Execution);
        kickoff.ClassicAsyncRequest = execution.ClassicAsyncRequest;
        var imported = false;
        PassContext context = PassContext.ForImport(
            _ =>
            {
                imported = true;
                return null;
            });

        new ClassicAsyncReconstructionPass().Run(kickoff, context);

        Assert.False(imported);
    }

    [Fact]
    public void SharedPipeline_ExactClassicExecutionRoleRetainsPhysicalBody()
    {
        using var source = OpenFixture();
        IrFunction function = ImportRole(
            source,
            StateMachineMethodRole.MoveNext,
            ClassicAsyncHostRole.Execution);

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Contains("SetResult", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "SetException",
            result.Output,
            StringComparison.Ordinal);
        Assert.NotEmpty(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void OrdinarySameNamedMethod_HasNoClassicMutationAuthority()
    {
        using var source =
            MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass.PatternEnumerator)
                    .FullName!
                    .Replace('+', '.'),
                "MoveNext"));
        var filtered = Assert.IsType<
            ClassicAsyncRequestAdapterResult.Filtered>(
                function.ClassicAsyncRequest);
        Assert.Equal(
            ClassicAsyncHostRole.Ordinary,
            filtered.Evidence.HostRole);
        Assert.Equal(
            ClassicAsyncStageApplicationKind.NoOpinion,
            ClassicAsyncStageApplication.Decide(
                function.ClassicAsyncRequest));

        AssertStageSnapshotPreserved(function);
    }

    static MetadataSource OpenFixture()
        => MetadataSource.Open(
            FixtureCatalog.DecompilerClassicAsync.AssemblyPath());

    static IrFunction ImportRole(
        MetadataSource source,
        StateMachineMethodRole role,
        ClassicAsyncHostRole expectedHostRole)
        => ImportRole(
            source,
            FixtureType,
            FixtureMethod,
            role,
            expectedHostRole);

    static IrFunction ImportRole(
        MetadataSource source,
        string kickoffType,
        string kickoffMethod,
        StateMachineMethodRole role,
        ClassicAsyncHostRole expectedHostRole)
    {
        IrFunction kickoff = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                kickoffType,
                kickoffMethod));
        var available = Assert.IsType<
            ClassicAsyncRequestAdapterResult.RequestAvailable>(
                kickoff.ClassicAsyncRequest);
        var present = Assert.IsType<StateMachineRoleDisposition.Present>(
            available.Request.Relationship.GetRole(role));

        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, present.Method.Handle));
        var filtered = Assert.IsType<
            ClassicAsyncRequestAdapterResult.Filtered>(
                function.ClassicAsyncRequest);
        Assert.Equal(expectedHostRole, filtered.Evidence.HostRole);
        Assert.Equal(
            present.Method,
            filtered.Evidence.RequestedMethod);
        Assert.Equal(
            ClassicAsyncStageApplicationKind.PreserveImportedBody,
            ClassicAsyncStageApplication.Decide(
                function.ClassicAsyncRequest));
        return function;
    }

    static void AssertStageSnapshotPreserved(IrFunction function)
    {
        BlockContainer body = function.Body;
        IrNode[] descendants = function.Descendants.ToArray();
        var locals = function.Locals;
        var localNames = function.LocalNames;
        var synthesizedLocalNames = function.SynthesizedLocalNames;
        var nestedScopeEvidence = function.LocalDeclaredInNestedScope;
        int[] eliminatedLocalSlots =
            [.. function.EliminatedLocalSlots.Order()];
        var diagnostics = function.Diagnostics.ToArray();
        bool requiresAsyncBodyModifier =
            function.RequiresAsyncBodyModifier;
        bool requiresUnsafeContract = function.RequiresUnsafeContract;
        bool usesUpdatedMemorySafetyRules =
            function.UsesUpdatedMemorySafetyRules;
        bool skipLocalsInit = function.SkipLocalsInit;

        new ClassicAsyncReconstructionPass().Run(
            function,
            PassContext.None);

        Assert.Same(body, function.Body);
        Assert.Equal(descendants, function.Descendants);
        Assert.Equal(locals, function.Locals);
        Assert.Equal(localNames, function.LocalNames);
        Assert.Equal(
            synthesizedLocalNames,
            function.SynthesizedLocalNames);
        Assert.Equal(
            nestedScopeEvidence,
            function.LocalDeclaredInNestedScope);
        Assert.Equal(
            eliminatedLocalSlots,
            function.EliminatedLocalSlots.Order());
        Assert.Equal(diagnostics, function.Diagnostics);
        Assert.Equal(
            requiresAsyncBodyModifier,
            function.RequiresAsyncBodyModifier);
        Assert.Equal(
            requiresUnsafeContract,
            function.RequiresUnsafeContract);
        Assert.Equal(
            usesUpdatedMemorySafetyRules,
            function.UsesUpdatedMemorySafetyRules);
        Assert.Equal(skipLocalsInit, function.SkipLocalsInit);
        function.CheckInvariant();
    }
}
