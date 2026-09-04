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
