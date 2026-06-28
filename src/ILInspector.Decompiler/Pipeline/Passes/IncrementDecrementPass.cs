namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds the compiler's <c>dup</c>-based increment/decrement idiom back into the
/// <c>++x</c>/<c>x++</c>/<c>--x</c>/<c>x--</c> operator the source spelled.
///
/// A pre/post increment or decrement used as a value — <c>a[--i] = src[j++];</c>,
/// <c>return list[index++];</c> — lowers to a <c>dup</c>: the updated (or
/// pre-update) value is duplicated, one copy stored to the local and the other
/// consumed in place. The importer raises the <c>dup</c> into a single-use stack
/// slot, leaving a three-statement shape:
///
/// <code>
/// S = x;      x = S + 1;   ... use S ...   ≡   ... x++ ...   (post-increment)
/// S = x - 1;  x = S;       ... use S ...   ≡   ... --x ...   (pre-decrement)
/// </code>
///
/// The slot capture and the local update spill the value into two extra locals
/// on recompile (no <c>dup</c>); folding them into the operator at the use site
/// restores the <c>dup</c> exactly. The fold only fires when the captured value
/// is read once downstream and the local is untouched between its update and
/// that read, so moving the side effect to the use site reorders nothing.
/// </summary>
public sealed class IncrementDecrementPass : IIrPass
{
    public string Name => "increment-decrement";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOnce(function, context.Stepper))
        {
        }

        FoldForLoopIncrements(function, context.Stepper);
        FoldUserOperatorSelfUpdates(function, context.Stepper);
        FoldUserOperatorValueForms(function, context.Stepper);
        MarkSurvivingUserIncrements(function, context.Stepper);
    }

    // Recover a value-form user-defined ++/-- on a NON-LOCAL lvalue (return
    // obj.Field++, return ++SF) — the dup fold (TryFold) only handles local/argument
    // updates. The IL captures the lvalue's old (postfix) or new (prefix) value in a
    // temp, updates the field/indirect place through the operator, and uses the temp
    // downstream:
    //   V = lvalue; lvalue = op_Increment(V); use V    →  use lvalue++
    //   V = op_Increment(lvalue); lvalue = V; use V     →  use ++lvalue
    // See #1777.
    static void FoldUserOperatorValueForms(IrFunction function, Stepper stepper)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var block in function.Descendants.OfType<Block>().ToList())
            {
                for (int i = 0; i + 1 < block.Children.Count; i++)
                {
                    if (TryFoldValueForm(function, block, i, stepper))
                    {
                        changed = true;
                        break;
                    }
                }
                if (changed)
                    break;
            }
        }
    }

    static bool TryFoldValueForm(IrFunction function, Block block, int i, Stepper stepper)
    {
        var capture = block.Children[i];
        var update = block.Children[i + 1];

        // The captured temp is a local or a stack slot, written by the capture.
        Func<IrExpression, bool>? isTempLoad = capture switch
        {
            StoreLocal s => e => e is LoadLocal l && l.Index == s.Index,
            StoreStackSlot s => e => e is LoadStackSlot l && l.Slot == s.Slot,
            _ => null,
        };
        IrExpression? captureValue = capture switch
        {
            StoreLocal s => s.Value,
            StoreStackSlot s => s.Value,
            _ => null,
        };
        if (isTempLoad is null || captureValue is null)
            return false;

        // The update must write a NON-LOCAL lvalue (field/indirect); the local and
        // argument value forms are handled by the dup fold.
        IrExpression? updateValue = update switch
        {
            StoreField s => s.Value,
            StoreIndirect s => s.Value,
            _ => null,
        };
        if (updateValue is null)
            return false;

        bool isPrefix;
        IrExpression lvalueLoad;
        bool isIncrement;
        bool isChecked;

        if (AsIncrementCall(updateValue) is { } postOp && isTempLoad(postOp.Operand) && WritesSamePlaceAsRead(update, captureValue))
        {
            // V = lvalue; lvalue = op_Increment(V);  →  lvalue++
            isPrefix = false;
            lvalueLoad = captureValue;
            isIncrement = postOp.IsIncrement;
            isChecked = postOp.IsChecked;
        }
        else if (AsIncrementCall(captureValue) is { } preOp && isTempLoad(updateValue) && WritesSamePlaceAsRead(update, preOp.Operand))
        {
            // V = op_Increment(lvalue); lvalue = V;  →  ++lvalue
            isPrefix = true;
            lvalueLoad = preOp.Operand;
            isIncrement = preOp.IsIncrement;
            isChecked = preOp.IsChecked;
        }
        else
        {
            return false;
        }

        // The temp must be written once and read exactly twice — the update above
        // plus one downstream consumer — so removing the capture is sound. For a
        // local temp, an address-of (ref/out) is an observation the load count
        // misses, so reject it outright.
        if (capture is StoreLocal { Index: var captureIndex }
            && function.Descendants.OfType<LoadLocalAddress>().Any(a => a.Index == captureIndex))
        {
            return false;
        }
        var tempLoads = function.Descendants.OfType<IrExpression>().Where(isTempLoad).ToList();
        if (tempLoads.Count != 2)
            return false;
        if (CountTempStores(function, capture) != 1)
            return false;
        if (tempLoads.Count(l => ReferenceOwnership.IsInside(l, update)) != 1)
            return false;
        if (tempLoads.FirstOrDefault(l => !ReferenceOwnership.IsInside(l, update)) is not { } useLoad)
            return false;

        // The consumer must be the statement immediately after the update, so the
        // lvalue is neither read nor written between its update and the use site.
        if (StatementOf(useLoad, block) is not { } useStatement || useStatement.ChildIndex != i + 2)
            return false;

        // The consumer must not access the lvalue itself other than through the
        // folded temp: `S = SF; SF = op_Increment(S); return SF + S;` would fold to
        // `return SF + SF++`, moving the SF read across the update and changing the
        // result. Such a shape stays an honest residual.
        if (AccessesPlaceOf(useStatement, lvalueLoad))
            return false;

        lvalueLoad.Detach();
        var increment = new IncrementDecrement(lvalueLoad, isIncrement, isPrefix, isUserDefined: true, isChecked: isChecked);
        stepper.StepOver($"fold user-defined {(isIncrement ? "++" : "--")} value form into operator", useLoad);
        useLoad.ReplaceWith(increment);
        update.Detach();
        capture.Detach();
        return true;
    }

    /// <summary>True when <paramref name="node"/> or any descendant reads, writes, or takes the address of the same field/indirect place as <paramref name="lvalueLoad"/>.</summary>
    static bool AccessesPlaceOf(IrNode node, IrExpression lvalueLoad)
    {
        foreach (var current in (IEnumerable<IrNode>)[node, .. node.Descendants])
        {
            bool hit = (current, lvalueLoad) switch
            {
                (LoadField n, LoadField lv) => SameField(n.Field, lv.Field) && SamePlaceExpr(n.Instance, lv.Instance),
                (StoreField n, LoadField lv) => SameField(n.Field, lv.Field) && SamePlaceExpr(n.Instance, lv.Instance),
                (LoadFieldAddress n, LoadField lv) => SameField(n.Field, lv.Field) && SamePlaceExpr(n.Instance, lv.Instance),
                (LoadIndirect n, LoadIndirect lv) => SamePlaceExpr(n.Address, lv.Address),
                (StoreIndirect n, LoadIndirect lv) => SamePlaceExpr(n.Address, lv.Address),
                _ => false,
            };
            if (hit)
                return true;
        }
        return false;
    }

    static int CountTempStores(IrFunction function, IrNode capture) => capture switch
    {
        StoreLocal s => function.Descendants.OfType<StoreLocal>().Count(x => x.Index == s.Index),
        StoreStackSlot s => function.Descendants.OfType<StoreStackSlot>().Count(x => x.Slot == s.Slot),
        _ => 0,
    };

    // A user-defined increment/decrement operator call that survived all folds
    // sits in a position with no faithful C# spelling — e.g. a value-form
    // increment of a non-local lvalue (`return field++`), whose dup pattern stores
    // through a field rather than a local. The bare op_Increment(x) call is CS0571,
    // so mark its enclosing statement an honest residual rather than leak invalid
    // Full. The common self-update and local/argument value forms have already
    // folded to ++/--. See #1712.
    static void MarkSurvivingUserIncrements(IrFunction function, Stepper stepper)
    {
        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (call.Parent is null || AsIncrementCall(call) is not { } op)
                continue;
            if (EnclosingBlockStatement(call) is not { Parent: not null } statement)
                continue;
            string symbol = op.IsIncrement ? "++" : "--";
            var marker = new UnsupportedNode(
                statement.SourceOffset >= 0 ? statement.SourceOffset : 0,
                symbol,
                $"user-defined {symbol} in this position is not representable as a C# statement");
            marker.InheritSourceOffset(statement);
            var replacement = new ExpressionStatement(marker);
            replacement.InheritSourceOffset(statement);
            stepper.StepOver($"mark unfoldable user-defined {symbol} unsupported", statement);
            statement.ReplaceWith(replacement);
        }
    }

    /// <summary>The ancestor of <paramref name="node"/> that is a direct child of a <see cref="Block"/> — the statement to degrade.</summary>
    static IrNode? EnclosingBlockStatement(IrNode node)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
            if (current.Parent is Block)
                return current;
        return null;
    }

    // Fold a user-defined self-update statement — lvalue = op_Increment(lvalue) —
    // to lvalue++/--. The dup folds above handle the value forms (where the result
    // is used); this handles the discarded forms (statements, for-loop updates,
    // field/indirect lvalues). Unlike the primitive x = x + 1 form (valid C# as a
    // statement), the operator-call spelling is CS0571, so it MUST fold. See #1712.
    static void FoldUserOperatorSelfUpdates(IrFunction function, Stepper stepper)
    {
        foreach (var store in function.Descendants.ToList())
        {
            if (store.Parent is null || SelfIncrement(store) is not { } fold)
                continue;
            fold.Target.Detach();
            var increment = new IncrementDecrement(fold.Target, fold.IsIncrement, isPrefix: false, isUserDefined: true, isChecked: fold.IsChecked);
            stepper.StepOver($"fold user-defined {(fold.IsIncrement ? "++" : "--")} self-update into operator", store);
            store.ReplaceWith(new ExpressionStatement(increment));
        }
    }

    /// <summary>A <c>lvalue = op_Increment(lvalue)</c> store (any place whose re-read is side-effect-free), or null. The target expression is the operand load, reused as the <c>++</c> target.</summary>
    static (IrExpression Target, bool IsIncrement, bool IsChecked)? SelfIncrement(IrNode store)
    {
        IrExpression? value = store switch
        {
            StoreLocal s => s.Value,
            StoreArgument s => s.Value,
            StoreStackSlot s => s.Value,
            StoreField s => s.Value,
            StoreIndirect s => s.Value,
            _ => null,
        };
        return value is not null && AsIncrementCall(value) is { } op && WritesSamePlaceAsRead(store, op.Operand)
            ? (op.Operand, op.IsIncrement, op.IsChecked)
            : null;
    }

    /// <summary>True when <paramref name="store"/> writes exactly the place <paramref name="load"/> reads (a self-update), comparing only side-effect-free places.</summary>
    static bool WritesSamePlaceAsRead(IrNode store, IrExpression load) => (store, load) switch
    {
        (StoreLocal s, LoadLocal l) => s.Index == l.Index,
        (StoreArgument s, LoadArgument l) => s.Index == l.Index,
        (StoreStackSlot s, LoadStackSlot l) => s.Slot == l.Slot,
        (StoreField s, LoadField l) => SameField(s.Field, l.Field) && SamePlaceExpr(s.Instance, l.Instance),
        (StoreIndirect s, LoadIndirect l) => SamePlaceExpr(s.Address, l.Address),
        _ => false,
    };

    static bool SameField(FieldRef a, FieldRef b) => a.Name == b.Name && Equals(a.DeclaringType, b.DeclaringType);

    /// <summary>Side-effect-free structural equality for the self-update receiver/address, composing the place-identity atoms with field-rooted recursion. Two null receivers are the same (a static field).</summary>
    static bool SamePlaceExpr(IrExpression? a, IrExpression? b)
        => (a, b) is (null, null)
            || PlaceIdentity.SameOperand(a, b)
            || PlaceIdentity.SameStackSlot(a, b)
            || (a, b) is (LoadField x, LoadField y) && SameField(x.Field, y.Field) && SamePlaceExpr(x.Instance, y.Instance);

    static bool FoldOnce(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>())
        {
            for (int i = 0; i + 1 < block.Children.Count; i++)
            {
                if (TryFold(function, block, i, stepper))
                    return true;
                if (TryFoldAddressCompound(function, block, i, stepper))
                    return true;
                if (TryFoldStatementIncrement(function, block, i, stepper))
                    return true;
            }
        }
        return false;
    }

    readonly record struct PlaceRef(bool IsLocal, int Index, string Name, TypeRef Type);

    static bool TryFold(IrFunction function, Block block, int i, Stepper stepper)
    {
        if (block.Children[i] is not StoreStackSlot slotStore)
            return false;
        var update = block.Children[i + 1];
        if (PlaceOf(update) is not { } place || StoreValue(update) is not { } updateValue)
            return false;

        int slot = slotStore.Slot;
        bool isIncrement;
        bool isPrefix;
        bool isUserDefined = false;
        bool isChecked = false;

        if (IsPlaceLoad(slotStore.Value, place)
            && updateValue is Binary { IsChecked: false, Kind: var postKind } post
            && post.Left is LoadStackSlot postLoad && postLoad.Slot == slot
            && post.Right is Constant { Value: 1 }
            && postKind is BinaryKind.Add or BinaryKind.Subtract)
        {
            // S = x; x = S ± 1;  →  x++ / x--
            isPrefix = false;
            isIncrement = postKind is BinaryKind.Add;
        }
        else if (slotStore.Value is Binary { IsChecked: false, Kind: var preKind } pre
            && IsPlaceLoad(pre.Left, place)
            && pre.Right is Constant { Value: 1 }
            && preKind is BinaryKind.Add or BinaryKind.Subtract
            && updateValue is LoadStackSlot preLoad && preLoad.Slot == slot)
        {
            // S = x ± 1; x = S;  →  ++x / --x
            isPrefix = true;
            isIncrement = preKind is BinaryKind.Add;
        }
        else if (IsPlaceLoad(slotStore.Value, place)
            && AsIncrementCall(updateValue) is { Operand: LoadStackSlot postOpLoad } postOp
            && postOpLoad.Slot == slot)
        {
            // S = x; x = op_Increment(S);  →  x++ / x-- (user-defined operator)
            isPrefix = false;
            isIncrement = postOp.IsIncrement;
            isUserDefined = true;
            isChecked = postOp.IsChecked;
        }
        else if (AsIncrementCall(slotStore.Value) is { } preOp
            && IsPlaceLoad(preOp.Operand, place)
            && updateValue is LoadStackSlot preOpLoad && preOpLoad.Slot == slot)
        {
            // S = op_Increment(x); x = S;  →  ++x / --x (user-defined operator)
            isPrefix = true;
            isIncrement = preOp.IsIncrement;
            isUserDefined = true;
            isChecked = preOp.IsChecked;
        }
        else
        {
            return false;
        }
        // The dup slot must be written once and read exactly twice: the local
        // update above, plus one downstream consumer.
        var loads = function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == slot).ToList();
        if (function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == slot) != 1)
            return false;
        if (loads.Count != 2 || loads.Count(l => ReferenceOwnership.IsInside(l, update)) != 1)
            return false;
        if (loads.FirstOrDefault(l => !ReferenceOwnership.IsInside(l, update)) is not { } useLoad)
            return false;

        // The consumer must be a later statement in this same block.
        if (StatementOf(useLoad, block) is not { } useStatement)
            return false;
        int useIndex = useStatement.ChildIndex;
        if (useIndex <= i + 1)
            return false;

        // Moving the ±1 to the use site is sound only if the place is neither
        // read nor written between its update and that use — otherwise an
        // intervening access would observe the pre-fold (already updated) value.
        for (int k = i + 2; k <= useIndex; k++)
        {
            if (ReferencesPlace(block.Children[k], place))
                return false;
        }
        // A user-defined operator place is incrementable by definition (we matched
        // its op_Increment/op_Decrement call); the primitive shape guard only
        // applies to the Binary ± 1 form.
        if (!isUserDefined && !IsIncrementable(place.Type, function))
        {
            MarkUnsupportedIncrementExpression(isPrefix ? slotStore.Value : updateValue, place, isIncrement ? BinaryKind.Add : BinaryKind.Subtract, stepper);
            return true;
        }

        stepper.StepOver($"fold dup {(isIncrement ? "++" : "--")} idiom into operator", useLoad);
        useLoad.ReplaceWith(new IncrementDecrement(ClonePlace(place), isIncrement, isPrefix, isUserDefined, isChecked));
        update.Detach();
        slotStore.Detach();
        return true;
    }

    /// <summary>
    /// Folds the address-captured compound-assignment idiom — <c>a[i] += v</c>,
    /// <c>a[i] &lt;&lt;= n</c>, <c>a[i]++</c> as a statement — back into the operator.
    ///
    /// A compound assignment to an lvalue whose address is non-trivial (an array
    /// element, a struct field) evaluates that address once and stores back
    /// through it via a <c>dup</c>: the importer raises the dup into a single-use
    /// stack slot, leaving
    ///
    /// <code>
    /// S = &amp;a[i];   *S = (*S) op v;
    /// </code>
    ///
    /// Inlining the captured address into both the read and the write turns this
    /// into a plain self-reading <c>StoreIndirect</c>, which the printer spells as
    /// <c>a[i] op= v</c>. The fold is opcode-exact: the captured address proves the
    /// receiver was evaluated once (the expanded <c>a[i] = a[i] + v</c> has no
    /// address slot and a structurally different tree), so collapsing the two
    /// reads of the slot back onto one address reorders nothing. A checked binary
    /// is left alone — the printer would not fold it, and spelling two reads of the
    /// element would re-evaluate the index. A ref-returning property/indexer getter
    /// (<c>LoadProperty</c>) is also left alone: the printer's
    /// <c>SameLValue</c> place-equality does not cover it, so the fold would leak as
    /// <c>target = (target) op v</c> and re-evaluate the getter (see the guard
    /// below).
    /// </summary>
    static bool TryFoldAddressCompound(IrFunction function, Block block, int i, Stepper stepper)
    {
        if (block.Children[i] is not StoreStackSlot slotStore)
            return false;
        if (slotStore.Value.ResultType is not { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer })
            return false;
        // The fold clones the captured byref address into both the read and the
        // store of a self-reading StoreIndirect, trusting the printer to spell it
        // back as `target op= v`. That collapse only happens when the printer's
        // CSharpPrinter.SameLValue recognizes the address as a side-effect-free
        // place (LoadArgument/LoadLocal/LoadField{,Address}/LoadElementAddress).
        // Anything outside that set (a ref-returning property/indexer getter, or
        // an array element whose index is a call) would leak as `target = target +
        // v`, re-evaluating the unrecognized receiver/index. Decline the fold
        // here and let the slot render as a `ref` local, which keeps a single
        // evaluation (opcode-exact).
        if (!IsPrinterFoldableClonedAddress(slotStore.Value))
            return false;
        if (block.Children[i + 1] is not StoreIndirect store)
            return false;
        if (store.Address is not LoadStackSlot storeSlot || storeSlot.Slot != slotStore.Slot)
            return false;
        if (store.Value is not Binary { IsChecked: false } binary)
            return false;
        if (binary.Left is not LoadIndirect { Address: LoadStackSlot readSlot } || readSlot.Slot != slotStore.Slot)
            return false;

        // The captured address slot must be written once and read exactly twice —
        // the store-back target and the read inside the operator — with nothing
        // else, including the right operand, touching it.
        int slot = slotStore.Slot;
        if (function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == slot) != 1)
            return false;
        var loads = function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == slot).ToList();
        if (loads.Count != 2 || !loads.All(l => ReferenceOwnership.IsInside(l, store)))
            return false;

        stepper.StepOver("inline captured address into compound assignment", store);
        var address = slotStore.Value;
        address.Detach();
        var readAddress = address.Clone();
        store.SetChild(0, address);
        ((LoadIndirect)binary.Left).SetChild(0, readAddress);
        slotStore.Detach();
        return true;
    }

    static bool IsPrinterFoldableClonedAddress(IrExpression? expression) => expression switch
    {
        null => true,
        LoadArgument or LoadLocal or Constant => true,
        LoadField field => IsPrinterFoldableClonedAddress(field.Instance),
        LoadFieldAddress field => IsPrinterFoldableClonedAddress(field.Instance),
        LoadElementAddress element => IsPrinterFoldableClonedAddress(element.Array)
            && IsPrinterFoldableClonedAddress(element.Index),
        _ => false,
    };

    /// <summary>
    /// Folds the dead-temp post-increment statement form — a value-less
    /// <c>x++</c>/<c>x--</c> that a spill left behind as
    ///
    /// <code>
    /// V = x;   x = V ± 1;        ≡   x++ / x--   (V never read again)
    /// </code>
    ///
    /// — back into the operator statement. The temporary captures the old value
    /// and is immediately dead, so the pair is exactly a discarded post-increment.
    /// This shape does not arise from csc directly (a statement <c>x++</c> lowers
    /// to <c>ldloc; ldc.i4.1; add; stloc</c> with no temp); it surfaces from the
    /// decompiler's own iterator/state-machine reconstruction, where the hoisted
    /// loop variable's increment is rebuilt through a spill slot. The fold only
    /// fires when the temporary is written once and read exactly once (the
    /// update), so removing it reorders nothing.
    /// </summary>
    static bool TryFoldStatementIncrement(IrFunction function, Block block, int i, Stepper stepper)
    {
        if (block.Children[i] is not StoreLocal { } tempStore)
            return false;
        if (PlaceLoad(tempStore.Value) is not { } readPlace)
            return false;
        if (PlaceOf(block.Children[i + 1]) is not { } writePlace || StoreValue(block.Children[i + 1]) is not { } updateValue)
            return false;

        // The captured place and the updated place must be the same local or
        // argument, and must not be the temporary itself.
        if (readPlace.IsLocal != writePlace.IsLocal || readPlace.Index != writePlace.Index)
            return false;
        if (writePlace.IsLocal && writePlace.Index == tempStore.Index)
            return false;

        if (updateValue is not Binary { IsChecked: false, Kind: var kind } binary
            || binary.Left is not LoadLocal load || load.Index != tempStore.Index
            || binary.Right is not Constant { Value: 1 }
            || kind is not (BinaryKind.Add or BinaryKind.Subtract))
        {
            return false;
        }

        // The temporary must be written once and read exactly once across the
        // whole function — the increment we are folding — so it is genuinely dead.
        if (function.Descendants.OfType<StoreLocal>().Count(s => s.Index == tempStore.Index) != 1)
            return false;
        if (function.Descendants.OfType<LoadLocal>().Count(l => l.Index == tempStore.Index) != 1)
            return false;

        if (!IsIncrementable(writePlace.Type, function))
        {
            MarkUnsupportedIncrementStatement(tempStore, writePlace, kind, stepper);
            block.Children[i + 1].Detach();
            return true;
        }

        var increment = new IncrementDecrement(ClonePlace(writePlace), kind is BinaryKind.Add, isPrefix: false);
        stepper.StepOver($"fold dead-temp {(kind is BinaryKind.Add ? "++" : "--")} statement into operator", tempStore);
        tempStore.ReplaceWith(new ExpressionStatement(increment));
        block.Children[i + 1].Detach();
        return true;
    }

    readonly record struct ForLoopIncrement(ForLoop Loop, int TempIndex, PlaceRef Place, StoreLocal Capture, LoadLocal IncrementRead, BinaryKind Kind, bool IsIncrementable);

    /// <summary>
    /// Folds the for-loop post-increment temp form that iterator reconstruction
    /// leaves in a rebuilt <c>for</c> loop:
    ///
    /// <code>
    /// for (int j = 0; j &lt; n; j = V_1 + 1)   // update reads the temp
    /// {
    ///     ... body ...
    ///     V_1 = j;                            // body's last statement captures j
    /// }
    /// </code>
    ///
    /// The hoisted loop variable's <c>j++</c> is rebuilt through a spill slot —
    /// the body's last statement copies <c>j</c> into the temp, and the loop's
    /// update reads the temp back as <c>j = V_1 + 1</c>. Inlining the temp turns
    /// the update into the self-reading <c>j = j + 1</c>, which the printer already
    /// spells <c>j++</c>, and drops the capture. A single temp slot is reused
    /// across the loops of a nest (so the function-wide single-use guard of
    /// <see cref="TryFoldStatementIncrement"/> does not apply); this fold instead
    /// verifies the temp is used <em>only</em> in these dup idioms — every store of
    /// it is one of the captures and every load one of the update reads — and folds
    /// the whole group together, after which the temp is dead and undeclared.
    /// </summary>
    static void FoldForLoopIncrements(IrFunction function, Stepper stepper)
    {
        var candidates = new List<ForLoopIncrement>();
        foreach (var loop in function.Descendants.OfType<ForLoop>())
        {
            if (PlaceOf(loop.Increment) is not { } place || StoreValue(loop.Increment) is not { } updateValue)
                continue;
            if (updateValue is not Binary { IsChecked: false, Kind: BinaryKind.Add or BinaryKind.Subtract } binary
                || binary.Left is not LoadLocal tempRead
                || binary.Right is not Constant { Value: 1 })
            {
                continue;
            }
            if (loop.Body.Children.Count == 0 || loop.Body.Children[^1] is not StoreLocal capture)
                continue;
            if (capture.Index != tempRead.Index || PlaceLoad(capture.Value) is not { } captured)
                continue;
            if (captured.IsLocal != place.IsLocal || captured.Index != place.Index)
                continue;
            candidates.Add(new ForLoopIncrement(loop, tempRead.Index, place, capture, tempRead, binary.Kind, IsIncrementable(place.Type, function)));
        }

        foreach (var group in candidates.GroupBy(c => c.TempIndex))
        {
            int temp = group.Key;
            var folds = group.ToList();

            // The temp must be exclusively the dup spill: every store of it is one
            // of our captures, every load one of our update reads, and its address
            // is never taken. Otherwise inlining would drop an observed value.
            if (function.Descendants.OfType<LoadLocalAddress>().Any(a => a.Index == temp))
                continue;
            var captures = folds.Select(c => c.Capture).ToHashSet();
            var reads = folds.Select(c => (LoadLocal)c.IncrementRead).ToHashSet();
            var stores = function.Descendants.OfType<StoreLocal>().Where(s => s.Index == temp).ToList();
            var loads = function.Descendants.OfType<LoadLocal>().Where(l => l.Index == temp).ToList();
            if (stores.Count != captures.Count || !stores.All(captures.Contains))
                continue;
            if (loads.Count != reads.Count || !loads.All(reads.Contains))
                continue;

            foreach (var fold in folds)
            {
                if (!fold.IsIncrementable)
                {
                    MarkUnsupportedIncrementStatement(fold.Loop.Increment, fold.Place, fold.Kind, stepper);
                    fold.Capture.Detach();
                    continue;
                }

                stepper.StepOver($"inline for-loop post-increment temp into {(fold.Kind is BinaryKind.Add ? "++" : "--")} update", fold.Loop.Increment);
                fold.IncrementRead.ReplaceWith(ClonePlace(fold.Place));
                fold.Capture.Detach();
            }
        }
    }

    static PlaceRef? PlaceLoad(IrExpression expression) => expression switch
    {
        LoadLocal { ResultType: { } t } l => new PlaceRef(true, l.Index, "", t),
        LoadArgument { ResultType: { } t } a => new PlaceRef(false, a.Index, a.Name, t),
        _ => null,
    };

    static PlaceRef? PlaceOf(IrNode store) => store switch
    {
        StoreLocal s => new PlaceRef(true, s.Index, "", s.Type),
        StoreArgument s => new PlaceRef(false, s.Index, s.Name, s.Type),
        _ => null,
    };

    static IrExpression? StoreValue(IrNode store) => store switch
    {
        StoreLocal s => s.Value,
        StoreArgument s => s.Value,
        _ => null,
    };

    static bool IsPlaceLoad(IrExpression expression, PlaceRef place) => place.IsLocal
        ? expression is LoadLocal local && local.Index == place.Index
        : expression is LoadArgument argument && argument.Index == place.Index;

    readonly record struct IncrementOp(bool IsIncrement, bool IsChecked, IrExpression Operand);

    /// <summary>A user-defined increment/decrement operator call (<c>op_Increment</c>, <c>op_Decrement</c>, and their <c>op_Checked*</c> variants) on a single operand, or null. Requires operator metadata evidence so an ordinary method that happens to be named <c>op_Increment</c> is not mistaken for the operator.</summary>
    static IncrementOp? AsIncrementCall(IrExpression value)
        => value is Call { Callee: { HasThis: false, IsSpecialName: true, Name: var name } callee, Arguments: [{ } operand] }
            && callee.IsOperator != MetadataFactState.No
            && name switch
            {
                "op_Increment" => (IsIncrement: true, IsChecked: false),
                "op_Decrement" => (false, false),
                "op_CheckedIncrement" => (true, true),
                "op_CheckedDecrement" => (false, true),
                _ => ((bool IsIncrement, bool IsChecked)?)null,
            } is { } kind
            ? new IncrementOp(kind.IsIncrement, kind.IsChecked, operand)
            : null;

    static IrExpression ClonePlace(PlaceRef place) => place.IsLocal
        ? new LoadLocal(place.Index, place.Type)
        : new LoadArgument(place.Index, place.Name, place.Type);

    static bool IsIncrementable(TypeRef type, IrFunction function)
        => TypeFamilies.IsNumericPrimitive(type)
            || MemberIdentity.IsCoreLibraryType(type, "System", "Decimal")
            || type.Kind == TypeRefKind.Pointer
            || IsEnumOrPotentialEnum(type, function);

    static bool IsEnumOrPotentialEnum(TypeRef type, IrFunction function)
    {
        if (function.TypeShapes.TryGetValue(type, out var shape))
            return shape == TypeShape.Enum
                || shape == TypeShape.Unknown && type.DeclaredValueTypeHint == ValueTypeHint.ValueType;

        // Cross-assembly enum definitions are commonly unresolved here: signature
        // metadata proves value-type-ness, while the Binary ± 1 shape cannot be a
        // user-defined struct operator (those import as calls).
        return type.Kind == TypeRefKind.Definition && type.DeclaredValueTypeHint == ValueTypeHint.ValueType;
    }

    static void MarkUnsupportedIncrementExpression(IrExpression expression, PlaceRef place, BinaryKind kind, Stepper stepper)
    {
        var marker = UnsupportedIncrementNode(expression, place, kind);
        stepper.StepOver($"mark non-incrementable {place.Type.ToDisplayString()} {marker.Opcode} expression unsupported", expression);
        expression.ReplaceWith(marker);
    }

    static void MarkUnsupportedIncrementStatement(IrNode node, PlaceRef place, BinaryKind kind, Stepper stepper)
    {
        var marker = UnsupportedIncrementNode(node, place, kind);
        var statement = new ExpressionStatement(marker);
        statement.InheritSourceOffset(node);
        stepper.StepOver($"mark non-incrementable {place.Type.ToDisplayString()} {marker.Opcode} shape unsupported", node);
        node.ReplaceWith(statement);
    }

    static UnsupportedNode UnsupportedIncrementNode(IrNode node, PlaceRef place, BinaryKind kind)
    {
        string op = kind is BinaryKind.Add ? "++" : "--";
        var marker = new UnsupportedNode(
            node.SourceOffset >= 0 ? node.SourceOffset : 0,
            op,
            $"{op} is not defined for {place.Type.ToDisplayString()}");
        marker.InheritSourceOffset(node);
        return marker;
    }

    static bool ReferencesPlace(IrNode node, PlaceRef place)
    {
        foreach (var current in (IEnumerable<IrNode>)[node, .. node.Descendants])
        {
            bool hit = place.IsLocal
                ? current is LoadLocal { } l && l.Index == place.Index
                    || current is StoreLocal { } s && s.Index == place.Index
                    || current is LoadLocalAddress { } a && a.Index == place.Index
                : current is LoadArgument { } la && la.Index == place.Index
                    || current is StoreArgument { } sa && sa.Index == place.Index
                    || current is LoadArgumentAddress { } aa && aa.Index == place.Index;
            if (hit)
                return true;
        }
        return false;
    }

    static IrNode? StatementOf(IrNode node, Block block)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, block))
                return current;
        }
        return null;
    }
}
