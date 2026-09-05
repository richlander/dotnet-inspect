using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal enum ClassicInverseProtocolKind
{
    /// <summary>No positive rule recognizes this node.</summary>
    None,

    /// <summary>Exact scaffolding; the whole subtree is protocol.</summary>
    OwnedProtocol,

    /// <summary>
    /// Exact scaffolding whose designated child slots carry user material and
    /// must be accounted separately.
    /// </summary>
    ProtocolFrame,

    /// <summary>A scaffolding container whose children are accounted separately.</summary>
    ProtocolContainer,

    /// <summary>A pure sequencing container: no condition, loop, or exception behavior.</summary>
    TransparentContainer,

    /// <summary>Positively proven semantically inert.</summary>
    Preserved,
}

internal readonly record struct ClassicInverseProtocolRule(
    ClassicInverseProtocolKind Kind,
    string Name,
    ImmutableArray<int> DescendSlots)
{
    internal static readonly ClassicInverseProtocolRule None =
        new(ClassicInverseProtocolKind.None, "", []);

    internal static ClassicInverseProtocolRule Owned(string name)
        => new(ClassicInverseProtocolKind.OwnedProtocol, name, []);

    internal static ClassicInverseProtocolRule Frame(
        string name,
        params int[] slots)
        => new(ClassicInverseProtocolKind.ProtocolFrame, name, [.. slots]);

    internal static ClassicInverseProtocolRule Container(string name)
        => new(ClassicInverseProtocolKind.ProtocolContainer, name, []);

    internal static ClassicInverseProtocolRule Transparent(string name)
        => new(ClassicInverseProtocolKind.TransparentContainer, name, []);

    internal static ClassicInverseProtocolRule Preserved(string name)
        => new(ClassicInverseProtocolKind.Preserved, name, []);
}

/// <summary>
/// Positive classification of the Roslyn classic-async lowering shell.
/// <para>
/// Every rule matches an exact shape. There is no deny list: a node the rules
/// do not recognize is unaccounted, and an unaccounted node declines. That is
/// what keeps an effect hidden inside a condition, operand, initializer, or
/// filter from escaping the semantic ledger.
/// </para>
/// </summary>
internal static class ClassicInverseProtocol
{
    internal static ClassicInverseProtocolRule Classify(
        IrNode node,
        ClassicInverseShellFacts shell,
        ClassicInverseCandidate candidate)
    {
        TypeRef machine = shell.Machine;

        switch (node)
        {
            case BlockContainer:
                return ClassicInverseProtocolRule.Transparent("block-container");

            case Block:
                return ClassicInverseProtocolRule.Transparent("block-sequence");

            case TryCatch tryCatch when IsBuilderCompletionShell(tryCatch, shell):
                return ClassicInverseProtocolRule.Container("builder-completion-try");

            case CatchClause clause when shell.Protocol.Proves(
                clause,
                ClassicInverseLoweringProof.CompletionCatch):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.CompletionCatch);

            // The shell's copy of <>1__state into its dispatch local, proven as
            // one role of the closed state protocol.
            case StoreLocal store
                when store.Index == shell.StateLocal
                    && store.Index >= 0
                    && shell.Protocol.Proves(
                        store,
                        ClassicInverseLoweringProof.StateLocalStore):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.StateLocalStore);

            case StoreField { Field.Name: "<>1__state" } state
                when state.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(state.Field, machine)
                    && shell.Protocol.Proves(
                        state,
                        ClassicInverseLoweringProof.StateFieldStore):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.StateFieldStore);

            // The import spills a state constant through the compiler's dup
            // slot; the proof binds that slot to its state stores.
            case StoreStackSlot spill when shell.Protocol.Proves(
                spill,
                ClassicInverseLoweringProof.StateSpill):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.StateSpill);

            // The awaiter transfer is protocol only where the proof bound this
            // exact cache, restore, or clear to one suspension's state.
            case StoreField cache when shell.Protocol.Proves(
                cache,
                ClassicInverseLoweringProof.AwaiterCacheStore):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.AwaiterCacheStore);

            case StoreLocal restore when shell.Protocol.Proves(
                restore,
                ClassicInverseLoweringProof.AwaiterRestore):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.AwaiterRestore);

            case InitObject clear when shell.Protocol.Proves(
                clear,
                ClassicInverseLoweringProof.AwaiterClear):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.AwaiterClear);

            // stloc awaiter <- callvirt GetAwaiter(<user operand>), proven
            // against the exact typed member in both raw and planning spaces.
            case StoreLocal bind when shell.Protocol.Proves(
                bind,
                ClassicInverseLoweringProof.AwaiterBind):
                return ClassicInverseProtocolRule.Frame(
                    ClassicInverseLoweringProof.AwaiterBind,
                    0);

            case Call call when shell.Protocol.Proves(
                call,
                ClassicInverseLoweringProof.GetAwaiterCall):
                return ClassicInverseProtocolRule.Frame(
                    ClassicInverseLoweringProof.GetAwaiterCall,
                    0);

            case StoreLocal store when shell.Protocol.Proves(
                store, ClassicInverseLoweringProof.AwaitOperandStore):
                return ClassicInverseProtocolRule.Frame(
                    ClassicInverseLoweringProof.AwaitOperandStore, 0);

            case LoadLocalAddress address when shell.Protocol.Proves(
                address, ClassicInverseLoweringProof.AwaitOperandAddress):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.AwaitOperandAddress);

            case ExpressionStatement { Expression: Call builderCall }
                when IsProvenCompletionCallback(builderCall, shell, candidate):
                return ClassicInverseProtocolRule.Owned(
                    shell.Protocol.RoleOf(builderCall)!);

            case Return { Value: null }:
                return ClassicInverseProtocolRule.Owned("shell-return");

            case ConditionalBranch stateBranch when shell.Protocol.Proves(
                stateBranch,
                ClassicInverseLoweringProof.StateDispatch):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.StateDispatch);

            case ConditionalBranch guard when shell.Protocol.Proves(
                guard,
                ClassicInverseLoweringProof.SuspensionGuard):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.SuspensionGuard);

            case ConditionalBranch completed when shell.Protocol.Proves(
                completed,
                ClassicInverseLoweringProof.AwaitCompletionBranch):
                return ClassicInverseProtocolRule.Owned(
                    ClassicInverseLoweringProof.AwaitCompletionBranch);

            // Reading the state-machine reference itself carries no behavior;
            // MoveNext's argument 0 is the compiler-owned machine.
            case LoadArgument { Index: 0 }:
                return ClassicInverseProtocolRule.Preserved("machine-receiver");

            default:
                return ClassicInverseProtocolRule.None;
        }
    }

    /// <summary>
    /// How a shell node is accounted when it appears on a consumed node's
    /// ancestor path. <c>null</c> means unmodeled, which declines.
    /// </summary>
    internal static ClassicInverseAncestorKind? ClassifyAncestor(
        IrNode node,
        ClassicInverseShellFacts shell,
        ClassicInverseCandidate candidate)
    {
        ClassicInverseProtocolRule rule = Classify(node, shell, candidate);
        return rule.Kind switch
        {
            ClassicInverseProtocolKind.TransparentContainer =>
                ClassicInverseAncestorKind.Transparent,
            ClassicInverseProtocolKind.OwnedProtocol
                or ClassicInverseProtocolKind.ProtocolFrame
                or ClassicInverseProtocolKind.ProtocolContainer =>
                ClassicInverseAncestorKind.Protocol,
            _ => null,
        };
    }

    static bool IsProvenCompletionCallback(
        Call call,
        ClassicInverseShellFacts shell,
        ClassicInverseCandidate candidate)
        => shell.Protocol.RoleOf(call) switch
        {
            ClassicInverseLoweringProof.AwaitCallback => true,
            ClassicInverseLoweringProof.SetExceptionCallback => true,
            // The recipe owns which local the completion result reads; the
            // protocol proof owns that there is exactly one such callback.
            ClassicInverseLoweringProof.SetResultCallback =>
                call.Arguments.Count == 1
                || (call.Arguments.Count == 2
                    && call.Arguments[1] is LoadLocal result
                    && result.Index == candidate.ResultLocal),
            _ => false,
        };

    static bool IsBuilderCompletionShell(
        TryCatch tryCatch,
        ClassicInverseShellFacts shell)
        => tryCatch.Children.Count == 2
            && tryCatch.Children[1] is CatchClause clause
            && shell.Protocol.Proves(
                clause,
                ClassicInverseLoweringProof.CompletionCatch);
}
