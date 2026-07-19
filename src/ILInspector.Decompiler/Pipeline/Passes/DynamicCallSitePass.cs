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
/// against token-anchored platform trust and exact signatures, every signature
/// type's assembly is tied back to a trusted declaring assembly rather than a
/// forgeable simple name, the cache setup ledger appears in its exact canonical
/// order with unique slot/local dataflow proven confined (no aliases, escapes,
/// or address-of leaks), the lazy-init guard's opposite arm is proven empty
/// before the guard is deleted, and any malformed metadata shape declines
/// instead of throwing. A near miss keeps the honest explicit call-site
/// scaffolding.
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
        // Only the root body's scope may be raised: the cache setup ledger and
        // every slot/local confinement proof reason about this body's storage
        // pool. A nested lambda or local function carries independent slot/local
        // numbering and receives its own pipeline run, so its blocks and
        // definitions must neither be transformed here nor contaminate a
        // candidate's confinement. Walk with the nested-function boundary.
        foreach (var block in GenericDeclarationPatternProof
            .DescendantsOutsideNestedFunctions(function).OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not IfStatement ifStmt)
                    continue;

                // The lazy-init guard: identify the cache-field load, the setup
                // arm (ending in the cache store), and the opposite arm.
                if (!TryGetCacheGuard(ifStmt, out var guardLoad, out var setupArm, out var oppositeArm))
                    continue;

                var cacheField = guardLoad.Field;

                // Cache ownership: a compiler-generated <>o__N dynamic call-site
                // container field, never a hand-authored lookalike.
                if (cacheField.DeclaringTypeCompilerGenerated != MetadataFactState.Yes
                    || !GeneratedCodeIdentity.IsDynamicCallSiteContainerType(cacheField.DeclaringType))
                {
                    continue;
                }

                // Deleting the guard removes both arms. The opposite arm must
                // carry no statements, or removing it would drop real work.
                if (oppositeArm is not null && oppositeArm.Children.Count != 0)
                    continue;

                if (children[i + 1] is not Return ret || ret.Value is not Call invokeCall)
                    continue;

                if (!TryRaise(function, setupArm, invokeCall, guardLoad, cacheField, out var receiver, out var memberName))
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

    /// <summary>
    /// Identifies a cache lazy-init guard and proves its polarity: the setup arm
    /// must be the arm the runtime selects when the cache is still null.
    /// <c>if (!cache) { setup } else {}</c> puts setup in the then arm;
    /// <c>if (cache) {} else { setup }</c> puts it in the else arm. The mandated
    /// setup arm must end in the cache store, so an inverted guard that would run
    /// setup when the cache is already populated (or an empty mandated arm)
    /// declines rather than deleting live behavior.
    /// </summary>
    static bool TryGetCacheGuard(IfStatement ifStmt, out LoadField guardLoad, out Block setupArm, out Block? oppositeArm)
    {
        guardLoad = null!;
        setupArm = null!;
        oppositeArm = null;

        if (!TryGuardCacheLoad(ifStmt.Condition, out var load, out var negated))
            return false;

        // Polarity fixes which arm is the null-path setup arm.
        Block mandatedSetup;
        Block? opposite;
        if (negated)
        {
            // if (!cache) { setup } else {}
            mandatedSetup = ifStmt.Then;
            opposite = ifStmt.Else;
        }
        else
        {
            // if (cache) {} else { setup } — the else arm must exist to hold setup.
            if (ifStmt.Else is null)
                return false;
            mandatedSetup = ifStmt.Else;
            opposite = ifStmt.Then;
        }

        // The null-path arm must be the one that ends in the cache store.
        if (!EndsWithCacheStore(mandatedSetup, load.Field))
            return false;

        guardLoad = load;
        setupArm = mandatedSetup;
        oppositeArm = opposite;
        return true;
    }

    /// <summary>
    /// The guard condition is a bare truthiness test on a static field:
    /// <c>cache</c> (<paramref name="negated"/> false) or <c>!cache</c>
    /// (<paramref name="negated"/> true).
    /// </summary>
    static bool TryGuardCacheLoad(IrExpression condition, out LoadField load, out bool negated)
    {
        switch (condition)
        {
            case LogicalNot { Operand: LoadField { Instance: null } inner }:
                load = inner;
                negated = true;
                return true;
            case LoadField { Instance: null } bare:
                load = bare;
                negated = false;
                return true;
            default:
                load = null!;
                negated = false;
                return false;
        }
    }

    static bool EndsWithCacheStore(Block block, FieldRef cacheField)
        => block.Children.Count > 0
            && block.Children[^1] is StoreField store
            && !store.HasInstance
            && store.Field == cacheField;

    static bool TryRaise(
        IrFunction function,
        Block setupArm,
        Call invokeCall,
        LoadField guardLoad,
        FieldRef cacheField,
        out IrExpression receiver,
        out string memberName)
    {
        receiver = null!;
        memberName = null!;

        var statements = setupArm.Children;
        // Exactly, and in this exact order:
        //   [0] StoreSlot(array   = new CSharpArgumentInfo[1])
        //   [1] StoreSlot(context = typeof(DeclaringType))
        //   [2] StoreElement(array[0] = CSharpArgumentInfo.Create(...))
        //   [3] StoreField(cache = CallSite<T>.Create(Binder.GetMember(...)))
        if (statements.Count != 4)
            return false;

        // --- [3] Cache store + CallSite<T>.Create identity ---
        if (statements[3] is not StoreField cacheStore || cacheStore.Field != cacheField || cacheStore.HasInstance)
            return false;
        if (cacheStore.Value is not Call createCall)
            return false;
        if (!IsExactCreate(createCall, cacheField, out var binderCall, out var cacheT, out var callSiteAssembly))
            return false;

        // --- Binder.GetMember identity ---
        if (!IsExactBinder(binderCall, callSiteAssembly, out memberName, out var contextUse, out var arrayUse, out var binderAssembly))
        {
            memberName = null!;
            return false;
        }

        // --- [0] Info array definition ---
        if (!TryDefinitionKey(statements[0], out var arrayKey, out var arrayValue))
            return false;
        if (arrayValue is not NewArray infoArray || !IsExactInfoArray(infoArray, binderAssembly))
            return false;

        // --- [1] Context definition: typeof(DeclaringType) ---
        if (!TryDefinitionKey(statements[1], out var contextKey, out var contextValue))
            return false;
        if (contextValue is not TypeOf contextTypeOf || !contextTypeOf.Type.Equals(function.DeclaringType))
            return false;

        // The two setup definitions occupy distinct storage (no duplicate
        // definition, even of the same value).
        if (arrayKey.Equals(contextKey))
            return false;

        // --- [2] Element store: array[0] = CSharpArgumentInfo.Create(...) ---
        if (statements[2] is not StoreElement elementStore)
            return false;
        if (elementStore.Index is not Constant idx || idx.Value is not int index || index != 0)
            return false;
        if (!UseMatchesKey(elementStore.Array, arrayKey))
            return false;
        if (elementStore.ElementType != null && !elementStore.ElementType.Equals(infoArray.ElementType))
            return false;
        if (elementStore.Value is not Call infoCreate || !IsExactInfoCreate(infoCreate, binderAssembly))
            return false;

        // --- Binder argument uses resolve to the owned definitions ---
        if (!UseMatchesKey(contextUse, contextKey))
            return false;
        if (!UseMatchesKey(arrayUse, arrayKey))
            return false;

        // --- Delegate Invoke + Target identity ---
        if (!IsExactInvoke(invokeCall, cacheField, cacheT, callSiteAssembly, out receiver, out var targetInstanceLoad, out var cacheArgLoad))
        {
            receiver = null!;
            return false;
        }

        // --- Dataflow confinement across the whole function scope ---
        // Each owned slot/local has exactly one definition (the ledger store) and
        // is loaded only by its proven uses; nothing aliases, escapes, or takes
        // its address.
        if (!SlotConfined(function, arrayKey, statements[0], [elementStore.Array, arrayUse]))
        {
            receiver = null!;
            return false;
        }
        if (!SlotConfined(function, contextKey, statements[1], [contextUse]))
        {
            receiver = null!;
            return false;
        }

        // The cache field is written once (the init store) and read only by the
        // guard, the delegate Target receiver, and the CallSite argument — and
        // never has its address taken.
        if (!CacheFieldConfined(function, cacheField, cacheStore, [guardLoad, targetInstanceLoad, cacheArgLoad]))
        {
            receiver = null!;
            return false;
        }

        return true;
    }

    static bool IsExactCreate(Call createCall, FieldRef cacheField, out Call binderCall, out TypeRef cacheT, out string callSiteAssembly)
    {
        binderCall = null!;
        cacheT = null!;
        callSiteAssembly = null!;

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

        // The trusted CallSite declaring assembly anchors every CallSite-family
        // signature type below (CallSiteBinder, the non-generic CallSite, and
        // CallSite<T>), so a planted lookalike from a different assembly declines.
        callSiteAssembly = createDef.Assembly;
        if (string.IsNullOrEmpty(callSiteAssembly))
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
        if (!IsByValueSignature(callee))
            return false;
        var binderParam = callee.ParameterTypes[0];
        if (binderParam.Kind != TypeRefKind.Definition
            || binderParam.Assembly != callSiteAssembly
            || binderParam.Namespace != CompilerServicesNamespace
            || binderParam.Name != "CallSiteBinder")
        {
            return false;
        }

        // T is exactly corelib Func`3<trusted non-generic CallSite, object, object>.
        var t = createType.TypeArguments[0];
        if (!IsCallSiteFunc(t, callSiteAssembly))
            return false;

        if (createCall.Arguments[0] is not Call binder)
            return false;

        binderCall = binder;
        cacheT = t;
        return true;
    }

    static bool IsExactBinder(
        Call binderCall,
        string callSiteAssembly,
        out string memberName,
        out IrExpression contextUse,
        out IrExpression arrayUse,
        out string binderAssembly)
    {
        memberName = null!;
        contextUse = null!;
        arrayUse = null!;
        binderAssembly = null!;

        var callee = binderCall.Callee;
        var decl = callee.DeclaringType;

        // Trusted, exact Microsoft.CSharp.RuntimeBinder.Binder.GetMember, static, non-generic.
        if (callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes)
            return false;
        if (decl.Kind != TypeRefKind.Definition || decl.Namespace != RuntimeBinderNamespace || decl.Name != "Binder")
            return false;

        // The trusted Binder declaring assembly anchors the RuntimeBinder-family
        // signature types below (CSharpBinderFlags, CSharpArgumentInfo,
        // CSharpArgumentInfoFlags).
        binderAssembly = decl.Assembly;
        if (string.IsNullOrEmpty(binderAssembly))
            return false;
        if (callee.Name != "GetMember" || callee.HasThis || !IsNonGeneric(callee))
            return false;

        // Returns exactly trusted CallSiteBinder, from the CallSite assembly.
        if (callee.ReturnType.Kind != TypeRefKind.Definition
            || callee.ReturnType.Assembly != callSiteAssembly
            || callee.ReturnType.Namespace != CompilerServicesNamespace
            || callee.ReturnType.Name != "CallSiteBinder")
        {
            return false;
        }

        // Exactly four value parameters with the exact types.
        if (callee.ParameterTypes.Length != 4 || binderCall.Arguments.Count != 4)
            return false;
        if (!IsByValueSignature(callee))
            return false;

        var p = callee.ParameterTypes;
        if (p[0].Kind != TypeRefKind.Definition
            || p[0].Assembly != binderAssembly
            || p[0].Namespace != RuntimeBinderNamespace
            || p[0].Name != "CSharpBinderFlags")
        {
            return false;
        }
        if (!IsCoreLib(p[1], "System", "String"))
            return false;
        if (!IsCoreLib(p[2], "System", "Type"))
            return false;
        if (!IsArgumentInfoEnumerable(p[3], binderAssembly))
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

    static bool IsExactInfoCreate(Call infoCreate, string binderAssembly)
    {
        var callee = infoCreate.Callee;
        var decl = callee.DeclaringType;

        if (callee.DeclaringTypeIsTrustedPlatform != MetadataFactState.Yes)
            return false;
        if (decl.Kind != TypeRefKind.Definition
            || decl.Assembly != binderAssembly
            || decl.Namespace != RuntimeBinderNamespace
            || decl.Name != "CSharpArgumentInfo")
        {
            return false;
        }
        if (callee.Name != "Create" || callee.HasThis || !IsNonGeneric(callee))
            return false;
        if (!callee.ReturnType.Equals(decl))
            return false;
        if (callee.ParameterTypes.Length != 2 || infoCreate.Arguments.Count != 2)
            return false;
        if (!IsByValueSignature(callee))
            return false;
        if (callee.ParameterTypes[0].Kind != TypeRefKind.Definition
            || callee.ParameterTypes[0].Assembly != binderAssembly
            || callee.ParameterTypes[0].Namespace != RuntimeBinderNamespace
            || callee.ParameterTypes[0].Name != "CSharpArgumentInfoFlags")
        {
            return false;
        }
        if (!IsCoreLib(callee.ParameterTypes[1], "System", "String"))
            return false;

        // flags == 0, name == null.
        if (infoCreate.Arguments[0] is not Constant fConst || fConst.Value is not int f || f != 0)
            return false;
        if (infoCreate.Arguments[1] is not Constant nConst || nConst.Value != null)
            return false;

        return true;
    }

    static bool IsExactInvoke(
        Call invokeCall,
        FieldRef cacheField,
        TypeRef cacheT,
        string callSiteAssembly,
        out IrExpression receiver,
        out LoadField targetInstanceLoad,
        out LoadField cacheArgLoad)
    {
        receiver = null!;
        targetInstanceLoad = null!;
        cacheArgLoad = null!;

        var callee = invokeCall.Callee;

        // The delegate type is exactly the cache's T (corelib Func<CallSite, object, object>).
        if (!callee.DeclaringType.Equals(cacheT) || !IsCallSiteFunc(callee.DeclaringType, callSiteAssembly))
            return false;
        if (callee.Name != "Invoke" || !callee.HasThis || !IsNonGeneric(callee))
            return false;

        // Exact Invoke signature: (CallSite, object) -> object, by value.
        if (!IsCoreLib(callee.ReturnType, "System", "Object"))
            return false;
        if (callee.ParameterTypes.Length != 2 || invokeCall.Arguments.Count != 3)
            return false;
        if (callee.ParameterTypes[0].Kind != TypeRefKind.Definition
            || callee.ParameterTypes[0].Assembly != callSiteAssembly
            || callee.ParameterTypes[0].Namespace != CompilerServicesNamespace
            || callee.ParameterTypes[0].Name != "CallSite")
        {
            return false;
        }
        if (!IsCoreLib(callee.ParameterTypes[1], "System", "Object"))
            return false;
        if (!IsByValueSignature(callee))
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
            || targetDecl.ElementType?.Name != "CallSite`1"
            || targetDecl.ElementType?.Assembly != callSiteAssembly)
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

    static bool IsExactInfoArray(NewArray na, string binderAssembly)
    {
        if (na.ElementType.Kind != TypeRefKind.Definition
            || na.ElementType.Assembly != binderAssembly
            || na.ElementType.Namespace != RuntimeBinderNamespace
            || na.ElementType.Name != "CSharpArgumentInfo")
        {
            return false;
        }
        return na.Length is Constant lenConst && lenConst.Value is int len && len == 1;
    }

    static bool IsCallSiteFunc(TypeRef t, string callSiteAssembly)
    {
        if (t.Kind != TypeRefKind.GenericInstance || t.TypeArguments.Length != 3)
            return false;
        var def = t.ElementType;
        if (def == null || def.Assembly != TypeRef.CoreLibrary || def.Namespace != "System" || def.Name != "Func`3")
            return false;
        var callSite = t.TypeArguments[0];
        if (callSite.Kind != TypeRefKind.Definition
            || callSite.Assembly != callSiteAssembly
            || callSite.Namespace != CompilerServicesNamespace
            || callSite.Name != "CallSite")
        {
            return false;
        }
        return IsCoreLib(t.TypeArguments[1], "System", "Object")
            && IsCoreLib(t.TypeArguments[2], "System", "Object");
    }

    static bool IsArgumentInfoEnumerable(TypeRef type, string binderAssembly)
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
            && arg.Assembly == binderAssembly
            && arg.Namespace == RuntimeBinderNamespace
            && arg.Name == "CSharpArgumentInfo";
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
        // Confinement is proven within this body's scope only: a nested
        // lambda/local function's identically numbered slot or local belongs to a
        // separate pool and must neither veto this candidate nor be treated as an
        // extra definition/use of it.
        foreach (var node in GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function))
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
                // Taking the address of the owned local aliases it out of the
                // proven load set — an escape. (Stack slots are synthetic
                // evaluation-stack values and have no address form.)
                case LoadLocalAddress lla when !key.IsSlot && lla.Index == key.Index:
                    return false;
            }
        }

        return true;
    }

    static bool CacheFieldConfined(IrFunction function, FieldRef cacheField, StoreField definition, IReadOnlyList<LoadField> allowedLoads)
    {
        // Scoped to this body: a nested function referencing a same-shaped cache
        // field runs its own pipeline and must not veto or be consumed here.
        foreach (var node in GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function))
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
                // Taking the address of the cache field aliases it out of the
                // proven load set — an escape.
                case LoadFieldAddress lfa when lfa.Field == cacheField:
                    return false;
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

    // A by-value signature: the metadata positively proved no by-ref parameter
    // facts were required (NotRequired) and carries no ref-kind entries. Anything
    // weaker (Unknown or Known) or any populated ref-kind array declines.
    static bool IsByValueSignature(MethodRef callee)
        => callee.ParameterRefKindsFacts == ParameterRefKindFacts.NotRequired
            && callee.ParameterRefKinds.IsDefaultOrEmpty;

    static bool IsCoreLib(TypeRef type, string ns, string name)
        => type.Kind == TypeRefKind.Definition
            && type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == ns
            && type.Name == name;
}
