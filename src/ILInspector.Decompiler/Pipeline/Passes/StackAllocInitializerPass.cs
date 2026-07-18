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

            IrExpression? instance = null;

            if (copyBlock.Source is Call callItem)
            {
                if (IsTrustedSpanGetItem(callItem.Callee) || IsTrustedSpanGetPinnableReference(callItem.Callee))
                {
                    instance = callItem.Arguments[0];
                }
                else if (IsTrustedMemoryMarshalGetReference(callItem.Callee))
                {
                    instance = callItem.Arguments[0];
                }
            }
            else if (copyBlock.Source is LoadProperty loadProp)
            {
                if (IsTrustedSpanGetItem(loadProp.Accessor))
                {
                    instance = loadProp.Instance;
                }
            }

            StoreLocal? spanSetupStore = null;
            if (instance != null)
            {
                if (instance is LoadLocalAddress lla && localStores.TryGetValue(lla.Index, out var stores) && stores.Count == 1)
                {
                    spanSetupStore = stores[0];
                }
            }
            else if (copyBlock.Source is LoadFieldAddress loadField && loadField.FieldRvaData != null)
            {
                // Valid RVA path
            }
            else
            {
                continue;
            }

            // Ledger proof: EXACT sequence.
            var expectedIntervening = new List<IrNode>();
            if (spanSetupStore != null)
            {
                expectedIntervening.Add(spanSetupStore);
            }

            var actualIntervening = new List<IrNode>();
            for (int i = allocIndex + 1; i < copyIndex; i++)
            {
                actualIntervening.Add(parentBlock.Children[i]);
            }

            if (actualIntervening.Count != expectedIntervening.Count) continue;
            bool match = true;
            for (int i = 0; i < actualIntervening.Count; i++)
            {
                if (actualIntervening[i] != expectedIntervening[i])
                {
                    match = false; break;
                }
            }
            if (!match) continue;

            List<IrExpression>? elements = null;
            TypeRef? elementType = null;

            if (spanSetupStore != null)
            {
                if (spanSetupStore.Value is SpanLiteral spanLit)
                {
                    var lla = (LoadLocalAddress)instance!;
                    var sourceUsages = function.Descendants.OfType<LoadLocalAddress>().Where(l => l.Index == lla.Index).ToList();
                    if (sourceUsages.Count == 1) // Exclusive source ownership
                    {
                        elementType = spanLit.ElementType;
                        int? elementSize = GetSizeOf(elementType);
                        if (elementSize != null && copySize % elementSize.Value == 0)
                        {
                            int requiredElementCount = copySize / elementSize.Value;
                            bool allConstants = true;
                            foreach (var child in spanLit.Children)
                            {
                                if (child is not Constant) // Must be constant
                                {
                                    allConstants = false; break;
                                }
                            }
                            if (allConstants && spanLit.Children.Count == requiredElementCount)
                            {
                                elements = spanLit.Children.Cast<IrExpression>().ToList();
                                foreach (var el in elements) el.Detach();
                                spanSetupStore.Detach();
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
                else if (finalUsage.Parent is NewObject no)
                {
                    var ctorDeclDef = no.Constructor.DeclaringType.Kind == TypeRefKind.GenericInstance ? no.Constructor.DeclaringType.ElementType! : no.Constructor.DeclaringType;
                    if (ctorDeclDef.Namespace == "System" && ctorDeclDef.Name is "Span`1" or "ReadOnlySpan`1")
                    {
                        if (no.Constructor.DeclaringTypeIsTrustedPlatform == MetadataFactState.Yes && no.Constructor.ParameterTypes.Length == 2 && no.Constructor.ParameterTypes[0].Kind == TypeRefKind.Pointer && no.Constructor.ParameterTypes[1].Name == "Int32")
                        {
                            elementType = no.Constructor.DeclaringType.TypeArguments[0];
                        }
                    }
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

    static bool IsTrustedMemoryMarshalGetReference(MethodRef method)
    {
        var declDef = method.DeclaringType.Kind == TypeRefKind.GenericInstance ? method.DeclaringType.ElementType! : method.DeclaringType;
        var param0Def = method.ParameterTypes.Length > 0 && method.ParameterTypes[0].Kind == TypeRefKind.GenericInstance ? method.ParameterTypes[0].ElementType! : (method.ParameterTypes.Length > 0 ? method.ParameterTypes[0] : null);

        return method.DeclaringTypeIsTrustedPlatform == MetadataFactState.Yes
            && declDef.Namespace == "System.Runtime.InteropServices"
            && declDef.Name == "MemoryMarshal"
            && method.Name == "GetReference"
            && method.ParameterTypes.Length == 1
            && method.ReturnType.Kind == TypeRefKind.ByRef
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericInstance
            && param0Def?.Name is "Span`1" or "ReadOnlySpan`1";
    }

    static bool IsTrustedSpanGetItem(MethodRef method)
    {
        var declDef = method.DeclaringType.Kind == TypeRefKind.GenericInstance ? method.DeclaringType.ElementType! : method.DeclaringType;

        return method.DeclaringTypeIsTrustedPlatform == MetadataFactState.Yes
            && method.HasThis
            && declDef.Namespace == "System"
            && declDef.Name is "Span`1" or "ReadOnlySpan`1"
            && method.Name == "get_Item"
            && method.ParameterTypes.Length == 1
            && method.ParameterTypes[0].Name == "Int32"
            && method.ReturnType.Kind == TypeRefKind.ByRef;
    }

    static bool IsTrustedSpanGetPinnableReference(MethodRef method)
    {
        var declDef = method.DeclaringType.Kind == TypeRefKind.GenericInstance ? method.DeclaringType.ElementType! : method.DeclaringType;

        return method.DeclaringTypeIsTrustedPlatform == MetadataFactState.Yes
            && method.HasThis
            && declDef.Namespace == "System"
            && declDef.Name is "Span`1" or "ReadOnlySpan`1"
            && method.Name == "GetPinnableReference"
            && method.ParameterTypes.Length == 0
            && method.ReturnType.Kind == TypeRefKind.ByRef;
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
