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

                        if (!IsCanonicalInitialization(thenBlock, cacheField.Field, function.DeclaringType, out var propertyName, out var arrayArgDef))
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

    static bool IsCanonicalInitialization(Block block, FieldRef cacheField, TypeRef sourceContextType, out string? propertyName, out IrExpression? arrayArgDef)
    {
        propertyName = null;
        arrayArgDef = null;

        if (block.Children.Count == 0) return false;

        var last = block.Children[^1] as StoreField;
        if (last == null || last.Field != cacheField) return false;

        var createCall = last.Value as Call;
        if (createCall == null) return false;

        // Is CallSite<T>.Create?
        var createType = createCall.Callee.DeclaringType;
        if (createType.Kind != TypeRefKind.GenericInstance) return false;
        var createDef = createType.ElementType;
        if (createDef == null || createDef.Namespace != "System.Runtime.CompilerServices" || createDef.Name != "CallSite`1") return false;
        if (createDef.Assembly != "System.Linq.Expressions" && createDef.Assembly != "System.Core" && createDef.Assembly != "netstandard" && createDef.Assembly != "System.Dynamic.Runtime") return false;
        if (!cacheField.Type.Equals(createType)) return false;
        if (createCall.Callee.Name != "Create" || createCall.Arguments.Count != 1) return false;
        if (createCall.Callee.HasThis) return false;

        var tArg = createType.TypeArguments[0];
        if (tArg.Kind != TypeRefKind.GenericInstance) return false;
        var tArgDef = tArg.ElementType;
        if (tArgDef == null || tArgDef.Assembly != TypeRef.CoreLibrary || tArgDef.Namespace != "System" || tArgDef.Name != "Func`3") return false;
        if (tArg.TypeArguments.Length != 3) return false;

        var binderCall = createCall.Arguments[0] as Call;
        if (binderCall == null) return false;

        // Validate Binder.GetMember
        var callee = binderCall.Callee;
        var decl = callee.DeclaringType;
        if (decl.Assembly != "Microsoft.CSharp" || decl.Namespace != "Microsoft.CSharp.RuntimeBinder" || decl.Name != "Binder") return false;
        if (callee.Name != "GetMember") return false;
        if (callee.ReturnType.Namespace != "System.Runtime.CompilerServices" || callee.ReturnType.Name != "CallSiteBinder") return false;
        var binderRetAssm = callee.ReturnType.Assembly;
        if (binderRetAssm != "System.Linq.Expressions" && binderRetAssm != "System.Core" && binderRetAssm != "System.Dynamic.Runtime" && binderRetAssm != "netstandard") return false;
        if (callee.ParameterTypes.Length != 4) return false;
        if (callee.HasThis) return false;
        if (binderCall.Arguments.Count != 4) return false;

        // Argument 0: flags
        if (binderCall.Arguments[0] is not Constant flagsConst || flagsConst.Value is not int flags || flags != 0) return false;

        // Argument 1: name
        if (binderCall.Arguments[1] is not Constant nameConst || nameConst.Value is not string propName) return false;
        if (!CSharpNaming.IsEscapableIdentifier(propName)) return false; // Name must be escapable
        propertyName = propName;

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

        // Argument 3: CSharpArgumentInfo[]
        var arg3Node = binderCall.Arguments[3];
        IrExpression? arrayDef = null;
        if (arg3Node is LoadStackSlot or LoadLocal)
        {
            arrayDef = FindDefinition(block, arg3Node);
        }
        if (arrayDef is not NewArray na || na.ElementType.Name != "CSharpArgumentInfo" || na.ElementType.Namespace != "Microsoft.CSharp.RuntimeBinder" || na.ElementType.Assembly != "Microsoft.CSharp") return false;
        arrayArgDef = arrayDef;

        // Ensure canonical owned statements and alias/dataflow graph
        int storeElementCount = 0;
        int newArrayCount = 0;
        int storeSlotCount = 0;

        for (int i = 0; i < block.Children.Count - 1; i++)
        {
            var child = block.Children[i];
            if (child is StoreStackSlot sss)
            {
                storeSlotCount++;
                if (sss.Value is NewArray) newArrayCount++;
                else if (sss.Value is not LoadToken && sss.Value is not TypeOf) return false;
            }
            else if (child is StoreLocal sl)
            {
                storeSlotCount++;
                if (sl.Value is NewArray) newArrayCount++;
                else if (sl.Value is not LoadToken && sl.Value is not TypeOf) return false;
            }
            else if (child is StoreElement se)
            {
                storeElementCount++;
                if (se.Index is not Constant idx || idx.Value is not int index || index != 0) return false;

                IrExpression? arrayRefDef = null;
                if (se.Array is LoadStackSlot or LoadLocal) arrayRefDef = FindDefinition(block, se.Array);
                if (arrayRefDef != arrayDef) return false;

                if (se.Value is not Call infoCreate) return false;
                if (infoCreate.Callee.Name != "Create" || infoCreate.Callee.DeclaringType.Name != "CSharpArgumentInfo" || infoCreate.Callee.DeclaringType.Namespace != "Microsoft.CSharp.RuntimeBinder" || infoCreate.Callee.DeclaringType.Assembly != "Microsoft.CSharp") return false;
                if (infoCreate.Callee.HasThis) return false;
                if (!infoCreate.Callee.ReturnType.Equals(infoCreate.Callee.DeclaringType)) return false;
                if (infoCreate.Arguments.Count != 2) return false;
                if (infoCreate.Arguments[0] is not Constant fConst || fConst.Value is not int f || f != 0) return false;
                if (infoCreate.Arguments[1] is not Constant nConst || nConst.Value != null) return false;
            }
            else
            {

                return false; // Extraneous statement
            }
        }

        if (storeElementCount != 1) return false;
        if (newArrayCount != 1) return false;

        return true;
    }

    static IrExpression? FindDefinition(Block block, IrExpression loadNode)
    {
        if (loadNode is LoadStackSlot lss)
        {
            var stores = block.Children.OfType<StoreStackSlot>().Where(s => s.Slot == lss.Slot).ToList();
            if (stores.Count == 1) return stores[0].Value;
        }
        else if (loadNode is LoadLocal ll)
        {
            var stores = block.Children.OfType<StoreLocal>().Where(s => s.Index == ll.Index).ToList();
            if (stores.Count == 1) return stores[0].Value;
        }
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

        if (invokeCall.Callee.ReturnType.Assembly != TypeRef.CoreLibrary || invokeCall.Callee.ReturnType.Namespace != "System" || invokeCall.Callee.ReturnType.Name != "Object") return false;
        if (invokeCall.Callee.ParameterTypes.Length != 2) return false;
        if (!invokeCall.Callee.HasThis) return false;

        if (invokeCall.Arguments.Count != 3) return false;
        var instanceArg = invokeCall.Arguments[0] as LoadField; // Target
        var callsiteArg = invokeCall.Arguments[1] as LoadField; // s_cache

        if (instanceArg != null && instanceArg.Field.Name == "Target" && instanceArg.Field.DeclaringType.Kind == TypeRefKind.GenericInstance && instanceArg.Field.DeclaringType.ElementType?.Name.StartsWith("CallSite") == true && instanceArg.Instance is LoadField lf1 && lf1.Field == cacheField &&
            callsiteArg != null && callsiteArg.Field == cacheField)
        {
            valueArg = invokeCall.Arguments[2];
            return true;
        }


        return false;
    }
}
