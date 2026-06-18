namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Canonicalizes the receiver of a constructor-chain call (<c>this..ctor</c>
/// or <c>base..ctor</c>) back to <c>this</c>. When the base-call argument
/// carries control flow — the ubiquitous <c>base(message ?? SR.Default)</c>
/// exception shape — the importer spills the loaded <c>this</c> across the
/// branch into a slot, and the inliner leaves it there: <c>this</c> is held
/// impure because <see cref="TypeRef"/> cannot yet prove the receiver is a
/// class rather than a mutable byref struct.
///
/// But the receiver of a constructor's own base/this call is the object under
/// construction, an immutable reference for the whole body — naming it
/// <c>this</c> at the call site is always sound regardless of intervening
/// computation. This pass makes that one safe move the general inliner
/// declines, then drops the now-dead spill so the printer can render
/// <c>base(args);</c> / <c>this(args);</c> instead of <c>S_0..ctor(args);</c>.
/// </summary>
public sealed class ConstructorChainPass : IIrPass
{
    public string Name => "constructor-chain";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Signature.HasThis)
            return;

        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (call.Callee.Name != ".ctor" || !call.Callee.HasThis || call.Arguments.Count == 0)
                continue;
            var receiver = call.Arguments[0];
            if (receiver is LoadArgument { Index: 0 })
                continue;  // already canonical
            if (ResolveThisSpill(function, receiver) is not { } spill)
                continue;

            var thisArgument = new LoadArgument(0, "this", function.DeclaringType);
            receiver.ReplaceWith(thisArgument);
            // The spill's sole load is gone; drop the store so it does not
            // print as a dead `T S_0 = this;` declaration.
            spill.Store.Detach();
        }
    }

    /// <summary>
    /// A receiver that is a slot or local with exactly one load (this call)
    /// and a single store of <c>this</c>; null when it is anything else.
    /// </summary>
    static (IrNode Store, IrExpression Load)? ResolveThisSpill(IrFunction function, IrExpression receiver)
    {
        (bool IsSlot, int Index)? key = receiver switch
        {
            LoadStackSlot slot => (true, slot.Slot),
            LoadLocal local => (false, local.Index),
            _ => null,
        };
        if (key is not { } place)
            return null;

        IrNode? store = null;
        int loads = 0;
        bool storeIsThis = false;
        foreach (var node in function.Descendants)
        {
            if (place.IsSlot)
            {
                if (node is LoadStackSlot l && l.Slot == place.Index)
                    loads++;
                else if (node is StoreStackSlot s && s.Slot == place.Index)
                {
                    if (store is not null)
                        return null;  // multiple stores
                    store = s;
                    storeIsThis = s.Value is LoadArgument { Index: 0 };
                }
            }
            else
            {
                if (node is LoadLocal l && l.Index == place.Index)
                    loads++;
                else if (node is LoadLocalAddress a && a.Index == place.Index)
                    return null;  // an escaped address makes the rename unsound
                else if (node is StoreLocal s && s.Index == place.Index)
                {
                    if (store is not null)
                        return null;
                    store = s;
                    storeIsThis = s.Value is LoadArgument { Index: 0 };
                }
            }
        }
        return store is not null && storeIsThis && loads == 1 ? (store, receiver) : null;
    }
}
