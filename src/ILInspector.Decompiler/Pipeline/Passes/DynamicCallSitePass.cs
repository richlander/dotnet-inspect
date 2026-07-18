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

                        if (ifStmt.Then is not Block thenBlock)
                            continue;

                        if (!IsCanonicalInitialization(thenBlock, cacheField.Field, function.DeclaringType, out var createCall, out var binderCall, out string? propertyName))
                            continue;

                        var next = children[i + 1];
                        if (next is Return ret && ret.Value is Call invokeCall)
                        {
                            if (IsCanonicalInvoke(invokeCall, cacheField.Field, out var valueArg))
                            {
                                valueArg.Detach();
                                var dynamicGet = new DynamicGetMember(valueArg, propertyName!);
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
        return changed;
    }

    static bool IsCanonicalInitialization(Block block, FieldRef cacheField, TypeRef sourceContextType, out Call? createCall, out Call? binderCall, out string? propertyName)
    {
        createCall = null;
        binderCall = null;
        propertyName = null;

        if (block.Children.Count == 0) return false;

        var last = block.Children[^1] as StoreField;
        if (last == null || last.Field != cacheField) return false;

        createCall = last.Value as Call;
        if (createCall == null) return false;

        // Is CallSite<T>.Create?
        if (createCall.Callee.Name != "Create" || createCall.Arguments.Count != 1) return false;
        var createType = createCall.Callee.DeclaringType;
        if (createType.Kind != TypeRefKind.GenericInstance) return false;
        var createDef = createType.ElementType;
        if (createDef == null || createDef.Namespace != "System.Runtime.CompilerServices" || createDef.Name != "CallSite`1") return false;

        binderCall = createCall.Arguments[0] as Call;
        if (binderCall == null) return false;

        // Validate Binder.GetMember
        var callee = binderCall.Callee;
        if (callee.Name != "GetMember") return false;
        var decl = callee.DeclaringType;
        if (decl.Assembly != "Microsoft.CSharp" || decl.Namespace != "Microsoft.CSharp.RuntimeBinder" || decl.Name != "Binder") return false;
        if (binderCall.Arguments.Count != 4) return false;

        // Argument 0: flags
        if (binderCall.Arguments[0] is not Constant flagsConst || flagsConst.Value is not int flags || flags != 0) return false;

        // Argument 1: name
        if (binderCall.Arguments[1] is not Constant nameConst || nameConst.Value is not string propName) return false;
        if (!CSharpNaming.IsUsableIdentifier(propName) || !CSharpNaming.IsEscapableIdentifier(propName)) return false; // Name must be escapable
        propertyName = CSharpNaming.EscapeIdentifier(propName);

        // Argument 2: context type
        var contextTypeNode = binderCall.Arguments[2];
        TypeRef? contextType = null;
        if (contextTypeNode is TypeOf typeOfNode)
            contextType = typeOfNode.Type;
        else if (contextTypeNode is LoadStackSlot or LoadLocal)
        {
            var def = FindDefinition(block, contextTypeNode);
            if (def is TypeOf defTypeOf)
                contextType = defTypeOf.Type;
            else if (def is LoadToken lt2 && lt2.Kind == RuntimeTokenKind.Type)
                contextType = lt2.Type;
        }
        else if (contextTypeNode is LoadToken lt && lt.Kind == RuntimeTokenKind.Type)
            contextType = lt.Type;

        if (contextType == null || !contextType.Equals(sourceContextType)) return false;

        // Ensure no unexpected side effects and validate CSharpArgumentInfo.Create
        int storeElementCount = 0;
        for (int i = 0; i < block.Children.Count - 1; i++)
        {
            var child = block.Children[i];
            if (child is StoreStackSlot || child is StoreLocal)
            {
                var value = (child as StoreStackSlot)?.Value ?? (child as StoreLocal)?.Value;
                if (value is not NewArray and not LoadToken and not Constant and not LoadStackSlot and not LoadLocal and not TypeOf)
                {
                    return false;
                }
            }
            else if (child is StoreElement se)
            {
                storeElementCount++;
                if (se.Value is not Call infoCreate) return false;
                if (infoCreate.Callee.Name != "Create" || infoCreate.Callee.DeclaringType.Name != "CSharpArgumentInfo" || infoCreate.Callee.DeclaringType.Namespace != "Microsoft.CSharp.RuntimeBinder") return false;
                if (infoCreate.Arguments.Count != 2) return false;
                if (infoCreate.Arguments[0] is not Constant fConst || fConst.Value is not int f || f != 0) return false;
                if (infoCreate.Arguments[1] is not Constant nConst || nConst.Value != null) return false;
            }
            else
            {
                return false;
            }
        }

        if (storeElementCount != 1) return false;

        return true;
    }

    static IrExpression? FindDefinition(Block block, IrExpression loadNode)
    {
        if (loadNode is LoadStackSlot lss)
            return block.Children.OfType<StoreStackSlot>().LastOrDefault(s => s.Slot == lss.Slot)?.Value;
        if (loadNode is LoadLocal ll)
            return block.Children.OfType<StoreLocal>().LastOrDefault(s => s.Index == ll.Index)?.Value;
        return null;
    }

    static bool IsCanonicalInvoke(Call invokeCall, FieldRef cacheField, out IrExpression valueArg)
    {
        valueArg = null!;
        if (invokeCall.Callee.Name != "Invoke") return false;

        var type = invokeCall.Callee.DeclaringType;
        if (type.Kind != TypeRefKind.GenericInstance) return false;
        var def = type.ElementType;
        if (def == null || def.Assembly != TypeRef.CoreLibrary || def.Namespace != "System" || def.Name != "Func`3") return false;
        if (type.TypeArguments.Length != 3) return false;

        var t0 = type.TypeArguments[0];
        if (t0.Namespace != "System.Runtime.CompilerServices" || t0.Name != "CallSite") return false;

        var t1 = type.TypeArguments[1];
        if (t1.Assembly != TypeRef.CoreLibrary || t1.Namespace != "System" || t1.Name != "Object") return false;

        var t2 = type.TypeArguments[2];
        if (t2.Assembly != TypeRef.CoreLibrary || t2.Namespace != "System" || t2.Name != "Object") return false;

        if (invokeCall.Arguments.Count != 3) return false;
        var instanceArg = invokeCall.Arguments[0] as LoadField; // Target
        var callsiteArg = invokeCall.Arguments[1] as LoadField; // s_cache

        if (instanceArg != null && instanceArg.Field.Name == "Target" && instanceArg.Instance is LoadField lf1 && lf1.Field == cacheField &&
            callsiteArg != null && callsiteArg.Field == cacheField)
        {
            valueArg = invokeCall.Arguments[2];
            return true;
        }

        return false;
    }
}
