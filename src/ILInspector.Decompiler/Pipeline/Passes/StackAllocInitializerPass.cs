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
            if (copyBlock.Parent is not Block parentBlock) continue;

            if (copyBlock.Destination is not LoadStackSlot loadDest || !stackSlots.TryGetValue(loadDest.Slot, out var storeDests) || storeDests.Count != 1 || storeDests[0].Value is not StackAllocate stackAlloc)
            {
                continue;
            }

            var storeDest = storeDests[0];
            if (storeDest.Parent != parentBlock) continue;

            int allocIndex = storeDest.ChildIndex;
            int copyIndex = copyBlock.ChildIndex;
            if (allocIndex >= copyIndex) continue;

            // Prove canonical same-block statement ledger for the compiler shapes being folded.
            // We permit NO intervening statements between the stack allocation and the copy, EXCEPT
            // exactly the initialization of the span literal if it's stored to a local that is used in the copy.
            bool interveningStatementsValid = true;
            for (int i = allocIndex + 1; i < copyIndex; i++)
            {
                var stmt = parentBlock.Children[i];
                if (stmt is StoreLocal sl && sl.Value is SpanLiteral)
                {
                    // This is the Span literal setup, it's allowed!
                    continue;
                }
                interveningStatementsValid = false;
                break;
            }
            if (!interveningStatementsValid) continue;

            // Prove destination alias/use ownership
            var usages = function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == loadDest.Slot).ToList();
            if (usages.Count != 2) continue; // One is the CopyBlock, the other is the actual usage.

            var finalUsage = usages.First(u => u != loadDest);
            var finalUsageStatement = GetStatement(finalUsage);
            if (finalUsageStatement == null || finalUsageStatement.Parent != parentBlock || finalUsageStatement.ChildIndex <= copyIndex)
            {
                continue; // Escaped or reordered destination
            }

            if (copyBlock.Size is not Constant { Value: int copySize } || stackAlloc.Size is not Constant { Value: int allocSize } || copySize != allocSize)
            {
                continue;
            }

            List<IrExpression>? elements = null;
            TypeRef? elementType = null;

            IrExpression? instance = null;

            if (copyBlock.Source is Call callItem)
            {
                if (MemberIdentity.IsSpanLikeType(callItem.Callee.DeclaringType) &&
                    (callItem.Callee.Name == "get_Item" || callItem.Callee.Name == "GetPinnableReference") &&
                    callItem.Arguments.Count > 0)
                {
                    instance = callItem.Arguments[0];
                }
                else if ((callItem.Callee.DeclaringType.Assembly == TypeRef.CoreLibrary || callItem.Callee.DeclaringType.Assembly == "System.Memory") &&
                         callItem.Callee.DeclaringType.Namespace == "System.Runtime.InteropServices" &&
                         callItem.Callee.DeclaringType.Name == "MemoryMarshal" &&
                         callItem.Callee.Name == "GetReference" &&
                         callItem.Arguments.Count == 1)
                {
                    instance = callItem.Arguments[0];
                }
            }
            else if (copyBlock.Source is LoadProperty loadProp)
            {
                if (MemberIdentity.IsSpanLikeType(loadProp.Accessor.DeclaringType) &&
                    (loadProp.PropertyName == "Item" || loadProp.Accessor.Name == "get_Item") &&
                    loadProp.Instance != null)
                {
                    instance = loadProp.Instance;
                }
            }

            if (instance != null)
            {
                if (instance is LoadLocalAddress lla && localStores.TryGetValue(lla.Index, out var stores) && stores.Count == 1)
                {
                    var store = stores[0];
                    if (store.Value is SpanLiteral spanLit)
                    {
                        var sourceUsages = function.Descendants.OfType<LoadLocalAddress>().Where(l => l.Index == lla.Index).ToList();
                        if (sourceUsages.Count == 1) // Exclusive source ownership
                        {
                            elementType = spanLit.ElementType;
                            int? elementSize = GetSizeOf(elementType);
                            if (elementSize != null && copySize % elementSize.Value == 0)
                            {
                                int requiredElementCount = copySize / elementSize.Value;
                                if (spanLit.Children.Count == requiredElementCount)
                                {
                                    elements = spanLit.Children.Cast<IrExpression>().ToList();
                                    foreach (var el in elements) el.Detach();
                                    store.Detach();
                                }
                            }
                        }
                    }
                }
            }
            else if (copyBlock.Source is LoadFieldAddress loadField && loadField.FieldRvaData is { } rvaData)
            {
                if (finalUsage.Parent is StoreLocal sl && sl.Type.Kind == TypeRefKind.Pointer)
                {
                    elementType = sl.Type.ElementType;
                }
                else if (finalUsage.Parent is Convert { Parent: StoreLocal slc } && slc.Type.Kind == TypeRefKind.Pointer)
                {
                    elementType = slc.Type.ElementType;
                }
                else if (finalUsage.Parent is NewObject no && no.Constructor.DeclaringType.Namespace == "System" && no.Constructor.DeclaringType.Name is "Span`1" or "ReadOnlySpan`1")
                {
                    elementType = no.Constructor.DeclaringType.TypeArguments[0];
                }

                if (elementType != null)
                {
                    int? elementSize = GetSizeOf(elementType);
                    if (elementSize != null && copySize % elementSize.Value == 0 && rvaData.Length == copySize)
                    {
                        int requiredElementCount = copySize / elementSize.Value;
                        elements = RvaSpanPass.DecodeElements(function, elementType, rvaData, requiredElementCount);
                        if (elements != null && elements.Count != requiredElementCount)
                        {
                            elements = null; // Mismatched size
                        }
                    }
                }
            }

            if (elements != null && elementType != null)
            {
                var stackAllocArray = new StackAllocArray(elementType, new Constant(elements.Count, TypeRef.CoreLib("System", "Int32")), TypeRef.Pointer(elementType), elements);
                stackAllocArray.InheritSourceOffset(stackAlloc);

                context.Stepper.StepOver("raise cpblk over localloc to stackalloc initializer", copyBlock);

                storeDest.Value.ReplaceWith(stackAllocArray);
                copyBlock.Detach();
            }
        }
    }

    static IrNode? GetStatement(IrNode node)
    {
        while (node.Parent != null && node.Parent is not Block)
            node = node.Parent;
        return node.Parent is Block ? node : null;
    }


    static int? GetSizeOf(TypeRef type)
    {
        if (type.Kind == TypeRefKind.Definition && type.Namespace == "System")
        {
            return type.Name switch
            {
                "Byte" or "SByte" or "Boolean" => 1,
                "Int16" or "UInt16" or "Char" => 2,
                "Int32" or "UInt32" or "Single" => 4,
                "Int64" or "UInt64" or "Double" => 8,
                _ => null
            };
        }
        return null;
    }
}
