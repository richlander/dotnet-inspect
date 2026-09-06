namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Replaces a statement whose unsafe context would contain an await. Earlier
/// inlining or await recovery can erase the source boundary before the final
/// statement shape exists; evaluation order cannot be recovered soundly at
/// this stage, so the final tree declines visibly instead of emitting
/// CS4004-invalid C# at Full fidelity.
/// </summary>
public sealed class UnsafeAwaitBoundaryPass : IIrPass
{
    public string Name => "unsafe-await-boundary";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.UsesUpdatedMemorySafetyRules
            && UnsafeAwaitOperand.ContainsAwait(function)
            && HasUnscopableLegacyPointerStorage(function))
        {
            context.Stepper.StepOver(
                "decline legacy pointer lifetime crossing await");
            DeclineFunction(
                function,
                "legacy pointer lifetime cannot be scoped outside await");
            return;
        }

        foreach (var statement in function.Descendants
            .Where(node => node.Parent is Block)
            .ToList())
        {
            if (!ReferenceOwnership.IsInside(statement, function)
                || !UnsafeAwaitOperand.ContainsAwait(statement)
                || !RequiresUnsafeContext(
                    statement,
                    function.UsesUpdatedMemorySafetyRules,
                    function.SkipLocalsInit))
            {
                continue;
            }

            context.Stepper.StepOver(
                "decline statement whose unsafe context contains await",
                statement);
            int sourceOffset = Math.Max(statement.SourceOffset, 0);
            var marker = new UnsupportedNode(
                sourceOffset,
                "unsafe await boundary",
                "statement requires unsafe context and contains await");
            marker.SetSourceOffset(sourceOffset);
            var replacement = new ExpressionStatement(marker);
            replacement.SetSourceOffset(sourceOffset);
            statement.ReplaceWith(replacement);
            function.Diagnostics.Add(new DecompilerDiagnostic(
                DiagnosticIds.UnsupportedConstruct,
                "statement reconstruction declined: unsafe context would contain await"));
        }
    }

    static bool HasUnscopableLegacyPointerStorage(IrFunction function)
    {
        if (UnsafeAwaitOperand.ContainsPointer(function.Signature.ReturnType)
            || function.Signature.Parameters.Any(
                parameter => UnsafeAwaitOperand.ContainsPointer(parameter.Type)))
        {
            return true;
        }

        var fixedLocals = function.DescendantsOutsideNestedFunctions
            .OfType<Fixed>()
            .Where(fixedStatement => !fixedStatement.LocalIsStackSlot)
            .Select(fixedStatement => fixedStatement.LocalIndex)
            .ToHashSet();
        var fixedStackSlots = function.DescendantsOutsideNestedFunctions
            .OfType<Fixed>()
            .Where(fixedStatement => fixedStatement.LocalIsStackSlot)
            .Select(fixedStatement => fixedStatement.LocalIndex)
            .ToHashSet();

        var firstLocalStores = function.DescendantsOutsideNestedFunctions
            .OfType<StoreLocal>()
            .GroupBy(store => store.Index)
            .ToDictionary(group => group.Key, group => group.First());
        for (int index = 0; index < function.Locals.Length; index++)
        {
            if (!UnsafeAwaitOperand.ContainsPointer(function.Locals[index])
                || fixedLocals.Contains(index)
                || !LocalIsReferenced(function, index))
            {
                continue;
            }

            if (!firstLocalStores.TryGetValue(index, out var store)
                || !UnsafeAwaitOperand.CanScopeLegacyPointerLocal(function, store))
            {
                return true;
            }
        }

        foreach (var store in function.DescendantsOutsideNestedFunctions
            .OfType<StoreStackSlot>()
            .GroupBy(store => store.Slot)
            .Select(group => group.First()))
        {
            if (fixedStackSlots.Contains(store.Slot)
                || !UnsafeAwaitOperand.ContainsPointer(store.Value.ResultType))
            {
                continue;
            }
            if (!UnsafeAwaitOperand.CanScopeLegacyPointerStackSlot(function, store))
                return true;
        }

        return false;
    }

    static bool LocalIsReferenced(IrFunction function, int index)
        => function.DescendantsOutsideNestedFunctions.Any(candidate =>
            candidate is StoreLocal store && store.Index == index
            || candidate is LoadLocal load && load.Index == index
            || candidate is LoadLocalAddress address && address.Index == index);

    static void DeclineFunction(IrFunction function, string reason)
    {
        function.Body.DetachChildren();
        var marker = new UnsupportedNode(
            0,
            "unsafe await boundary",
            reason);
        var statement = new ExpressionStatement(marker);
        var block = new Block(0);
        block.Add(statement);
        function.Body.Add(block);
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            $"method reconstruction declined: {reason}"));
    }

    internal static bool RequiresUnsafeContext(
        IrNode statement,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit = false)
        => statement switch
        {
            ForLoop loop =>
                RequiresUnsafe(loop.Initializer, usesUpdatedMemorySafetyRules, skipLocalsInit)
                || RequiresUnsafe(loop.Condition, usesUpdatedMemorySafetyRules, skipLocalsInit)
                || RequiresUnsafe(loop.Increment, usesUpdatedMemorySafetyRules, skipLocalsInit),
            WhileLoop loop => RequiresUnsafe(
                loop.Condition,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit),
            DoWhileLoop loop => RequiresUnsafe(
                loop.Condition,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit),
            IfStatement conditional => RequiresUnsafe(
                conditional.Condition,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit),
            Switch @switch => RequiresUnsafe(
                @switch.Value,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit),
            Lock @lock => RequiresUnsafe(
                @lock.LockObject,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit),
            Fixed fixedStatement =>
                fixedStatement.RequiresUnsafeContext
                || RequiresUnsafe(
                    fixedStatement.PinSource,
                    usesUpdatedMemorySafetyRules,
                    skipLocalsInit),
            UsingStatement usingStatement =>
                RequiresUnsafe(
                    usingStatement.Resource,
                    usesUpdatedMemorySafetyRules,
                    skipLocalsInit)
                || MethodsRequireUnsafe(
                    usingStatement.ConsumedMemberRefs,
                    usesUpdatedMemorySafetyRules),
            ForeachStatement foreachStatement =>
                RequiresUnsafe(
                    foreachStatement.Collection,
                    usesUpdatedMemorySafetyRules,
                    skipLocalsInit)
                || MethodsRequireUnsafe(
                    foreachStatement.ConsumedMemberRefs,
                    usesUpdatedMemorySafetyRules),
            TryCatch tryCatch => tryCatch.Clauses.Any(
                clause => RequiresUnsafe(
                    clause.Filter,
                    usesUpdatedMemorySafetyRules,
                    skipLocalsInit)),
            LocalFunctionStatement or TryFinally => false,
            _ => RequiresUnsafe(
                statement,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit),
        };

    static bool RequiresUnsafe(
        IrNode? node,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit)
        => node is not null
            && UnsafeAwaitOperand.RequiresUnsafeContext(
                node,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit);

    static bool MethodsRequireUnsafe(
        IEnumerable<MethodRef> methods,
        bool usesUpdatedMemorySafetyRules)
        => methods.Any(method => UnsafeAwaitOperand.MethodRequiresUnsafe(
            method,
            usesUpdatedMemorySafetyRules));
}
