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

            case CatchClause clause when IsBuilderCompletionCatch(clause, shell):
                return ClassicInverseProtocolRule.Owned("builder-completion-catch");

            // The shell's copy of <>1__state into its dispatch local.
            case StoreLocal store
                when store.Index == shell.StateLocal
                    && store.Index >= 0
                    && store.Value is Constant
                        or LoadStackSlot
                        or LoadField { Field.Name: "<>1__state" }:
                return ClassicInverseProtocolRule.Owned("state-local-store");

            case StoreField { Field.Name: "<>1__state", Value: Constant } state
                when state.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(state.Field, machine):
                return ClassicInverseProtocolRule.Owned("state-field-store");

            case StoreField { Value: LoadLocal awaiterValue } cache
                when cache.Field.Name.StartsWith("<>u__", StringComparison.Ordinal)
                    && cache.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(cache.Field, machine)
                    && shell.AwaiterLocals.Contains(awaiterValue.Index):
                return ClassicInverseProtocolRule.Owned("awaiter-cache-store");

            case StoreLocal { Value: LoadField cachedAwaiter } restore
                when shell.AwaiterLocals.Contains(restore.Index)
                    && cachedAwaiter.Field.Name.StartsWith("<>u__", StringComparison.Ordinal)
                    && cachedAwaiter.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(cachedAwaiter.Field, machine):
                return ClassicInverseProtocolRule.Owned("awaiter-restore");

            case InitObject { Address: LoadFieldAddress clear }
                when clear.Field.Name.StartsWith("<>u__", StringComparison.Ordinal)
                    && clear.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(clear.Field, machine):
                return ClassicInverseProtocolRule.Owned("awaiter-clear");

            // stloc awaiter <- callvirt GetAwaiter(<user operand>).
            case StoreLocal { Value: Call { Callee.Name: "GetAwaiter" } getAwaiter } bind
                when shell.AwaiterLocals.Contains(bind.Index)
                    && getAwaiter.Arguments.Count == 1:
                return ClassicInverseProtocolRule.Frame("awaiter-bind", 0);

            case Call { Callee.Name: "GetAwaiter" } call
                when call.Arguments.Count == 1
                    && call.Parent is StoreLocal parentStore
                    && shell.AwaiterLocals.Contains(parentStore.Index):
                return ClassicInverseProtocolRule.Frame("get-awaiter", 0);

            case ExpressionStatement { Expression: Call builderCall }
                when IsBuilderCallback(builderCall, shell, candidate):
                return ClassicInverseProtocolRule.Owned(
                    $"builder-{builderCall.Callee.Name}");

            case Return { Value: null }:
                return ClassicInverseProtocolRule.Owned("shell-return");

            case ConditionalBranch stateBranch when IsStateDispatch(stateBranch.Condition, shell):
                return ClassicInverseProtocolRule.Owned("state-dispatch");

            case ConditionalBranch { Condition: LoadProperty completed }
                when completed.PropertyName == "IsCompleted"
                    && completed.Instance is LoadLocalAddress awaiter
                    && shell.AwaiterLocals.Contains(awaiter.Index):
                return ClassicInverseProtocolRule.Owned("awaiter-completed-branch");

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

    static bool IsStateDispatch(IrExpression condition, ClassicInverseShellFacts shell)
    {
        if (shell.StateLocal < 0)
            return false;
        return condition switch
        {
            LogicalNot { Operand: LoadLocal load } => load.Index == shell.StateLocal,
            Comparison { Left: LoadLocal load, Right: Constant } =>
                load.Index == shell.StateLocal,
            Comparison { Right: LoadLocal load, Left: Constant } =>
                load.Index == shell.StateLocal,
            LoadLocal load => load.Index == shell.StateLocal,
            _ => false,
        };
    }

    static bool IsBuilderCallback(
        Call call,
        ClassicInverseShellFacts shell,
        ClassicInverseCandidate candidate)
    {
        if (!ClassicInverseNodeFacts.IsAsyncMethodBuilder(call.Callee.DeclaringType)
            || call.Arguments.Count == 0
            || !ClassicInverseNodeFacts.IsBuilderAccess(
                call.Arguments[0],
                shell.Machine))
        {
            return false;
        }

        return call.Callee.Name switch
        {
            "AwaitUnsafeOnCompleted" =>
                call.Arguments.Count == 3
                && call.Arguments[1] is LoadLocalAddress awaiter
                && shell.AwaiterLocals.Contains(awaiter.Index)
                && call.Arguments[2] is LoadArgument { Index: 0 },
            "SetException" =>
                call.Arguments.Count == 2
                && call.Arguments[1] is LoadLocal,
            "SetResult" =>
                call.Arguments.Count == 1
                || (call.Arguments.Count == 2
                    && call.Arguments[1] is LoadLocal result
                    && result.Index == candidate.ResultLocal),
            _ => false,
        };
    }

    static bool IsBuilderCompletionShell(
        TryCatch tryCatch,
        ClassicInverseShellFacts shell)
        => tryCatch.Children.Count == 2
            && tryCatch.Children[1] is CatchClause clause
            && IsBuilderCompletionCatch(clause, shell);

    /// <summary>
    /// The compiler's <c>catch (Exception e) { state = -2; builder.SetException(e);
    /// return; }</c> completion arm, matched exactly.
    /// </summary>
    static bool IsBuilderCompletionCatch(
        CatchClause clause,
        ClassicInverseShellFacts shell)
    {
        if (clause.Filter is not null)
            return false;
        if (clause.Body.Blocks is not [Block block])
            return false;
        if (block.Children is not
            [
                StoreField { Field.Name: "<>1__state", Value: Constant { Value: -2 } } state,
                ExpressionStatement { Expression: Call setException },
                Return { Value: null },
            ])
        {
            return false;
        }

        return state.Instance is LoadArgument { Index: 0 }
            && ClassicInverseNodeFacts.IsMachineField(state.Field, shell.Machine)
            && setException.Callee.Name == "SetException"
            && ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                setException.Callee.DeclaringType)
            && setException.Arguments.Count == 2
            && ClassicInverseNodeFacts.IsBuilderAccess(
                setException.Arguments[0],
                shell.Machine)
            && setException.Arguments[1] is LoadLocal;
    }
}
