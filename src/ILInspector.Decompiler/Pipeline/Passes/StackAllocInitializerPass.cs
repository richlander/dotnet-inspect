namespace ILInspector.Decompiler.Pipeline;

using System.Collections.Generic;
using System.Linq;

public sealed class StackAllocInitializerPass : IIrPass
{
    public string Name => "stackalloc-initializer";

    public void Run(IrFunction function, PassContext context)
    {
        var stackSlots = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function).OfType<StoreStackSlot>().GroupBy(s => s.Slot).ToDictionary(g => g.Key, g => g.ToList());
        var localStores = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function).OfType<StoreLocal>().GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var copyBlock in GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function).OfType<CopyBlock>().ToList())
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
            if (allocIndex >= copyIndex || copyBlock.IsVolatile) continue;

            // Prove destination alias/use ownership
            var usages = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function).OfType<LoadStackSlot>().Where(l => l.Slot == loadDest.Slot).ToList();
            if (usages.Count != 2) continue; // One is the CopyBlock, the other is the actual usage.

            var finalUsage = usages.First(u => u != loadDest);
            var finalUsageStatement = GetStatement(finalUsage);
            if (finalUsageStatement == null || finalUsageStatement.Parent != parentBlock || finalUsageStatement.ChildIndex <= copyIndex)
            {
                continue; // Escaped or reordered destination
            }

            if (copyBlock.Size is not Constant { Value: int copySize } cSize || cSize.Type is not TypeRef cSizeType || cSizeType.Kind != TypeRefKind.Definition || cSizeType.Assembly != TypeRef.CoreLibrary || cSizeType.Namespace != "System" || cSizeType.Name != "Int32" || stackAlloc.Size is not Constant { Value: int allocSize } aSize || aSize.Type is not TypeRef aSizeType || aSizeType.Kind != TypeRefKind.Definition || aSizeType.Assembly != TypeRef.CoreLibrary || aSizeType.Namespace != "System" || aSizeType.Name != "Int32" || copySize != allocSize)
            {
                continue;
            }

            IrExpression? instance = null;
            TypeRef? expectedSourceType = null;
            TypeRef? elementType = null;

            if (copyBlock.Source is Call callItem)
            {
                if (IsTrustedSpanGetItem(callItem.Callee, out elementType, out expectedSourceType))
                {
                    if (callItem.Arguments.Count == 2 && callItem.Arguments[1] is Constant { Value: 0 } cIdx && cIdx.Type is TypeRef cIdxType && cIdxType.Kind == TypeRefKind.Definition && cIdxType.Assembly == TypeRef.CoreLibrary && cIdxType.Namespace == "System" && cIdxType.Name == "Int32")
                        instance = callItem.Arguments[0];
                }
                else if (IsTrustedSpanGetPinnableReference(callItem.Callee, out elementType, out expectedSourceType))
                {
                    if (callItem.Arguments.Count == 1) instance = callItem.Arguments[0];
                }
                else if (IsTrustedMemoryMarshalGetReference(callItem.Callee, out elementType, out expectedSourceType))
                {
                    if (callItem.Arguments.Count == 1) instance = callItem.Arguments[0];
                }
            }

            else if (copyBlock.Source is LoadProperty loadProp)
            {
                if (IsTrustedSpanGetItem(loadProp.Accessor, out elementType, out expectedSourceType))
                {
                    if (loadProp.IndexArguments.Count == 1 && loadProp.IndexArguments[0] is Constant { Value: 0 } cIdx && cIdx.Type is TypeRef cIdxType && cIdxType.Kind == TypeRefKind.Definition && cIdxType.Assembly == TypeRef.CoreLibrary && cIdxType.Namespace == "System" && cIdxType.Name == "Int32")
                        instance = loadProp.Instance;
                }
            }
            StoreLocal? spanSetupStore = null;
            if (instance != null)
            {
                if (instance is LoadLocalAddress lla && localStores.TryGetValue(lla.Index, out var stores) && stores.Count == 1)
                {
                    spanSetupStore = stores[0];
                    if (!expectedSourceType!.Equals(spanSetupStore.Type) || spanSetupStore.Type.Kind != TypeRefKind.GenericInstance || !expectedSourceType.Equals(lla.Type))
                    {
                        spanSetupStore = null;
                    }
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

            if (spanSetupStore != null)
            {
                if (spanSetupStore.Value is SpanLiteral spanLit)
                {
                    var lla = (LoadLocalAddress)instance!;
                    var allowedRefs = new List<IrNode> { spanSetupStore, lla };
                    if (LocalReferencesOnlyWithinCurrentBody(function, lla.Index, allowedRefs)) // Exclusive source ownership
                    {
                        if (elementType == null || elementType.Equals(spanLit.ElementType))
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
                                // Reject empty sources: an empty Span/ReadOnlySpan accessor call (e.g. get_Item(0))
                                // observably throws IndexOutOfRangeException, and removing it alongside the
                                // CopyBlock would silently erase that exception instead of preserving it.
                                if (allConstants && requiredElementCount > 0 && spanLit.Children.Count == requiredElementCount)
                                {
                                    elements = spanLit.Children.Cast<IrExpression>().ToList();
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
                else if (finalUsage.Parent is NewObject no)
                {
                    var ctor = no.Constructor;
                    if (ctor.DeclaringTypeIsTrustedPlatform == MetadataFactState.Yes
                        && ctor.HasThis
                        && ctor.Name == ".ctor"
                        && ctor.ReturnType.Kind == TypeRefKind.Definition && ctor.ReturnType.Assembly == TypeRef.CoreLibrary && ctor.ReturnType.Namespace == "System" && ctor.ReturnType.Name == "Void"
                        && ctor.DeclaringType.Kind == TypeRefKind.GenericInstance
                        && ctor.DeclaringType.TypeArguments.Length == 1
                        && ctor.TypeArguments.Length == 0)
                    {
                        var ctorDeclDef = ctor.DeclaringType.ElementType!;
                        if ((ctorDeclDef.Assembly == TypeRef.CoreLibrary || ctorDeclDef.Assembly == "System.Memory") && ctorDeclDef.Namespace == "System" && ctorDeclDef.Name is "Span`1" or "ReadOnlySpan`1")
                        {
                            if (ctor.ParameterTypes.Length == 2
                                && ctor.ParameterTypes[0].Kind == TypeRefKind.Pointer
                                && ctor.ParameterTypes[0].ElementType!.Kind == TypeRefKind.Definition
                                && ctor.ParameterTypes[0].ElementType!.Assembly == TypeRef.CoreLibrary
                                && ctor.ParameterTypes[0].ElementType!.Namespace == "System"
                                && ctor.ParameterTypes[0].ElementType!.Name == "Void"
                                && ctor.ParameterTypes[1].Kind == TypeRefKind.Definition
                                && ctor.ParameterTypes[1].Assembly == TypeRef.CoreLibrary
                                && ctor.ParameterTypes[1].Namespace == "System"
                                && ctor.ParameterTypes[1].Name == "Int32"
                                && ctor.ParameterRefKindsFacts == ParameterRefKindFacts.NotRequired && ctor.ParameterRefKinds.IsEmpty)
                            {
                                if (no.Arguments.Count == 2
                                    && no.Arguments[0] is LoadStackSlot destSlot
                                    && destSlot.Slot == loadDest.Slot
                                    && destSlot.Type != null && destSlot.Type.Equals(ctor.ParameterTypes[0])
                                    && no.Arguments[1] is Constant { Value: int len } cLen
                                    && cLen.Type is TypeRef lenType
                                    && lenType.Kind == TypeRefKind.Definition
                                    && lenType.Assembly == TypeRef.CoreLibrary
                                    && lenType.Namespace == "System"
                                    && lenType.Name == "Int32"
                                    && len == copySize / (GetSizeOf(ctor.DeclaringType.TypeArguments[0]) ?? 1))
                                {
                                    elementType = ctor.DeclaringType.TypeArguments[0];
                                    if (!TypeRef.Pointer(elementType).Equals(loadDest.Type))
                                    {
                                        elementType = null;
                                    }
                                }
                            }
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
                if (spanSetupStore != null)
                {
                    foreach (var el in elements) el.Detach();
                    spanSetupStore.Detach();
                }

                var stackAllocArray = new StackAllocArray(elementType, new Constant(elements.Count, TypeRef.CoreLib("System", "Int32")), TypeRef.Pointer(elementType), elements);
                stackAllocArray.InheritSourceOffset(stackAlloc);

                context.Stepper.StepOver("raise cpblk over localloc to stackalloc initializer", copyBlock);

                storeDest.Value.ReplaceWith(stackAllocArray);
                copyBlock.Detach();
            }
        }
    }

    static bool LocalReferencesOnlyWithinCurrentBody(IrFunction function, int localIndex, List<IrNode> allowedReferences)
    {
        foreach (var node in GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function))
        {
            if (node is LoadLocal ll && ll.Index == localIndex)
            {
                if (!allowedReferences.Contains(ll)) return false;
            }
            else if (node is LoadLocalAddress lla && lla.Index == localIndex)
            {
                if (!allowedReferences.Contains(lla)) return false;
            }
            else if (node is StoreLocal sl && sl.Index == localIndex)
            {
                if (!allowedReferences.Contains(sl)) return false;
            }
        }
        return true;
    }

    static bool IsTrustedMemoryMarshalGetReference(MethodRef method, out TypeRef? elementType, out TypeRef? expectedSourceType)
    {
        elementType = null; expectedSourceType = null;
        if (method.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes) return false;
        if (method.HasThis) return false;
        if (method.DeclaringType.Kind != TypeRefKind.Definition) return false;
        if (method.DeclaringType.Namespace != "System.Runtime.InteropServices" || method.DeclaringType.Name != "MemoryMarshal") return false;
        if (method.Name != "GetReference") return false;
        if (method.ParameterTypes.Length != 1) return false;
        if (method.ReturnType.Kind != TypeRefKind.ByRef) return false;
        if (method.ParameterTypes[0].Kind != TypeRefKind.GenericInstance) return false;
        if (method.TypeArguments.Length != 1) return false;
        if (method.ParameterRefKindsFacts != ParameterRefKindFacts.NotRequired || !method.ParameterRefKinds.IsEmpty) return false;

        var typeArg = method.TypeArguments[0];
        if (!typeArg.Equals(method.ReturnType.ElementType)) return false;

        var param0Def = method.ParameterTypes[0].ElementType!;
        if ((param0Def.Assembly != TypeRef.CoreLibrary && param0Def.Assembly != "System.Memory") || param0Def.Namespace != "System" || param0Def.Name is not ("Span`1" or "ReadOnlySpan`1")) return false;
        if (method.ParameterTypes[0].TypeArguments.Length != 1) return false;
        if (!typeArg.Equals(method.ParameterTypes[0].TypeArguments[0])) return false;

        elementType = typeArg;
        expectedSourceType = method.ParameterTypes[0];
        return true;
    }

    static bool IsTrustedSpanGetItem(MethodRef method, out TypeRef? elementType, out TypeRef? expectedSourceType)
    {
        elementType = null; expectedSourceType = null;
        if (method.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes) return false;
        if (!method.HasThis) return false;
        if (method.TypeArguments.Length != 0) return false;

        if (method.DeclaringType.Kind != TypeRefKind.GenericInstance) return false;
        var declDef = method.DeclaringType.ElementType!;
        if ((declDef.Assembly != TypeRef.CoreLibrary && declDef.Assembly != "System.Memory") || declDef.Namespace != "System" || declDef.Name is not ("Span`1" or "ReadOnlySpan`1")) return false;
        if (method.DeclaringType.TypeArguments.Length != 1) return false;
        if (method.ParameterRefKindsFacts != ParameterRefKindFacts.NotRequired || !method.ParameterRefKinds.IsEmpty) return false;

        if (method.Name != "get_Item") return false;
        if (method.ParameterTypes.Length != 1) return false;
        var p0 = method.ParameterTypes[0];
        if (p0.Kind != TypeRefKind.Definition || p0.Assembly != TypeRef.CoreLibrary || p0.Namespace != "System" || p0.Name != "Int32") return false;

        if (method.ReturnType.Kind != TypeRefKind.ByRef) return false;
        var typeArg = method.DeclaringType.TypeArguments[0];
        if (!typeArg.Equals(method.ReturnType.ElementType)) return false;

        elementType = typeArg;
        expectedSourceType = method.DeclaringType;
        return true;
    }

    static bool IsTrustedSpanGetPinnableReference(MethodRef method, out TypeRef? elementType, out TypeRef? expectedSourceType)
    {
        elementType = null; expectedSourceType = null;
        if (method.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes) return false;
        if (!method.HasThis) return false;
        if (method.TypeArguments.Length != 0) return false;

        if (method.DeclaringType.Kind != TypeRefKind.GenericInstance) return false;
        var declDef = method.DeclaringType.ElementType!;
        if ((declDef.Assembly != TypeRef.CoreLibrary && declDef.Assembly != "System.Memory") || declDef.Namespace != "System" || declDef.Name is not ("Span`1" or "ReadOnlySpan`1")) return false;
        if (method.DeclaringType.TypeArguments.Length != 1) return false;
        if (method.ParameterRefKindsFacts != ParameterRefKindFacts.NotRequired || !method.ParameterRefKinds.IsEmpty) return false;

        if (method.Name != "GetPinnableReference") return false;
        if (method.ParameterTypes.Length != 0) return false;

        if (method.ReturnType.Kind != TypeRefKind.ByRef) return false;
        var typeArg = method.DeclaringType.TypeArguments[0];
        if (!typeArg.Equals(method.ReturnType.ElementType)) return false;

        elementType = typeArg;
        expectedSourceType = method.DeclaringType;
        return true;
    }

    static IrNode? GetStatement(IrNode node)
    {
        while (node.Parent != null && node.Parent is not Block)
            node = node.Parent;
        return node.Parent is Block ? node : null;
    }

    static int? GetSizeOf(TypeRef type)
    {
        if (type.Kind == TypeRefKind.Definition && type.Assembly == TypeRef.CoreLibrary && type.Namespace == "System")
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
