using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

public class RaisingPassTests
{
    static readonly TypeRef[] Dragon4StableParameters =
    [
        TypeRef.CoreLib("System", "UInt64"),
        TypeRef.CoreLib("System", "Int32"),
        TypeRef.CoreLib("System", "UInt32"),
        TypeRef.CoreLib("System", "Boolean"),
        TypeRef.CoreLib("System", "Int32"),
        TypeRef.CoreLib("System", "Boolean"),
        TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Span`1"),
            [TypeRef.CoreLib("System", "Byte")]),
        TypeRef.ByRef(TypeRef.CoreLib("System", "Int32")),
    ];

    static MethodDefinitionHandle ResolveMethodBySignature(
        MetadataSource source,
        string typeFullName,
        string methodName,
        Func<OverloadInfo, bool> matchesSignature)
    {
        var overloads = IrImporter.Overloads(source, typeFullName, methodName);
        var matches = overloads.Where(matchesSignature).ToArray();
        Assert.True(
            matches.Length == 1,
            $"Expected exactly one {typeFullName}::{methodName} signature match, " +
            $"found {matches.Length}. Candidates: {string.Join(
                "; ",
                overloads.Select(candidate =>
                    $"{candidate.Index}: {candidate.ReturnType.ToDisplayString()} " +
                    $"{methodName}{candidate.Describe()}"))}");
        var overload = matches[0];
        var handle = IrImporter.ResolveMethodHandle(
            source.Reader,
            typeFullName,
            methodName,
            overload.Index);
        Assert.True(handle.HasValue);
        return handle.Value;
    }

    static MethodDefinitionHandle ResolveDragon4Method(
        MetadataSource source,
        string typeFullName)
    {
        var optionalExactnessParameter =
            TypeRef.ByRef(TypeRef.CoreLib("System", "Boolean"));
        return ResolveMethodBySignature(
            source,
            typeFullName,
            "Dragon4",
            candidate =>
                candidate.ReturnType.Equals(TypeRef.CoreLib("System", "UInt32"))
                && !candidate.HasThis
                && candidate.HasBody
                && candidate.IsPrivate
                && candidate.ParameterTypes.Length is 8 or 9
                && candidate.ParameterTypes.Take(Dragon4StableParameters.Length)
                    .SequenceEqual(Dragon4StableParameters)
                && (candidate.ParameterTypes.Length == Dragon4StableParameters.Length
                    || candidate.ParameterTypes[^1]
                        .Equals(optionalExactnessParameter)));
    }

    static void AssertRefLocalBodyCompiles(string body)
    {
        string source = $$"""
            using System;

            public static class T
            {
                static int s_value;
                public static ref int A() => ref s_value;
                public static ref int A(ref int value) => ref s_value;
                public static void Use(ref int value) { }
                public static void M()
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = RoslynTestReferences.TrustedPlatform.AsEnumerable();
        var compilation = CSharpCompilation.Create(
            "RefLocalCanary",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        EmitResult result = compilation.Emit(Stream.Null);
        Assert.True(result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            + Environment.NewLine + source);
    }

    static string PrintWithPasses(string typeName, string methodName, MetadataSource source)
    {
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n").TrimEnd();
    }

    static IrFunction RunThroughStructConstructor(string methodName, MetadataSource source)
    {
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        foreach (var pass in IrPasses.Default)
        {
            pass.Run(function!, PassContext.None);
            if (pass is StructConstructorPass)
                break;
        }
        return function!;
    }

    [Fact]
    public void StructConstructor_InPlaceCtor_PrintsNewObject()
    {
        // The in-place struct .ctor (ldloca; call S::.ctor) must print as an
        // assignment of a fresh value, never the illegal handler..ctor(...). The
        // type is apparent on the declaration, so the creation renders target-typed.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var result = CSharpPrinter.Print(RunThroughStructConstructor(nameof(CfgSampleClass.InterpolatedStruct), source));
        Assert.True(result.Succeeded);
        string output = result.Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Contains("DefaultInterpolatedStringHandler V_0 = new(", output);
        Assert.DoesNotContain("..ctor", output);
    }

    [Fact]
    public void StructConstructor_RaisesToNewObject_AtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = RunThroughStructConstructor(nameof(CfgSampleClass.InterpolatedStruct), source);

        // No struct .ctor call survives on a storage-location address...
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            c => c.Callee.Name == ".ctor" && c.Arguments is [LoadLocalAddress, ..]);
        // ...the handler construction became a NewObject node.
        Assert.Contains(
            function.Descendants.OfType<NewObject>(),
            n => n.Constructor.DeclaringType.Name == "DefaultInterpolatedStringHandler");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void StructConstructor_ByRefStackSlotReceiver_RaisesToNewObject()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var structType = TypeRef.Definition("Synthetic", "Samples", "Carrier", ValueTypeHint.ValueType);
        var ctor = new MethodRef(structType, ".ctor", voidType, [intType], HasThis: true);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new LoadLocalAddress(0, structType)));
        block.Add(new ExpressionStatement(new Call(ctor, isVirtual: false, [new LoadStackSlot(0, TypeRef.ByRef(structType)), new Constant(42, intType)])));
        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "Samples", "Owner"), signature, [structType], container);

        new StructConstructorPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<StoreStackSlot>(), s => s.Slot == 0);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), s => s.Slot == 0);
        var store = Assert.IsType<StoreLocal>(Assert.Single(block.Children));
        Assert.Equal(0, store.Index);
        var value = Assert.IsType<NewObject>(store.Value);
        Assert.Equal(ctor, value.Constructor);
        Assert.Equal("Carrier V_0 = new(42);", CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void StructConstructor_MismatchedReceiverStorageType_StaysLowered()
    {
        // ldloca A; call B::.ctor()  — the receiver's storage type (A) differs from the
        // constructor's declaring type (B), so raising would emit the invalid whole-value
        // assignment `A V_0 = new B();`. The pass must decline and leave the call lowered.
        var voidType = TypeRef.CoreLib("System", "Void");
        var typeA = TypeRef.Definition("Synthetic", "Samples", "A", ValueTypeHint.ValueType);
        var typeB = TypeRef.Definition("Synthetic", "Samples", "B", ValueTypeHint.ValueType);
        var ctorB = new MethodRef(typeB, ".ctor", voidType, [], HasThis: true);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ExpressionStatement(new Call(ctorB, isVirtual: false, [new LoadLocalAddress(0, typeA)])));
        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "Samples", "Owner"), signature, [typeA], container);

        new StructConstructorPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<StoreLocal>(), s => s.Index == 0);
        Assert.Empty(function.Descendants.OfType<NewObject>());
        var call = Assert.IsType<Call>(Assert.IsType<ExpressionStatement>(Assert.Single(block.Children)).Expression);
        Assert.Equal(".ctor", call.Callee.Name);
        Assert.Equal(typeB, call.Callee.DeclaringType);
    }

    [Fact]
    public void StructConstructor_GenericValueTypeReceiverMatch_RaisesToNewObject()
    {
        // A matching generic value-type receiver (MyStruct<int> storage and declaring type)
        // is a real `s = new MyStruct<int>(7)` shape and must still raise.
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var definition = TypeRef.Definition("Synthetic", "Samples", "MyStruct`1", ValueTypeHint.ValueType);
        var genericStruct = TypeRef.GenericInstance(definition, [intType]);
        var ctor = new MethodRef(genericStruct, ".ctor", voidType, [intType], HasThis: true);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ExpressionStatement(new Call(ctor, isVirtual: false, [new LoadLocalAddress(0, genericStruct), new Constant(7, intType)])));
        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("Synthetic", "Samples", "Owner"), signature, [genericStruct], container);

        new StructConstructorPass().Run(function, PassContext.None);

        var store = Assert.IsType<StoreLocal>(Assert.Single(block.Children));
        var value = Assert.IsType<NewObject>(store.Value);
        Assert.Equal(ctor, value.Constructor);
    }

    [Fact]
    public void CallSite_RefKinds_PrintRefOutAndBareIn()
    {
        // A managed pointer forwarded to a by-ref parameter needs the parameter's
        // own keyword at the call site: `ref`/`out` are required (CS1620), while
        // an `in` argument must stay bare (adding `ref` would be CS1615). The
        // importer recovers each kind from the callee's MethodDef parameter rows.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.RefKindCallSites), source);

        Assert.Contains("RefHelper(ref ", output);
        Assert.Contains("OutHelper(out ", output);
        // The `in` argument is passed without a keyword.
        Assert.Contains("InHelper(", output);
        Assert.DoesNotContain("InHelper(ref", output);
        Assert.DoesNotContain("InHelper(out", output);
    }

    [Fact]
    public void CallSite_RefKinds_RecoveredForGenericInstanceCalls()
    {
        // A call on a constructed generic type is a MemberRef (TypeSpec parent)
        // with no parameter rows; the keyword is recovered from the underlying
        // generic MethodDef. Without that, `out` would render as `ref` (CS1620).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GenericRefKindCallSites), source);

        Assert.Contains("TryGet(out ", output);
        // The `in` argument is passed without a keyword.
        Assert.Contains("Put(", output);
        Assert.DoesNotContain("Put(ref", output);
        Assert.DoesNotContain("Put(out", output);
    }

    [Fact]
    public void BooleanMaterialization_SelectWithIntLiteral_DeclaresBoolSlot()
    {
        // `cond ? false : boolExpr` lowers to a select whose false arm is the
        // literal 0; the pass recovers it as `false` and types the slot bool, so
        // the bool-returning method no longer returns an int slot (CS0029).
        // ShortCircuitTernaryPass then canonicalizes the recovered
        // `cond ? false : boolExpr` to `!cond && boolExpr`.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SelectBoolReturn), source);

        Assert.Contains("bool S_0", output);
        Assert.DoesNotContain("int S_0", output);
        Assert.Contains("x <= 0 && x.GetHashCode() > 0", output);
        Assert.DoesNotContain("? 0", output);
        Assert.DoesNotContain(": 0", output);
    }

    [Fact]
    public void BooleanMaterialization_MultiStoreSlotDiamond_DeclaresBoolSlot()
    {
        // A bool computed across an if/else diamond spills to a stack slot whose
        // else arm stores the literal 0; the pass retypes the constant store and
        // the slot's loads to bool (the multi-store counterpart of the select).
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.RuntimeType", "get_IsActualEnum", source);

        Assert.Contains("bool S_0", output);
        Assert.DoesNotContain("int S_0", output);
        Assert.Contains("S_0 = false;", output);
    }

    [Fact]
    public void BooleanMaterialization_IndirectBoolStore_DeclaresBoolSlot()
    {
        // A bool computed into a stack slot and stored through ref bool is still a
        // bool consumer. Without this, the slot stays int and the ref bool store is
        // `target = S_0` (CS0029). The recovered `flag ? false : other` is left as a
        // ternary: its surviving operand is a bare parameter load, which csc would
        // emit branchless, so ShortCircuitTernaryPass declines it to stay opcode-exact.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var sbyteType = TypeRef.CoreLib("System", "SByte");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Conditional(
            new LoadArgument(0, "flag", boolType),
            new Constant(0, intType),
            new LoadArgument(1, "other", boolType))
        { MergedType = intType }));
        block.Add(new StoreIndirect(sbyteType,
            new LoadArgument(2, "target", TypeRef.ByRef(boolType)),
            new LoadStackSlot(0, intType)));
        // A second consumer keeps the slot materialized (two loads), so this test
        // pins slot typing rather than being subsumed by single-use inlining.
        block.Add(new StoreIndirect(sbyteType,
            new LoadArgument(2, "target", TypeRef.ByRef(boolType)),
            new LoadStackSlot(0, intType)));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flag", boolType), new Parameter("other", boolType), new Parameter("target", TypeRef.ByRef(boolType))],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("bool S_0", output);
        Assert.Contains("flag ? false : other", output);
        Assert.DoesNotContain("int S_0", output);
        Assert.Contains("target = S_0;", output);
    }

    [Fact]
    public void BooleanMaterialization_NestedZeroOneSelect_DeclaresBoolSlot()
    {
        // A nested 0/1 select tree can still be a boolean when every live load is
        // consumed as bool. Without recursively materializing the arms, the store
        // declares as int while the load declares as bool (S_0_1), causing CS0165.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Conditional(
            new LoadArgument(0, "explicitInclude", boolType),
            new Constant(1, intType),
            new Conditional(
                new LoadArgument(1, "wantsFetch", boolType),
                new Constant(0, intType),
                new Conditional(
                    new Comparison(ComparisonKind.GreaterThanOrEqual, isUnsigned: false, new LoadArgument(2, "verbosity", intType), new Constant(3, intType)),
                    new Constant(1, intType),
                    new Constant(0, intType))
                { MergedType = intType })
            { MergedType = intType })
        { MergedType = intType }));
        var then = new Block(4);
        then.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        block.Add(new IfStatement(new LoadStackSlot(0, intType), then, null));
        var secondThen = new Block(8);
        secondThen.Add(new StoreLocal(0, intType, new Constant(2, intType)));
        block.Add(new IfStatement(new LoadStackSlot(0, intType), secondThen, null));
        block.Add(new Return(null));
        var signature = new MethodSignature(voidType,
            [new Parameter("explicitInclude", boolType), new Parameter("wantsFetch", boolType), new Parameter("verbosity", intType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("bool S_0", output);
        Assert.Contains("if (S_0)", output);
        Assert.DoesNotContain("int S_0", output);
        Assert.DoesNotContain("S_0_1", output);
        Assert.DoesNotContain(": 0", output);
        Assert.DoesNotContain(": 1", output);
    }

    [Fact]
    public void ReferenceConditionalStore_UsesConsumerSlotType()
    {
        // A null/string conditional can arrive with object as its merged stack type
        // while the single live consumer is a string field store. The slot must
        // declare as the consumer type; otherwise the load gets a split S_0_1 name
        // that was never assigned (CS0165).
        var owner = TypeRef.Definition("Synthetic", "Samples", "SlotProbe");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var objectType = TypeRef.CoreLib("System", "Object");
        var stringType = TypeRef.CoreLib("System", "String");
        var voidType = TypeRef.CoreLib("System", "Void");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Conditional(
            new LoadArgument(1, "empty", boolType),
            new Constant(null, objectType),
            new LoadArgument(2, "value", stringType))
        { MergedType = objectType }));
        block.Add(new StoreField(
            new FieldRef(owner, "_fmt", stringType),
            new LoadArgument(0, "this", owner),
            new LoadStackSlot(0, stringType)));
        // A second consumer keeps the slot materialized (two loads), so this test
        // pins slot typing rather than being subsumed by single-use inlining.
        block.Add(new StoreField(
            new FieldRef(owner, "_fmt", stringType),
            new LoadArgument(0, "this", owner),
            new LoadStackSlot(0, stringType)));
        block.Add(new Return(null));
        var signature = new MethodSignature(voidType,
            [new Parameter("empty", boolType), new Parameter("value", stringType)],
            HasThis: true,
            GenericParameterCount: 0);
        var function = new IrFunction("set_DateTimeFormat", owner, signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("string S_0", output);
        Assert.Contains("_fmt = S_0;", output);
        Assert.DoesNotContain("object S_0", output);
        Assert.DoesNotContain("S_0_1", output);
    }

    [Fact]
    public void ReferenceConditionalStore_DoesNotNarrowToUnprovenReferenceTarget()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "SlotProbe");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var objectType = TypeRef.CoreLib("System", "Object");
        var unresolved = TypeRef.Definition("OtherAssembly", "Samples", "MaybeStruct");
        var voidType = TypeRef.CoreLib("System", "Void");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Conditional(
            new LoadArgument(0, "empty", boolType),
            new Constant(null, objectType),
            new LoadArgument(1, "value", unresolved))
        { MergedType = objectType }));
        block.Add(new StoreField(
            new FieldRef(owner, "Maybe", unresolved),
            new LoadArgument(2, "this", owner),
            new LoadStackSlot(0, unresolved)));
        block.Add(new Return(null));
        var signature = new MethodSignature(voidType,
            [new Parameter("empty", boolType), new Parameter("value", unresolved)],
            HasThis: true,
            GenericParameterCount: 0);
        var function = new IrFunction("set_Maybe", owner, signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("object S_0", output);
        Assert.DoesNotContain("MaybeStruct S_0 =", output);
    }

    [Fact]
    public void ReferenceConditionalStore_CompiledSetterUsesOneSlotName()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, "set_SlotMergedDateTimeFormat", source);

        Assert.Contains("_dateTimeFormat", output);
        Assert.DoesNotContain("object S_", output);
        Assert.DoesNotContain("S_1_1", output);
    }

    [Fact]
    public void BooleanMaterialization_ReusedReceiverSlot_KeepsBoolLiveRangeDistinct()
    {
        // A ?. bool test can reuse the same edge slot first for the string
        // receiver and then for the bool result. The bool live range must not
        // inherit the receiver slot's string declaration (CS0029).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReusedSlotNullableBool), source);

        Assert.Contains("node.Label.Contains(\"x\")", output);
        Assert.Contains("return \"hit\";", output);
        Assert.Contains("return \"miss\";", output);
        Assert.DoesNotContain(": 0", output);
        Assert.DoesNotContain("string S_0 = ", output);
    }

    [Fact]
    public void ReusedTempSpillFold_StringListCount_RaisesObjectInitializer()
    {
        // This fixture used to reach StackSlotLiveRangePass with one edge slot
        // carrying a string property value, then a nullable list receiver, then
        // the int Count — disjoint live ranges that needed distinct typed
        // carriers to avoid CS0029. After #3336 stage 2, the post-structuring
        // reused-temp spill fold raises that dup-chain into an object initializer
        // *before* StackSlotLiveRangePass runs, so the reused edge slot no longer
        // reaches that pass. That reuse is intrinsic to the foldable dup-chain
        // lowering (the member values sit on the stack beneath the dup'd receiver
        // and merge into reused edge slots), so it cannot be reproduced in a
        // non-folding fixture. The slot-splitting mechanism itself stays guarded
        // by BooleanMaterialization_ReusedReceiverSlot_KeepsBoolLiveRangeDistinct
        // (string -> bool) and the SlotMergedDateTimeFormat object-slot case.
        //
        // This test now pins the fold: the method raises to
        // `new SlotReuseSection { Status = ..., Missing = ... }`, with the
        // nullable-list Count spelled correctly and no mistyped carrier leaked.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReusedSlotStringListCount), source);

        Assert.Contains("new SlotReuseSection", output);
        Assert.Contains("Status = complete ? \"Complete\" : \"Partial\"", output);
        Assert.Contains("Missing = missing is not null ? missing.Count : 0", output);
        // The verbose S_NNN version-copy chain is gone: no leaked receiver copy
        // and no member store through a synthetic carrier.
        Assert.DoesNotContain("section.Missing", output);
        Assert.DoesNotContain("string S_0 = ", output);
    }

    [Fact]
    public void IncrementDecrement_DupSlotIdiom_FoldsToOperatorAtUseSite()
    {
        // `dst[--j] = src[i++];` lowers to two dup-captured stack slots: one for
        // the pre-decremented j, one for the pre-increment value of i. The pass
        // folds each spilled capture back into the ++/-- operator at the use
        // site, restoring the source spelling (and the dup on recompile).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReverseCopy), source);

        Assert.Contains("dst[--j] = src[i++];", output);
        // The dup-capture slots must no longer leak as explicit spill statements.
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    public void CompoundAssignment_ArrayElement_FoldsToCompoundOperator()
    {
        // `a[i] += v` lowers to &a[i] captured in a dup slot, then a store-back
        // through that slot. The pass inlines the slot so the printer can spell
        // the compound operator — and the spill slot must not leak.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ArrayElementAdd), source);

        Assert.Contains("a[i] += v;", output);
        Assert.DoesNotContain("S_", output);
        Assert.DoesNotContain("ref int", output);
    }

    [Fact]
    public void CompoundAssignment_ArrayElementEffectfulIndex_KeepsSingleIndexEvaluation()
    {
        // `a[RecordIndex()] += v` lowers to one captured &a[RecordIndex()]. The
        // pass must not clone that address unless the printer can fold it back to
        // a compound lvalue; otherwise the call index would be evaluated twice.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ArrayElementAddEffectfulIndex), source);

        Assert.Equal(1, output.Split("RecordIndex()", StringSplitOptions.None).Length - 1);
        Assert.Contains("ref int", output);
        Assert.Contains("= ref a[RecordIndex()];", output);
        Assert.Contains("+ v;", output);
    }

    [Fact]
    public void CompoundAssignment_ArrayElementShift_FoldsToCompoundOperator()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ArrayElementShift), source);

        Assert.Contains("a[i] <<= n;", output);
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    public void CompoundAssignment_ArrayElementIncrement_FoldsToIncrementOperator()
    {
        // `a[i]++` shares the address-slot shape; the ±1 case still spells ++.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ArrayElementInc), source);

        Assert.Contains("a[i]++;", output);
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    public void CompoundAssignment_ExpandedArrayElement_DoesNotFold()
    {
        // `a[i] = a[i] + v` evaluates the index twice (no address slot); it is a
        // structurally different shape and must keep its expanded spelling.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ArrayElementExpandedAdd), source);

        Assert.Contains("a[i] = a[i] + v;", output);
    }

    [Fact]
    public void CompoundAssignment_RefTarget_FoldsToCompoundOperator()
    {
        // `p += v` through a ref parameter — compiles identically to `p = p + v`,
        // so folding is faithful; the spurious parens around the deref also drop.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.RefAdd), source);

        Assert.Contains("p += v;", output);
    }

    [Fact]
    public void CompoundAssignment_Property_FoldsToCompoundOperator()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.PropertyAdd), source);

        Assert.Contains("CompoundProperty += v;", output);
    }

    [Fact]
    public void CompoundAssignment_RefReturningIndexer_KeepsSingleGetterEvaluation()
    {
        // `s[i] += v` over Span<int> evaluates the ref-returning indexer ONCE
        // (get_Item -> ref int, dup'd). The address-compound fold must decline a
        // LoadProperty address: the printer's SameLValue cannot fold it, so folding
        // would leak `s[i] = (s[i]) + v` and call get_Item twice. Declining lets the
        // captured ref render as a `ref` local, preserving the single evaluation.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SpanElementCompoundAdd), source);

        Assert.DoesNotContain("s[i] = (s[i])", output);
        Assert.DoesNotContain("s[i] = s[i]", output);
        Assert.Contains("ref int", output);
        Assert.Contains("= ref s[i];", output);
    }


    [Fact]
    public void LocalNames_RecoveredFromPdb_RenderSourceNamesNotVSlots()
    {
        // ReverseCopy's loop variables are `i` and `j` in source. With the
        // portable PDB present, the printer must spell them by their recovered
        // names rather than the synthetic V_0/V_1 fallback.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReverseCopy), source);

        Assert.Contains("int i = 0;", output);
        Assert.Contains("int j = dstIndex + count;", output);
        Assert.DoesNotContain("V_0", output);
        Assert.DoesNotContain("V_1", output);
    }

    [Fact]
    public void OpenWithoutSymbols_IgnoresPdb_RendersVSlotsNotSourceNames()
    {
        // The same fixture and method as above, but opened with symbols
        // disabled. Even though a portable PDB is present, local names must not
        // be recovered: the printer falls back to V_index, giving deterministic,
        // symbol-independent output.
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReverseCopy), source);

        Assert.Contains("V_0", output);
        Assert.DoesNotContain("int i = 0;", output);
    }

    [Fact]
    public void TypedConstants_BoolReturn_PrintsFalse()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        Assert.Equal("return false;", PrintWithPasses("System.Array", "get_IsReadOnly", source));
    }

    [Fact]
    public void PropertySugar_GetterCall_PrintsPropertyAccess()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        Assert.Equal("return s.Length;",
            PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.LengthOf), source));
    }

    [Fact]
    public void DelegateConstruction_StaticMethodGroup_PrintsNewDelegate()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        Assert.Equal("return new Action(CfgSampleClass.Tick);",
            PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StaticMethodGroup), source));
    }

    [Fact]
    public void DelegateConstruction_InstanceMethodGroup_DropsThisQualifier()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        Assert.Equal("return new Action(Instance);",
            PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.InstanceMethodGroup), source));
    }

    [Fact]
    public void DelegateConstruction_RaisesToTypedNode_AtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StaticMethodGroup));
        Assert.NotNull(function);
        IrPasses.Run(function);

        var creation = Assert.Single(function.Descendants.OfType<DelegateCreation>());
        Assert.Equal("Action", creation.DelegateType.ToDisplayString());
        Assert.Equal("Tick", creation.Method.Name);
        Assert.Empty(function.Descendants.OfType<LoadFunctionPointer>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void LoadFunctionPointer_BeforeRaising_ImportsAtPartialFidelity()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StaticMethodGroup));
        Assert.NotNull(function);

        // Before the pass runs, the bare function-pointer load has no C#
        // spelling, so the unraised tree is honestly Partial.
        Assert.Single(function.Descendants.OfType<LoadFunctionPointer>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void StackAlloc_ImportsToTypedNode_AtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StackAllocFirst));
        Assert.NotNull(function);

        var alloc = Assert.Single(function.Descendants.OfType<StackAllocate>());
        Assert.Equal("byte*", alloc.ResultType?.ToDisplayString());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Diagnostics);
    }

    [Fact]
    public void StackAlloc_Prints_StackallocByteCount()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StackAllocFirst), source);
        Assert.Contains("stackalloc byte[", output);
    }

    [Fact]
    public void StackAllocSpanInitializerThroughSlot_RaisesAtFullFidelity()
    {
        // #2907 real-compiled regression: before StackAllocSpanPass resolved
        // the slot indirection, this shape imported at (an incorrectly)
        // Full fidelity while printing the invalid
        // `new Span<int>((void*)(stackalloc int[] { 1, 2, 3 }), 3)` (CS8346).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StackAllocSpanInitializerThroughSlot));
        Assert.NotNull(function);

        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Descendants.OfType<NewObject>());
        var raised = Assert.Single(function.Descendants.OfType<StackAllocArray>());
        Assert.True(raised.HasInitializer);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("Consume(stackalloc int[] { 1, 2, 3 });", output);
    }

    [Fact]
    public void StackAlloc_ReturningPointer_PrintsPointerLocal()
    {
        // A stackalloc expression cannot be returned directly as a pointer, nor
        // explicitly cast in expression position (CS8346). Route it through a
        // pointer local and cast that local to the signature's pointer type.
        using var source = MetadataSource.Open(FixtureCatalog.DecompilerUnsafeChainA.AssemblyPath());
        var function = IrImporter.Import(
            source,
            typeof(ILInspector.Decompiler.Fixtures.UnsafeChainA.LibraryA).FullName!,
            nameof(ILInspector.Decompiler.Fixtures.UnsafeChainA.LibraryA.EscapingStackPointer));
        Assert.NotNull(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("byte* __stackalloc = stackalloc byte[40];", output);
        Assert.Contains("return (int*)__stackalloc;", output);
        Assert.DoesNotContain("return stackalloc", output);
    }

    [Fact]
    public void StackAlloc_SyntheticHelperAvoidsMethodGenericParameter()
    {
        using var source = MetadataSource.Open(typeof(NamePreservationSamples).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(NamePreservationSamples).FullName!,
            nameof(NamePreservationSamples.StackAllocGenericNameCollision));
        Assert.NotNull(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("byte* __stackalloc0 = stackalloc byte[4];", output);
        Assert.Contains("return (int*)__stackalloc0;", output);
        Assert.DoesNotContain("byte* __stackalloc =", output);
    }

    [Fact]
    public void StoreElement_PreservesExactNamedReceiverTemp()
    {
        using var source = MetadataSource.Open(typeof(NamePreservationSamples).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(NamePreservationSamples).FullName!,
            nameof(NamePreservationSamples.StoreElementNamedReceiverTemp));
        Assert.NotNull(function);
        Assert.Contains("trimmed", function.LocalNames);

        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("ReadOnlySpan<char> trimmed = MemoryExtensions.Trim(item);", output);
        Assert.Contains("items[i] = trimmed.ToString();", output);
    }

    [Fact]
    public void GenericInstanceNullCheck_RendersIsNull()
    {
        // A brtrue/brfalse operand can never be a struct value, so a generic
        // instance (List<int>) is soundly a reference type: null-test it,
        // never the uncompilable !items.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CountOrZero), source);

        Assert.Contains("items is null", output);
        Assert.DoesNotContain("!items", output);
    }

    [Fact]
    public void SameAssemblyReferenceNullCheck_RendersIsNull()
    {
        // CfgNullableTarget is a non-generic reference type defined in this
        // assembly; same-assembly shape resolution proves it a reference, so
        // the guard null-tests rather than printing the uncompilable !gate.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GateOrZero), source);

        Assert.Contains("gate is null", output);
        Assert.DoesNotContain("!gate", output);
    }

    [Fact]
    public void NestedGenericType_RendersInnermostName_NotOuter()
    {
        // List<T>.GetEnumerator returns the nested List`1+Enumerator. The old
        // StripArity-at-first-backtick bug rendered it new List<T>(this); the
        // correct innermost-only spelling is new Enumerator(this) — Enumerator
        // is non-generic (the T belongs to the elided outer List), so
        // Enumerator<T> would be CS0308.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        Assert.Equal("return new Enumerator(this);",
            PrintWithPasses("System.Collections.Generic.List`1", "GetEnumerator", source));
    }

    [Fact]
    public void ExpressionInlining_SingleUseTemp_Collapses()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        Assert.Equal("return x + x;",
            PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Twice), source));
    }

    [Fact]
    public void MultiUseLocal_DeclaresAtItsEntryBlockStore()
    {
        // Two loads: no inlining; the declaration merges into the store,
        // current-style. Debug uses a local (V_0), Release a dup slot
        // (S_256) — the merged-declaration shape must hold for both.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Reused), source);

        Assert.Matches(@"int (V_0|S_\d+) = x \+ 1;", output);
        Assert.DoesNotMatch(@"int (V_0|S_\d+);", output);
        Assert.Matches(@"return (V_0|S_\d+) \* \1;", output);
    }

    [Fact]
    public void TypedConstants_RunAgainAfterInlining_CatchExposedPositions()
    {
        // A slot constant only reaches its typed position (the bool return)
        // after inlining — the pass list runs typed constants twice.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, boolType, new Constant(0, TypeRef.CoreLib("System", "Int32"))));
        block.Add(new Return(new LoadLocal(0, boolType)));
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [boolType], container);

        Assert.Equal("return false;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void Inlining_DoesNotCrossExceptionRegionBoundaries()
    {
        // The handler block is physically next but not normal fallthrough;
        // moving the computation would change what the try protects.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var tryBlock = new Block(0);
        container.Add(tryBlock);
        tryBlock.Add(new StoreLocal(0, intType, new Constant(7, intType)));
        var handlerBlock = new Block(4);
        container.Add(handlerBlock);
        handlerBlock.Add(new Return(new LoadLocal(0, intType)));
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container)
        {
            Regions = [new HandlerRegion(HandlerKind.Catch, 0, 4, 4, 4, 0, null)],
        };

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreLocal>());
        Assert.Single(function.Descendants.OfType<LoadLocal>());
        function.CheckInvariant();
    }

    [Fact]
    public void Inlining_ArgumentRead_NotPureWhenAddressEscapes()
    {
        // V_0 = x; M(ref x, V_0): inlining the copy would read x AFTER the
        // ref call may have mutated it. The copy must stay.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, intType, new LoadArgument(0, "x", intType)));
        var callee = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "M",
            TypeRef.CoreLib("System", "Void"), [TypeRef.ByRef(intType), intType], HasThis: false);
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false,
            [new LoadArgumentAddress(0, "x", intType), new LoadLocal(0, intType)])));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("F", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        new ExpressionInliningPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreLocal>());
        function.CheckInvariant();
    }

    [Fact]
    public void RefLocal_StoreRendersRefAssignment()
    {
        // stloc into a ref-typed local rebinds the reference (it is not a
        // write-through), so it must spell C#'s ref (re)assignment: `= ref`
        // on the initial declaration (CS8172) and on any later rebind (CS8173).
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var getA = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        var getB = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "B", refInt, [], HasThis: false);
        block.Add(new StoreLocal(0, refInt, new Call(getA, isVirtual: false, [])));
        block.Add(new StoreLocal(0, refInt, new Call(getB, isVirtual: false, [])));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref A();", output);
        Assert.Contains("V_0 = ref B();", output);
        Assert.DoesNotContain("V_0 = A();", output);
    }

    [Fact]
    public void RefLocal_AssignedBeforeUse_DeclaresAtRefAssignment()
    {
        // A ref local whose defining store is not the entry-block first reference
        // can still declare at that store when every reference stays in the same
        // block after it. This avoids a synthetic Unsafe.NullRef<T>() initializer
        // whose call/stloc was not present in the original IL (#1899).
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var entry = new Block(0);
        container.Add(entry);
        entry.Add(new Branch(1));
        var body = new Block(1);
        container.Add(body);
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        var use = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "Use", TypeRef.CoreLib("System", "Void"), [refInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        body.Add(new StoreLocal(0, refInt, new Call(getRef, isVirtual: false, [])));
        body.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, refInt)])));
        body.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref A();", output);
        Assert.DoesNotContain("Unsafe.NullRef", output);
        AssertRefLocalBodyCompiles(output);
    }

    [Fact]
    public void RefLocal_AssignedBeforeUseInLaterBlock_InitializesToNullRef()
    {
        // A declaration at the store would strand the later block's use outside
        // scope. Keep the up-front null ref when references leave the store block.
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var entry = new Block(0);
        container.Add(entry);
        entry.Add(new Branch(1));
        var assign = new Block(1);
        container.Add(assign);
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        assign.Add(new StoreLocal(0, refInt, new Call(getRef, isVirtual: false, [])));
        assign.Add(new Branch(2));
        var useBlock = new Block(2);
        container.Add(useBlock);
        var use = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "Use", TypeRef.CoreLib("System", "Void"), [refInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        useBlock.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, refInt)])));
        useBlock.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();", output);
        Assert.Contains("V_0 = ref A();", output);
        AssertRefLocalBodyCompiles(output);
    }

    [Fact]
    public void RefLocal_LaterLabelTarget_InitializesToNullRef()
    {
        // A branch can target a later statement in the same flat block. Declaring
        // at the store would let the goto bypass the declaration and hit CS0165.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new Constant(true, boolType), 0x10));
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        block.Add(new StoreLocal(0, refInt, new Call(getRef, isVirtual: false, [])));
        var use = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "Use", TypeRef.CoreLib("System", "Void"), [refInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        var useStatement = new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, refInt)]));
        useStatement.SetSourceOffset(0x10);
        block.Add(useStatement);
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();", output);
        Assert.Contains("V_0 = ref A();", output);
        AssertRefLocalBodyCompiles(output);
    }

    [Fact]
    public void RefLocal_SelfReferentialStore_InitializesToNullRef()
    {
        // IL can read the zero-initialized ref local while computing its first
        // store. C# cannot reference a variable in its own declaration initializer.
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [refInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        block.Add(new StoreLocal(0, refInt, new Call(getRef, isVirtual: false, [new LoadLocal(0, refInt)])));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();", output);
        Assert.Contains("V_0 = ref A(ref V_0);", output);
        AssertRefLocalBodyCompiles(output);
    }

    [Fact]
    public void RefLocal_ReadBeforeStore_InitializesToNullRef()
    {
        // If a ref local is referenced before its first store, C# has no bare
        // declaration spelling. Preserve IL's zero-initialized managed pointer with
        // Unsafe.NullRef<T>().
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var use = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "Use", TypeRef.CoreLib("System", "Void"), [refInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadLocal(0, refInt)])));
        block.Add(new StoreLocal(0, refInt, new Call(getRef, isVirtual: false, [])));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();", output);
        AssertRefLocalBodyCompiles(output);
    }

    [Fact]
    public void ResidualRefSlot_FailsVisiblyAtPrinterBoundary()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var entry = new Block(0);
        container.Add(entry);
        entry.Add(new Branch(1));
        var body = new Block(1);
        container.Add(body);
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        body.Add(new StoreStackSlot(0, new Call(getRef, isVirtual: false, [])));
        body.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        var result = CSharpPrinter.Print(function);
        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticIds.InternalError, diagnostic.Id);
        Assert.Equal(
            "InvalidOperationException: Managed-reference stack slot 0 reached C# emission after slot materialization.",
            diagnostic.Message);
    }

    [Fact]
    public void RefConditional_BindsRefToEachArm()
    {
        // A ref-typed conditional is a ref ternary: `ref` binds each arm, not
        // the whole expression. `= ref (cond ? a : b)` would be CS8173.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var ternary = new Conditional(
            new LoadArgument(0, "flag", boolType),
            new LoadArgument(1, "a", refInt),
            new LoadArgument(2, "b", refInt)) { MergedType = refInt };
        block.Add(new StoreLocal(0, refInt, ternary));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flag", boolType), new Parameter("a", refInt), new Parameter("b", refInt)],
            HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref (flag ? ref a : ref b);", output);
    }

    [Fact]
    public void RefConditional_WithNestedConditionalCondition_ParenthesizesCondition()
    {
        // #2376: the ref-ternary condition slot must parenthesize a nested
        // conditional. A bare `c ? p : q ? ref a : ref b` right-associates to
        // `c ? p : (q ? ref a : ref b)` — a bool/ref arm mismatch, invalid C#.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var condition = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "p", boolType),
            new LoadArgument(2, "q", boolType)) { MergedType = boolType };
        var ternary = new Conditional(
            condition,
            new LoadArgument(3, "a", refInt),
            new LoadArgument(4, "b", refInt)) { MergedType = refInt };
        block.Add(new StoreLocal(0, refInt, ternary));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("c", boolType), new Parameter("p", boolType), new Parameter("q", boolType),
             new Parameter("a", refInt), new Parameter("b", refInt)],
            HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(c ? p : q) ? ref a : ref b", output);
    }

    [Fact]
    public void UnionSwitchArm_WithConditionalGuard_ParenthesizesGuard()
    {
        // #2376: a ternary `when` guard must be parenthesized. A bare
        // `when c ? b1 : b2 =>` terminates the guard at `=>` and mis-parses
        // (CS1003 '=>' expected / CS1525). Comparisons/`&&`/`??` stay bare.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var petType = TypeRef.Definition("Synthetic", "UnionFixtures", "Pet", ValueTypeHint.ValueType);
        var catType = TypeRef.Definition("Synthetic", "UnionFixtures", "Cat");
        var guard = new Conditional(
            new LoadArgument(1, "c", boolType),
            new LoadArgument(2, "b1", boolType),
            new LoadArgument(3, "b2", boolType)) { MergedType = boolType };
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new UnionSwitchExpression(
            new LoadArgument(0, "pet", petType),
            [new UnionSwitchExpressionArm(catType, null, new Constant(1, intType), guard)],
            nullValue: new Constant(0, intType))));
        container.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("pet", petType), new Parameter("c", boolType),
             new Parameter("b1", boolType), new Parameter("b2", boolType)],
            HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("when (c ? b1 : b2) =>", output);
    }

    [Fact]
    public void Truthiness_UnknownDefinition_DoesNotGuessNull()
    {
        // A bare definition could be a struct or an enum; '!= null' would be
        // a guess that might not compile. The raw value prints instead.
        var unknownType = TypeRef.Definition("Some.Assembly", "Some", "Widget");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LoadArgument(0, "w", unknownType), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("w", unknownType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("!= null", output);
        Assert.Contains("if (w) goto IL_0004;", output);
    }

    [Fact]
    public void NonBoolBranchOperands_SpellTheComparison()
    {
        // brtrue over a reference must not print 'if (s)' — that is not C#.
        var stringType = TypeRef.CoreLib("System", "String");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LoadArgument(0, "s", stringType), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("s", stringType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Contains("if (s is not null) goto IL_0004;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void FloatUnorderedOrdering_PrintsNegatedOrderedDual()
    {
        // 'a >= b unordered' over doubles: C#'s >= is ordered, so the honest
        // spelling is !(a < b) — NaN inputs take the same path as the IL.
        var doubleType = TypeRef.CoreLib("System", "Double");
        var comparison = new Comparison(ComparisonKind.GreaterThanOrEqual, isUnsigned: true,
            new LoadArgument(0, "a", doubleType), new LoadArgument(1, "b", doubleType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(comparison));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Boolean"),
            [new Parameter("a", doubleType), new Parameter("b", doubleType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return !(a < b);", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void BooleanFolding_GuardReturn_FoldsToSourceForm()
    {
        // The corpus front door, character-identical to the dotnet/runtime
        // source: return value == null || value.Length == 0; (is-form per
        // the taste doc).
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.String", "IsNullOrEmpty", source);

        Assert.Equal("return value is null || value.Length == 0;\n", output + "\n");
    }

    [Fact]
    public void BooleanFolding_GuardAndBoolComparison_FoldToAndChain()
    {
        // The && lowering plus the ceq-with-zero value form, folded back to
        // the exact dotnet/runtime source: _size != 0 && IndexOf(item) >= 0.
        // (Release-compiled CoreLib: the Debug result-local pattern with a
        // shared failure tail is a later structuring slice.)
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.Collections.Generic.List`1", "Contains", source);

        Assert.Equal("return _size != 0 && IndexOf(item) >= 0;", output);
    }

    [Fact]
    public void BooleanFolding_TernarySource_PerConfigShape()
    {
        // Debug lowers the ternary through a stack-slot diamond, which folds
        // back to the ternary (with the double-negative unwrapped and arms
        // swapped). Release lowers it as bool-guard dual returns, outside the
        // narrow non-bool relational ternary-return fold.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Pick), source);

#if DEBUG
        Assert.Equal("return c ? a : b;", output);
#else
        Assert.Equal("""
            if (!c)
            {
                return b;
            }
            return a;
            """.ReplaceLineEndings("\n"), output);
#endif
    }

    [Fact]
    public void BooleanFolding_GuardReturn_NonBoolArms_RaisesConditional()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.TernaryInt), source);

        Assert.Equal("return a > b ? a : b;", output);
    }

    [Fact]
    public void BooleanFolding_GuardReturn_AfterLocalPrelude_StaysStatementForm()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GuardReturnAfterLocalPrelude), source);

        Assert.DoesNotContain("?", output);
        Assert.Contains("LastValue = positive + negative;", output);
        Assert.Contains("if (value > 0)", output);
    }

    [Fact]
    public void BooleanFolding_EqualityGuardReturn_StaysStatementForm()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.EqualityGuardReturn), source);

        Assert.Equal("""
            if (value == 0)
            {
                return whenZero;
            }
            return otherwise;
            """.ReplaceLineEndings("\n"), output);
        Assert.DoesNotContain("?", output);
    }

    [Fact]
    public void BooleanFolding_ObjectReferenceGuardReturn_RaisesCanonicalConditional()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ObjectReferenceEqualityGuardReturn), source);

        Assert.Equal("return left != right ? whenDifferent : whenSame;", output);
    }

    [Fact]
    public void BooleanFolding_StringEqualityGuardReturn_StaysStatementForm()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.StringEqualityGuardReturn), source);

        Assert.Equal("""
            if (left == right)
            {
                return whenSame;
            }
            return whenDifferent;
            """.ReplaceLineEndings("\n"), output);
        Assert.DoesNotContain("?", output);
    }

    [Fact]
    public void BooleanFolding_FloatGuardReturn_PreservesUnorderedDual()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.FloatUnorderedGuardReturn), source);

        Assert.Equal("return !(value <= limit) ? whenGreaterOrUnordered : whenLessOrEqual;", output);
        Assert.DoesNotContain("value > limit ?", output);
    }

    [Fact]
    public void BooleanFolding_UnsignedGuardReturn_StaysStatementForm()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.UnsignedBoundsGuard), source);

        Assert.Equal("""
            if ((uint)index >= (uint)length)
            {
                return "out";
            }
            return "in";
            """.ReplaceLineEndings("\n"), output);
        Assert.DoesNotContain("?", output);
    }

    [Fact]
    public void BooleanFolding_GuardReturn_BothConstantArms_CollapseToCondition()
    {
        // if (a > 0) { if (b > 0) return true; } return false; folds the nested
        // guards to one condition, then the dual-constant return to the bare
        // condition — not a dead `a > 0 && b > 0 && true` (the true arm is the
        // result, not an extra && term).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.BothPositive), source);

        Assert.Equal("return a > 0 && b > 0;", output);
    }

    [Fact]
    public void Structuring_NestedGuards_NestAndDropGotos()
    {
        using var fixtureSource = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.AbsShort), fixtureSource);

        Assert.DoesNotContain("goto", output);
        Assert.Contains("if (", output);
        // The inner overflow guard nests inside the outer negative guard.
        Assert.Contains("    if (", output);
    }

    [Fact]
    public void Structuring_GuardedWhile_RaisesToForLoop()
    {
        // The full composition: guard, for-recognition with the declaration
        // in the initializer, increment sugar, indexer — character-identical
        // to the current emitter's rendering of this method.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.String", "IsNullOrWhiteSpace", source);

        Assert.Equal("""
            if (value is null)
            {
                return true;
            }
            for (int V_0 = 0; V_0 < value.Length; V_0++)
            {
                if (!char.IsWhiteSpace(value[V_0]))
                {
                    return false;
                }
            }
            return true;
            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void Structuring_BottomTestedLoop_RaisesToDoWhile()
    {
        // The bottom-tested back edge raises to a do-while: no goto, no label.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.DoWhileSum), source);

        Assert.Contains("do", output);
        Assert.Contains("while (", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void DoWhileWithBreak_RaisesBreak()
    {
        // The conditional exit out of the loop raises to `if (...) break;`
        // inside a structured do-while — no goto, no label.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.DoWhileWithBreak), source);

        Assert.Contains("do", output);
        Assert.Contains("while (", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void NestedSelfLoopsWithExternalHeaderEntry_Raise()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.PartitionStyleNestedSelfLoops), source);

        Assert.Contains("do", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void NestedDoWhileWithBranchingBody_RaisesBothLoops()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.NestedDoWhileWithBranchingBody));
        Assert.NotNull(function);

        IrPasses.Run(function);

        Assert.Equal(2, function.Descendants.OfType<DoWhileLoop>().Count());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
        function.CheckInvariant();
    }

    [Fact]
    public void ExternalEntryToMultiBlockDoWhileHeader_Raises()
    {
        var function = ExternalEntryIntoMultiBlockLoop(entryTarget: 10);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void ExternalEntryIntoMultiBlockDoWhileBody_StaysFlat()
    {
        var function = ExternalEntryIntoMultiBlockLoop(entryTarget: 20);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        Assert.NotEmpty(function.Descendants.OfType<Branch>());
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(NestedEntryKind.Branch)]
    [InlineData(NestedEntryKind.ConditionalBranch)]
    [InlineData(NestedEntryKind.SwitchBranch)]
    [InlineData(NestedEntryKind.Leave)]
    public void NestedExternalEntryIntoMultiBlockDoWhileBody_StaysFlat(NestedEntryKind kind)
    {
        var function = ExternalEntryIntoMultiBlockLoop(entryTarget: 20, nestedEntry: kind);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Theory]
    [InlineData(NestedEntryKind.Branch)]
    [InlineData(NestedEntryKind.ConditionalBranch)]
    [InlineData(NestedEntryKind.SwitchBranch)]
    [InlineData(NestedEntryKind.Leave)]
    public void NestedExternalEntryToMultiBlockDoWhileHeader_Raises(NestedEntryKind kind)
    {
        var function = ExternalEntryIntoMultiBlockLoop(entryTarget: 10, nestedEntry: kind);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void DecimalExternalHeaderEntry_RaisesOnEarlyPass()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Decimal.DecCalc", "VarDecCmpSub");
        Assert.NotNull(function);

        var stages = IrPasses.RunWithStages(function);
        var doWhileStages = stages.Where(stage => stage.PassName == "do-while").ToArray();

        Assert.Equal(2, doWhileStages.Length);
        Assert.Contains("DoWhileLoop", doWhileStages[0].Projection);
        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void DefaultPipeline_RunsDoWhileAgainAfterSlotStoreDiamond()
    {
        Type[] passTypes = [.. IrPasses.Default.Select(pass => pass.GetType())];
        int[] doWhileIndices =
        [
            .. passTypes
                .Select((type, index) => (type, index))
                .Where(item => item.type == typeof(DoWhileLoopPass))
                .Select(item => item.index),
        ];
        int slotStoreDiamondIndex =
            Array.IndexOf(passTypes, typeof(SlotStoreDiamondPass));
        int structuringIndex =
            Array.IndexOf(passTypes, typeof(StructuringPass));

        Assert.Equal(2, doWhileIndices.Length);
        Assert.InRange(slotStoreDiamondIndex, doWhileIndices[0] + 1, doWhileIndices[1] - 1);
        Assert.Equal(doWhileIndices[1] + 1, structuringIndex);
    }

    [Fact]
    public void Dragon4Resolution_DoesNotDependOnPreviewSevenOrdinal()
    {
        using var source = MetadataSource.Open(
            typeof(Dragon4PreviewSixOverloads).Assembly.Location);
        var handle = ResolveDragon4Method(
            source,
            typeof(Dragon4PreviewSixOverloads).FullName!);
        var expected = typeof(Dragon4PreviewSixOverloads).GetMethod(
            "Dragon4",
            System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ulong),
                typeof(int),
                typeof(uint),
                typeof(bool),
                typeof(int),
                typeof(bool),
                typeof(Span<byte>),
                typeof(int).MakeByRefType(),
            ],
            modifiers: null);

        Assert.NotNull(expected);
        Assert.Equal(expected.MetadataToken, MetadataTokens.GetToken(handle));
        Assert.Equal(1, IrImporter.Overloads(
            source,
            typeof(Dragon4PreviewSixOverloads).FullName!,
            "Dragon4").Single(candidate => candidate.IsPrivate).Index);
    }

    [Fact]
    public void SignatureResolution_SelectsExactPrivateAccessibility()
    {
        using var source = MetadataSource.Open(
            typeof(InterleavedVisibilityOverloads).Assembly.Location);
        var handle = ResolveMethodBySignature(
            source,
            typeof(InterleavedVisibilityOverloads).FullName!,
            nameof(InterleavedVisibilityOverloads.Marker),
            candidate =>
                candidate.ReturnType.Equals(TypeRef.CoreLib("System", "Int32"))
                && candidate.HasThis
                && candidate.HasBody
                && candidate.IsPrivate);
        var expected = typeof(InterleavedVisibilityOverloads).GetMethod(
            nameof(InterleavedVisibilityOverloads.Marker),
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(int)],
            modifiers: null);

        Assert.NotNull(expected);
        Assert.Equal(expected.MetadataToken, MetadataTokens.GetToken(handle));
    }

    [Fact]
    public void SwitchOwnedBreakExitingLateDoWhileCandidate_StaysOwnedBySwitch()
    {
        var function = SwitchCaseContainingBottomTestedRegion();

        new DoWhileLoopPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());

        new SwitchRaisingPass().Run(function, PassContext.None);
        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(2, function.Descendants.OfType<Break>().Count());

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        Assert.Equal(2, function.Descendants.OfType<Break>().Count());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedSwitchBreakInsideDoWhileCandidate_KeepsOwnerAndRaises()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var switchBody = new BlockContainer();
        var switchBlock = new Block(10);
        switchBlock.Add(new Break());
        switchBody.Add(switchBlock);

        var body = new BlockContainer();
        var header = new Block(10);
        header.Add(new Switch(
            new Constant(0, intType),
            [new SwitchSection([], isDefault: true, switchBody)]));
        body.Add(header);
        var bottom = new Block(20);
        bottom.Add(new ConditionalBranch(new LoadArgument(0, "repeat", boolType), 10));
        body.Add(bottom);
        var exit = new Block(30);
        exit.Add(new Return(new Constant(0, intType)));
        body.Add(exit);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Loops"),
            new MethodSignature(
                intType,
                [new Parameter("repeat", boolType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<Break>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedContainerSecondBackEdge_StaysFlat()
    {
        var function = BottomTestedRegionWithNestedContainerTransfer(targetOffset: 10);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedContainerNonCanonicalExit_StaysFlat()
    {
        var function = BottomTestedRegionWithNestedContainerTransfer(targetOffset: 40);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedLeaveToCanonicalDoWhileExit_Raises()
    {
        var function = BottomTestedRegionWithNestedLeave(targetOffset: 30);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedLeaveToNonCanonicalDoWhileExit_StaysFlat()
    {
        var function = BottomTestedRegionWithNestedLeave(targetOffset: 40);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedLeaveLeavingContainer_StaysFlat()
    {
        var function = BottomTestedRegionWithNestedLeave(targetOffset: 9999);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void OutsideLeaveLeavingContainer_DoesNotDeclineDoWhile()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var body = new BlockContainer();
        var loop = new Block(10);
        loop.Add(new ConditionalBranch(
            new LoadArgument(0, "repeat", boolType),
            10));
        body.Add(loop);
        var exit = new Block(20);
        exit.Add(new Leave(9999));
        body.Add(exit);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Loops"),
            new MethodSignature(
                intType,
                [new Parameter("repeat", boolType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    [Fact]
    public void TaskWhenAllPromiseNestedCanonicalLeave_RetainsDoWhile()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(
            source,
            "System.Threading.Tasks.Task.WhenAllPromise",
            "Invoke");
        Assert.NotNull(function);

        IrPasses.Run(function);

        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        function.CheckInvariant();
    }

    static IrFunction BottomTestedRegionWithNestedLeave(int targetOffset)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var nested = new Block(12);
        nested.Add(new Leave(targetOffset));

        var body = new BlockContainer();
        var header = new Block(10);
        header.Add(new IfStatement(
            new LoadArgument(0, "leave", boolType),
            nested,
            null));
        body.Add(header);
        var bottom = new Block(20);
        bottom.Add(new ConditionalBranch(
            new LoadArgument(1, "repeat", boolType),
            10));
        body.Add(bottom);
        var exit = new Block(30);
        exit.Add(new Return(new Constant(0, intType)));
        body.Add(exit);
        var alternateExit = new Block(40);
        alternateExit.Add(new Return(new Constant(1, intType)));
        body.Add(alternateExit);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Loops"),
            new MethodSignature(
                intType,
                [
                    new Parameter("leave", boolType),
                    new Parameter("repeat", boolType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction BottomTestedRegionWithNestedContainerTransfer(int targetOffset)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var switchBody = new BlockContainer();
        var switchBlock = new Block(12);
        switchBlock.Add(new Branch(targetOffset));
        switchBody.Add(switchBlock);

        var body = new BlockContainer();
        var header = new Block(10);
        header.Add(new Switch(
            new Constant(0, intType),
            [new SwitchSection([], isDefault: true, switchBody)]));
        body.Add(header);
        var bottom = new Block(20);
        bottom.Add(new ConditionalBranch(
            new LoadArgument(0, "repeat", boolType),
            10));
        body.Add(bottom);
        var exit = new Block(30);
        exit.Add(new Return(new Constant(0, intType)));
        body.Add(exit);
        var alternateExit = new Block(40);
        alternateExit.Add(new Return(new Constant(1, intType)));
        body.Add(alternateExit);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Loops"),
            new MethodSignature(
                intType,
                [new Parameter("repeat", boolType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }

    public enum NestedEntryKind
    {
        Branch,
        ConditionalBranch,
        SwitchBranch,
        Leave,
    }

    static IrFunction ExternalEntryIntoMultiBlockLoop(
        int entryTarget,
        NestedEntryKind? nestedEntry = null)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var body = new BlockContainer();

        var entry = new Block(0);
        if (nestedEntry is { } kind)
        {
            var then = new Block(0);
            then.Add(kind switch
            {
                NestedEntryKind.Branch => new Branch(entryTarget),
                NestedEntryKind.ConditionalBranch => new ConditionalBranch(
                    new LoadArgument(0, "enterAtTarget", boolType),
                    entryTarget),
                NestedEntryKind.SwitchBranch => new SwitchBranch(
                    new Constant(0, intType),
                    [entryTarget]),
                NestedEntryKind.Leave => new Leave(entryTarget),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            });
            entry.Add(new IfStatement(
                new LoadArgument(0, "enterAtTarget", boolType),
                then,
                null));
        }
        else
        {
            entry.Add(new ConditionalBranch(
                new LoadArgument(0, "enterAtTarget", boolType),
                entryTarget));
        }
        body.Add(entry);

        var preheader = new Block(5);
        preheader.Add(new Branch(10));
        body.Add(preheader);

        var head = new Block(10);
        head.Add(new StoreLocal(0, intType, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(0, intType), new Constant(1, intType))));
        body.Add(head);

        var middle = new Block(20);
        middle.Add(new StoreLocal(0, intType, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(0, intType), new Constant(2, intType))));
        middle.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, isUnsigned: false, new LoadLocal(0, intType), new Constant(10, intType)), 10));
        body.Add(middle);

        var exit = new Block(30);
        exit.Add(new Return(new LoadLocal(0, intType)));
        body.Add(exit);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Loops"),
            new MethodSignature(
                intType,
                [new Parameter("enterAtTarget", boolType)],
                HasThis: false,
                GenericParameterCount: 0),
            [intType],
            body);
    }

    static IrFunction SwitchCaseContainingBottomTestedRegion()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var body = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(new LoadArgument(0, "selector", intType), [10, 10]));
        body.Add(dispatch);

        var defaultBlock = new Block(4);
        defaultBlock.Add(new Branch(60));
        body.Add(defaultBlock);

        var header = new Block(10);
        header.Add(new ConditionalBranch(new LoadArgument(1, "stayInCase", boolType), 30));
        body.Add(header);

        var leaveCase = new Block(20);
        leaveCase.Add(new Branch(60));
        body.Add(leaveCase);

        var loopBody = new Block(30);
        loopBody.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        body.Add(loopBody);

        var bottom = new Block(40);
        bottom.Add(new ConditionalBranch(new LoadArgument(2, "repeat", boolType), 10));
        body.Add(bottom);

        var loopExit = new Block(50);
        loopExit.Add(new StoreLocal(0, intType, new Constant(999, intType)));
        loopExit.Add(new Branch(60));
        body.Add(loopExit);

        var join = new Block(60);
        join.Add(new Return(new LoadLocal(0, intType)));
        body.Add(join);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Loops"),
            new MethodSignature(
                intType,
                [
                    new Parameter("selector", intType),
                    new Parameter("stayInCase", boolType),
                    new Parameter("repeat", boolType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [intType],
            body);
    }

    [Fact]
    public void UpFrontLocal_InitializesToDefault()
    {
        // A local referenced with no defining store relies on IL's zero-init;
        // it declares `= default` so C#'s definite-assignment accepts it (a bare
        // `int V_0;` then `return V_0;` is CS0165).
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new LoadLocal(0, intType)));
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("V_0 = default;", output);
    }

    [Fact]
    public void UnionSwitchNullArm_ReadsPreserveDefaultInitializer()
    {
        // The null arm is a conditional value path. If it is skipped by the
        // definite-assignment walk, the printer drops this required initializer.
        var intType = TypeRef.CoreLib("System", "Int32");
        var petType = TypeRef.Definition("Synthetic", "UnionFixtures", "Pet", ValueTypeHint.ValueType);
        var catType = TypeRef.Definition("Synthetic", "UnionFixtures", "Cat");
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new UnionSwitchExpression(
            new LoadArgument(0, "pet", petType),
            [new UnionSwitchExpressionArm(catType, null, new Constant(1, intType))],
            nullValue: new LoadLocal(0, intType))));
        container.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("pet", petType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("int V_0 = default;", output);
        Assert.Contains("null => V_0", output);
    }

    [Fact]
    public void UnraisedLocalFunctionCall_RendersSanitizedNameNotSourceName()
    {
        // A call to the compiler-generated local function <Outer>g__Helper|0_0 that the
        // raising pass considered and declined — the seam is present but the body
        // cannot be imported, so no declaration of Helper is emitted. Rendering the
        // call as `Helper()` would therefore be a call to a method that exists nowhere:
        // CS0103 dressed as ordinary recovered C# (#3631). The sanitized spelling keeps
        // the compiler-generated identity visible instead, and the method degrades to
        // Partial.
        var voidType = TypeRef.CoreLib("System", "Void");
        var callee = new MethodRef(TypeRef.CoreLib("Synthetic", "Owner"),
            "<Outer>g__Helper|0_0", voidType, [], HasThis: false)
        {
            CompilerGenerated = MetadataFactState.Yes,
        };

        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false, [])));
        block.Add(new Return(null));
        container.Add(block);
        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "Owner"), signature, [], container);

        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(_ => null));
        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("Helper()", output);
        Assert.Contains("__Outer_g__Helper_0_0()", output);
        Assert.DoesNotContain("<", output);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Indirect_ThroughManagedRef_HasNoPointerStar()
    {
        // *x = *x + 1 where x is a ref int param: a managed ref dereferences
        // implicitly in C#, so no `*` — `*x` would be CS0193. The self-reading
        // store also folds to the increment operator (`x++`).
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        LoadArgument X() => new(0, "x", refInt);

        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreIndirect(intType, X(),
            new Binary(BinaryKind.Add, false, false, new LoadIndirect(intType, X()), new Constant(1, intType))));
        block.Add(new Return(null));
        container.Add(block);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("x", refInt)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("x++;", output);
        Assert.DoesNotContain("*", output);
    }

    [Fact]
    public void OrChainGuard_FoldsToSingleGuard()
    {
        // The csc OR-chain shape, built directly so the test is independent of
        // build-config codegen: two guards branch to the throw, the last
        // branches past it to the return. The fold raises one || guard.
        //   if (a < 0)  goto IL_000C;   // → throw
        //   if (b < 0)  goto IL_000C;   // → throw
        //   if (a <= b) goto IL_0010;   // → skip (return)
        //   IL_000C: throw null;
        //   IL_0010: return a;
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 12));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), new Constant(0, intType)), 12));
        var b2 = new Block(8);
        b2.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThanOrEqual, false, A(), B()), 16));
        var consequence = new Block(12);
        consequence.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        var join = new Block(16);
        join.Add(new Return(A()));
        foreach (var block in (Block[])[b0, b1, b2, consequence, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("if (a < 0 || b < 0 || a > b)", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void OrChainGuard_RootCarriesSetupStatements()
    {
        // The CoreLib Range.GetOffsetAndLength shape: the first guard block also
        // holds the method's unconditional prologue (computing the operands) just
        // ahead of its branch, so it is not a pure single-condition block. The
        // fold still combines the chain into one || guard, keeping that prologue
        // in place. Built directly so the test is independent of csc codegen:
        //   IL_0000: V_0 = a + b;             // prologue, then:
        //            if (a < 0)  goto IL_000C;   // → throw
        //   IL_0004: if (b < 0)  goto IL_000C;   // → throw
        //   IL_0008: if (a <= b) goto IL_0010;   // → skip (return)
        //   IL_000C: throw null;
        //   IL_0010: return V_0;
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new StoreLocal(0, intType, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, A(), B())));
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 12));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), new Constant(0, intType)), 12));
        var b2 = new Block(8);
        b2.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThanOrEqual, false, A(), B()), 16));
        var consequence = new Block(12);
        consequence.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        var join = new Block(16);
        join.Add(new Return(new LoadLocal(0, intType)));
        foreach (var block in (Block[])[b0, b1, b2, consequence, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        // The prologue survives, the chain folds into one || guard, no goto left.
        Assert.Contains("= a + b", output);
        Assert.Contains("if (a < 0 || b < 0 || a > b)", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void OrChainGuard_AllowsSiblingArmBetweenOneBlockConsequenceAndJoin()
    {
        // A nested OR-chain guard inside a larger diamond: the one-block
        // consequence branches to the join, while a sibling arm between that
        // consequence and the join is reached from outside the chain.
        //   V_0 = 0;
        //   if (a == 0) goto IL_0010;  // sibling arm
        //   if (b <= 0) goto IL_000C;  // → consequence
        //   if (c > 0)  goto IL_0014;  // → join
        //   IL_000C: V_0 = 1; goto IL_0014;
        //   IL_0010: V_0 = 2; goto IL_0014;
        //   IL_0014: return V_0;
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);
        LoadArgument C() => new(2, "c", intType);
        LoadLocal V0() => new(0, intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new StoreLocal(0, intType, new Constant(0, intType)));
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, A(), new Constant(0, intType)), 16));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThanOrEqual, false, B(), new Constant(0, intType)), 12));
        var b2 = new Block(8);
        b2.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, C(), new Constant(0, intType)), 20));
        var consequence = new Block(12);
        consequence.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        consequence.Add(new Branch(20));
        var sibling = new Block(16);
        sibling.Add(new StoreLocal(0, intType, new Constant(2, intType)));
        sibling.Add(new Branch(20));
        var join = new Block(20);
        join.Add(new Return(V0()));
        foreach (var block in (Block[])[b0, b1, b2, consequence, sibling, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType), new Parameter("c", intType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("if (b <= 0 || c <= 0)", output);
        Assert.Contains("V_0 = 2;", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void OrChainGuard_RejectsExternalEntryIntoOneBlockConsequence()
    {
        // The relaxation above is only for sibling arms that remain as ordinary
        // blocks. A sibling branch into the consequence itself must still block
        // the fold, since the structurer may consume that consequence as the
        // folded guard's arm.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);
        LoadArgument C() => new(2, "c", intType);
        LoadLocal V0() => new(0, intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new StoreLocal(0, intType, new Constant(0, intType)));
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, A(), new Constant(0, intType)), 16));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThanOrEqual, false, B(), new Constant(0, intType)), 12));
        var b2 = new Block(8);
        b2.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, C(), new Constant(0, intType)), 20));
        var consequence = new Block(12);
        consequence.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        consequence.Add(new Branch(20));
        var sibling = new Block(16);
        sibling.Add(new Branch(12));
        var join = new Block(20);
        join.Add(new Return(V0()));
        foreach (var block in (Block[])[b0, b1, b2, consequence, sibling, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType), new Parameter("c", intType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);
        var stepper = new Stepper(enabled: true);

        new OrChainGuardPass().Run(function, new PassContext(stepper));

        Assert.Equal(0, stepper.Count);
    }

    [Fact]
    public void PastRegionTerminatorTarget_InlinesAsGuardExit()
    {
        // A #1031 shared-forward-merge slice from Array.CopyImpl: an outer guard
        // skips to the fast path, while the inner region can jump past that fast
        // path to a later terminating slow path. Cloning that terminating target
        // into the inner guard lets the remaining control flow nest.
        //   if (a > 0) goto IL_0010;   // fast path
        //   V_0 = b - 1;
        //   V_1 = V_0;                    // value used by the slow path
        //   if (V_0 != 0) goto IL_0018;   // slow path past region
        //   IL_0010: return a + 1;
        //   IL_0018: if (b >= 0) goto IL_0020;
        //   IL_001C: throw null;
        //   IL_0020: return b;
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);
        LoadLocal V0() => new(0, intType);
        LoadLocal V1() => new(1, intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, A(), new Constant(0, intType)), 16));
        var b1 = new Block(4);
        b1.Add(new StoreLocal(0, intType, new Binary(BinaryKind.Subtract, isChecked: false, isUnsigned: false, B(), new Constant(1, intType))));
        b1.Add(new StoreLocal(1, intType, V0()));
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.NotEqual, false, V0(), new Constant(0, intType)), 24));
        var fast = new Block(16);
        fast.Add(new Return(new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, A(), new Constant(1, intType))));
        var slowGuard = new Block(24);
        slowGuard.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThanOrEqual, false, B(), new Constant(0, intType)), 32));
        var slowThrow = new Block(28);
        slowThrow.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        var slowReturn = new Block(32);
        slowReturn.Add(new Return(new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, V1(), B())));
        foreach (var block in (Block[])[b0, b1, fast, slowGuard, slowThrow, slowReturn])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType, intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal("""
            int V_0;

            if (a <= 0)
            {
                V_0 = b - 1;
                if (V_0 != 0)
                {
                    if (b < 0)
                    {
                        throw null;
                    }
                    return V_0 + b;
                }
            }
            return a + 1;
            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void PastRegionTerminatorTarget_UnconditionalTargetKeepsLabel()
    {
        // Near-miss: the past-region target is also reached by an unconditional
        // branch. Inlining would erase a label that a real goto still needs, so
        // the container must stay flat.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);
        LoadLocal V0() => new(0, intType);
        LoadLocal V1() => new(1, intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, A(), new Constant(0, intType)), 16));
        var b1 = new Block(4);
        b1.Add(new StoreLocal(0, intType, new Binary(BinaryKind.Subtract, isChecked: false, isUnsigned: false, B(), new Constant(1, intType))));
        b1.Add(new StoreLocal(1, intType, V0()));
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.NotEqual, false, V0(), new Constant(0, intType)), 24));
        var fast = new Block(16);
        fast.Add(new Branch(24)); // external unconditional edge into the slow target
        var slowGuard = new Block(24);
        slowGuard.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThanOrEqual, false, B(), new Constant(0, intType)), 32));
        var slowThrow = new Block(28);
        slowThrow.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        var slowReturn = new Block(32);
        slowReturn.Add(new Return(new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, V1(), B())));
        foreach (var block in (Block[])[b0, b1, fast, slowGuard, slowThrow, slowReturn])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType, intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("goto", output);
        Assert.Contains("IL_0018", output);
    }

    [Fact]
    public void OrChainDiamond_FoldsToSingleConditionDiamond()
    {
        // The csc OR-chain diamond shape (HashCode.Combine / ValueTuple, the
        // `(a || b) ? T : F` family): two guards branch to the shared true arm,
        // an unconditional false arm jumps past it to the merge, then the true
        // arm. Built directly so the test is independent of csc codegen:
        //   if (a < 0) goto IL_000C;   // → true arm
        //   if (b < 0) goto IL_000C;   // → true arm
        //   IL_0008: V_0 = 99; goto IL_0010;   // false arm, jumps past
        //   IL_000C: V_0 = 1;                  // true arm
        //   IL_0010: return V_0;
        // The fold collapses the two guards into one || conditional, leaving the
        // single-conditional diamond the structuring pass raises into if/else.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 12));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), new Constant(0, intType)), 12));
        var falseArm = new Block(8);
        falseArm.Add(new StoreLocal(0, intType, new Constant(99, intType)));
        falseArm.Add(new Branch(16));
        var trueArm = new Block(12);
        trueArm.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        var join = new Block(16);
        join.Add(new Return(new LoadLocal(0, intType)));
        foreach (var block in (Block[])[b0, b1, falseArm, trueArm, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal("""
            if (!(a < 0 || b < 0))
            {
                return 99;
            }
            else
            {
                return 1;
            }
            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void ReturnDispatch_TypeDispatchGuardReturns_Structures()
    {
        // OperationMemorySafetyContract.RequiresUnsafeOperation is a real #921
        // nonnested-forward-guards representative: csc lowers a type-test dispatch
        // to guard branches whose arms either return directly or feed a tiny
        // stack-slot return diamond. Folding the inner return diamonds and isolated
        // return tail exposes return leaves that the structurer can inline,
        // eliminating the goto soup.
        using var source = MetadataSource.Open(
            typeof(OperationMemorySafetyContract).Assembly.Location);
        var function = IrImporter.Import(
            source,
            "ILInspector.Decompiler.Pipeline.OperationMemorySafetyContract",
            "RequiresUnsafeOperation");
        Assert.NotNull(function);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain(function.Descendants.OfType<ConditionalBranch>(), _ => true);
        Assert.Contains("if (node is Call call)", output);
        Assert.Contains(
            "return CallRendersPointerDereference(call);",
            output);
    }

    [Fact]
    public void OrChainDiamond_InnerGuardWithPrologue_StaysFlat()
    {
        // The adversarial near-miss: the inner guard carries a prologue (V_0 = 5)
        // ahead of its conditional, so it is NOT a pure single-condition block.
        // This is the HashCode.Combine `value?.GetHashCode() ?? 0` shape, where
        // the second null test reloads the struct first. Folding to `a || b` would
        // hoist that prologue out of short-circuit order (it must run only when
        // the first guard fell through, and cannot live inside the || expression),
        // so the pass refuses and the container stays flat. Differs from the
        // positive fixture by exactly one statement on the inner guard.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 12));
        var b1 = new Block(4);
        b1.Add(new StoreLocal(0, intType, new Constant(5, intType)));   // prologue
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), new Constant(0, intType)), 12));
        var falseArm = new Block(8);
        falseArm.Add(new StoreLocal(0, intType, new Constant(99, intType)));
        falseArm.Add(new Branch(16));
        var trueArm = new Block(12);
        trueArm.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        var join = new Block(16);
        join.Add(new Return(new LoadLocal(0, intType)));
        foreach (var block in (Block[])[b0, b1, falseArm, trueArm, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        // The prologue forbids the fold: no combined || guard, and the always-correct
        // flat goto form survives.
        Assert.DoesNotContain("a < 0 || b < 0", output);
        Assert.Contains("V_0 = 5", output);
        Assert.Contains("goto", output);
    }

    [Fact]
    public void OrChainDiamond_GuardsToDifferentTargets_NotFolded()
    {
        // Near-miss: the two guards branch to DIFFERENT targets (an `&&`-mixed
        // dispatch, not a shared-true-arm OR chain). The run extends only while
        // the next guard targets the same block, so a single guard is all it sees
        // — below the two-guard bar — and nothing folds. No `a < 0 || …` guard.
        //   if (a < 0) goto IL_0008;   // → one place
        //   if (b < 0) goto IL_000C;   // → a DIFFERENT place
        //   IL_0008: return 7;
        //   IL_000C: return 9;
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 8));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), new Constant(0, intType)), 12));
        var arm1 = new Block(8);
        arm1.Add(new Return(new Constant(7, intType)));
        var arm2 = new Block(12);
        arm2.Add(new Return(new Constant(9, intType)));
        foreach (var block in (Block[])[b0, b1, arm1, arm2])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("a < 0 || b < 0", output);
    }

    [Fact]
    public void SlotDiamondDispatch_FoldsArmsAndRaisesDispatch()
    {
        // A clustered-case switch whose bool arms csc lowers to returned slot
        // diamonds. Without SlotDiamondPass the diamond arm is not a straight-line
        // terminator, so the dispatch cannot inline it as a leaf and the whole
        // method stays flat goto soup (issue #912). The pass folds each diamond to
        // `return c ? a : b`, making the arm a terminator so the dispatch fully
        // raises — no surviving goto. ShortCircuitTernaryPass then canonicalizes the
        // recovered when-true-false diamond arm (`(y < 16) ? false : (y <= 31)`) to
        // the `&&` it spells (`y >= 16 && y <= 31`).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SlotDiamondDispatch));
        Assert.NotNull(function);
        IrPasses.Run(function!);
        string output = CSharpPrinter.Print(function!).Output!;

        Assert.DoesNotContain("goto", output);
        Assert.Contains("return y >= 16 && y <= 31;", output);   // a folded diamond arm survives as a short-circuit terminator
    }

    [Fact]
    public void ComparisonTreeBoolGuardArm_FoldsSharedFalseTail()
    {
        // A sparse comparison tree dispatches to a bool arm with guarded range
        // checks. The guarded arm branches to one shared false-return tail and
        // falls through to a true return. Fold only that arm to a straight-line
        // `return y >= 64 && y <= 127;` terminator so the outer tree can inline it.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        LoadArgument X() => new(0, "x", intType);
        LoadArgument Y() => new(1, "y", intType);
        Constant C(int value) => new(value, intType);
        Block BoolReturn(int offset, bool value)
        {
            var block = new Block(offset);
            block.Add(new StoreLocal(0, boolType, new Constant(value, boolType)));
            block.Add(new Return(new LoadLocal(0, boolType)));
            return block;
        }

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(0)), 20));
        var b4 = new Block(4);
        b4.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(1)), 44));
        var b8 = new Block(8);
        b8.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(2)), 48));
        var b12 = new Block(12);
        b12.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(3)), 52));
        var defaultBlock = BoolReturn(16, false);
        var rangeLow = new Block(20);
        rangeLow.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, Y(), C(64)), 60));
        var rangeHigh = new Block(24);
        rangeHigh.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, Y(), C(127)), 60));
        var success = BoolReturn(28, true);
        foreach (var block in (Block[])[
            b0, b4, b8, b12, defaultBlock, rangeLow, rangeHigh, success,
            BoolReturn(44, true), BoolReturn(48, false), BoolReturn(52, true), BoolReturn(60, false)])
        {
            container.Add(block);
        }

        var signature = new MethodSignature(boolType,
            [new Parameter("x", intType), new Parameter("y", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [boolType], container);

        new ComparisonTreeBoolArmPass().Run(function, PassContext.None);

        var arm = Assert.Single(function.Body.Blocks, b => b.StartOffset == 20);
        var ret = Assert.IsType<Return>(Assert.Single(arm.Children));
        var and = Assert.IsType<LogicalBinary>(ret.Value);
        Assert.Equal(LogicalKind.And, and.Kind);
        var lower = Assert.IsType<Comparison>(and.Left);
        var upper = Assert.IsType<Comparison>(and.Right);
        Assert.Equal(ComparisonKind.GreaterThanOrEqual, lower.Kind);
        Assert.Equal(ComparisonKind.LessThanOrEqual, upper.Kind);
        Assert.DoesNotContain(function.Body.Blocks, b => b.StartOffset is 24 or 28 or 60);
    }

    [Fact]
    public void ComparisonTreeBoolGuardArm_ShortCircuitTailBelowTreeGate_NotFolded()
    {
        // This is the ordinary `if (a && b) return true; return false;` false-exit
        // tail shape. It must not fold unless the surrounding container is a real
        // comparison tree; otherwise the guard combiner canary would split.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);
        Constant C(int value) => new(value, intType);
        Block BoolReturn(int offset, bool value)
        {
            var block = new Block(offset);
            block.Add(new StoreLocal(0, boolType, new Constant(value, boolType)));
            block.Add(new Return(new LoadLocal(0, boolType)));
            return block;
        }

        var container = new BlockContainer();
        var low = new Block(0);
        low.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), C(0)), 12));
        var high = new Block(4);
        high.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), C(0)), 12));
        foreach (var block in (Block[])[low, high, BoolReturn(8, true), BoolReturn(12, false)])
            container.Add(block);

        var signature = new MethodSignature(boolType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [boolType], container);

        new ComparisonTreeBoolArmPass().Run(function, PassContext.None);

        Assert.Equal([0, 4, 8, 12], function.Body.Blocks.Select(b => b.StartOffset).ToArray());
        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
    }

    [Fact]
    public void SplitSlotStoreDiamond_FoldsToIfElseBeforeContinuation()
    {
        // CSharpPrinter.NumericConstant hits this #1081 sibling of the returned
        // slot diamond: the true arm stores the carrier slot and branches to the
        // continuation, while the false arm first performs setup and then stores
        // the same slot. Folding the store join into an if/else lets the later
        // continuation structure normally.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);

        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 8));
        var falseSetup = new Block(4);
        falseSetup.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        falseSetup.Add(new Branch(12));
        var trueStore = new Block(8);
        trueStore.Add(new StoreStackSlot(0, new Constant(2, intType)));
        trueStore.Add(new Branch(16));
        var falseStore = new Block(12);
        falseStore.Add(new StoreStackSlot(0, new LoadLocal(0, intType)));
        var continuation = new Block(16);
        continuation.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.GreaterThan, false, new LoadStackSlot(0, intType), new Constant(1, intType)), 24));
        var low = new Block(20);
        low.Add(new Return(new Constant(3, intType)));
        var high = new Block(24);
        high.Add(new Return(new Constant(4, intType)));
        foreach (var block in (Block[])[head, falseSetup, trueStore, falseStore, continuation, low, high])
            container.Add(block);

        var signature = new MethodSignature(intType, [new Parameter("a", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("goto", output);
        Assert.Contains("if (a < 0)", output);
        Assert.Contains("else", output);
        Assert.Contains("if (S_0 <= 1)", output);
    }

    [Fact]
    public void GuardSlotStore_FoldsBeforeJoin()
    {
        // The small guard-store sibling left in ConstantText's fallback tail:
        // setup initializes the carried slot, the conditional skips the fallback
        // store, and the join consumes the slot. Folding the fallthrough arm into
        // an if keeps the continuation label-free.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);

        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new StoreStackSlot(0, A()));
        head.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, A(), new Constant(0, intType)), 8));
        var fallback = new Block(4);
        fallback.Add(new StoreStackSlot(0, new Constant(0, intType)));
        var join = new Block(8);
        join.Add(new Return(new LoadStackSlot(0, intType)));
        foreach (var block in (Block[])[head, fallback, join])
            container.Add(block);

        var signature = new MethodSignature(intType, [new Parameter("a", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("goto", output);
        Assert.Contains("if (a <= 0)", output);
        Assert.Contains("S_0 = 0", output);
    }

    [Fact]
    public void MaterializeBooleanSlots_NegatedBoolSlot_RetypesSiblingConstant()
    {
        // A stack slot holds a bool (a > b), then is negated via csc's `ceq 0`
        // idiom (S = S == 0) and returned. MaterializeBooleanSlots retypes the slot
        // to bool; a load-only retype would leave the sibling `0` an int — the
        // CS0019 `bool == int` (the StackAllocSpanPass::Run defect found via the
        // #1011 stepper audit). The fix flips the 0 to false alongside the load, so
        // FoldBoolConstantComparison reduces `S == false` to the negation.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Comparison(ComparisonKind.GreaterThan, false,
            new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType))));
        block.Add(new StoreStackSlot(0, new Comparison(ComparisonKind.Equal, false,
            new LoadStackSlot(0, intType), new Constant(0, intType))));
        block.Add(new Return(new LoadStackSlot(0, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("== 0", output);   // no `bool == int` (CS0019)
    }

    [Fact]
    public void FoldBoolConstantComparison_NonSlotBoolEqualsIntZero_FoldsToNegation()
    {
        // The general bool/int-join fix (#1031 follow-through to #1019): csc's
        // `ceq 0` negation idiom over a non-slot bool — here a comparison result
        // `(a > b) == 0` — must fold to `!(a > b)`, not leave the CS0019
        // `bool == int`. FoldBoolConstantComparison now accepts the 0/1 int
        // spelling, not only a bool constant (the LibrarySections.CanRender defect).
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var block = new Block(0);
        block.Add(new Return(new Comparison(ComparisonKind.Equal, false,
            new Comparison(ComparisonKind.GreaterThan, false,
                new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType)),
            new Constant(0, intType))));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("== 0", output);   // no `bool == int` (CS0019)
    }

    [Fact]
    public void SharedThrowGuards_InlineAsGuardClauses()
    {
        // Two guards branch to one shared throw block — the shape that breaks
        // strict nesting (the throw is a join with two predecessors). Built
        // directly so the test is independent of csc codegen:
        //   if (a < 0) goto IL_000C;   // → throw
        //   if (b < 0) goto IL_000C;   // → throw
        //   return a;
        //   IL_000C: throw null;
        // The structurer inlines a copy of the throw into each guard (the
        // taken path is the throw, so the condition is not negated) and drops
        // the now-dead shared block — no goto, the return survives.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 12));
        var b1 = new Block(4);
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, B(), new Constant(0, intType)), 12));
        var ret = new Block(8);
        ret.Add(new Return(A()));
        var consequence = new Block(12);
        consequence.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        foreach (var block in (Block[])[b0, b1, ret, consequence])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal("""
            if (a < 0)
            {
                throw null;
            }
            if (b < 0)
            {
                throw null;
            }
            return a;
            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void SharedThrow_SingleGuardFallenInto_InlinesAsGuardClauses()
    {
        // The OR-to-throw shape whose throw block has ONE conditional predecessor
        // but is also fallen into — the CheckTicksRange / `if (A || B) throw;`
        // case where the inner guard carries a prologue, so OrChainGuardPass
        // (which needs pure inner guards) cannot flatten it. Built directly so the
        // test is independent of csc codegen:
        //   if (a < 0) goto IL_000C;     // → throw  (one conditional predecessor)
        //   V_0 = 7;                     // inner-guard prologue
        //   if (b > 100) goto IL_0010;   // → return; falls through to the throw
        //   IL_000C: throw null;         // fallen into AND one conditional pred
        //   IL_0010: return a;
        // The structurer inlines a copy of the throw into the a-guard clause and
        // keeps the fallthrough copy as the b-guard (negated, since the taken
        // branch returns) — no goto, the return survives. Without the
        // one-conditional-plus-fallen relaxation this throw (one conditional
        // predecessor, below the >= 2 bar) is left as goto-soup.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument A() => new(0, "a", intType);
        LoadArgument B() => new(1, "b", intType);

        var container = new BlockContainer();
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.LessThan, false, A(), new Constant(0, intType)), 12));
        var b1 = new Block(4);
        b1.Add(new StoreLocal(0, intType, new Constant(7, intType)));
        b1.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, B(), new Constant(100, intType)), 16));
        var consequence = new Block(12);
        consequence.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        var ret = new Block(16);
        ret.Add(new Return(A()));
        foreach (var block in (Block[])[b0, b1, consequence, ret])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal("""
            if (a < 0)
            {
                throw null;
            }
            int V_0 = 7;
            if (b <= 100)
            {
                throw null;
            }
            return a;
            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void Clone_DeepCopiesSubtree_AndIsIndependent()
    {
        // Clone produces a detached, structurally identical deep copy: distinct
        // node references throughout, shared (immutable) payload, and edits to
        // the original do not disturb the copy.
        var intType = TypeRef.CoreLib("System", "Int32");
        var original = new Comparison(ComparisonKind.LessThan, false,
            new LoadArgument(0, "a", intType), new Constant(0, intType));

        var copy = (Comparison)original.Clone();

        Assert.NotSame(original, copy);
        Assert.Null(copy.Parent);
        AssertStructurallyEqualButDistinct(original, copy);

        // Mutating the original leaves the clone intact.
        original.DetachChildren();
        Assert.Equal(2, copy.Children.Count);
        Assert.Equal("Comparison.LessThan", copy.Describe());
    }

    static void AssertStructurallyEqualButDistinct(IrNode a, IrNode b)
    {
        Assert.NotSame(a, b);
        Assert.Equal(a.Describe(), b.Describe());
        Assert.Equal(a.Children.Count, b.Children.Count);
        for (int i = 0; i < a.Children.Count; i++)
        {
            Assert.Same(b, b.Children[i].Parent);
            Assert.Equal(i, b.Children[i].ChildIndex);
            AssertStructurallyEqualButDistinct(a.Children[i], b.Children[i]);
        }
    }

    [Fact]
    public void JumpTable_RaisesToSwitch()
    {
        // switch (x) goto [IL_0008, IL_000C]; IL_0004: default return; cases return.
        // The jump table raises to a C# switch with cases and a default.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new SwitchBranch(new LoadArgument(0, "x", intType), [8, 12]));
        var fallthrough = new Block(4);
        fallthrough.Add(new Return(new Constant(-1, intType)));
        var case0 = new Block(8);
        case0.Add(new Return(new Constant(10, intType)));
        var case1 = new Block(12);
        case1.Add(new Return(new Constant(20, intType)));
        foreach (var block in (Block[])[head, fallthrough, case0, case1])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("switch (x)", output);
        Assert.Contains("case 0:", output);
        Assert.Contains("case 1:", output);
        Assert.Contains("default:", output);
        Assert.Contains("return 10;", output);
        Assert.Contains("return 20;", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void JumpTable_DefaultSharesTerminatorCase_FoldsDefaultOntoCase()
    {
        // switch (x) goto [IL_0008, IL_000C, IL_000C]; default IL_0004 is a bare
        // `goto IL_000C` into a case body that throws. The shared throw is both a
        // direct case target (label 1 and 2) and the default's destination, so the
        // `default:` label folds onto that case section (`case 1: case 2: default:
        // throw;`) — one block backs one section, no duplication. This is the
        // System.Enum::GetNamesNoCopy shape. Without the fold the switch stays flat.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new SwitchBranch(new LoadArgument(0, "x", intType), [8, 12, 12]));
        var dispatch = new Block(4);
        dispatch.Add(new Branch(12));
        var case0 = new Block(8);
        case0.Add(new Return(new Constant(10, intType)));
        var sharedThrow = new Block(12);
        sharedThrow.Add(new Throw(new Constant(null, TypeRef.CoreLib("System", "Object"))));
        foreach (var block in (Block[])[head, dispatch, case0, sharedThrow])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("switch (x)", output);
        Assert.Contains("case 1:", output);
        Assert.Contains("case 2:", output);
        Assert.Contains("default:", output);
        Assert.Contains("throw", output);
        Assert.Contains("return 10;", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void JumpTable_MultiBlockCaseSection_RaisesInteriorIf()
    {
        // switch (x) goto [IL_0008, IL_0010]; default IL_0004 returns. case 0 is a
        // single block, but case 1 (IL_0010) carries an interior `if` — a
        // conditional branch whose two arms each return — so its section spans
        // three blocks. The section-as-single-entry-region relaxation wraps the
        // whole span as the case body, and the structuring pass raises the `if`,
        // leaving no goto. This is the System.ValueType::GetHashCode shape (a case
        // body with its own control flow). Without it the switch stays flat.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new SwitchBranch(new LoadArgument(0, "x", intType), [8, 16]));
        var fallthrough = new Block(4);
        fallthrough.Add(new Return(new Constant(-1, intType)));
        var case0 = new Block(8);
        case0.Add(new Return(new Constant(10, intType)));
        var case1 = new Block(16);   // IL_0010 — the case body's `if`
        case1.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.GreaterThan, false,
                new LoadArgument(0, "x", intType), new Constant(0, intType)), 24));
        var case1False = new Block(20);   // IL_0014
        case1False.Add(new Return(new Constant(20, intType)));
        var case1True = new Block(24);    // IL_0018
        case1True.Add(new Return(new Constant(30, intType)));
        foreach (var block in (Block[])[head, fallthrough, case0, case1, case1False, case1True])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("switch (x)", output);
        Assert.Contains("case 0:", output);
        Assert.Contains("case 1:", output);
        Assert.Contains("default:", output);
        Assert.Contains("if (", output);
        Assert.Contains("return 10;", output);
        Assert.Contains("return 20;", output);
        Assert.Contains("return 30;", output);
        Assert.Contains("return -1;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void JumpTable_DefaultBlockIsCaseTarget_FoldsDefaultOntoCase()
    {
        // switch (x) goto [IL_0008, IL_0004]; the table's fall-through default is
        // IL_0004 — which is also case 1's target. The `default:` label folds onto
        // that case section (`case 1: default: …`): the block right after the
        // switch is itself a case body, so no separate default is built. This is
        // the ReaderWriterLockSlim.SpinLock::IsEnterDeprioritized shape. Without
        // the fold the block is double-owned and the switch stays flat.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new SwitchBranch(new LoadArgument(0, "x", intType), [8, 4]));
        var case1AndDefault = new Block(4);   // IL_0004 — both case 1 and the default
        case1AndDefault.Add(new Return(new Constant(99, intType)));
        var case0 = new Block(8);
        case0.Add(new Return(new Constant(10, intType)));
        foreach (var block in (Block[])[head, case1AndDefault, case0])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("switch (x)", output);
        Assert.Contains("case 0:", output);
        Assert.Contains("case 1:", output);
        Assert.Contains("default:", output);
        Assert.Contains("return 10;", output);
        Assert.Contains("return 99;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void JumpTable_DefaultRoutesIntoCases_RaisesWithConditionalBreak()
    {
        // switch (x) goto [J, J, T, J] where J (IL_0014) is the post-switch join
        // — itself a case target — and T (IL_000C) is a throw case. The default
        // (IL_0008) is a conditional ladder that either breaks to J (`x == 100`)
        // or falls into the throw case T. The cases targeting J become empty
        // `break;` sections, the default's branch to J becomes `if (x == 100)
        // break;`, and the fall-through into T duplicates its `throw`. This is the
        // TraceLoggingMetadataCollector::AddArray shape. Without it the switch is
        // left flat (`switch (x) goto [...]`).
        var intType = TypeRef.CoreLib("System", "Int32");
        var objType = TypeRef.CoreLib("System", "Object");
        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new SwitchBranch(new LoadArgument(0, "x", intType), [20, 20, 12, 20]));
        var defaultLadder = new Block(8);   // IL_0008 — default: if (x == 100) break; else throw
        defaultLadder.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.Equal, false, new LoadArgument(0, "x", intType), new Constant(100, intType)),
            20));
        var throwCase = new Block(12);      // IL_000C — case 2, and the default's fall-through
        throwCase.Add(new Throw(new Constant(null, objType)));
        var join = new Block(20);           // IL_0014 — the join, also cases 0/1/3
        join.Add(new Return(new LoadArgument(0, "x", intType)));
        foreach (var block in (Block[])[head, defaultLadder, throwCase, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("switch (x)", output);
        Assert.Contains("case 0:", output);
        Assert.Contains("case 3:", output);
        Assert.Contains("case 2:", output);
        Assert.Contains("default:", output);
        Assert.Contains("if (x == 100)", output);
        Assert.Contains("break;", output);
        Assert.Contains("throw null;", output);
        Assert.Contains("return x;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void JumpTable_ValueBlocksReturnedAtJoin_RaisesToSwitchExpression()
    {
        // switch (ch - 9) goto [T, T, F, F, T] where each target is a one-block
        // value block assigning a single bool local and converging on the join
        // `return local`. The default (IL_0008) is a conditional choosing between
        // the same two value blocks. The whole dispatch is one value, so it raises
        // to `return (ch - 9) switch { 0 or 1 or 4 => true, 2 or 3 => false, _ => … };`
        // — the AssemblyNameParser::IsWhiteSpace shape. Without it the switch is
        // left flat (`switch (ch - 9) goto [...]`), which is invalid C#.
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new SwitchBranch(
            new Binary(BinaryKind.Subtract, false, false, new LoadArgument(0, "ch", intType), new Constant(9, intType)),
            [14, 14, 20, 20, 14]));
        var defaultChoice = new Block(8);   // IL_0008 — default: if (ch != 32) -> false-block else true-block
        defaultChoice.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.NotEqual, false, new LoadArgument(0, "ch", intType), new Constant(32, intType)),
            20));
        var trueBlock = new Block(14);      // IL_000E — cases 0/1/4, and the default's fall-through
        trueBlock.Add(new StoreLocal(0, boolType, new Constant(true, boolType)));
        trueBlock.Add(new Branch(24));
        var falseBlock = new Block(20);     // IL_0014 — cases 2/3, and the default's taken branch
        falseBlock.Add(new StoreLocal(0, boolType, new Constant(false, boolType)));
        var join = new Block(24);           // IL_0018 — the join: returns the assigned local
        join.Add(new Return(new LoadLocal(0, boolType)));
        foreach (var block in (Block[])[head, defaultChoice, trueBlock, falseBlock, join])
            container.Add(block);

        var signature = new MethodSignature(boolType,
            [new Parameter("ch", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [boolType], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("switch", output);
        Assert.Contains("0 or 1 or 4 => true", output);
        Assert.Contains("2 or 3 => false", output);
        Assert.Contains("_ =>", output);
        Assert.DoesNotContain("case ", output);     // an expression, not a statement
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void TopTestedLoopWithBreak_RaisesBreak()
    {
        // A forward exit out of a top-tested (while/for) loop body raises to a
        // structured `if (...) break;` — the whole method de-gotos, the for
        // loop composes on top.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.LoopWithBreak), source);

        Assert.Contains("for (", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void TypeOfAndOperatorSugar_PrintSourceForms()
    {
        // typeof folding plus op_Equality spelling: the generic-dispatch
        // idiom prints as the source writes it.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Numerics.Vector", "AllWhereAllBitsSet");
        Assert.NotNull(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Contains("typeof(T) == typeof(float)", output);
        Assert.DoesNotContain("GetTypeFromHandle", output);
        Assert.DoesNotContain("op_Equality", output);
    }

    [Fact]
    public void DefaultInitialization_MergesIntoDeclaration()
    {
        // initobj over a local address: 'CancellationToken V_0 = default;',
        // not '*(ref V_0) = default(...)' nor a separate declaration.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Threading.CancellationToken", "get_None");
        Assert.NotNull(function);
        string output = CSharpPrinter.PrintRaised(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal("CancellationToken V_0 = default;\nreturn V_0;", output);
    }

    [Fact]
    public void Passes_PreserveInvariants_AcrossCoreLibSample()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        foreach (var (type, method) in new[]
        {
            ("System.String", "IsNullOrEmpty"),
            ("System.Collections.Generic.Dictionary`2", "ContainsValue"),
            ("System.Text.StringBuilder", "Clear"),
        })
        {
            var function = IrImporter.Import(source, type, method);
            Assert.NotNull(function);
            IrPasses.Run(function);  // CheckInvariant runs after every pass in debug
            Assert.True(CSharpPrinter.Print(function).Succeeded);
        }
    }
}
