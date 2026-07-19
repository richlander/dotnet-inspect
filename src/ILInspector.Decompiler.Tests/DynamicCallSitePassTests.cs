using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gate-isolating tests for <see cref="DynamicCallSitePass"/>. Each negative
/// starts from the exact compiler-emitted canonical IR of a real
/// <c>((dynamic)x).Member</c> call site, applies a single mutation that
/// preserves every earlier gate, and proves the pass declines only because of
/// its intended discriminator (no <see cref="DynamicGetMember"/> is produced and
/// the honest call-site scaffolding is kept). Malformed-metadata mutations
/// additionally prove the pass declines without throwing. The
/// <see cref="IrNode.CheckInvariant"/> assertions bracket every pass run.
/// </summary>
public class DynamicCallSitePassTests
{
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");

    // ---- fixture load + pass driver ---------------------------------------

    static IrFunction LoadCanonicalFunction(string method = "DynamicGetLength")
    {
        var path = typeof(LadderRung9.DynamicAndExpressionTrees).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, "LadderRung9.DynamicAndExpressionTrees", method, 0, false);

        var context = new PassContext(new Stepper(enabled: false));
        foreach (var pass in IrPasses.Default)
        {
            if (pass.Name == "dynamic-callsite")
                break;
            pass.Run(function!, context);
        }
        return function!;
    }

    static IrFunction LoadLookalike(string method)
    {
        var path = typeof(LadderRung9.DynamicLookalikes).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, "LadderRung9.DynamicLookalikes", method, 0, false);

        var context = new PassContext(new Stepper(enabled: false));
        foreach (var pass in IrPasses.Default)
        {
            if (pass.Name == "dynamic-callsite")
                break;
            pass.Run(function!, context);
        }
        return function!;
    }

    /// <summary>Runs only the dynamic-callsite pass, asserting the tree invariant before and after, and reports whether any member-get was raised.</summary>
    static bool RunPass(IrFunction function)
    {
        function.CheckInvariant();
        var pass = new DynamicCallSitePass();
        var context = new PassContext(new Stepper(enabled: false));
        pass.Run(function, context);
        function.CheckInvariant();
        return function.Descendants.OfType<DynamicGetMember>().Any();
    }

    static string RaiseAndPrint(IrFunction function)
    {
        RunPass(function);
        return CSharpPrinter.Print(function).Output ?? string.Empty;
    }

    // ---- node finders (canonical shape) -----------------------------------

    static IfStatement CacheIf(IrFunction f)
        => f.Descendants.OfType<IfStatement>()
            .Single(s => s.Condition is LogicalNot { Operand: LoadField { Instance: null } });

    static LoadField GuardLoad(IrFunction f)
        => (LoadField)((LogicalNot)CacheIf(f).Condition).Operand;

    static Block ThenBlock(IrFunction f) => CacheIf(f).Then;

    static StoreField CacheStore(IrFunction f) => (StoreField)ThenBlock(f).Children[^1];

    static Call CreateCall(IrFunction f)
        => f.Descendants.OfType<Call>()
            .Single(c => c.Callee.Name == "Create" && c.Callee.DeclaringType.ElementType?.Name == "CallSite`1");

    static Call BinderCall(IrFunction f)
        => f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");

    static Call InfoCall(IrFunction f)
        => f.Descendants.OfType<Call>()
            .Single(c => c.Callee.DeclaringType.Name == "CSharpArgumentInfo" && c.Callee.Name == "Create");

    static Call InvokeCall(IrFunction f)
        => f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "Invoke");

    static NewArray ArrayNode(IrFunction f) => f.Descendants.OfType<NewArray>().Single();

    static StoreElement ElemStore(IrFunction f) => f.Descendants.OfType<StoreElement>().Single();

    static StoreStackSlot ArrayDefStore(IrFunction f)
        => f.Descendants.OfType<StoreStackSlot>().Single(s => s.Value is NewArray);

    static StoreStackSlot ContextDefStore(IrFunction f)
        => f.Descendants.OfType<StoreStackSlot>().Single(s => s.Value is TypeOf or LoadToken);

    static LoadField TargetLoad(IrFunction f) => (LoadField)InvokeCall(f).Arguments[0];

    // ---- mutation helpers -------------------------------------------------

    static T Detach<T>(T node) where T : IrNode
    {
        node.Detach();
        return node;
    }

    /// <summary>Swaps a call's callee, preserving arguments and virtualness.</summary>
    static Call ReplaceCallee(Call call, MethodRef callee)
    {
        var args = call.Arguments.ToList();
        foreach (var arg in args)
            arg.Detach();
        var replacement = new Call(callee, call.IsVirtual, args) { ConstrainedTo = call.ConstrainedTo };
        call.ReplaceWith(replacement);
        return replacement;
    }

    /// <summary>Rebuilds a call dropping its last argument (a malformed arg count).</summary>
    static Call DropLastArgument(Call call)
    {
        var args = call.Arguments.ToList();
        foreach (var arg in args)
            arg.Detach();
        args.RemoveAt(args.Count - 1);
        var replacement = new Call(call.Callee, call.IsVirtual, args) { ConstrainedTo = call.ConstrainedTo };
        call.ReplaceWith(replacement);
        return replacement;
    }

    static ImmutableArray<TypeRef> WithParam(ImmutableArray<TypeRef> parameters, int index, TypeRef type)
    {
        var array = parameters.ToArray();
        array[index] = type;
        return ImmutableArray.Create(array);
    }

    // ======================================================================
    // Positives
    // ======================================================================

    [Fact]
    public void CanonicalPositive_RaisesAndInvariantHolds()
    {
        var f = LoadCanonicalFunction();
        f.CheckInvariant();
        Assert.True(RunPass(f));
        Assert.Single(f.Descendants.OfType<DynamicGetMember>());
    }

    [Fact]
    public void CanonicalPositive_PrintsDynamicMemberAccess()
    {
        var f = LoadCanonicalFunction();
        var output = RaiseAndPrint(f);
        Assert.Contains("((dynamic)value).Length", output);
    }

    [Fact]
    public void KeywordMemberName_RaisesAndKeepsRawName()
    {
        var f = LoadCanonicalFunction();
        BinderCall(f).Arguments[1].ReplaceWith(new Constant("class", StringType));
        Assert.True(RunPass(f));
        var raised = f.Descendants.OfType<DynamicGetMember>().Single();
        Assert.Equal("class", raised.PropertyName);
    }

    [Fact]
    public void KeywordMemberName_PrinterEscapesToValidSyntax()
    {
        var f = LoadCanonicalFunction();
        BinderCall(f).Arguments[1].ReplaceWith(new Constant("class", StringType));
        var output = RaiseAndPrint(f);
        Assert.Contains("((dynamic)value).@class", output);
    }

    [Fact]
    public void UnspellableMemberName_Declines()
    {
        var f = LoadCanonicalFunction();
        BinderCall(f).Arguments[1].ReplaceWith(new Constant("1-not-an-identifier", StringType));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Ownership (first gate)
    // ======================================================================

    [Fact]
    public void ManualCacheLookalike_DeclinesAtOwnership()
    {
        var f = LoadLookalike("ManualCache");
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CacheNotCompilerGenerated_DeclinesAtOwnership()
    {
        var f = LoadCanonicalFunction();
        var guard = GuardLoad(f);
        var mutated = guard.Field with { DeclaringTypeCompilerGenerated = MetadataFactState.Unknown };
        guard.ReplaceWith(new LoadField(mutated, null));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // CallSite<T>.Create identity
    // ======================================================================

    [Fact]
    public void CreateMissingPlatformTrust_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { DeclaringTypeIsTrustedPlatform = MetadataFactState.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateWrongName_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { Name = "NotCreate" });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateHasThis_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { HasThis = true });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateWrongReturnType_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { ReturnType = ObjectType });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateWrongParameterType_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { ParameterTypes = WithParam(create.Callee.ParameterTypes, 0, ObjectType) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateRefKindFactsUnknown_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { ParameterRefKindsFacts = ParameterRefKindFacts.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateByRefParameter_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with
        {
            ParameterRefKinds = ImmutableArray.Create(ArgumentRefKind.Ref),
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateMalformedZeroArityTypeArgs_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        var malformed = TypeRef.GenericInstance(create.Callee.DeclaringType.ElementType!, ImmutableArray<TypeRef>.Empty);
        ReplaceCallee(create, create.Callee with { DeclaringType = malformed });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateMalformedArgumentCount_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        DropLastArgument(CreateCall(f));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateWrongDelegateT_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        var t = create.Callee.DeclaringType.TypeArguments[0];
        var badT = TypeRef.GenericInstance(t.ElementType!, ImmutableArray.Create(t.TypeArguments[0], ObjectType, Int32Type));
        var badCacheType = TypeRef.GenericInstance(create.Callee.DeclaringType.ElementType!, ImmutableArray.Create(badT));

        // Retype the cache field consistently so the earlier field/return gates
        // still pass and the decline is attributable to T alone.
        var guard = GuardLoad(f);
        var newField = guard.Field with { Type = badCacheType };
        guard.ReplaceWith(new LoadField(newField, null));

        var cacheStore = CacheStore(f);
        var value = Detach(cacheStore.Value);
        cacheStore.ReplaceWith(new StoreField(newField, null, value));

        create = CreateCall(f);
        ReplaceCallee(create, create.Callee with { DeclaringType = badCacheType, ReturnType = badCacheType });

        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Binder.GetMember identity
    // ======================================================================

    [Fact]
    public void BinderWrongName_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { Name = "NotGetMember" });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderMissingPlatformTrust_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { DeclaringTypeIsTrustedPlatform = MetadataFactState.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderHasThis_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { HasThis = true });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderWrongReturnType_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { ReturnType = ObjectType });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderWrongFirstParameter_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { ParameterTypes = WithParam(binder.Callee.ParameterTypes, 0, Int32Type) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderWrongArgumentInfoEnumerable_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { ParameterTypes = WithParam(binder.Callee.ParameterTypes, 3, ObjectType) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderRefKindFactsUnknown_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with { ParameterRefKindsFacts = ParameterRefKindFacts.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderNonZeroFlags_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        binder.Arguments[0].ReplaceWith(new Constant(1, Int32Type));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderMalformedParameterCount_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        var shortParams = ImmutableArray.Create(binder.Callee.ParameterTypes.Take(3).ToArray());
        ReplaceCallee(binder, binder.Callee with { ParameterTypes = shortParams });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderMalformedArgumentCount_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        DropLastArgument(BinderCall(f));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderMalformedArgInfoZeroArity_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        var enumerable = binder.Callee.ParameterTypes[3];
        var malformed = TypeRef.GenericInstance(enumerable.ElementType!, ImmutableArray<TypeRef>.Empty);
        ReplaceCallee(binder, binder.Callee with { ParameterTypes = WithParam(binder.Callee.ParameterTypes, 3, malformed) });
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // CSharpArgumentInfo.Create identity
    // ======================================================================

    [Fact]
    public void InfoMissingPlatformTrust_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        ReplaceCallee(info, info.Callee with { DeclaringTypeIsTrustedPlatform = MetadataFactState.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoWrongName_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        ReplaceCallee(info, info.Callee with { Name = "NotCreate" });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoWrongReturnType_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        ReplaceCallee(info, info.Callee with { ReturnType = ObjectType });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoWrongFirstParameter_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        ReplaceCallee(info, info.Callee with { ParameterTypes = WithParam(info.Callee.ParameterTypes, 0, Int32Type) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoWrongSecondParameter_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        ReplaceCallee(info, info.Callee with { ParameterTypes = WithParam(info.Callee.ParameterTypes, 1, ObjectType) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoRefKindFactsUnknown_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        ReplaceCallee(info, info.Callee with { ParameterRefKindsFacts = ParameterRefKindFacts.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoNonZeroFlags_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        info.Arguments[0].ReplaceWith(new Constant(1, Int32Type));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoNonNullName_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        info.Arguments[1].ReplaceWith(new Constant("named", StringType));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoMalformedArgumentCount_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        DropLastArgument(InfoCall(f));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Info array + element store
    // ======================================================================

    [Fact]
    public void ArrayLengthNotOne_Declines()
    {
        var f = LoadCanonicalFunction();
        ArrayNode(f).Length.ReplaceWith(new Constant(2, Int32Type));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ArrayNonConstantLength_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        ArrayNode(f).Length.ReplaceWith(new LoadStackSlot(999, Int32Type));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ArrayWrongElementType_Declines()
    {
        var f = LoadCanonicalFunction();
        var na = ArrayNode(f);
        na.ReplaceWith(new NewArray(ObjectType, Detach(na.Length)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ElementStoreIndexNotZero_Declines()
    {
        var f = LoadCanonicalFunction();
        ElemStore(f).Index.ReplaceWith(new Constant(1, Int32Type));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ElementStoreDeclaredTypeDisagrees_Declines()
    {
        var f = LoadCanonicalFunction();
        var se = ElemStore(f);
        var array = se.Array;
        var index = se.Index;
        var value = se.Value;
        se.ReplaceWith(new StoreElement(ObjectType, Detach(array), Detach(index), Detach(value)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ElementStoreArrayAliasesOtherSlot_Declines()
    {
        var f = LoadCanonicalFunction();
        ElemStore(f).Array.ReplaceWith(new LoadStackSlot(999, null));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ArraySlotEscapesViaExtraLoad_Declines()
    {
        var f = LoadCanonicalFunction();
        var arraySlot = ArrayDefStore(f).Slot;
        // The delegate receiver argument becomes an extra, disallowed load of the
        // info-array slot, so the array-slot confinement check must reject it.
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadStackSlot(arraySlot, null));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Context definition
    // ======================================================================

    [Fact]
    public void WrongContextType_Declines()
    {
        var f = LoadCanonicalFunction();
        var context = ContextDefStore(f);
        context.Value.ReplaceWith(new TypeOf(ObjectType));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ContextSlotEscapesViaExtraLoad_Declines()
    {
        var f = LoadCanonicalFunction();
        var contextSlot = ContextDefStore(f).Slot;
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadStackSlot(contextSlot, null));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Setup ledger multiplicity
    // ======================================================================

    [Fact]
    public void DuplicateArrayDefinition_Declines()
    {
        var f = LoadCanonicalFunction();
        var arrayStore = ArrayDefStore(f);
        var contextStore = ContextDefStore(f);
        // Replace the context definition with a second same-slot array definition
        // (same value shape). Two definitions of one slot must be rejected.
        var clonedArray = new NewArray(ArrayNode(f).ElementType, new Constant(1, Int32Type));
        contextStore.ReplaceWith(new StoreStackSlot(arrayStore.Slot, clonedArray));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void DuplicateContextDefinition_Declines()
    {
        var f = LoadCanonicalFunction();
        var arrayStore = ArrayDefStore(f);
        var contextStore = ContextDefStore(f);
        var contextType = ((TypeOf)contextStore.Value).Type;
        // Replace the array definition with a second same-slot context definition.
        arrayStore.ReplaceWith(new StoreStackSlot(contextStore.Slot, new TypeOf(contextType)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ExtraSetupStatement_Declines()
    {
        var f = LoadCanonicalFunction();
        var thenBlock = ThenBlock(f);
        var dummy = new StoreLocal(100, Int32Type, new Constant(1, Int32Type));

        var ordered = thenBlock.Children.ToList();
        ordered.Insert(ordered.Count - 1, dummy);
        foreach (var c in thenBlock.Children.ToList())
            c.Detach();
        foreach (var c in ordered)
            thenBlock.Add(c);
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Cache store
    // ======================================================================

    [Fact]
    public void CacheStoreFieldMismatch_Declines()
    {
        var f = LoadCanonicalFunction();
        var cacheStore = CacheStore(f);
        var otherField = cacheStore.Field with { Name = "<>p__other" };
        cacheStore.ReplaceWith(new StoreField(otherField, null, Detach(cacheStore.Value)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CacheStoreHasInstance_Declines()
    {
        var f = LoadCanonicalFunction();
        var cacheStore = CacheStore(f);
        cacheStore.ReplaceWith(new StoreField(cacheStore.Field, new LoadStackSlot(998, null), Detach(cacheStore.Value)));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Delegate Invoke
    // ======================================================================

    [Fact]
    public void InvokeWrongName_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        ReplaceCallee(invoke, invoke.Callee with { Name = "NotInvoke" });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeNotHasThis_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        ReplaceCallee(invoke, invoke.Callee with { HasThis = false });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeWrongReturnType_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        ReplaceCallee(invoke, invoke.Callee with { ReturnType = Int32Type });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeWrongFirstParameter_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        ReplaceCallee(invoke, invoke.Callee with { ParameterTypes = WithParam(invoke.Callee.ParameterTypes, 0, ObjectType) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeRefKindFactsUnknown_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        ReplaceCallee(invoke, invoke.Callee with { ParameterRefKindsFacts = ParameterRefKindFacts.Unknown });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeDelegateTypeMismatchesCacheT_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        var t = invoke.Callee.DeclaringType;
        var badT = TypeRef.GenericInstance(t.ElementType!, ImmutableArray.Create(t.TypeArguments[0], ObjectType, Int32Type));
        ReplaceCallee(invoke, invoke.Callee with { DeclaringType = badT });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeMalformedArgumentCount_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        DropLastArgument(InvokeCall(f));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeMalformedParameterCount_DeclinesWithoutThrow()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        var shortParams = ImmutableArray.Create(invoke.Callee.ParameterTypes.Take(1).ToArray());
        ReplaceCallee(invoke, invoke.Callee with { ParameterTypes = shortParams });
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Delegate Target field
    // ======================================================================

    [Fact]
    public void TargetFieldWrongName_Declines()
    {
        var f = LoadCanonicalFunction();
        var target = TargetLoad(f);
        var mutated = target.Field with { Name = "NotTarget" };
        target.ReplaceWith(new LoadField(mutated, Detach(target.Instance!)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void TargetFieldWrongType_Declines()
    {
        var f = LoadCanonicalFunction();
        var target = TargetLoad(f);
        var mutated = target.Field with { Type = ObjectType };
        target.ReplaceWith(new LoadField(mutated, Detach(target.Instance!)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void TargetFieldWrongDeclaringType_Declines()
    {
        var f = LoadCanonicalFunction();
        var target = TargetLoad(f);
        var mutated = target.Field with { DeclaringType = ObjectType };
        target.ReplaceWith(new LoadField(mutated, Detach(target.Instance!)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void TargetReceiverNotCacheField_Declines()
    {
        var f = LoadCanonicalFunction();
        var target = TargetLoad(f);
        var otherField = ((LoadField)target.Instance!).Field with { Name = "<>p__other" };
        Detach(target.Instance!);
        target.ReplaceWith(new LoadField(target.Field, new LoadField(otherField, null)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeCacheArgumentNotCacheField_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        var cacheArg = (LoadField)invoke.Arguments[1];
        var otherField = cacheArg.Field with { Name = "<>p__other" };
        cacheArg.ReplaceWith(new LoadField(otherField, null));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Lazy-init guard opposite arm (blocker #1)
    // ======================================================================

    [Fact]
    public void OppositeArmNotEmpty_Declines()
    {
        var f = LoadCanonicalFunction();
        var oldIf = CacheIf(f);

        // Rebuild the guard as `if (!cache) { setup } else { <extra work> }`.
        // Deleting the guard would drop the non-empty opposite arm, so the pass
        // must decline rather than silently discarding real statements.
        var condition = oldIf.Condition;
        var setupArm = oldIf.Then;
        condition.Detach();
        setupArm.Detach();

        var elseArm = new Block();
        elseArm.Add(new StoreLocal(101, Int32Type, new Constant(0, Int32Type)));
        oldIf.ReplaceWith(new IfStatement(condition, setupArm, elseArm));

        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Canonical setup statement order (blocker #2)
    // ======================================================================

    static void ReorderThen(IrFunction f, params int[] order)
    {
        var then = ThenBlock(f);
        var original = then.Children.ToList();
        var reordered = order.Select(i => original[i]).ToList();
        foreach (var c in original)
            c.Detach();
        foreach (var c in reordered)
            then.Add(c);
    }

    [Fact]
    public void SetupArrayContextSwapped_Declines()
    {
        // Canonical order is [array, context, element, cache]; swap the array
        // and context definitions so statement[0] is no longer the NewArray def.
        var f = LoadCanonicalFunction();
        ReorderThen(f, 1, 0, 2, 3);
        Assert.False(RunPass(f));
    }

    [Fact]
    public void SetupElementBeforeContext_Declines()
    {
        // Move the element store ahead of the context definition so statement[1]
        // is no longer a context definition.
        var f = LoadCanonicalFunction();
        ReorderThen(f, 0, 2, 1, 3);
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Address-of escapes (blocker #3)
    // ======================================================================

    [Fact]
    public void CacheFieldAddressTaken_Declines()
    {
        var f = LoadCanonicalFunction();
        var cacheField = GuardLoad(f).Field;
        // An extra by-ref use of the cache field (its address) aliases it out of
        // the proven load set, so the cache-field confinement check rejects it.
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadFieldAddress(cacheField, null));
        Assert.False(RunPass(f));
    }

    /// <summary>
    /// Rewrites the context definition (and its single binder use) from a stack
    /// slot to a local, exercising the pass's local-storage acceptance so the
    /// local-address escape guard can be isolated.
    /// </summary>
    static (int Index, TypeRef Type) ConvertContextToLocal(IrFunction f)
    {
        var store = ContextDefStore(f);
        var typeType = TypeRef.CoreLib("System", "Type");
        const int localIndex = 200;

        var typeOf = Detach(store.Value);
        store.ReplaceWith(new StoreLocal(localIndex, typeType, typeOf));

        // The binder's source-context argument is the single load of that slot.
        BinderCall(f).Arguments[2].ReplaceWith(new LoadLocal(localIndex, typeType));
        return (localIndex, typeType);
    }

    [Fact]
    public void ContextStoredInLocal_StillRaises()
    {
        var f = LoadCanonicalFunction();
        ConvertContextToLocal(f);
        Assert.True(RunPass(f));
    }

    [Fact]
    public void ContextLocalAddressTaken_Declines()
    {
        var f = LoadCanonicalFunction();
        var (index, type) = ConvertContextToLocal(f);
        // Take the address of the owned context local — an escape the local-slot
        // confinement check must reject.
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadLocalAddress(index, type));
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // By-value signature ref-kind facts (blocker #4)
    // ======================================================================

    [Fact]
    public void CreateRefKindFactsKnownEmpty_Declines()
    {
        // Known (rather than the canonical NotRequired) is not positive evidence
        // of a by-value signature even with no ref-kind entries.
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with
        {
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
            ParameterRefKinds = ImmutableArray<ArgumentRefKind>.Empty,
        });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CreateRefKindsNonEmptyAllValue_Declines()
    {
        // A populated ref-kind array declines even when every entry is Value.
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        ReplaceCallee(create, create.Callee with
        {
            ParameterRefKindsFacts = ParameterRefKindFacts.NotRequired,
            ParameterRefKinds = ImmutableArray.Create(ArgumentRefKind.Value),
        });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderRefKindsNonEmptyAllValue_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        ReplaceCallee(binder, binder.Callee with
        {
            ParameterRefKindsFacts = ParameterRefKindFacts.NotRequired,
            ParameterRefKinds = ImmutableArray.Create(
                ArgumentRefKind.Value, ArgumentRefKind.Value, ArgumentRefKind.Value, ArgumentRefKind.Value),
        });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeRefKindFactsKnownEmpty_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        ReplaceCallee(invoke, invoke.Callee with
        {
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
            ParameterRefKinds = ImmutableArray<ArgumentRefKind>.Empty,
        });
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Exact signature-type assemblies (blocker #5)
    // ======================================================================

    static TypeRef WithAssembly(TypeRef definition, string assembly)
        => TypeRef.Definition(assembly, definition.Namespace, definition.Name);

    [Fact]
    public void CreateCallSiteBinderParameterWrongAssembly_Declines()
    {
        var f = LoadCanonicalFunction();
        var create = CreateCall(f);
        var badBinder = WithAssembly(create.Callee.ParameterTypes[0], "Forged.Assembly");
        ReplaceCallee(create, create.Callee with { ParameterTypes = WithParam(create.Callee.ParameterTypes, 0, badBinder) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void BinderFlagsParameterWrongAssembly_Declines()
    {
        var f = LoadCanonicalFunction();
        var binder = BinderCall(f);
        var badFlags = WithAssembly(binder.Callee.ParameterTypes[0], "Forged.Assembly");
        ReplaceCallee(binder, binder.Callee with { ParameterTypes = WithParam(binder.Callee.ParameterTypes, 0, badFlags) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InfoFlagsParameterWrongAssembly_Declines()
    {
        var f = LoadCanonicalFunction();
        var info = InfoCall(f);
        var badFlags = WithAssembly(info.Callee.ParameterTypes[0], "Forged.Assembly");
        ReplaceCallee(info, info.Callee with { ParameterTypes = WithParam(info.Callee.ParameterTypes, 0, badFlags) });
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvokeCallSiteParameterWrongAssembly_Declines()
    {
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        var badCallSite = WithAssembly(invoke.Callee.ParameterTypes[0], "Forged.Assembly");
        ReplaceCallee(invoke, invoke.Callee with { ParameterTypes = WithParam(invoke.Callee.ParameterTypes, 0, badCallSite) });
        Assert.False(RunPass(f));
    }

    // ======================================================================
    // Context definition token shape (blocker #6)
    // ======================================================================

    [Fact]
    public void ContextLoadTokenInsteadOfTypeOf_Declines()
    {
        // The canonical context definition is typeof(DeclaringType) (a TypeOf).
        // A ldtoken-shaped context is not the compiler's canonical form.
        var f = LoadCanonicalFunction();
        var context = ContextDefStore(f);
        var type = ((TypeOf)context.Value).Type;
        context.Value.ReplaceWith(new LoadToken(RuntimeTokenKind.Type, type, type.ToDisplayString()));
        Assert.False(RunPass(f));
    }
}
