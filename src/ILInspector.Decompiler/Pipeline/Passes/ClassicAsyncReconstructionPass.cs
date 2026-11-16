using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs classic async state-machine kickoffs (runtime-async=off) back to
/// async bodies. The source logic lives in <c>&lt;M&gt;d__N.MoveNext</c>; the public
/// kickoff only initializes the state machine and returns the builder's task.
/// <para>
/// The pass owns acquisition and application only. It preserves every imported
/// host unless the Decompiler request adapter supplies an authenticated declared
/// kickoff, then imports the exact execution MethodDef and hands the request to
/// <see cref="ClassicGeneralInverse"/>. Every reconstruction decision — and
/// every proof that licenses one — belongs to that owner (<c>docs/design/classic-async-reconstruction.md</c>). The pass
/// never reconstructs from shape resemblance of its own.
/// </para>
/// </summary>
public sealed class ClassicAsyncReconstructionPass : IIrPass
{
    public string Name => "classic-async-reconstruction";

    public void Run(IrFunction function, PassContext context)
    {
        if (ClassicAsyncStageApplication.Decide(
                function.ClassicAsyncRequest)
            == ClassicAsyncStageApplicationKind.PreserveImportedBody)
        {
            return;
        }

        if (context.ImportMethodBody is null)
            return;
        var available =
            (ClassicAsyncRequestAdapterResult.RequestAvailable)
                function.ClassicAsyncRequest!;
        ClassicAsyncRequestSeed seed = available.Request;
        if (!TryGetKickoff(function, seed.Relationship.StateMachineType, out var kickoff))
            return;

        var moveNextMethod = new MethodRef(
            kickoff.StateMachineType,
            "MoveNext",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: true);
        moveNextMethod = moveNextMethod with
        {
            ExactDefinitionAddress = seed.ExecutionMethod,
            ExactDefinitionAcquisitionGuard =
                seed.AcquisitionGuard,
        };

        if (!context.TryEnterCrossMethodPipeline(moveNextMethod, out var scope))
            return;

        ClassicGeneralInverse.Outcome general;
        ImmutableArray<DecompilerFidelityCause> localNameImportCauses;
        using (scope)
        {
            IrFunction? rawMoveNext = scope.Import();
            if (rawMoveNext is null)
                return;
            localNameImportCauses = [
                .. rawMoveNext.LocalNameImportCauses.Select(static cause =>
                    cause.WithLocation(DecompilerFidelityLocation.Unknown)),
            ];

            IrFunction rawKickoff = context.ImportMethodBody!(new MethodRef(
                function.DeclaringType,
                function.Name,
                function.Signature.ReturnType,
                [.. function.Signature.Parameters.Select(
                    static parameter => parameter.Type)],
                function.Signature.HasThis)
            {
                ExactDefinitionAddress = seed.DeclaredMethod,
                ExactDefinitionAcquisitionGuard = seed.AcquisitionGuard,
            })!;
            if (rawKickoff is null)
                return;

            var request = new ClassicInverseRequest(
                seed.DeclaredMethod,
                seed.ExecutionMethod,
                seed.Relationship,
                seed.AcquisitionGuard,
                rawKickoff,
                rawMoveNext,
                kickoff.StateMachineLocal);
            if (request.CorrelationFailure() is { } correlation)
            {
                MarkInvalidRequest(function, kickoff, context, correlation);
                return;
            }

            // Replacement IL in a reference assembly has no lowering to invert;
            // the imported host is kept as it is.
            if (request.HasBodyReplacingBodies)
                return;

            general = ClassicGeneralInverse.TryReconstruct(
                rawMoveNext,
                function,
                kickoff.StateMachineType,
                scope.Run);
        }

        switch (general)
        {
            case ClassicGeneralInverse.Outcome.Reconstructed reconstructed:
                ApplyGeneral(function, kickoff, reconstructed, localNameImportCauses, context);
                return;

            case ClassicGeneralInverse.Outcome.Declined declined:
                MarkDeclined(
                    function,
                    kickoff,
                    context,
                    "general inverse declined",
                    declined.Reason);
                return;
        }
    }

    static void ApplyGeneral(
        IrFunction function,
        Kickoff kickoff,
        ClassicGeneralInverse.Outcome.Reconstructed reconstructed,
        ImmutableArray<DecompilerFidelityCause> localNameImportCauses,
        PassContext context)
    {
        IrFunction body = reconstructed.Body;
        context.Stepper.StepOver(
            $"reconstruct classic async '{function.Name}' from "
            + $"{kickoff.StateMachineType.Name}.MoveNext via the general inverse "
            + $"({reconstructed.AwaitCount} awaits)");
        function.MergeTypeFactsFrom(body);
        function.ClearImportedExceptionFacts();
        // Slots the splice emptied (the state local, awaiter locals, the this
        // copy, machine temporaries) keep their lowered types but have no use;
        // declare only the slots the body still binds or reads, judged by the
        // same reference census the printer and the invariant use (catch
        // variables, using and foreach headers, and pattern bindings included).
        var eliminated = new HashSet<int>(body.EliminatedLocalSlots);
        eliminated.UnionWith(
            Enumerable.Range(0, body.Locals.Length)
                .Where(index => !IrFunction.LocalSlotReferencesInScope(body, index).Any()));
        function.ResetLocals(
            body.Locals,
            body.LocalNames,
            eliminatedSlots: eliminated,
            synthesizedNames: body.SynthesizedLocalNames,
            declaredInNestedScope: body.LocalDeclaredInNestedScope,
            declarationBindings: body.LocalDeclarationBindings,
            pdbLocalNameCandidates: body.PdbLocalNameCandidates);
        function.LocalNameImportCauses =
            function.LocalNameImportCauses.AddRange(localNameImportCauses);
        function.RequiresAsyncBodyModifier = true;
        function.Body.DetachChildren();
        foreach (Block block in body.Body.Blocks.ToList())
        {
            block.Detach();
            foreach (IrNode statement in block.Children)
                Reanchor(statement, kickoff.SourceOffset);
            function.Body.Add(block);
        }
    }

    // The transplanted nodes carry MoveNext IL offsets that are foreign to the
    // kickoff; anchor each statement to the state-machine construction instead.
    static void Reanchor(IrNode node, int offset)
    {
        foreach (IrNode descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    sealed record Kickoff(TypeRef StateMachineType, int StateMachineLocal, int SourceOffset);

    static bool MatchesStateMachine(
        TypeRef observed,
        MetadataTypeDefinitionAddress expected)
    {
        if (observed.Kind == TypeRefKind.GenericInstance
            && observed.ElementType is { } definition)
        {
            observed = definition;
        }

        return observed.DefinitionModuleVersionId == expected.ModuleVersionId
            && !observed.DefinitionHandle.IsNil
            && MetadataTokens.GetToken(observed.DefinitionHandle)
                == expected.Definition.Value;
    }

    static bool TryGetKickoff(
        IrFunction function,
        MetadataTypeDefinitionAddress certifiedStateMachine,
        out Kickoff kickoff)
    {
        kickoff = null!;
        if (function.Body.Blocks is not [var block])
            return false;

        StoreField? builderStore = null;
        ExpressionStatement? startStatement = null;
        Return? returnTask = null;

        foreach (var statement in block.Children)
        {
            if (statement is StoreField { Field.Name: "<>t__builder", Instance: LoadLocalAddress builderLocal } store
                && builderStore is null)
            {
                builderStore = store;
                if (builderLocal.Index < 0 || builderLocal.Index >= function.Locals.Length)
                    return false;
                continue;
            }

            if (statement is ExpressionStatement { Expression: Call { Callee.Name: "Start" } } expression)
                startStatement = expression;
            else if (statement is Return { Value: LoadProperty { PropertyName: "Task" } } ret)
                returnTask = ret;
        }

        // An async void kickoff has no `return builder.Task;`.
        bool voidKickoff = MemberIdentity.IsCoreLibraryType(
            function.Signature.ReturnType, "System", "Void");
        if (builderStore?.Instance is not LoadLocalAddress stateMachineAddress
            || startStatement is null
            || (returnTask is null && !voidKickoff))
        {
            return false;
        }

        var stateMachineType = function.Locals[stateMachineAddress.Index];
        // The certified machine type identifies the local; the generated-name
        // test also admits the `<Method>d__N` spelling, which misses local
        // functions and lambdas (`<<Outer>g__Local|N_M>d`). Request validation
        // rejects an execution body that does not belong to the kickoff's
        // machine, so this test only selects the candidate local.
        if (MatchesStateMachine(stateMachineType, certifiedStateMachine)
            || IsStateMachineType(stateMachineType))
        {
            kickoff = new Kickoff(stateMachineType, stateMachineAddress.Index, builderStore.SourceOffset);
            return true;
        }

        return false;
    }

    static bool IsStateMachineType(TypeRef type)
    {
        var name = MetadataName(type);
        return name.StartsWith("<", StringComparison.Ordinal)
            && name.Contains(">d__", StringComparison.Ordinal);
    }

    static string MetadataName(TypeRef type)
    {
        var name = type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } definition
            ? definition.Name
            : type.Name;
        var nested = name.LastIndexOf('+');
        return nested >= 0 ? name[(nested + 1)..] : name;
    }

    internal static bool IsMachineFieldStore(
        StoreField store,
        TypeRef machine)
        => ClassicInverseNodeFacts.IsMachineField(store.Field, machine);

    static void MarkDeclined(
        IrFunction function,
        Kickoff kickoff,
        PassContext context,
        string reason,
        string detail)
    {
        context.Stepper.StepOver(
            $"decline classic async '{function.Name}': {reason}");

        PreserveKickoff(
            function,
            kickoff,
            $"{reason}; original kickoff preserved");
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            $"classic async reconstruction declined: {reason} ({detail})"));
    }

    static void MarkInvalidRequest(
        IrFunction function,
        Kickoff kickoff,
        PassContext context,
        string correlation)
    {
        context.Stepper.StepOver(
            $"classic async request for '{function.Name}' is invalid: {correlation}");

        PreserveKickoff(
            function,
            kickoff,
            "classic async request is invalid; original kickoff preserved");
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            $"classic async reconstruction failed: {correlation}"));
    }

    static void PreserveKickoff(IrFunction function, Kickoff kickoff, string reason)
    {
        IReadOnlyList<Block> originalBlocks = function.Body.Blocks;
        function.Body.DetachChildren();

        var block = new Block(originalBlocks[0].StartOffset);
        var marker = new UnsupportedNode(
            kickoff.SourceOffset,
            "classic async",
            reason);
        marker.SetSourceOffset(kickoff.SourceOffset);
        var markerStatement = new ExpressionStatement(marker);
        markerStatement.SetSourceOffset(kickoff.SourceOffset);
        block.Add(markerStatement);

        foreach (Block originalBlock in originalBlocks)
        {
            foreach (IrNode statement in originalBlock.DetachChildren())
                block.Add(statement);
        }

        function.Body.Add(block);
        function.RequiresAsyncBodyModifier = false;
    }
}
