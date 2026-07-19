using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the C# compiler's dynamic member-get call-site scaffolding
/// (<c>Binder.GetMember</c> over a <c>CallSite&lt;Func&lt;CallSite, object, object&gt;&gt;</c>
/// cache) back to <c>((dynamic)receiver).Member</c>.
///
/// Recovery is intentionally narrow: every identity (cache ownership, the
/// <c>CallSite&lt;T&gt;.Create</c> factory, <c>Binder.GetMember</c>,
/// <c>CSharpArgumentInfo.Create</c>, and the delegate <c>Invoke</c>) is proven
/// against token-anchored platform trust and exact signatures, the cache setup
/// ledger and its slot/local dataflow are proven unique and confined, and any
/// malformed metadata shape declines instead of throwing. A near miss keeps the
/// honest explicit call-site scaffolding.
/// </summary>
public sealed class DynamicCallSitePass : IIrPass
{
    public string Name => "dynamic-callsite";

    const string RuntimeBinderNamespace = "Microsoft.CSharp.RuntimeBinder";
    const string CompilerServicesNamespace = "System.Runtime.CompilerServices";

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not IfStatement ifStmt)
                    continue;
                if (ifStmt.Condition is not LogicalNot ln || ln.Operand is not LoadField guardLoad || guardLoad.Instance != null)
                    continue;
                if (ifStmt.Then is not Block thenBlock)
                    continue;

                var cacheField = guardLoad.Field;

                // Cache ownership: a compiler-generated <>o__N dynamic call-site
                // container field, never a hand-authored lookalike.
                if (cacheField.DeclaringTypeCompilerGenerated != MetadataFactState.Yes
                    || !GeneratedCodeIdentity.IsDynamicCallSiteContainerType(cacheField.DeclaringType))
                {
                    continue;
                }

                if (children[i + 1] is not Return ret || ret.Value is not Call invokeCall)
                    continue;

                if (!TryRaise(function, thenBlock, invokeCall, guardLoad, cacheField, out var receiver, out var memberName))
                    continue;

                receiver.Detach();
                var newReturn = new Return(new DynamicGetMember(receiver, memberName));
                ret.ReplaceWith(newReturn);
                ifStmt.Detach();
                stepper.StepOver("raise dynamic get", newReturn);
                return true;
            }
        }

        return false;
    }

    static bool TryRaise(
        IrFunction function,
        Block thenBlock,
        Call invokeCall,
        LoadField guardLoad,
        FieldRef cacheField,
        out IrExpression receiver,
        out string memberName)
    {
        receiver = null!;
        memberName = null!;

        var statements = thenBlock.Children;
        // Exactly: <array def>, <context def>, <element store>, <cache store>.
        if (statements.Count != 4)
            return false;

        // --- Cache store + CallSite<T>.Create identity ---
        if (statements[^1] is not StoreField cacheStore || cacheStore.Field != cacheField || cacheStore.HasInstance)
            return false;
        if (cacheStore.Value is not Call createCall)
            return false;
        if (!IsExactCreate(createCall, cacheField, out var binderCall, out var cacheT))
            return false;

        // --- Binder.GetMember identity ---
        if (!IsExactBinder(binderCall, out memberName, out var contextUse, out var arrayUse))
        {
            memberName = null!;
            return false;
        }

        // --- Setup ledger: resolve each statement, then require exact multiplicity ---
        StoreElement? elementStore = null;
        (IrNode Store, DefKey Key, NewArray Array)? arrayDef = null;
        (IrNode Store, DefKey Key)? contextDef = null;
        var seenKeys = new HashSet<DefKey>();

        for (int s = 0; s < statements.Count - 1; s++)
        {
            var statement = statements[s];
            if (statement is StoreElement se)
            {
                if (elementStore != null)
                    return false;
                elementStore = se;
                continue;
            }

            if (!TryDefinitionKey(statement, out var key, out var value))
                return false;

            // Duplicate definition of the same slot/local — even the same value —
            // means the dataflow is not the single canonical shape.
            if (!seenKeys.Add(key))
                return false;

            if (value is NewArray na)
            {
                if (arrayDef != null)
                    return false;
                if (!IsExactInfoArray(na))
                    return false;
                arrayDef = (statement, key, na);
            }
            else if (value is TypeOf or LoadToken)
            {
                if (contextDef != null)
                    return false;
                if (!TryContextType(value, out var contextType) || !contextType.Equals(function.DeclaringType))
                    return false;
                contextDef = (statement, key);
            }
            else
            {
                return false;
            }
        }

        if (elementStore == null || arrayDef == null || contextDef == null)
            return false;

        // --- Element store: array[0] = CSharpArgumentInfo.Create(...) ---
        if (elementStore.Index is not Constant idx || idx.Value is not int index || index != 0)
            return false;
        if (!UseMatchesKey(elementStore.Array, arrayDef.Value.Key))
            return false;
        if (elementStore.ElementType != null && !elementStore.ElementType.Equals(arrayDef.Value.Array.ElementType))
            return false;
        if (elementStore.Value is not Call infoCreate || !IsExactInfoCreate(infoCreate))
            return false;

        // --- Binder argument uses resolve to the owned definitions ---
        if (!UseMatchesKey(contextUse, contextDef.Value.Key))
            return false;
        if (!UseMatchesKey(arrayUse, arrayDef.Value.Key))
            return false;

        // --- Ordering: the array definition precedes its element-store use ---
        if (arrayDef.Value.Store.ChildIndex > elementStore.ChildIndex)
            return false;
        // Context and array are both consumed by the (last) cache store; being
        // setup statements they already precede it.

        // --- Delegate Invoke + Target identity ---
        if (!IsExactInvoke(invokeCall, cacheField, cacheT, out receiver, out var targetInstanceLoad, out var cacheArgLoad))
        {
            receiver = null!;
            return false;
        }

        // --- Dataflow confinement across the whole function scope ---
        // Each owned slot/local has exactly one definition (the ledger store) and
        // is loaded only by its proven uses; nothing aliases or escapes.
        if (!SlotConfined(function, arrayDef.Value.Key, arrayDef.Value.Store, [elementStore.Array, arrayUse]))
        {
            receiver = null!;
            return false;
        }
        if (!SlotConfined(function, contextDef.Value.Key, contextDef.Value.Store, [contextUse]))
        {
            receiver = null!;
            return false;
        }

        // The cache field is written once (the init store) and read only by the
        // guard, the delegate Target receiver, and the CallSite argument.
        if (!CacheFieldConfined(function, cacheField, cacheStore, [guardLoad, targetInstanceLoad, cacheArgLoad]))
        {
            receiver = null!;
            return false;
        }

        return true;
    }

    static bool IsExactCreate(Call createCall, FieldRef cacheField, out Call binderCall, out TypeRef cacheT)
    {
        binderCall = null!;
        cacheT = null!;

        var callee = createCall.Callee;
        var createType = callee.DeclaringType;

        // Declaring type CallSite<T>: trusted, generic arity 1.
        if (createType.Kind != TypeRefKind.GenericInstance || createType.TypeArguments.Length != 1)
            return false;
        var createDef = createType.ElementType;
        if (createDef == null || createDef.Namespace != CompilerServicesNamespace || createDef.Name != "CallSite`1")
            return false;
        if (callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes)
            return false;

        // Cache field type is exactly CallSite<T>; static Create returns exactly CallSite<T>.
        if (!cacheField.Type.Equals(createType))
            return false;
        if (!callee.ReturnType.Equals(createType))
            return false;
        if (callee.Name != "Create" || callee.HasThis || !IsNonGeneric(callee))
            return false;

        // Exactly one parameter (trusted CallSiteBinder) and one argument, by value.
        if (callee.ParameterTypes.Length != 1 || createCall.Arguments.Count != 1)
            return false;
        if (!IsValueRefKinds(callee))
            return false;
        var binderParam = callee.ParameterTypes[0];
        if (binderParam.Namespace != CompilerServicesNamespace || binderParam.Name != "CallSiteBinder")
            return false;

        // T is exactly corelib Func`3<trusted non-generic CallSite, object, object>.
        var t = createType.TypeArguments[0];
        if (!IsCallSiteFunc(t))
            return false;

        if (createCall.Arguments[0] is not Call binder)
            return false;

        binderCall = binder;
        cacheT = t;
        return true;
    }

    static bool IsExactBinder(Call binderCall, out string memberName, out IrExpression contextUse, out IrExpression arrayUse)
    {
        memberName = null!;
        contextUse = null!;
        arrayUse = null!;

        var callee = binderCall.Callee;
        var decl = callee.DeclaringType;

        // Trusted, exact Microsoft.CSharp.RuntimeBinder.Binder.GetMember, static, non-generic.
        if (callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes)
            return false;
        if (decl.Namespace != RuntimeBinderNamespace || decl.Name != "Binder")
            return false;
        if (callee.Name != "GetMember" || callee.HasThis || !IsNonGeneric(callee))
            return false;

        // Returns exactly trusted CallSiteBinder.
        if (callee.ReturnType.Namespace != CompilerServicesNamespace || callee.ReturnType.Name != "CallSiteBinder")
            return false;

        // Exactly four value parameters with the exact types.
        if (callee.ParameterTypes.Length != 4 || binderCall.Arguments.Count != 4)
            return false;
        if (!IsValueRefKinds(callee))
            return false;

        var p = callee.ParameterTypes;
        if (p[0].Namespace != RuntimeBinderNamespace || p[0].Name != "CSharpBinderFlags")
            return false;
        if (!IsCoreLib(p[1], "System", "String"))
            return false;
        if (!IsCoreLib(p[2], "System", "Type"))
            return false;
        if (!IsArgumentInfoEnumerable(p[3]))
            return false;

        // Exact flags (0), escapable member name, then the source-context and
        // info-array argument uses (resolved against owned definitions by caller).
        if (binderCall.Arguments[0] is not Constant flagsConst || flagsConst.Value is not int flags || flags != 0)
            return false;
        if (binderCall.Arguments[1] is not Constant nameConst || nameConst.Value is not string name)
            return false;
        if (!CSharpNaming.IsEscapableIdentifier(name))
            return false;

        memberName = name;
        contextUse = binderCall.Arguments[2];
        arrayUse = binderCall.Arguments[3];
        return true;
    }

    static bool IsExactInfoCreate(Call infoCreate)
    {
        var callee = infoCreate.Callee;
        var decl = callee.DeclaringType;

        if (callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes)
            return false;
        if (decl.Namespace != RuntimeBinderNamespace || decl.Name != "CSharpArgumentInfo")
            return false;
        if (callee.Name != "Create" || callee.HasThis || !IsNonGeneric(callee))
            return false;
        if (!callee.ReturnType.Equals(decl))
            return false;
        if (callee.ParameterTypes.Length != 2 || infoCreate.Arguments.Count != 2)
            return false;
        if (!IsValueRefKinds(callee))
            return false;
        if (callee.ParameterTypes[0].Namespace != RuntimeBinderNamespace || callee.ParameterTypes[0].Name != "CSharpArgumentInfoFlags")
            return false;
        if (!IsCoreLib(callee.ParameterTypes[1], "System", "String"))
            return false;

        // flags == 0, name == null.
        if (infoCreate.Arguments[0] is not Constant fConst || fConst.Value is not int f || f != 0)
            return false;
        if (infoCreate.Arguments[1] is not Constant nConst || nConst.Value != null)
            return false;

        return true;
    }

    static bool IsExactInvoke(Call invokeCall, FieldRef cacheField, TypeRef cacheT, out IrExpression receiver, out LoadField targetInstanceLoad, out LoadField cacheArgLoad)
    {
        receiver = null!;
        targetInstanceLoad = null!;
        cacheArgLoad = null!;

        var callee = invokeCall.Callee;

        // The delegate type is exactly the cache's T (corelib Func<CallSite, object, object>).
        if (!callee.DeclaringType.Equals(cacheT) || !IsCallSiteFunc(callee.DeclaringType))
            return false;
        if (callee.Name != "Invoke" || !callee.HasThis || !IsNonGeneric(callee))
            return false;

        // Exact Invoke signature: (CallSite, object) -> object, by value.
        if (!IsCoreLib(callee.ReturnType, "System", "Object"))
            return false;
        if (callee.ParameterTypes.Length != 2 || invokeCall.Arguments.Count != 3)
            return false;
        if (callee.ParameterTypes[0].Kind != TypeRefKind.Definition
            || callee.ParameterTypes[0].Namespace != CompilerServicesNamespace
            || callee.ParameterTypes[0].Name != "CallSite")
        {
            return false;
        }
        if (!IsCoreLib(callee.ParameterTypes[1], "System", "Object"))
            return false;
        if (!IsValueRefKinds(callee))
            return false;

        // arg0: the delegate Target field, declared on CallSite<T>, typed exactly T,
        // read from the same cache field.
        if (invokeCall.Arguments[0] is not LoadField targetLoad || targetLoad.Field.Name != "Target")
            return false;
        if (!targetLoad.Field.Type.Equals(cacheT))
            return false;
        var targetDecl = targetLoad.Field.DeclaringType;
        if (targetDecl.Kind != TypeRefKind.GenericInstance
            || targetDecl.ElementType?.Namespace != CompilerServicesNamespace
            || targetDecl.ElementType?.Name != "CallSite`1")
        {
            return false;
        }
        if (!targetDecl.Equals(cacheField.Type))
            return false;
        if (targetLoad.Instance is not LoadField targetInstance || targetInstance.Field != cacheField || targetInstance.Instance != null)
            return false;

        // arg1: the same cache field passed as the CallSite argument.
        if (invokeCall.Arguments[1] is not LoadField cacheArg || cacheArg.Field != cacheField || cacheArg.Instance != null)
            return false;

        targetInstanceLoad = targetInstance;
        cacheArgLoad = cacheArg;
        receiver = invokeCall.Arguments[2];
        return true;
    }

    static bool IsExactInfoArray(NewArray na)
    {
        if (na.ElementType.Namespace != RuntimeBinderNamespace || na.ElementType.Name != "CSharpArgumentInfo")
            return false;
        return na.Length is Constant lenConst && lenConst.Value is int len && len == 1;
    }

    static bool IsCallSiteFunc(TypeRef t)
    {
        if (t.Kind != TypeRefKind.GenericInstance || t.TypeArguments.Length != 3)
            return false;
        var def = t.ElementType;
        if (def == null || def.Assembly != TypeRef.CoreLibrary || def.Namespace != "System" || def.Name != "Func`3")
            return false;
        var callSite = t.TypeArguments[0];
        if (callSite.Kind != TypeRefKind.Definition
            || callSite.Namespace != CompilerServicesNamespace
            || callSite.Name != "CallSite")
        {
            return false;
        }
        return IsCoreLib(t.TypeArguments[1], "System", "Object")
            && IsCoreLib(t.TypeArguments[2], "System", "Object");
    }

    static bool IsArgumentInfoEnumerable(TypeRef type)
    {
        if (type.Kind != TypeRefKind.GenericInstance || type.TypeArguments.Length != 1)
            return false;
        var def = type.ElementType;
        if (def == null || def.Assembly != TypeRef.CoreLibrary
            || def.Namespace != "System.Collections.Generic" || def.Name != "IEnumerable`1")
        {
            return false;
        }
        var arg = type.TypeArguments[0];
        return arg.Kind == TypeRefKind.Definition
            && arg.Namespace == RuntimeBinderNamespace
            && arg.Name == "CSharpArgumentInfo";
    }

    static bool TryContextType(IrExpression value, out TypeRef contextType)
    {
        switch (value)
        {
            case TypeOf typeOf:
                contextType = typeOf.Type;
                return true;
            case LoadToken { Kind: RuntimeTokenKind.Type, Type: { } tokenType }:
                contextType = tokenType;
                return true;
            default:
                contextType = null!;
                return false;
        }
    }

    readonly record struct DefKey(bool IsSlot, int Index);

    static bool TryDefinitionKey(IrNode statement, out DefKey key, out IrExpression value)
    {
        switch (statement)
        {
            case StoreStackSlot sss:
                key = new DefKey(true, sss.Slot);
                value = sss.Value;
                return true;
            case StoreLocal sl:
                key = new DefKey(false, sl.Index);
                value = sl.Value;
                return true;
            default:
                key = default;
                value = null!;
                return false;
        }
    }

    static bool UseMatchesKey(IrExpression use, DefKey key)
        => key.IsSlot
            ? use is LoadStackSlot lss && lss.Slot == key.Index
            : use is LoadLocal ll && ll.Index == key.Index;

    static bool SlotConfined(IrFunction function, DefKey key, IrNode definition, IReadOnlyList<IrExpression> allowedLoads)
    {
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreStackSlot sss when key.IsSlot && sss.Slot == key.Index:
                    if (!ReferenceEquals(sss, definition))
                        return false;
                    break;
                case StoreLocal sl when !key.IsSlot && sl.Index == key.Index:
                    if (!ReferenceEquals(sl, definition))
                        return false;
                    break;
                case LoadStackSlot lss when key.IsSlot && lss.Slot == key.Index:
                    if (!ContainsReference(allowedLoads, lss))
                        return false;
                    break;
                case LoadLocal ll when !key.IsSlot && ll.Index == key.Index:
                    if (!ContainsReference(allowedLoads, ll))
                        return false;
                    break;
            }
        }

        return true;
    }

    static bool CacheFieldConfined(IrFunction function, FieldRef cacheField, StoreField definition, IReadOnlyList<LoadField> allowedLoads)
    {
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreField sf when sf.Field == cacheField:
                    if (!ReferenceEquals(sf, definition))
                        return false;
                    break;
                case LoadField lf when lf.Field == cacheField:
                    if (!ContainsReference(allowedLoads, lf))
                        return false;
                    break;
            }
        }

        return true;
    }

    static bool ContainsReference<T>(IReadOnlyList<T> nodes, T node) where T : class
    {
        foreach (var candidate in nodes)
            if (ReferenceEquals(candidate, node))
                return true;
        return false;
    }

    static bool IsNonGeneric(MethodRef callee) => callee.TypeArguments.IsDefaultOrEmpty;

    static bool IsValueRefKinds(MethodRef callee)
        => callee.ParameterRefKindsFacts != ParameterRefKindFacts.Unknown
            && (callee.ParameterRefKinds.IsDefaultOrEmpty
                || callee.ParameterRefKinds.All(rk => rk == ArgumentRefKind.Value));

    static bool IsCoreLib(TypeRef type, string ns, string name)
        => type.Kind == TypeRefKind.Definition
            && type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == ns
            && type.Name == name;
}
