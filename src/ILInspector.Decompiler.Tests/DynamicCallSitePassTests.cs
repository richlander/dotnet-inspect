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

    // ---- node finders (polarity-agnostic canonical shape) -----------------
    //
    // csc may emit the lazy-init guard in either polarity:
    //   if (!cache) { setup } else {}       (negated condition, setup in Then)
    //   if (cache)  {}       else { setup }  (bare condition,   setup in Else)
    // These finders select the guard and its real setup arm exactly as the pass
    // does, so every mutation targets the actual setup regardless of the
    // structure a given SDK's compiler produces (rather than hard-coding the
    // negated form and throwing during setup on the other).

    static bool TryGuardLoad(IrExpression condition, out LoadField load, out bool negated)
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

    static bool EndsWithCacheStoreShape(Block block)
        => block.Children.Count > 0
            && block.Children[^1] is StoreField { HasInstance: false };

    /// <summary>The cache lazy-init guard with its load, polarity, and real setup/opposite arms — selected exactly as the pass selects them.</summary>
    static (IfStatement If, LoadField Load, bool Negated, Block Setup, Block? Opposite) CacheGuard(IrFunction f)
    {
        foreach (var ifStmt in f.Descendants.OfType<IfStatement>())
        {
            if (!TryGuardLoad(ifStmt.Condition, out var load, out var negated))
                continue;
            var setup = negated ? ifStmt.Then : ifStmt.Else;
            var opposite = negated ? ifStmt.Else : ifStmt.Then;
            // The setup arm is the null-path arm that ends in the cache store; the
            // opposite arm is empty. Shape (a parameterless field store) is enough
            // to pick the setup arm in either polarity, without requiring the
            // store's field to equal the guard load's — tests may retype the load
            // and store in separate steps.
            if (setup is not null && EndsWithCacheStoreShape(setup))
                return (ifStmt, load, negated, setup, opposite);
        }
        throw new InvalidOperationException("canonical cache guard not found");
    }

    static IfStatement CacheIf(IrFunction f) => CacheGuard(f).If;

    static LoadField GuardLoad(IrFunction f) => CacheGuard(f).Load;

    static Block SetupArm(IrFunction f) => CacheGuard(f).Setup;

    static StoreField CacheStore(IrFunction f) => (StoreField)SetupArm(f).Children[^1];

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
        var thenBlock = SetupArm(f);
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
        var g = CacheGuard(f);

        // Give the opposite (currently empty) arm a real statement, preserving
        // the guard's polarity. Deleting the guard would drop that work, so the
        // pass must decline rather than silently discarding real statements.
        var condition = Detach(g.If.Condition);
        var setup = Detach(g.Setup);
        var extra = new Block();
        extra.Add(new StoreLocal(101, Int32Type, new Constant(0, Int32Type)));
        var rebuilt = g.Negated
            ? new IfStatement(condition, setup, extra)    // if (!cache) { setup } else { extra }
            : new IfStatement(condition, extra, setup);   // if (cache) { extra } else { setup }
        g.If.ReplaceWith(rebuilt);

        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvertedGuardTruthyConditionSetupInThen_Declines()
    {
        // Malformed inverted guard `if (cache) { setup } else {}`: a truthy cache
        // condition selects the then arm when the cache is already populated, so
        // running setup there would re-init a live cache. The pass must decline
        // rather than delete the guard. Built from the real guard load and setup
        // arm regardless of the canonical fixture's polarity.
        var f = LoadCanonicalFunction();
        var g = CacheGuard(f);
        var load = Detach(g.Load);
        var setup = Detach(g.Setup);
        g.If.ReplaceWith(new IfStatement(load, setup, new Block()));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void InvertedGuardNegatedConditionSetupInElse_Declines()
    {
        // Malformed inverted guard `if (!cache) {} else { setup }`: the negated
        // condition selects the then arm when the cache is null, but setup lives
        // in the else arm (reached only when the cache is already populated).
        var f = LoadCanonicalFunction();
        var g = CacheGuard(f);
        var load = Detach(g.Load);
        var setup = Detach(g.Setup);
        g.If.ReplaceWith(new IfStatement(new LogicalNot(load), new Block(), setup));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void CanonicalSetupInElsePolarity_Raises()
    {
        // The well-formed bare-condition polarity `if (cache) {} else { setup }`
        // (setup in the else arm) is the shape some SDKs' csc emit. It must raise
        // exactly like the negated `if (!cache) { setup } else {}` form — the
        // pass and these helpers are polarity-agnostic.
        var f = LoadCanonicalFunction();
        var g = CacheGuard(f);
        var load = Detach(g.Load);
        var setup = Detach(g.Setup);
        g.If.ReplaceWith(new IfStatement(load, new Block(), setup));
        Assert.True(RunPass(f));
    }

    // ======================================================================
    // Canonical setup statement order (blocker #2)
    // ======================================================================

    static void ReorderSetup(IrFunction f, params int[] order)
    {
        var setup = SetupArm(f);
        var original = setup.Children.ToList();
        var reordered = order.Select(i => original[i]).ToList();
        foreach (var c in original)
            c.Detach();
        foreach (var c in reordered)
            setup.Add(c);
    }

    [Fact]
    public void SetupArrayContextSwapped_Declines()
    {
        // Canonical order is [array, context, element, cache]; swap the array
        // and context definitions so statement[0] is no longer the NewArray def.
        var f = LoadCanonicalFunction();
        ReorderSetup(f, 1, 0, 2, 3);
        Assert.False(RunPass(f));
    }

    [Fact]
    public void SetupElementBeforeContext_Declines()
    {
        // Move the element store ahead of the context definition so statement[1]
        // is no longer a context definition.
        var f = LoadCanonicalFunction();
        ReorderSetup(f, 0, 2, 1, 3);
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

    [Fact]
    public void ReceiverIsUnrelatedAddressExpression_Declines()
    {
        var f = LoadCanonicalFunction();
        // A managed-pointer / address expression as the object-typed dynamic-get
        // receiver is unverifiable IL that would render as `((dynamic)ref x).M`.
        // It aliases no owned slot/field, so neither slot nor cache-field
        // confinement fires; only the receiver value-expression gate rejects it.
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadLocalAddress(300, ObjectType));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverIsCoercedAddressExpression_Declines()
    {
        var f = LoadCanonicalFunction();
        InvokeCall(f).Arguments[2].ReplaceWith(
            new Coerce(ObjectType, new LoadLocalAddress(300, ObjectType)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverRefConditionalWithAddressArms_Declines()
    {
        var f = LoadCanonicalFunction();
        InvokeCall(f).Arguments[2].ReplaceWith(new Conditional(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            new LoadLocalAddress(300, ObjectType),
            new LoadLocalAddress(301, ObjectType)));
        // The receiver renders through the same Operand/Expression path as any
        // value (ConditionalText carries no ByRef special case), so both arms
        // render with a leading `ref` — `((dynamic)(cond ? ref l300 : ref l301))`.
        // An `unbox` arm in that position spells `ref (int)o`, which is CS0445
        // ("cannot modify the result of an unboxing conversion"), so the shared
        // predicate must inspect both arms rather than trusting the ref merge.
        // It cannot cheaply tell a legal address arm from an illegal one there,
        // so it conservatively declines every address-arm ref conditional; these
        // shapes are adversarial-IL-only and declining is safe.
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverRefConditionalHidingUnboxArm_Declines()
    {
        var f = LoadCanonicalFunction();
        InvokeCall(f).Arguments[2].ReplaceWith(new Conditional(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            new Unbox(Int32Type, new LoadLocal(300, ObjectType)),
            new Unbox(Int32Type, new LoadLocal(301, ObjectType))));
        // The #2916 bug: the old gate skipped both arms when the conditional was
        // ByRef-typed, so a hidden `unbox` slipped through and raised to
        // `((dynamic)(cond ? ref (int)l300 : ref (int)l301)).Length` — CS0445.
        // The shared predicate now reaches the `Unbox` leaves and declines.
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverSwitchArmIsAddressExpression_Declines()
    {
        var f = LoadCanonicalFunction();
        InvokeCall(f).Arguments[2].ReplaceWith(new SwitchExpression(
            new Constant(0, Int32Type),
            [
                new SwitchExpressionArm(
                    [0],
                    isDefault: false,
                    new Constant(null, ObjectType)),
                new SwitchExpressionArm(
                    [],
                    isDefault: true,
                    new LoadLocalAddress(300, ObjectType)),
            ]));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverSlotDefinedByAddressExpression_Declines()
    {
        var f = LoadCanonicalFunction();
        InsertBeforeGuard(
            f,
            new StoreStackSlot(500, new LoadLocalAddress(300, ObjectType)));
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadStackSlot(500, ObjectType));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverCallWithByRefArgument_StillRaises()
    {
        var f = LoadCanonicalFunction();
        var helperType = TypeRef.Definition("Synthetic", "Tests", "ReceiverHelpers");
        var receiver = new Call(
            new MethodRef(
                helperType,
                "Read",
                ObjectType,
                [TypeRef.ByRef(ObjectType)],
                HasThis: false),
            isVirtual: false,
            [new LoadLocalAddress(300, ObjectType)]);
        InvokeCall(f).Arguments[2].ReplaceWith(receiver);
        Assert.True(RunPass(f));
    }

    [Fact]
    public void ReceiverIsUnmanagedPointerValue_Declines()
    {
        var f = LoadCanonicalFunction();
        // A pointer-valued receiver (`int*`) has no conversion to `dynamic`
        // (CS0030); the receiver gate rejects it on its ResultType even though it
        // is a plain value load that aliases no owned storage.
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadLocal(300, TypeRef.Pointer(Int32Type)));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverIsByRefToPointer_Declines()
    {
        var f = LoadCanonicalFunction();
        // A `ByRef` receiver is implicitly dereferenced, so `ref int*` still
        // yields `int*` — no `dynamic` conversion (CS0030). The gate must look
        // through `ByRef` wrappers to the pointer element (real IL cannot nest
        // `ByRef`, but the peel loop closes the synthetic case as well).
        InvokeCall(f).Arguments[2].ReplaceWith(
            new LoadArgument(0, "p", TypeRef.ByRef(TypeRef.Pointer(Int32Type))));
        Assert.False(RunPass(f));

        var g = LoadCanonicalFunction();
        InvokeCall(g).Arguments[2].ReplaceWith(
            new LoadArgument(0, "p", TypeRef.ByRef(TypeRef.ByRef(TypeRef.Pointer(Int32Type)))));
        Assert.False(RunPass(g));
    }

    [Fact]
    public void ReceiverIsPinnedPointer_Declines()
    {
        var f = LoadCanonicalFunction();
        InvokeCall(f).Arguments[2].ReplaceWith(new LoadLocal(
            300,
            TypeRef.Pinned(TypeRef.ByRef(TypeRef.Pointer(Int32Type)))));
        Assert.False(RunPass(f));
    }

    [Fact]
    public void ReceiverIsPinnedObject_StillRaises()
    {
        var f = LoadCanonicalFunction();
        InvokeCall(f).Arguments[2].ReplaceWith(
            new LoadLocal(300, TypeRef.Pinned(ObjectType)));
        Assert.True(RunPass(f));
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

    // ======================================================================
    // Nested-function body scoping
    // ======================================================================
    //
    // Locals and stack slots are per-body pools, so an identically numbered slot
    // in a nested lambda/local function must not veto the outer candidate. The
    // static dynamic call-site cache field is a single shared identity, so a
    // nested reference to the SAME field must veto (consuming the guard would
    // leave that reference dangling against a field the pass believes confined).

    [Fact]
    public void SameCacheFieldInNestedBody_Declines()
    {
        var f = LoadCanonicalFunction();

        var cacheField = GuardLoad(f).Field;

        // A nested lambda body reads the SAME cache field. Because the cache
        // field is shared identity, this extra load is outside the proven
        // guard/Target/argument set, so the whole-function cache confinement
        // check must veto the raise.
        var nestedCacheLoad = new LoadField(cacheField, null);
        var nestedBlock = new Block();
        nestedBlock.Add(new Return(nestedCacheLoad));
        var container = new BlockContainer();
        container.Add(nestedBlock);
        var lambda = new Lambda(
            ObjectType,
            ImmutableArray<Parameter>.Empty,
            ImmutableArray<TypeRef>.Empty,
            ImmutableArray<string?>.Empty,
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            container);

        InsertBeforeGuard(f, new StoreStackSlot(500, lambda));

        Assert.False(RunPass(f));
    }

    [Fact]
    public void DistinctCacheFieldAndShadowedSlot_RaisesOuterAndLeavesNestedUntouched()
    {
        var f = LoadCanonicalFunction();

        var arraySlot = ArrayDefStore(f).Slot;
        var cacheField = GuardLoad(f).Field;
        var infoElementType = ArrayNode(f).ElementType;

        // A DISTINCT cache field (different name => different identity) plus a
        // nested slot that reuses the outer info-array slot index. The slot pool
        // is per-body, so the shadowed slot must not veto; the distinct field is
        // not the owned cache, so it must not veto either. The outer site raises
        // and the nested body is left untouched.
        var distinctField = new FieldRef(cacheField.DeclaringType, cacheField.Name + "__other", cacheField.Type)
        {
            DeclaringTypeCompilerGenerated = cacheField.DeclaringTypeCompilerGenerated,
        };
        var nestedArrayStore = new StoreStackSlot(arraySlot, new NewArray(infoElementType, new Constant(1, Int32Type)));
        var nestedCacheLoad = new LoadField(distinctField, null);
        var nestedBlock = new Block();
        nestedBlock.Add(nestedArrayStore);
        nestedBlock.Add(new Return(nestedCacheLoad));
        var container = new BlockContainer();
        container.Add(nestedBlock);
        var lambda = new Lambda(
            ObjectType,
            ImmutableArray<Parameter>.Empty,
            ImmutableArray<TypeRef>.Empty,
            ImmutableArray<string?>.Empty,
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            container);

        InsertBeforeGuard(f, new StoreStackSlot(500, lambda));

        Assert.True(RunPass(f));
        Assert.Single(f.Descendants.OfType<DynamicGetMember>());

        // The nested body is untouched: its shadowing definitions survive and no
        // raise happened inside it.
        Assert.Contains(nestedArrayStore, lambda.Descendants);
        Assert.Contains(nestedCacheLoad, lambda.Descendants);
        Assert.Empty(lambda.Descendants.OfType<DynamicGetMember>());
    }

    /// <summary>Inserts <paramref name="statement"/> immediately before the cache guard, inside the guard's block.</summary>
    static void InsertBeforeGuard(IrFunction f, IrNode statement)
    {
        var guardIf = CacheIf(f);
        var outerBlock = (Block)guardIf.Parent!;
        var ordered = outerBlock.Children.ToList();
        int guardIndex = ordered.IndexOf(guardIf);
        ordered.Insert(guardIndex, statement);
        foreach (var c in outerBlock.Children.ToList())
            c.Detach();
        foreach (var c in ordered)
            outerBlock.Add(c);
    }

    // ======================================================================
    // Immediate-use forms beyond direct return + nested-context boundary
    // ======================================================================

    /// <summary>Imports a method of the compiler-backed member-context fixtures and runs the full raise pipeline (with the cross-method import seam).</summary>
    static string RaiseMemberContext(string method)
    {
        var path = typeof(LadderRung9.DynamicMemberContexts).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, "LadderRung9.DynamicMemberContexts", method, 0, false);
        var result = CSharpPrinter.PrintRaised(function!, mr => IrImporter.Import(source, mr));
        return result.Output ?? string.Empty;
    }

    [Fact]
    public void ImmediateUse_FieldAssignment_Raises()
    {
        // Compiler-backed: `_last = value.Length;` — the dynamic access is the
        // value of a field store, not a return.
        var output = RaiseMemberContext("AssignToField");
        Assert.Contains("_last = ((dynamic)value).Length;", output);
        Assert.DoesNotContain("Binder.GetMember", output);
    }

    [Fact]
    public void ImmediateUse_CallArgument_Raises()
    {
        // Compiler-backed: `return Identity(value.Length);` — the GetMember
        // access is a call argument (nested inside an unrelated InvokeMember
        // site that legitimately stays explicit).
        var output = RaiseMemberContext("UseAsArgument");
        Assert.Contains("((dynamic)value).Length", output);
        Assert.DoesNotContain("Binder.GetMember", output);
    }

    [Fact]
    public void ImmediateUse_LocalInitializer_Raises()
    {
        // Compiler-backed: `object length = value.Length;` used twice.
        var output = RaiseMemberContext("AssignToLocal");
        Assert.Contains("((dynamic)value).Length", output);
        Assert.DoesNotContain("Binder.GetMember", output);
    }

    [Fact]
    public void NestedContext_LocalFunction_Raises()
    {
        // A local function is declared on the authored enclosing type, so the
        // GetMember context typeof matches the body's declaring type and the
        // nested site raises.
        var output = RaiseMemberContext("InLocalFunction");
        Assert.Contains("((dynamic)value).Length", output);
    }

    [Fact]
    public void NestedContext_LambdaDisplayClass_RemainsPartial()
    {
        // Boundary (tracked): csc lowers the lambda body into a display-class
        // method whose declaring type is the generated environment, while the
        // GetMember context typeof is the authored enclosing type. No typed
        // enclosing-authored-type fact exists to bridge them without parsing the
        // display-class name (prohibited), so the exact context check declines
        // and the site honestly stays explicit rather than raising via a fuzzy
        // heuristic.
        var path = typeof(LadderRung9.DynamicMemberContexts).Assembly.Location;
        using var source = MetadataSource.Open(path);
        string? displayClassOutput = null;
        foreach (var (_, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (methodName.Contains("InLambda") && methodName.Contains("b__"))
            {
                displayClassOutput = CSharpPrinter.PrintRaised(function, mr => IrImporter.Import(source, mr)).Output;
                break;
            }
        }
        Assert.NotNull(displayClassOutput);
        Assert.Contains("Binder.GetMember", displayClassOutput);
        Assert.DoesNotContain("(dynamic)", displayClassOutput);
    }

    [Fact]
    public void ImmediateUse_InterveningStatement_Declines()
    {
        // A statement between the guard and the use means the invoke is no longer
        // the immediately-following statement, so the cache is not consumed and
        // the pass declines.
        var f = LoadCanonicalFunction();
        var guardIf = CacheIf(f);
        var outerBlock = (Block)guardIf.Parent!;
        var ordered = outerBlock.Children.ToList();
        int guardIndex = ordered.IndexOf(guardIf);
        ordered.Insert(guardIndex + 1, new StoreLocal(300, Int32Type, new Constant(0, Int32Type)));
        foreach (var c in outerBlock.Children.ToList())
            c.Detach();
        foreach (var c in ordered)
            outerBlock.Add(c);

        Assert.False(RunPass(f));
    }

    [Fact]
    public void ImmediateUse_MultipleInvokes_Declines()
    {
        // Two exact invokes of the same cache in the use statement make the use
        // ambiguous; the pass declines rather than picking one arbitrarily.
        var f = LoadCanonicalFunction();
        var invoke = InvokeCall(f);
        var callee = invoke.Callee;
        var targetFieldRef = ((LoadField)invoke.Arguments[0]).Field;
        var cacheField = GuardLoad(f).Field;

        Call MakeInvoke(IrExpression recv) => new Call(callee, invoke.IsVirtual, new List<IrExpression>
        {
            new LoadField(targetFieldRef, new LoadField(cacheField, null)),
            new LoadField(cacheField, null),
            recv,
        });

        var receiver = Detach(invoke.Arguments[2]);
        invoke.ReplaceWith(MakeInvoke(MakeInvoke(receiver)));

        Assert.False(RunPass(f));
    }
}
