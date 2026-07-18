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

        var children = block.Children;
        if (children.Count == 0) return false;

        var last = children[^1] as StoreField;
        if (last == null || last.Field != cacheField) return false;

        var createCall = last.Value as Call;
        if (createCall == null) return false;

        var createType = createCall.Callee.DeclaringType;
        if (createType.Kind != TypeRefKind.GenericInstance) return false;
        var createDef = createType.ElementType;

        if (createCall.Callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes) return false;
        if (createDef?.Namespace != "System.Runtime.CompilerServices" || createDef.Name != "CallSite`1") return false;

        if (!cacheField.Type.Equals(createType)) return false;
        if (!createCall.Callee.ReturnType.Equals(createType)) return false;
        if (createCall.Callee.Name != "Create" || createCall.Arguments.Count != 1) return false;
        if (createCall.Callee.HasThis) return false;

        var tArg = createType.TypeArguments[0];
        if (tArg.Kind != TypeRefKind.GenericInstance) return false;
        var tArgDef = tArg.ElementType;
        if (tArgDef == null || tArgDef.Assembly != TypeRef.CoreLibrary || tArgDef.Namespace != "System" || tArgDef.Name != "Func`3") return false;
        if (tArg.TypeArguments.Length != 3) return false;

        var binderArgType = createCall.Callee.ParameterTypes[0];
        if (binderArgType.Namespace != "System.Runtime.CompilerServices" || binderArgType.Name != "CallSiteBinder") return false;

        var binderCall = createCall.Arguments[0] as Call;
        if (binderCall == null) return false;

        var callee = binderCall.Callee;
        var decl = callee.DeclaringType;
        if (callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes) return false;
        if (decl.Namespace != "Microsoft.CSharp.RuntimeBinder" || decl.Name != "Binder") return false;
        if (callee.Name != "GetMember") return false;
        
        if (!callee.ReturnType.Equals(binderArgType)) return false;
        if (callee.ParameterTypes.Length != 4) return false;
        if (callee.HasThis) return false;

        var refKinds = callee.ParameterRefKinds;
        if (refKinds.Any(rk => rk != ArgumentRefKind.Value)) return false;

        if (callee.ParameterTypes[0].Namespace != "Microsoft.CSharp.RuntimeBinder" || callee.ParameterTypes[0].Name != "CSharpBinderFlags") return false;
        if (callee.ParameterTypes[1].Assembly != TypeRef.CoreLibrary || callee.ParameterTypes[1].Name != "String") return false;
        if (callee.ParameterTypes[2].Assembly != TypeRef.CoreLibrary || callee.ParameterTypes[2].Name != "Type") return false;
        if (callee.ParameterTypes[3].ElementType?.Name != "IEnumerable`1") return false;

        if (binderCall.Arguments.Count != 4) return false;

        if (binderCall.Arguments[0] is not Constant flagsConst || flagsConst.Value is not int flags || flags != 0) return false;
        if (binderCall.Arguments[1] is not Constant nameConst || nameConst.Value is not string propName) return false;
        if (!CSharpNaming.IsEscapableIdentifier(propName)) return false;
        propertyName = propName;

        var ledger = new List<IrNode>();
        IrExpression? currentArrayDef = null;
        IrExpression? currentContextDef = null;

        for (int i = 0; i < children.Count - 1; i++)
        {
            var child = children[i];
            if (child is StoreStackSlot sss)
            {
                if (sss.Value is NewArray) { currentArrayDef = sss.Value; ledger.Add(child); }
                else if (sss.Value is LoadToken or TypeOf) { currentContextDef = sss.Value; ledger.Add(child); }
                else return false;
            }
            else if (child is StoreLocal sl)
            {
                if (sl.Value is NewArray) { currentArrayDef = sl.Value; ledger.Add(child); }
                else if (sl.Value is LoadToken or TypeOf) { currentContextDef = sl.Value; ledger.Add(child); }
                else return false;
            }
            else if (child is StoreElement se)
            {
                if (se.Index is not Constant idx || idx.Value is not int index || index != 0) return false;
                if (!AreSameDefinition(block, se.Array, currentArrayDef)) return false;

                if (se.Value is not Call infoCreate) return false;
                if (infoCreate.Callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes) return false;
                if (infoCreate.Callee.Name != "Create" || infoCreate.Callee.DeclaringType.Name != "CSharpArgumentInfo" || infoCreate.Callee.DeclaringType.Namespace != "Microsoft.CSharp.RuntimeBinder") return false;
                if (infoCreate.Callee.HasThis) return false;
                if (!infoCreate.Callee.ReturnType.Equals(infoCreate.Callee.DeclaringType)) return false;
                if (infoCreate.Arguments.Count != 2) return false;
                if (infoCreate.Callee.ParameterTypes.Length != 2) return false;
                if (infoCreate.Callee.ParameterRefKinds.Any(rk => rk != ArgumentRefKind.Value)) return false;
                if (infoCreate.Callee.ParameterTypes[0].Namespace != "Microsoft.CSharp.RuntimeBinder" || infoCreate.Callee.ParameterTypes[0].Name != "CSharpArgumentInfoFlags") return false;
                if (infoCreate.Callee.ParameterTypes[1].Assembly != TypeRef.CoreLibrary || infoCreate.Callee.ParameterTypes[1].Name != "String") return false;

                if (infoCreate.Arguments[0] is not Constant fConst || fConst.Value is not int f || f != 0) return false;
                if (infoCreate.Arguments[1] is not Constant nConst || nConst.Value != null) return false;
                
                ledger.Add(child);
            }
            else
            {
                return false;
            }
        }

        if (currentArrayDef is not NewArray na) return false;
        if (na.ElementType.Name != "CSharpArgumentInfo" || na.ElementType.Namespace != "Microsoft.CSharp.RuntimeBinder") return false;
        if (na.Length is not Constant lenConst || lenConst.Value is not int len || len != 1) return false;
        arrayArgDef = na;

        if (currentContextDef == null) return false;
        TypeRef? contextType = null;
        if (currentContextDef is TypeOf typeOfNode) contextType = typeOfNode.Type;
        else if (currentContextDef is LoadToken lt && lt.Kind == RuntimeTokenKind.Type) contextType = lt.Type;

        if (contextType == null || !contextType.Equals(sourceContextType)) return false;

        if (!AreSameDefinition(block, binderCall.Arguments[2], currentContextDef)) return false;
        if (!AreSameDefinition(block, binderCall.Arguments[3], currentArrayDef)) return false;

        if (ledger.Count != children.Count - 1) return false;

        return true;
    }

    static bool AreSameDefinition(Block block, IrExpression useNode, IrExpression? defNode)
    {
        if (defNode == null) return false;
        if (useNode == defNode) return true;
        if (useNode is LoadStackSlot lss && defNode.Parent is StoreStackSlot sss && sss.Slot == lss.Slot) return true;
        if (useNode is LoadLocal ll && defNode.Parent is StoreLocal sl && sl.Index == ll.Index) return true;
        return false;
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
        if (!invokeCall.Callee.ParameterTypes[0].Equals(t0)) return false;
        if (!invokeCall.Callee.ParameterTypes[1].Equals(t1)) return false;
        if (invokeCall.Callee.ParameterRefKinds.Any(rk => rk != ArgumentRefKind.Value)) return false;

        if (!invokeCall.Callee.HasThis) return false;
        if (invokeCall.Arguments.Count != 3) return false;

        var instanceArg = invokeCall.Arguments[0] as LoadField; // Target
        var callsiteArg = invokeCall.Arguments[1] as LoadField; // s_cache

        if (instanceArg == null || instanceArg.Field.Name != "Target") return false;
        var targetType = instanceArg.Field.Type;
        if (!targetType.Equals(type)) return false;

        var targetDecl = instanceArg.Field.DeclaringType;
        if (targetDecl.Kind != TypeRefKind.GenericInstance || targetDecl.ElementType?.Namespace != "System.Runtime.CompilerServices" || targetDecl.ElementType?.Name != "CallSite`1") return false;
        if (!targetDecl.Equals(cacheField.Type)) return false;
        
        if (instanceArg.Instance is not LoadField lf1 || lf1.Field != cacheField) return false;
        if (callsiteArg == null || callsiteArg.Field != cacheField) return false;

        valueArg = invokeCall.Arguments[2];
        return true;
    }
}
