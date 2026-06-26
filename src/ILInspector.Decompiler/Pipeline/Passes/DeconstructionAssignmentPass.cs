using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the narrow tuple deconstruction lowering:
/// <code>
/// S = tuple;
/// T1 a = S.Item1;
/// target = S.Item2;
/// </code>
/// into <c>(T1 a, T2 b) = tuple;</c> when the targets are fresh locals declared
/// here, <c>(a, b) = tuple;</c> when they assign into pre-existing locals, or a
/// mix such as <c>(T1 a, b) = tuple;</c> — each local target is classified
/// independently as a declaration or an assignment. Scoped to direct
/// <c>System.ValueTuple</c> fields, arities 2-7, with local targets, by-value
/// parameter targets, static/<c>this</c>-field targets, and simple property
/// targets over side-effect-free receivers. Indexers, non-this field receivers,
/// nested/rest tuples, and user-defined <c>Deconstruct</c> calls with non-local
/// targets are later slices.
/// </summary>
public sealed class DeconstructionAssignmentPass : IIrPass
{
    public string Name => "deconstruction-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (TryRaiseValueTuple(function, block, i, context)
                    || TryRaiseDeconstructMethod(function, children[i], context))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The receiver-spill + <c>ItemN</c> store form: a <c>ValueTuple</c> value
    /// stored to a stack slot, then read field-by-field into the targets.
    /// </summary>
    static bool TryRaiseValueTuple(IrFunction function, Block block, int i, PassContext context)
    {
        var children = block.Children;
        if (TryMatchTupleSeed(function, children[i]) is not { } seed
            || seed.Value.ResultType is not { } tupleType
            || !MemberIdentity.IsSupportedValueTupleType(tupleType, out var arity)
            || i + arity >= children.Count)
        {
            return false;
        }

        var targets = new List<TargetMatch>(arity);
        for (int j = 0; j < arity; j++)
        {
            if (TryMatchTarget(function, children[i + 1 + j], seed, j + 1) is not { } target)
            {
                targets.Clear();
                break;
            }
            targets.Add(target);
        }

        if (targets.Count != arity
            || !ReferencedOnlyWithin(function, seed, [seed.Statement, .. targets.Select(target => target.Statement)]))
        {
            return false;
        }

        if (ClassifyTargets(function, seed, targets) is not { } resolved)
            return false;

        var source = (IrExpression)seed.Statement.DetachChildren()[0];
        var deconstruction = new DeconstructionAssignment([.. resolved], source);
        context.Stepper.StepOver("raise ValueTuple field stores to deconstruction", seed.Statement);
        seed.Statement.ReplaceWith(deconstruction);
        foreach (var target in targets)
            target.DetachConsumedStatement();
        return true;
    }

    /// <summary>
    /// The <c>Deconstruct</c>-method form: a single void <c>r.Deconstruct(out a,
    /// out b, ...)</c> call statement, the lowering of <c>(a, b) = r;</c> when
    /// <c>r</c>'s type supplies a <c>Deconstruct</c> method. Scoped to a
    /// side-effect-free local/parameter receiver (the only shape in the corpus —
    /// foreach <c>Current</c> and locals); the out-temp + copy form and other
    /// receivers are later slices. Requires known ref-kind metadata proving every
    /// non-receiver parameter is <c>out</c>; <c>ref</c>/<c>in</c>/unknown calls keep
    /// their explicit call spelling.
    /// </summary>
    static bool TryRaiseDeconstructMethod(IrFunction function, IrNode statement, PassContext context)
    {
        var call = statement as Call ?? (statement as ExpressionStatement)?.Expression as Call;
        if (call is null
            || call.Callee is not { Name: "Deconstruct", HasThis: true, ReturnType: { Namespace: "System", Name: "Void" } }
            || call.Arguments.Count is < 3 or > 8)
        {
            return false;
        }

        int arity = call.Arguments.Count - 1;
        if (!HasKnownOutTargets(call.Callee, arity))
            return false;

        var receiver = call.Arguments[0];
        if (ReceiverValue(receiver) is not { } source)
            return false;

        var outArgs = call.Arguments.Skip(1).ToList();
        if (outArgs.Any(arg => arg is not LoadLocalAddress))
            return false;

        var targets = outArgs.Cast<LoadLocalAddress>()
            .Select(arg => (arg.Index, arg.Type, (IrNode)arg));
        // A target that aliases the receiver local would re-read the value it is
        // overwriting; `(a, b) = a` keeps the de-sugared call instead.
        if (receiver is LoadLocalAddress receiverLocal
            && outArgs.Cast<LoadLocalAddress>().Any(arg => arg.Index == receiverLocal.Index))
        {
            return false;
        }

        if (ClassifyTargets(function, targets) is not { } resolved)
            return false;

        var deconstruction = new DeconstructionAssignment(resolved.indices, resolved.types, source, resolved.isDeclared);
        context.Stepper.StepOver("raise Deconstruct-method call to deconstruction", statement);
        statement.ReplaceWith(deconstruction);
        return true;
    }

    static bool HasKnownOutTargets(MethodRef deconstruct, int arity)
    {
        if (deconstruct.ParameterRefKindsFacts != ParameterRefKindFacts.Known
            || deconstruct.ParameterRefKinds.Length != arity
            || deconstruct.ParameterTypes.Length != arity)
        {
            return false;
        }

        for (int i = 0; i < arity; i++)
        {
            if (deconstruct.ParameterTypes[i].Kind != TypeRefKind.ByRef
                || deconstruct.ParameterRefKinds[i] != ArgumentRefKind.Out)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The value form of a side-effect-free <c>Deconstruct</c> receiver, or null when the receiver is unsupported.</summary>
    static IrExpression? ReceiverValue(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress address => new LoadLocal(address.Index, address.Type),
        LoadArgumentAddress address => new LoadArgument(address.Index, address.Name, address.Type),
        LoadLocal local => new LoadLocal(local.Index, local.Type),
        LoadArgument argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
        _ => null,
    };

    sealed record TupleSeed(IrNode Statement, IrExpression Value, int Index, bool IsLocal)
    {
        public bool Matches(IrExpression expression) => IsLocal
            ? expression is LoadLocal local && local.Index == Index
            : expression is LoadStackSlot slot && slot.Slot == Index;

        public bool IsReference(IrNode node) => IsLocal
            ? node switch
            {
                LoadLocal local => local.Index == Index,
                StoreLocal local => local.Index == Index,
                LoadLocalAddress address => address.Index == Index,
                _ => false,
            }
            : node switch
            {
                LoadStackSlot slot => slot.Slot == Index,
                StoreStackSlot slot => slot.Slot == Index,
                _ => false,
            };
    }

    sealed record TargetMatch(IrNode Statement, Func<DeconstructionTarget> BuildTarget)
    {
        public void DetachConsumedStatement() => Statement.Detach();
    }

    static TupleSeed? TryMatchTupleSeed(IrFunction function, IrNode node) => node switch
    {
        StoreStackSlot stack => new TupleSeed(stack, stack.Value, stack.Slot, IsLocal: false),
        StoreLocal local when IsHiddenLocal(function, local.Index) => new TupleSeed(local, local.Value, local.Index, IsLocal: true),
        _ => null,
    };

    static TargetMatch? TryMatchTarget(IrFunction function, IrNode statement, TupleSeed seed, int ordinal)
    {
        if (statement is StoreLocal
            {
                Value: LoadField
                {
                    Field.Name: var localFieldName,
                    Instance: var localInstance,
                },
            } local
            && localInstance is not null
            && seed.Matches(localInstance)
            && localFieldName == $"Item{ordinal}")
        {
            return new TargetMatch(local, () => DeconstructionTarget.Local(
                local.Index,
                local.Type,
                IsFirstReference(function, local, local.Index)));
        }

        if (statement is StoreProperty
            {
                Value: LoadField
                {
                    Field.Name: var propertyFieldName,
                    Instance: var propertyInstance,
                },
            } property
            && propertyInstance is not null
            && seed.Matches(propertyInstance)
            && propertyFieldName == $"Item{ordinal}"
            && IsSimplePropertyTarget(property, seed))
        {
            return new TargetMatch(property, () => BuildPropertyTarget(property));
        }

        if (statement is StoreArgument
            {
                Value: LoadField
                {
                    Field.Name: var argumentFieldName,
                    Instance: var argumentInstance,
                },
            } argument
            && argumentInstance is not null
            && seed.Matches(argumentInstance)
            && argumentFieldName == $"Item{ordinal}")
        {
            return new TargetMatch(argument, () => DeconstructionTarget.Argument(argument.Index, argument.Name, argument.Type));
        }

        if (statement is StoreField
            {
                Value: LoadField
                {
                    Field.Name: var fieldName,
                    Instance: var fieldInstance,
                },
            } field
            && fieldInstance is not null
            && seed.Matches(fieldInstance)
            && fieldName == $"Item{ordinal}"
            && IsSupportedFieldTarget(function, field))
        {
            return new TargetMatch(field, () => DeconstructionTarget.FieldTarget(field.Field, field.HasInstance));
        }

        return null;
    }

    static DeconstructionTarget BuildPropertyTarget(StoreProperty property)
    {
        int indexArgumentCount = property.Accessor.ParameterTypes.Length - 1;
        var children = property.DetachChildren();
        var instance = property.HasInstance ? (IrExpression?)children[0] : null;
        var indexArguments = children
            .Skip(property.HasInstance ? 1 : 0)
            .Take(indexArgumentCount)
            .Cast<IrExpression>()
            .ToArray();
        return DeconstructionTarget.Property(property.Accessor, instance, indexArguments, property.IsVirtual);
    }

    static bool IsSimplePropertyTarget(StoreProperty property, TupleSeed seed)
    {
        if (!IsSpellablePropertySetterTarget(property))
            return false;

        return property.Instance switch
        {
            null => true,
            LoadArgument => true,
            LoadLocal local => !(seed.IsLocal && local.Index == seed.Index),
            _ => false,
        };
    }

    static bool IsSpellablePropertySetterTarget(StoreProperty property)
        => property.Accessor.Name.StartsWith("set_", StringComparison.Ordinal)
            && property.Accessor.TypeArguments.IsEmpty
            && property.Accessor.ReturnType is { Namespace: "System", Name: "Void" }
            && property.Accessor.ParameterTypes.Length == 1
            && property.IndexArguments.Count == 0
            && property.Accessor.HasThis == property.HasInstance;

    static bool IsSupportedFieldTarget(IrFunction function, StoreField field)
        => !field.HasInstance
            || (function.Signature.HasThis && field.Instance is LoadArgument { Index: 0 });

    /// <summary>
    /// Resolves the deconstruction targets, classifying each independently as a
    /// fresh local introduced here (a declaration, its first reference is this
    /// store) or a pre-existing local (an assignment). A run may mix the two —
    /// <c>(int x, y) = …</c> — so the per-target flags are returned parallel to the
    /// indices. Distinct targets are required since <c>(a, a) = …</c> is not a
    /// valid deconstruction; the all-fresh form is inherently distinct. Returns
    /// null to decline.
    /// </summary>
    static (ImmutableArray<int> indices, ImmutableArray<TypeRef> types, ImmutableArray<bool> isDeclared)? ClassifyTargets(
        IrFunction function,
        IEnumerable<(int index, TypeRef type, IrNode reference)> targets)
    {
        var resolved = targets.ToList();
        if (resolved.Select(target => target.index).Distinct().Count() != resolved.Count)
            return null;

        var isDeclared = resolved.Select(target => IsFirstReference(function, target.reference, target.index)).ToImmutableArray();
        return ([.. resolved.Select(target => target.index)], [.. resolved.Select(target => target.type)], isDeclared);
    }

    static ImmutableArray<DeconstructionTarget>? ClassifyTargets(
        IrFunction function,
        TupleSeed seed,
        IReadOnlyList<TargetMatch> targets)
    {
        var localIndices = new HashSet<int>();
        foreach (var local in targets.Select(target => target.Statement).OfType<StoreLocal>())
        {
            if (seed.IsLocal && local.Index == seed.Index)
                return null;
            if (!localIndices.Add(local.Index))
                return null;
        }

        foreach (var property in targets.Select(target => target.Statement).OfType<StoreProperty>())
            if (property.Instance is LoadLocal receiver && localIndices.Contains(receiver.Index))
                return null;

        var builder = ImmutableArray.CreateBuilder<DeconstructionTarget>(targets.Count);
        foreach (var target in targets)
            builder.Add(target.BuildTarget());
        var resolved = builder.MoveToImmutable();
        return DistinctTargets(resolved) ? resolved : null;
    }

    /// <summary>True when every target names a distinct assignment place.</summary>
    static bool DistinctTargets(ImmutableArray<DeconstructionTarget> targets)
    {
        var seen = new HashSet<(int Kind, int Index, string? Name)>();
        foreach (var target in targets)
        {
            var key = target.Kind switch
            {
                DeconstructionTargetKind.Local => (0, target.LocalIndex, (string?)null),
                DeconstructionTargetKind.Argument => (1, target.ArgumentIndex, (string?)null),
                DeconstructionTargetKind.Field => (target.IsThisInstance ? 2 : 3, 0, $"{target.Field!.DeclaringType.ToDisplayString()}.{target.Field.Name}"),
                DeconstructionTargetKind.Property => (4, 0, $"{target.Accessor!.DeclaringType.ToDisplayString()}.{target.PropertyName}"),
                _ => (5, 0, (string?)null),
            };
            if (!seen.Add(key))
                return false;
        }
        return true;
    }

    static bool ReferencedOnlyWithin(IrFunction function, TupleSeed seed, IReadOnlyList<IrNode> allowed)
    {
        foreach (var reference in function.Descendants.Where(seed.IsReference))
            if (!allowed.Any(allowedNode => ReferenceOwnership.IsInside(reference, allowedNode)))
                return false;
        return true;
    }

    static bool IsHiddenLocal(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && function.LocalNames[index] is null;

    static bool IsFirstReference(IrFunction function, IrNode target, int index)
    {
        foreach (var node in function.Descendants)
        {
            if (ReferenceEquals(node, target))
                return true;
            if (node is LoadLocal load && load.Index == index
                || node is StoreLocal store && store.Index == index
                || node is LoadLocalAddress address && address.Index == index)
            {
                return false;
            }
        }
        return false;
    }
}
