using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.ControlFlow;
using ILInspector.Metadata;
using Inspector.Text;
using static ILInspector.Decompiler.Pipeline.PointerArithmetic;
using static ILInspector.Decompiler.Pipeline.PlaceIdentity;
using DecisionKey = (string RuleId, string Category, string Subject, string Detail, string OldValue, string NewValue, string DedupDiscriminator);

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Unsafe-context inference: which statements need an unsafe block or body
/// modifier, and how unsafe runs end. A flow decision the thin-writer plan
/// (#2095) moves to a pre-print pass.
/// </summary>
public sealed partial class CSharpPrinter
{
    int UnsafeRunEnd(IReadOnlyList<IrNode> statements, int start)
    {
        int end = start + 1;
        while (end < statements.Count && NeedsUnsafeBlock(statements[end]))
            end++;

        for (int i = start; i < end; i++)
        {
            int requiredEnd = UnsafeRunRequiredEnd(statements, i, end);
            if (requiredEnd > end)
            {
                end = requiredEnd;
                while (end < statements.Count && NeedsUnsafeBlock(statements[end]))
                    end++;
            }
        }

        return end;
    }

    int UnsafeRunRequiredEnd(IReadOnlyList<IrNode> statements, int declarationIndex, int searchStart)
    {
        return statements[declarationIndex] switch
        {
            StoreLocal store when _declaringStores.Contains(store) && NeedsUnsafeBlock(store)
                => LastReferenceEnd(statements, searchStart, node => ReferencesLocalIncludingSharedNestedScopes(node, store.Index)),
            _ => searchStart,
        };
    }

    static int LastReferenceEnd(IReadOnlyList<IrNode> statements, int start, Func<IrNode, bool> hasReference)
    {
        int end = start;
        for (int i = start; i < statements.Count; i++)
            if (hasReference(statements[i]))
                end = i + 1;
        return end;
    }

    void AppendStatementLabel(StringBuilder sb, IrNode statement, int indent)
    {
        if (statement.OwnsSourceLabel
            && statement.SourceOffset >= 0
            && _labelTargets.Contains(statement.SourceOffset))
            AppendLabel(sb, new string(' ', indent * 4), statement.SourceOffset);
    }

    /// <summary>
    /// Whether a statement must itself sit in an unsafe context. For a compound
    /// statement only its own header expressions are considered — the body is a
    /// separate statement sequence the recursion wraps independently, keeping the
    /// block minimal. A simple statement is tested whole.
    /// </summary>
    bool NeedsUnsafeContext(IrNode node)
    {
        if (ReferenceEquals(node, _constructorInitializerStatement)
            && _newMemorySafetyRules
            && _function.RequiresUnsafeContract)
        {
            return false;
        }

        return node switch
        {
            ForLoop f => HasRequiredUnsafeOperation(f.Initializer)
                || HasRequiredUnsafeOperation(f.Condition)
                || HasRequiredUnsafeOperation(f.Increment),
            WhileLoop w => HasRequiredUnsafeOperation(w.Condition),
            DoWhileLoop d => HasRequiredUnsafeOperation(d.Condition),
            IfStatement s => HasRequiredUnsafeOperation(s.Condition),
            Switch s => HasRequiredUnsafeOperation(s.Value),
            Lock l => HasRequiredUnsafeOperation(l.LockObject),
            Fixed { RequiresUnsafeContext: true } => true,
            Fixed fx => HasRequiredUnsafeOperation(fx.PinSource)
                || !_newMemorySafetyRules,
            UsingStatement u => HasRequiredUnsafeOperation(u.Resource)
                || MethodsRequireUnsafe(u.ConsumedMemberRefs),
            ForeachStatement f => HasRequiredUnsafeOperation(f.Collection)
                || MethodsRequireUnsafe(f.ConsumedMemberRefs)
                || !_newMemorySafetyRules && ContainsPointer(f.LocalType),
            LocalFunctionStatement => false,
            TryCatch t => t.Clauses.Any(c => HasRequiredUnsafeOperation(c.Filter)),
            TryFinally => false,
            _ => HasRequiredUnsafeOperation(node),
        };
    }

    bool HasUnsafeOperation(IrNode? node)
        => node is not null
            && node.DescendantsAndSelfOutsideNestedFunctions.Any(IsUnsafeOperation);

    bool HasRequiredUnsafeOperation(IrNode? node)
        => node is not null
            && (HasUnsafeOperation(node)
                || !_newMemorySafetyRules
                    && node.DescendantsAndSelfOutsideNestedFunctions
                        .Any(IsLegacyPointerOperation));

    bool EmitsExplicitUnsafeContexts => _newMemorySafetyRules || _containsAwaitSyntax;

    bool NeedsExplicitUnsafeContext(IrNode node)
        => EmitsExplicitUnsafeContexts && NeedsUnsafeContext(node);

    bool NeedsUnsafeBlock(IrNode node)
        => NeedsExplicitUnsafeContext(node)
            && !CanRenderUnsafeContextAsExpressions(node);

    bool CanRenderUnsafeContextAsExpressions(IrNode node)
    {
        if (!_newMemorySafetyRules)
            return false;

        switch (node)
        {
            case Return { Value: { } value }
                when value is not StackAllocate
                    and not SwitchExpression
                    and not UnionSwitchExpression
                    and not PatternSwitchExpression
                    and not TupleSwitchExpression:
                return UnsafeRequirementsAreWithin(
                    node,
                    CurrentReturnType.Kind == TypeRefKind.ByRef,
                    value);
            case YieldReturn yieldReturn:
                return UnsafeRequirementsAreWithin(node, yieldReturn.Value);
            case Throw { Value: { } value } when value is not CaughtException:
                return UnsafeRequirementsAreWithin(node, value);
            case ScalarStore { Value: not StackAllocate } store:
                return AssignmentUnsafeExpressionRoot(store.Value, store.UpdateKind) is { } root
                    && UnsafeRequirementsAreWithin(store, store is StoreLocal { Type.Kind: TypeRefKind.ByRef }, root);
            case StoreElement store
                when !store.ReceiverTempInlined:
                return UnsafeRequirementsAreWithin(store, store.Value);
            case DeconstructionAssignment assignment:
                return UnsafeRequirementsAreWithin(assignment, assignment.Source);
            case ChainedAssignment assignment:
                return UnsafeRequirementsAreWithin(assignment, assignment.Value);
            case NullCoalescingAssignment assignment:
                return UnsafeRequirementsAreWithin(assignment, assignment.Value);
            case NullCoalescingFieldAssignment assignment:
                return UnsafeRequirementsAreWithin(assignment, assignment.Value);
            case NullCoalescingPropertyAssignment assignment:
                return UnsafeRequirementsAreWithin(assignment, assignment.Value);
            case EventSubscription subscription:
                return UnsafeRequirementsAreWithin(subscription, subscription.Value);
            case ExpressionStatement { Expression: { } expression }
                when expression is not UnsupportedNode:
                return CanDiscardUnsafeExpression(expression)
                    && UnsafeRequirementsAreWithin(node, expression);
            case ConditionalBranch branch:
                return UnsafeRequirementsAreWithin(branch, branch.Condition);
            case SwitchBranch branch:
                return UnsafeRequirementsAreWithin(branch, branch.Value);
            case ForLoop loop:
                return !NeedsUnsafeBlock(loop.Initializer)
                    && !NeedsUnsafeBlock(loop.Increment)
                    && UnsafeExpressionCompilerSupports(
                        loop.Condition,
                        acceptsDirectRequiresUnsafeMember: true);
            case WhileLoop loop:
                return UnsafeExpressionCompilerSupports(loop.Condition);
            case DoWhileLoop loop:
                return UnsafeExpressionCompilerSupports(loop.Condition);
            case IfStatement conditional:
                return UnsafeExpressionCompilerSupports(conditional.Condition);
            case Switch switchNode:
                return UnsafeExpressionCompilerSupports(
                    switchNode.Value,
                    acceptsDirectRequiresUnsafeMember: true);
            case Fixed fixedStatement:
                return !fixedStatement.RequiresUnsafeContext
                    && !fixedStatement.SourceIsAddress
                    && UnsafeExpressionCompilerSupports(fixedStatement.PinSource);
            case UsingStatement usingStatement:
                return !MethodsRequireUnsafe(usingStatement.ConsumedMemberRefs)
                    && UnsafeExpressionCompilerSupports(usingStatement.Resource);
            case ForeachStatement foreachStatement:
                return !MethodsRequireUnsafe(foreachStatement.ConsumedMemberRefs)
                    && UnsafeExpressionCompilerSupports(
                        foreachStatement.Collection,
                        acceptsDirectRequiresUnsafeMember: true);
            case Lock lockStatement:
                return UnsafeExpressionCompilerSupports(lockStatement.LockObject);
            case TryCatch tryCatch:
                return tryCatch.Clauses.All(clause =>
                    clause.Filter is not { } filter
                    || UnsafeExpressionCompilerSupports(filter));
            default:
                return false;
        }
    }

    bool UnsafeRequirementsAreWithin(IrNode node, params IrExpression[] expressions)
        => UnsafeRequirementsAreWithin(node, allowOwnerOperation: false, expressions);

    bool UnsafeRequirementsAreWithin(
        IrNode node,
        bool allowOwnerOperation,
        params IrExpression[] expressions)
    {
        bool found = false;
        foreach (var operation in node.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (!IsUnsafeOperation(operation))
                continue;
            found = true;
            if (allowOwnerOperation && ReferenceEquals(operation, node))
                continue;
            if (!expressions.Any(expression => IsDescendantOrSelf(operation, expression)))
                return false;
        }
        return found && expressions.All(expression =>
            UnsafeExpressionCompilerSupports(expression));
    }

    /// <summary>
    /// Roslyn 5.9 parses and emits unsafe expressions, but does not yet treat a
    /// direct requires-unsafe property access or method-address conversion as
    /// satisfied by the wrapper in every expression position (CS9362). A larger
    /// enclosing expression, and direct for/switch/foreach headers, do bind
    /// correctly. A pointer-targeted stackalloc still loses its target type
    /// inside the wrapper (CS8346), even when nested.
    /// </summary>
    bool UnsafeExpressionCompilerSupports(
        IrExpression expression,
        bool acceptsDirectRequiresUnsafeMember = false)
        => (acceptsDirectRequiresUnsafeMember
                || !(expression is AddressOfMethod { Method: { } method }
                    && MethodRequiresUnsafe(method))
                && !(expression is LoadProperty { Accessor: { } accessor }
                    && MethodRequiresUnsafe(accessor)))
            && !expression.DescendantsAndSelfOutsideNestedFunctions.Any(operation =>
                operation is StackAllocArray
                {
                    ResultType: { Kind: TypeRefKind.Pointer }
                });

    static IrExpression? AssignmentUnsafeExpressionRoot(IrExpression value, ScalarUpdateKind? updateKind)
    {
        if (updateKind is null)
            return value;
        var binary = (Binary)value;
        if (binary.IsChecked
            || binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight)
        {
            return null;
        }
        return binary.Right;
    }

    static bool CanDiscardUnsafeExpression(IrExpression expression)
        => expression.ResultType is { Kind: not TypeRefKind.ByRef } type
            && type is not { Namespace: "System", Name: "Void" };

    bool ShouldDiscardForUnsafeExpression(IrExpression expression)
        => _newMemorySafetyRules
            && _unsafeDepth == 0
            && IsStatementExpression(expression)
            && CanDiscardUnsafeExpression(expression)
            && HasRequiredUnsafeOperation(expression);

    bool NeedsUnsafeBodyModifier(IrNode node)
        => NeedsUnsafeContext(node)
            || !_newMemorySafetyRules && IsLegacyPointerOperation(node);

    /// <summary>
    /// A single IR operation that requires an unsafe context under the updated
    /// rules: a function-pointer invocation (<c>calli</c>), a read/write through
    /// an unmanaged pointer, pointer member access, a call to a
    /// <em>requires-unsafe</em> member (one stamped with
    /// <c>RequiresUnsafeAttribute</c> — declared <c>unsafe</c>/<c>extern</c> —
    /// or, by the compat heuristic, one with a pointer in its signature), or a
    /// <c>stackalloc</c> converted to a <c>Span</c> with no initializer in a
    /// <c>[SkipLocalsInit]</c> body. Dereferencing a managed reference
    /// (<c>ByRef</c>) is safe and excluded. Converting an unbox reference to a
    /// native integer is included because its faithful spelling uses
    /// <c>Unsafe.AsPointer</c>. Creating pointers, the
    /// String-pin fixed statements raised through a synthesized stack-slot
    /// pointer need an unsafe context for their header. Creating pointers,
    /// ordinary <c>fixed</c> statements, and <c>sizeof</c> are safe under the new
    /// rules.
    /// </summary>
    bool IsUnsafeOperation(IrNode node)
    {
        if (OperationMemorySafetyContract.RequiresUnsafe(
                node,
                _newMemorySafetyRules,
                _skipLocalsInit,
                _consumedMembers,
                RefBindingTargetType))
            return true;

        return false;
    }

    TypeRef? RefBindingTargetType(IrNode node)
        => node switch
        {
            StoreLocal store => store.Type,
            Return => CurrentReturnType,
            _ => null,
        };

    static bool IsPointerReceiver(IrExpression? receiver)
        => OperationMemorySafetyContract.IsPointerReceiver(receiver);

    bool AccessorRequiresUnsafe(MethodRef accessor, IrExpression? receiver)
        => MethodRequiresUnsafe(accessor)
            || IsPointerReceiver(receiver);

    bool MethodRequiresUnsafe(MethodRef? method)
        => method is not null
            && OperationMemorySafetyContract.MethodRequiresUnsafe(
                method,
                _newMemorySafetyRules);

    bool MethodsRequireUnsafe(IEnumerable<MethodRef?> methods)
        => methods.Any(MethodRequiresUnsafe);

    static bool IsLegacyPointerOperation(IrNode node)
        => UnsafeAwaitOperand.IsLegacyPointerOperation(node);

    static bool ContainsPointer(TypeRef? type)
        => OperationMemorySafetyContract.ContainsPointer(type);

    /// <summary>
    /// Whether <see cref="Deref"/> renders this load/store-indirect address with
    /// a leading <c>*</c> — i.e. it is a read/write through an unmanaged pointer
    /// rather than a managed reference. Mirrors the managed-reference cases of
    /// <see cref="Deref"/> exactly: anything not spelled as a place or a
    /// <c>ByRef</c> is a pointer dereference, which requires an unsafe context.
    /// </summary>
    static bool RendersAsPointerDeref(IrExpression address)
        => OperationMemorySafetyContract.RendersAsPointerDereference(address);
}
