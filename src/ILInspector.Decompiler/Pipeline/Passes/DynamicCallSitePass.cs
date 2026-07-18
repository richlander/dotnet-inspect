using System;
using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises dynamic call sites initialized by C# compiler-generated scaffolding.
/// Replaces the CallSite cache initialization and invocation with a typed dynamic IR node.
/// </summary>
public sealed class DynamicCallSitePass : IIrPass
{
    public string Name => "dynamic-callsite";

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
    {
        bool changed = false;
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is IfStatement ifStmt)
                {
                    if (ifStmt.Condition is LogicalNot ln && ln.Operand is LoadField cacheField && cacheField.Instance == null)
                    {
                        if (cacheField.Field.DeclaringTypeCompilerGenerated != MetadataFactState.Yes
                            || !GeneratedCodeIdentity.IsDynamicCallSiteContainerType(cacheField.Field.DeclaringType))
                        {
                            continue; // cache ownership proof
                        }

                        var storeField = FindStoreField(ifStmt.Then);
                        if (storeField != null && storeField.Value is Call createCall)
                        {
                            if (createCall.Callee.Name == "Create" && createCall.Callee.DeclaringType.ElementType?.Name == "CallSite`1")
                            {
                                var binderCall = createCall.Arguments[0] as Call;
                                if (binderCall != null && binderCall.Callee.Name == "GetMember" && binderCall.Callee.DeclaringType.Name == "Binder")
                                {
                                    var propertyNameConst = binderCall.Arguments[1] as Constant;
                                    string? propertyName = propertyNameConst?.Value as string;

                                    if (propertyName != null)
                                    {
                                        var next = children[i + 1];
                                        if (next is Return ret && ret.Value is Call invokeCall && invokeCall.Callee.Name == "Invoke")
                                        {
                                            var delegateSig = invokeCall.Callee.DeclaringType.ToDisplayString();
                                            if (delegateSig == "Func<CallSite, object, object>")
                                            {
                                                var valueArg = invokeCall.Arguments[2];
                                                valueArg.Detach();

                                                var dynamicGet = new DynamicGetMember(valueArg, propertyName);
                                                var newReturn = new Return(dynamicGet);

                                                next.ReplaceWith(newReturn);
                                                ifStmt.Detach();
                                                stepper.StepOver("raise dynamic get", newReturn);
                                                changed = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return changed;
    }

    static StoreField? FindStoreField(IrNode node)
    {
        return node.Descendants.OfType<StoreField>().LastOrDefault();
    }
}
