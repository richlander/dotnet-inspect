namespace ILInspector.Decompiler.Pipeline;

using System.Collections.Generic;
using System.Linq;

public sealed class StackAllocInitializerPass : IIrPass
{
    public string Name => "stackalloc-initializer";

    public void Run(IrFunction function, PassContext context)
    {
        var stackSlots = function.Descendants.OfType<StoreStackSlot>().GroupBy(s => s.Slot).ToDictionary(g => g.Key, g => g.ToList());
        var localStores = function.Descendants.OfType<StoreLocal>().GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var copyBlock in function.Descendants.OfType<CopyBlock>().ToList())
        {
            if (copyBlock.Destination is not LoadStackSlot loadDest || !stackSlots.TryGetValue(loadDest.Slot, out var storeDests) || storeDests.Count != 1 || storeDests[0].Value is not StackAllocate stackAlloc)
            {
                continue;
            }

            if (copyBlock.Size is not Constant { Value: int copySize } || stackAlloc.Size is not Constant { Value: int allocSize } || copySize != allocSize)
            {
                continue;
            }

            var storeDest = storeDests[0];

            List<IrExpression>? elements = null;
            TypeRef? elementType = null;

            if (copyBlock.Source is LoadProperty loadProp)
            {
                if (loadProp.Accessor.Name == "get_Item")
                {
                    var instance = loadProp.Instance;
                    if (instance is LoadLocalAddress lla && localStores.TryGetValue(lla.Index, out var stores) && stores.Count == 1)
                    {
                        var store = stores[0];
                        if (store.Value is SpanLiteral spanLit)
                        {
                            elements = spanLit.Children.Cast<IrExpression>().ToList();
                            foreach (var el in elements)
                                el.Detach();
                            elementType = spanLit.ElementType;

                            var usages = function.Descendants.OfType<LoadLocalAddress>().Where(l => l.Index == lla.Index).ToList();
                            if (usages.Count == 1) // Only used by this CopyBlock's get_Item call
                            {
                                store.Detach();
                            }
                        }
                    }
                }
            }
            else if (copyBlock.Source is LoadFieldAddress loadField && loadField.FieldRvaData is { } rvaData)
            {
                var usages = function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == loadDest.Slot && l != loadDest).ToList();
                foreach (var usage in usages)
                {
                    if (usage.Parent is StoreLocal sl && sl.Type.Kind == TypeRefKind.Pointer)
                    {
                        elementType = sl.Type.ElementType;
                        break;
                    }
                    else if (usage.Parent is Convert { Parent: StoreLocal slc } && slc.Type.Kind == TypeRefKind.Pointer)
                    {
                        elementType = slc.Type.ElementType;
                        break;
                    }
                    else if (usage.Parent is NewObject no && no.Constructor.DeclaringType.Name is "Span`1" or "ReadOnlySpan`1")
                    {
                        elementType = no.Constructor.DeclaringType.TypeArguments[0];
                        break;
                    }
                }

                if (elementType != null)
                {
                    elements = RvaSpanPass.DecodeElements(function, elementType, rvaData);
                }
            }

            if (elements != null && elementType != null)
            {
                var stackAllocArray = new StackAllocArray(elementType, new Constant(elements.Count, TypeRef.CoreLib("System", "Int32")), TypeRef.Pointer(elementType), elements);
                stackAllocArray.InheritSourceOffset(stackAlloc);

                context.Stepper.StepOver("raise cpblk over localloc to stackalloc initializer", copyBlock);

                // We want to remove the CopyBlock and fold the stack slot.
                var remainingUsages = function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == loadDest.Slot && l != loadDest).ToList();
                if (remainingUsages.Count == 1)
                {
                    remainingUsages[0].ReplaceWith(stackAllocArray);
                    storeDest.Detach();
                }
                else
                {
                    storeDest.Value.ReplaceWith(stackAllocArray);
                }

                copyBlock.Detach();
            }
        }
    }
}
