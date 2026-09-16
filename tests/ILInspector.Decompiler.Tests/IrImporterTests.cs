using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

public class IrImporterTests
{
    static IrFunction ImportFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }

    static IrFunction ImportFixtureWithTrustedPlatformContext(string methodName)
    {
        using var context = new MetadataContext(TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, context: context);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }

    static Block SingleBlock(IrFunction function)
        => (Block)Assert.Single(function.Body.Children);

    [Fact]
    public void PublicOnlyResolution_SkipsNonPublicSameNameOverload()
    {
        // `internal VisibilityOverload()` is declared before the public
        // `VisibilityOverload(int)`. With publicOnly resolution, overload 0 must
        // be the PUBLIC method (`return 2;`), not the internal one (`return 1;`).
        // MethodAttributes.Public is the value 6 within MemberAccessMask, so a
        // naive `(attrs & Public) == 0` filter lets internal (3) and protected
        // (4) through and selects the wrong method.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, "VisibilityOverload",
            overloadIndex: 0, publicOnly: true);

        Assert.NotNull(function);
        var ret = Assert.IsType<Return>(Assert.Single(SingleBlock(function).Children));
        var constant = Assert.IsType<Constant>(ret.Value);
        Assert.Equal(2, constant.Value);
    }

    [Fact]
    public void Add_BuildsTypedExpressionTree()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));

        var ret = Assert.IsType<Return>(Assert.Single(SingleBlock(function).Children));
        var binary = Assert.IsType<Binary>(ret.Value);
        Assert.Equal(BinaryKind.Add, binary.Kind);
        Assert.Equal("int", binary.ResultType?.ToDisplayString());
        Assert.IsType<LoadArgument>(binary.Left);
        Assert.IsType<LoadArgument>(binary.Right);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Diagnostics);
        function.CheckInvariant();
    }

    [Fact]
    public void ImportedFunction_SurvivesSourceDisposal()
    {
        IrFunction function;
        using (var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location))
        {
            function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Add))!;
        }

        string dump = IrPrinter.Dump(function);
        Assert.Contains("Binary.Add", dump);
        Assert.Contains("LoadArgument", dump);
        Assert.Contains("fidelity: Full", dump);
    }

    [Fact]
    public void ReplaceWith_RewiresParentAndSlot()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));
        var binary = (Binary)((Return)SingleBlock(function).Children[0]).Value!;
        var left = binary.Left;

        var constant = new Constant(42, TypeRef.CoreLib("System", "Int32"));
        left.ReplaceWith(constant);

        Assert.Same(constant, binary.Left);
        Assert.Same(binary, constant.Parent);
        Assert.Equal(0, constant.ChildIndex);
        Assert.Null(left.Parent);
        Assert.Equal(-1, left.ChildIndex);
        function.CheckInvariant();
    }

    [Fact]
    public void Adoption_RejectsNodesThatAlreadyHaveParents()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));
        var binary = (Binary)((Return)SingleBlock(function).Children[0]).Value!;

        // Re-using an attached node without detaching it would silently
        // corrupt the tree; the IR refuses at the rewrite site.
        Assert.Throws<InvalidOperationException>(
            () => new ExpressionStatement(binary.Left));
    }

    [Fact]
    public void ExceptionRegions_ImportFlat_WithTypedHandlerEntry()
    {
        var function = ImportFixture(nameof(CfgSampleClass.ChecksThenTry));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var region = Assert.Single(function.Regions);
        Assert.Equal(HandlerKind.Catch, region.Kind);
        // Region boundaries are block leaders in the flat container.
        Assert.True(function.Body.IndexOfOffset(region.TryOffset) >= 0);
        Assert.True(function.Body.IndexOfOffset(region.HandlerOffset) >= 0);
        // The handler's first block consumes the CLR-pushed exception.
        var caught = function.Descendants.OfType<CaughtException>().First();
        Assert.Equal(region.CatchType, caught.Type);
        Assert.NotEmpty(function.Descendants.OfType<Leave>());
        function.CheckInvariant();
    }

    [Fact]
    public void BranchingMethod_BuildsBlocks()
    {
        var function = ImportFixture(nameof(CfgSampleClass.AbsShort));

        Assert.True(function.Body.Blocks.Count > 1);
        Assert.NotEmpty(function.Descendants.OfType<ConditionalBranch>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void BranchingMethod_MarksConditionalBranchesImported()
    {
        var function = ImportFixture(nameof(CfgSampleClass.AbsShort));

        Assert.All(
            function.Descendants.OfType<ConditionalBranch>(),
            branch => Assert.Equal(ConditionalBranchOrigin.Imported, branch.Origin));
    }

    [Fact]
    public void Convert_UnsignedNarrowing_MapsToCorrectTargetUnchecked()
    {
        // conv.u1 and conv.u2 live far from the main conv.* opcode range;
        // a range-based importer mapped them to UIntPtr and marked them
        // checked (mid-point review catch).
        var function = ImportFixture(nameof(CfgSampleClass.ToByte));

        var convert = Assert.Single(function.Descendants.OfType<Pipeline.Convert>());
        Assert.Equal("byte", convert.Target.ToDisplayString());
        Assert.False(convert.IsChecked);
        Assert.False(convert.IsUnsigned);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Fidelity_ScansSignatureAndNonExpressionTypes()
    {
        // An unsupported type anywhere — signature, locals, store targets —
        // must cap fidelity, not only expression result types.
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(null));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("p", TypeRef.Unsupported("refanytype"))],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [], container);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void LoadIndirect_RefForm_DereferencesPointerAddress()
    {
        // ldind.ref through an unmanaged pointer to a reference type (e.g.
        // `object*`, expressible only in IL) carries no element-type token, so
        // the result type comes from the address. A Pointer address must be
        // dereferenced like a ByRef one — otherwise the result type is null,
        // capping fidelity silently with no diagnostic.
        var pointerToObject = TypeRef.Pointer(TypeRef.CoreLib("System", "Object"));
        var load = new LoadIndirect(null, new LoadArgument(0, "p", pointerToObject));

        Assert.Equal("object", load.ResultType!.ToDisplayString());
    }

    [Fact]
    public void FunctionPointerSignature_ImportsAtFullFidelity()
    {
        // A delegate*<int, int> parameter is a representable function-pointer
        // type, not an unsupported shape: the body imports at Full, emits no
        // UnsupportedType stop, and the parameter renders in C# function-pointer
        // syntax (return type last).
        var function = ImportFixture(nameof(CfgSampleClass.TakesFunctionPointer));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.DoesNotContain(function.Diagnostics, d => d.Id == DiagnosticIds.UnsupportedType);
        var parameter = Assert.Single(function.Signature.Parameters);
        Assert.Equal(TypeRefKind.FunctionPointer, parameter.Type.Kind);
        Assert.Equal("delegate*<int, int>", parameter.Type.ToDisplayString());
    }

    [Fact]
    public void UnmanagedFunctionPointerSignature_RendersCallingConvention()
    {
        // delegate* unmanaged[Cdecl]<int, void> must surface the calling
        // convention in the rendered function-pointer syntax.
        var function = ImportFixture(nameof(CfgSampleClass.TakesUnmanagedFunctionPointer));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.DoesNotContain(function.Diagnostics, d => d.Id == DiagnosticIds.UnsupportedType);
        var parameter = Assert.Single(function.Signature.Parameters);
        Assert.Equal(TypeRefKind.FunctionPointer, parameter.Type.Kind);
        Assert.Equal("delegate* unmanaged[Cdecl]<int, void>", parameter.Type.ToDisplayString());
    }

    [Fact]
    public void Calli_RaisesToFunctionPointerInvocation()
    {
        // `callback(value)` compiles to calli through a delegate*<int, int>.
        // The importer consumes the pointer and its argument into a typed
        // CallIndirect instead of stopping on the opcode. (Roslyn spills the
        // pointer to a local first, per C# evaluation order, so the rendered
        // invocation reads through that local.)
        var function = ImportFixture(nameof(CfgSampleClass.InvokesFunctionPointer));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var call = Assert.Single(function.Descendants.OfType<CallIndirect>());
        Assert.Equal("int", call.ReturnType.ToDisplayString());
        Assert.Single(call.Arguments);
        Assert.IsType<LoadLocal>(call.Pointer);
        Assert.Contains("(value)", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void Calli_VoidReturn_RendersAsStatement()
    {
        // A void function-pointer invocation has no result on the stack, so it
        // renders as an expression statement, not a pushed value.
        var function = ImportFixture(nameof(CfgSampleClass.InvokesVoidFunctionPointer));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var call = Assert.Single(function.Descendants.OfType<CallIndirect>());
        Assert.Equal("void", call.ReturnType.ToDisplayString());
        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(value);", output);
        Assert.DoesNotContain("return", output);
    }

    [Fact]
    public void Calli_GenericUnmanagedFunctionPointer_PreservesSignature()
    {
        // Minimized from runtime's GenericFunctionPointer test: a void* cast to a
        // generic unmanaged delegate* then invoked through calli. The call-site
        // signature, not the pointer's static void* type, carries the generic
        // argument and return types.
        var function = ImportFixture(nameof(CfgSampleClass.InvokesGenericUnmanagedFunctionPointer));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var call = Assert.Single(function.Descendants.OfType<CallIndirect>());
        Assert.Equal("T", call.ReturnType.ToDisplayString());
        Assert.Equal("U", Assert.Single(call.ParameterTypes).ToDisplayString());
        Assert.Single(call.Arguments);
        Assert.Contains("delegate* unmanaged<U, T>", call.Pointer.ResultType!.ToDisplayString());

        var raised = ImportFixture(nameof(CfgSampleClass.InvokesGenericUnmanagedFunctionPointer));
        IrPasses.Run(raised);
        string output = CSharpPrinter.PrintRaised(raised).Output!;
        Assert.Contains("delegate* unmanaged<U, T>", output);
        Assert.Contains("return V_0(value);", output);
    }

    [Fact]
    public void Calli_UnmanagedFunctionPointerWithRef_PreservesRefArgument()
    {
        // The ref argument is part of the calli signature and must survive both
        // import and rendering; dropping the ref-kind would turn the minimized
        // runtime specimen into invalid or semantically different C#.
        var function = ImportFixture(nameof(CfgSampleClass.InvokesUnmanagedFunctionPointerWithRef));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var call = Assert.Single(function.Descendants.OfType<CallIndirect>());
        Assert.Equal("void", call.ReturnType.ToDisplayString());
        Assert.Equal(["ref int", "float"], call.ParameterTypes.Select(t => t.ToDisplayString()).ToArray());
        Assert.Equal(2, call.Arguments.Count);

        var raised = ImportFixture(nameof(CfgSampleClass.InvokesUnmanagedFunctionPointerWithRef));
        IrPasses.Run(raised);
        string output = CSharpPrinter.PrintRaised(raised).Output!;
        Assert.Contains("delegate* unmanaged<ref int, float, void>", output);
        Assert.Contains("V_0(ref value, arg);", output);
    }

    [Fact]
    public void UnmanagedCallersOnly_StdcallMethodAddress_PreservesFunctionPointerWitness()
    {
        // Minimized from runtime's UnmanagedCallersOnly tests: a static ldftn for
        // an UnmanagedCallersOnly target is assigned to a delegate* local whose
        // type carries the unmanaged calling convention, then invoked through
        // calli. The local type is the source-level convention witness.
        var function = ImportFixture(nameof(CfgSampleClass.InvokesUnmanagedCallersOnlyStdcallTarget));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var functionPointerLocal = Assert.Single(function.Locals.Where(t => t.Kind == TypeRefKind.FunctionPointer));
        Assert.Equal("delegate* unmanaged[Stdcall]<int, int>", functionPointerLocal.ToDisplayString());

        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Descendants.OfType<LoadFunctionPointer>());
        var address = Assert.Single(function.Descendants.OfType<AddressOfMethod>());
        Assert.Equal("UnmanagedStdcallTarget", address.Method.Name);
        Assert.Equal("delegate* unmanaged[Stdcall]<int, int>", address.ResultType!.ToDisplayString());
        var call = Assert.Single(function.Descendants.OfType<CallIndirect>());
        Assert.Equal("int", call.ReturnType.ToDisplayString());
        Assert.Equal(["int"], call.ParameterTypes.Select(t => t.ToDisplayString()).ToArray());

        string output = CSharpPrinter.PrintRaised(function).Output!;
        // A bare `&Method` address cannot be invoked directly — `(&Method)(value)`
        // is invalid C# (CS0149). The printer casts it to its delegate* result type
        // first, which compiles and stays Full (#1435 / calli &Method cast).
        Assert.Contains("return ((delegate* unmanaged[Stdcall]<int, int>)&UnmanagedStdcallTarget)(value);", output);
    }

    [Fact]
    public void Ldftn_StaticMethodAddress_RaisesToAmpersandMethod()
    {
        // A static ldftn that feeds a delegate*-typed field store (not a
        // delegate constructor) survives DelegateConstructionPass and is raised
        // by MethodAddressPass to AddressOfMethod — C#'s &Method — instead of
        // stopping as an unspellable function-pointer load.
        var function = ImportFixture(nameof(CfgSampleClass.StoresMethodAddress));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Descendants.OfType<LoadFunctionPointer>());
        var address = Assert.Single(function.Descendants.OfType<AddressOfMethod>());
        Assert.Equal("FunctionPointerTarget", address.Method.Name);
        Assert.Equal(TypeRefKind.FunctionPointer, address.ResultType!.Kind);
        Assert.Contains("&FunctionPointerTarget", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void PInvokeMethodDef_ImportsTargetFact()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var reader = source.Reader;
        var typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetFullTypeName(reader.GetTypeDefinition(handle)) == typeof(CfgSampleClass).FullName);
        var type = reader.GetTypeDefinition(typeHandle);
        var methodHandle = type.GetMethods().Single(handle =>
        {
            var method = reader.GetMethodDefinition(handle);
            return reader.GetString(method.Name) == "Overloaded"
                && method.RelativeVirtualAddress == 0;
        });

        var methodRef = IrImporter.ResolveMethod(
            reader,
            methodHandle,
            GenericScope.Empty,
            source.MemorySafety);

        Assert.Equal("Overloaded", methodRef.Name);
        Assert.Equal(MetadataFactState.Yes, methodRef.IsPInvoke);
        Assert.Equal(MetadataFactState.No, methodRef.IsRuntimeAsync);
    }

    [Fact]
    public void Shift_DropsRedundantWidthMask()
    {
        // C# masks a shift count by the operand width (int -> & 31, long -> & 63)
        // and bakes that mask into the IL; rendering it explicitly would double-
        // mask on recompile. The count must read back bare so the opcode stream
        // stays faithful.
        foreach (var name in new[] { nameof(CfgSampleClass.UnsignedShift), nameof(CfgSampleClass.LongLeftShift) })
        {
            var function = ImportFixture(name);
            IrPasses.Run(function);
            string output = CSharpPrinter.PrintRaised(function).Output!;

            Assert.DoesNotContain("& 31", output);
            Assert.DoesNotContain("& 63", output);
        }
    }

    [Fact]
    public void PinnedLocal_RaisesToFixedStatement()
    {
        // The csc pin lowering imports as a `pinned ref` local with a derived
        // unmanaged pointer (Convert(nuint, LoadLocal pinned)). Rendered
        // literally that is invalid C# (`pinned ref int V_0 = ref values[0]; ...
        // (nuint)V_0`) and the top syntactic malformedness driver in CoreLib
        // (#622 item F). FixedStatementPass raises the single-pinned-local shape
        // back into a real `fixed (T* p = &place) { ... }` — the pinned local IS
        // the fixed pointer. The loop deref keeps the pin alive through csc
        // optimization (a single deref is elided), so the shape survives.
        var function = ImportFixture(nameof(CfgSampleClass.SumPinnedArray));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<Fixed>());
        Assert.Contains("fixed (int* ", output);
        Assert.Contains("= &values[0])", output);
        Assert.DoesNotContain("pinned", output);
    }

    [Fact]
    public void PinnedLocal_DerivedPointerFoldsIntoFixedVariable()
    {
        // csc derives the unmanaged pointer into its own local
        // (`V_ptr = (int*)V_pin; ... *V_ptr`). FixedStatementPass folds that single
        // derived pointer into the `fixed` variable itself, so the redundant copy
        // and its `(int*)` cast disappear — the derived local IS the fixed pointer
        // (`fixed (int* p = &values[0]) { ... *p }`). This recompiles to csc's own
        // lowering (opcode-exact, PinnedExact) and never renders a
        // managed-reference-to-nuint `(nuint)` conversion.
        var function = ImportFixture(nameof(CfgSampleClass.SumPinnedArray));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<Fixed>());
        Assert.DoesNotContain("(int*)", output);
        Assert.DoesNotContain("(nuint)", output);
    }

    [Fact]
    public void MultiplePinnedLocals_RaiseToNestedFixedStatements()
    {
        // A [LibraryImport] custom-marshaller stub pins several arguments at once,
        // so the method carries multiple `pinned ref` locals nested LIFO. The
        // single-pinned-local guard left every such method flat as the unspellable
        // `pinned ref int` (the dominant CoreLib CS1002 family). FixedStatementPass
        // now nests them into stacked `fixed` headers over a shared body.
        var function = ImportFixture(nameof(CfgSampleClass.SumTwoPinned));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(2, function.Descendants.OfType<Fixed>().Count());
        Assert.Contains("fixed (int* ", output);
        Assert.Contains("= &a[0])", output);
        Assert.Contains("= &b[0])", output);
        Assert.DoesNotContain("pinned", output);
    }

    [Fact]
    public void ConstantSpan_RaisesToArrayLiteral()
    {
        // A constant array initializer in a ReadOnlySpan<T> context lowers to
        // RuntimeHelpers.CreateSpan<uint>(ldtoken <PrivateImplementationDetails>.blob).
        // Left flat the ldtoken of a compiler-internal field name renders as an
        // unspellable comment, dropping the call's only argument. RvaSpanPass
        // decodes the field's mapped RVA bytes and raises the call back to the
        // `new uint[] { ... }` literal csc re-lowers to the same content-addressed
        // field — opcode-exact.
        var function = ImportFixture(nameof(CfgSampleClass.ConstantUIntSpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains("new uint[] { 1, 10, 100, 1000, 10000 }", output);
        Assert.DoesNotContain("CreateSpan", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void ConstantByteSpan_RaisesToArrayLiteral()
    {
        // A constant byte-array initializer in a ReadOnlySpan<byte> context uses
        // csc's 1-byte-element optimization, building the span directly as
        // `new ReadOnlySpan<byte>(ref <PrivateImplementationDetails>.HASH, length)`
        // rather than through CreateSpan. The field name has angle brackets, so
        // left flat it never parses. RvaSpanPass decodes the field's mapped RVA
        // bytes and raises the construction back to the `new byte[] { ... }`
        // literal csc re-lowers to the same content-addressed field — opcode-exact.
        var function = ImportFixture(nameof(CfgSampleClass.ConstantByteSpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains("new byte[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void InlineArraySpan_RaisesToCollectionExpression()
    {
        // A collection expression with non-constant elements in a ReadOnlySpan<T>
        // context lowers to a compiler-synthesized inline-array buffer:
        // `<>y__InlineArray2<int>` (or `InlineArray2<int>` on .NET 11+) default-
        // init'd, each slot stored through
        // <PrivateImplementationDetails>.InlineArrayElementRef, then exposed via
        // InlineArrayAsReadOnlySpan. Left flat the angle-bracketed buffer/method
        // names never parse. InlineArrayCollectionPass raises it back to `[a, b]`.
        var function = ImportFixture(nameof(CfgSampleClass.InlineArraySpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains("[a, b]", output);
        Assert.DoesNotContain("InlineArray", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void InlineArrayObjectConditionalElementSpan_RaisesSpilledStore()
    {
        // The address-spilled dual of InlineArraySpan: an element VALUE that
        // carries a branch and a call (`s is not null ? s.ToUpper() : "null"`) in
        // a params ReadOnlySpan<object> context cannot be evaluated in one push,
        // so csc computes the element-ref address first and spills it to an
        // evaluation-stack temp, spills the conditional's value to a second temp,
        // then stores through the spilled address. Left flat, both the angle-
        // bracketed buffer name and the raw stack temps never parse.
        // InlineArrayCollectionPass recovers the spilled address+value and raises
        // the whole thing back to `[a, s is not null ? s.ToUpper() : "null"]`.
        // This is the shape EncLocalInfo's params string.Format hits in the wild
        // (issue #3129, S4).
        var function = ImportFixture(nameof(CfgSampleClass.InlineArrayObjectConditionalElementSpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains("[a, s is not null ? s.ToUpper() : \"null\"]", output);
        Assert.DoesNotContain("InlineArray", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }


    [Fact]
    public void InlineArraySpanTernaryConditionValue_RaisesRunOnceConditionEdge()
    {
        // A params ReadOnlySpan<object> collection whose span is the CONDITION of a
        // value-position ternary is evaluated exactly once, unconditionally, before
        // either arm. Left flat the `<>y__InlineArray2<object>` buffer name never
        // parses and caps fidelity at Partial; InlineArrayCollectionPass now allow-
        // lists the ternary condition as a run-once governing edge (issue #3129, S4
        // follow-up #3281) and raises it to `[a, b]`, restoring Full. Real compiled
        // witness for the synthetic ConditionalConditionSpan case in
        // InlineArraySpilledElementTests.
        AssertRunOnceEdgeSpanRaises(nameof(CfgSampleClass.InlineArraySpanTernaryConditionValue));
    }

    [Fact]
    public void InlineArraySpanSwitchExpressionValue_RaisesRunOnceScrutineeEdge()
    {
        // Span on the scrutinee of a switch expression — a run-once governing edge
        // (#3281). Left flat it caps fidelity at Partial; the pass raises it to
        // `[a, b]`, restoring Full.
        AssertRunOnceEdgeSpanRaises(nameof(CfgSampleClass.InlineArraySpanSwitchExpressionValue));
    }

    [Fact]
    public void InlineArraySpanUsingResource_RaisesRunOnceResourceEdge()
    {
        // Span on the resource of a using statement; the body is a separate block,
        // so the using statement itself is the span consumer and the element stores
        // are its contiguous earlier siblings — a run-once governing edge (#3281).
        // Left flat it caps fidelity at Partial; the pass raises it to `[a, b]`,
        // restoring Full.
        AssertRunOnceEdgeSpanRaises(nameof(CfgSampleClass.InlineArraySpanUsingResource));
    }

    [Fact]
    public void InlineArraySpanUsingResource_RaisesToVariableLessUsing()
    {
        // Real compiled witness for #3346: SpanScope.Dispose() is a no-op and the
        // body (`n = 1;`) never reads the resource local — it is disposed-only,
        // referenced solely by the compiler-generated dispose guard. That makes
        // `using (DisposableFromObjectSpan([a, b]))` the idiomatic, fully raised
        // endpoint rather than `using (IDisposable V_2 = ...)`.
        var function = ImportFixture(nameof(CfgSampleClass.InlineArraySpanUsingResource));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        var usingStatement = Assert.Single(function.Descendants.OfType<UsingStatement>());
        Assert.False(usingStatement.DeclaresResourceVariable);
        Assert.Contains("using (DisposableFromObjectSpan([a, b]))", output);
        Assert.DoesNotContain("IDisposable", output);
    }

    static void AssertRunOnceEdgeSpanRaises(string methodName)
    {
        var function = ImportFixture(methodName);
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains("[a, b]", output);
        Assert.DoesNotContain("InlineArray", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void InlineArrayFieldAsSpan_RaisesToCast()
    {
        // A real [InlineArray(4)] field viewed as a Span<int>: csc lowers the
        // span conversion to <PrivateImplementationDetails>.InlineArrayAsSpan(ref
        // _inlineField, 4). Left flat the angle-bracketed method name never parses.
        // InlineArrayCollectionPass raises the bare conversion to `(Span<int>)
        // _inlineField`, which csc re-lowers to the same AsSpan call.
        var function = ImportFixture(nameof(CfgSampleClass.InlineArrayFieldAsSpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains("(Span<int>)_inlineField", output);
        Assert.DoesNotContain("InlineArray", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void InlineArrayFieldAsReadOnlySpan_RaisesToCast()
    {
        // The ReadOnlySpan dual of InlineArrayFieldAsSpan: the read-only span
        // conversion lowers to InlineArrayAsReadOnlySpan and raises to the cast
        // `(ReadOnlySpan<int>)_inlineField`.
        var function = ImportFixture(nameof(CfgSampleClass.InlineArrayFieldAsReadOnlySpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains("(ReadOnlySpan<int>)_inlineField", output);
        Assert.DoesNotContain("InlineArray", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void RuntimeInlineArrayIndexer_RaisesParameterSpanConversion()
    {
        // Runtime has user-defined [InlineArray] structs with indexers and
        // enumerators. Direct element access over a parameter lowers through an
        // InlineArrayAsSpan helper; this stays distinct from synthesized
        // collection-expression buffers and raises only to the span cast.
        var function = ImportFixture(nameof(CfgSampleClass.RuntimeInlineArrayIndexer));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains("(Span<int>)values", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void RuntimeInlineArrayForeach_RaisesRefLocalElementRef()
    {
        // `foreach` over the runtime-style inline array lowers to a counted loop
        // that stores `ref values` in a ref local, then calls
        // InlineArrayElementRef(ref V_1, i). The element-ref raise must accept the
        // ref local as the inline-array place without treating it as a synthesized
        // collection-expression buffer.
        var function = ImportFixture(nameof(CfgSampleClass.RuntimeInlineArrayForeach));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains(function.Descendants.OfType<LoadElementAddress>(),
            a => a.Array is LoadIndirect { Address: LoadLocal { ResultType.Kind: TypeRefKind.ByRef } });
        Assert.Contains("ref RuntimeStyleInlineArray", output);
        Assert.Contains("sum += value;", output);
        Assert.DoesNotContain("InlineArrayElementRef", output);
        Assert.DoesNotContain("PrivateImplementationDetails", output);
    }

    [Fact]
    public void HandWrittenCreateSpan_NotRaisedToInlineArrayCast()
    {
        // Adversarial negative (#1045, runtime MyArray<T>.AsSpan shape): a
        // hand-written MemoryMarshal.CreateSpan view, then indexed, is NOT csc's
        // <PrivateImplementationDetails>.InlineArrayAsSpan conversion (a name user
        // code cannot declare). InlineArrayCollectionPass keys on that helper, so it
        // must NOT raise the CreateSpan to a (Span<int>) cast — it renders faithfully.
        var function = ImportFixture(nameof(CfgSampleClass.HandWrittenCreateSpan));
        IrPasses.Run(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Empty(function.Descendants.OfType<InlineArraySpanConversion>());
        Assert.Contains("CreateSpan", output);
        Assert.DoesNotContain("(Span<int>)", output);
    }

    [Fact]
    public void CheckedCompound_RendersCheckedBlockWithCompoundSugar()
    {
        // `checked(x += v)` lowers to add.ovf; `checked(x += v);` is not a legal
        // statement (CS0201), so the overflow context is restored with a
        // single-statement checked block that keeps the compound sugar.
        var function = ImportFixture(nameof(CfgSampleClass.CheckedCompoundAdd));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Contains("checked { x += v; }", output);
        Assert.DoesNotContain("x = checked(", output);
    }

    [Fact]
    public void CheckedCompoundMultiply_RendersCheckedBlock()
    {
        var function = ImportFixture(nameof(CfgSampleClass.CheckedCompoundMul));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("checked { x *= v; }", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void CheckedCompoundIncrement_RendersCheckedBlockWithOperator()
    {
        var function = ImportFixture(nameof(CfgSampleClass.CheckedCompoundInc));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("checked { x++; }", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void CheckedAdd_RendersCheckedExpression()
    {
        // add.ovf carries an overflow check the default (unchecked) C# context
        // would drop; the binary must spell checked(...) so the recompiled IL
        // keeps the .ovf opcode rather than silently becoming a plain add.
        var function = ImportFixture(nameof(CfgSampleClass.CheckedAdd));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("checked(", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void CheckedConversionOverCheckedAdd_CollapsesNestedChecked()
    {
        // Both the conversion (conv.ovf.u1) and the add (add.ovf) are checked, but
        // one checked(...) already covers the whole expression. The printer must
        // collapse the redundant nesting to checked((byte)(left + right)), never
        // checked((byte)(checked(left + right))). Roslyn keeps both .ovf opcodes.
        var function = ImportFixture(nameof(CfgSampleClass.CheckedAddToByte));
        IrPasses.Run(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("checked((byte)(left + right))", output);
        Assert.DoesNotContain("checked(left + right)", output);
    }

    [Fact]
    public void StaleFieldRead_PinsFieldReadTakenBeforeStore()
    {
        // `int v = h.Value; h.Value = 99; return v + h.Value;` carries the first
        // h.Value read on the symbolic stack across the store. Without spilling it
        // the importer re-materialized the load after the store, yielding
        // `h.Value + h.Value` — a different result (issue #605). The pre-store read
        // must be pinned to a temp so it keeps its old value.
        var function = ImportFixture(nameof(CfgSampleClass.StaleFieldRead));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.DoesNotContain("h.Value + h.Value", output);
        Assert.Contains("= h.Value;", output);
    }

    [Fact]
    public void DeadDefaultInitializer_DroppedWhenAssignedBeforeUse()
    {
        // A local assigned on every path before it is read — here in each switch
        // section, or in both the try and catch arms — must declare bare. The
        // `= default` the emitter used to add is a dead store the IL never had
        // (locals lean on .locals init), so recompiling it diverges from the
        // original opcode stream.
        foreach (var name in new[]
        {
            nameof(CfgSampleClass.PowerOfTwo),
            nameof(CfgSampleClass.CatchEverything),
            nameof(CfgSampleClass.TryFinallyAdd),
        })
        {
            var function = ImportFixture(name);
            IrPasses.Run(function);
            Assert.DoesNotContain("= default", CSharpPrinter.PrintRaised(function).Output!);
        }
    }

    [Fact]
    public void DeadDefaultInitializer_DroppedAcrossGotoCfgWhenAssignedOnEveryPath()
    {
        // GotoCommonExitGuardedMerge assigns result on every path before the
        // shared guarded exit. The old global bail flooded every local to
        // `= default`; CFG definite-assignment proves `result` assigned on every
        // path first, and the region-exit diamond now structures the gotos away.
        var function = ImportFixture(nameof(CfgSampleClass.GotoCommonExitGuardedMerge));
        IrPasses.Run(function);
        var output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.DoesNotContain("goto ", output);
        Assert.DoesNotContain("= default", output);
    }

    [Fact]
    public void ComparisonTree_RaisesSparseSwitchWithoutGotos()
    {
        // ClassifyMode is a sparse switch csc lowered to a binary-search comparison
        // tree (relational pivots over linear == chains). SwitchRaisingPass collects
        // the equality leaves back into a `switch` statement — no surviving goto, no
        // nested `if`, and no `= default` dead initializer.
        var function = ImportFixture(nameof(CfgSampleClass.ClassifyMode));
        IrPasses.Run(function);
        var output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.DoesNotContain("goto ", output);
        Assert.DoesNotContain("= default", output);
        Assert.DoesNotContain("if (", output);
        Assert.Contains("switch (", output);
    }

    [Fact]
    public void DeadDefaultInitializer_DroppedWhenAssignedInsideLock()
    {
        // A lock body runs in program order, so a local it assigns before a read
        // after the lock is definitely assigned — modeling the lock (rather than
        // bailing on it) declares it bare.
        var function = ImportFixture(nameof(CfgSampleClass.LockedAssign));
        IrPasses.Run(function);

        Assert.DoesNotContain("= default", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void DeadDefaultInitializer_KeptWhenAssignmentNotProven()
    {
        // Without a metadata context, ParseOrZero reaches its local first through
        // an unresolved by-ref argument. The conservative analysis does not treat
        // that as a proven assignment, so the `= default` stays — dropping it
        // would be CS0165 if the parameter were actually `ref`.
        var function = ImportFixture(nameof(CfgSampleClass.ParseOrZero));
        IrPasses.Run(function);

        Assert.Contains("= default", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void DeadDefaultInitializer_DroppedForVerifiedOutArgument()
    {
        // The fidelity harness opens a metadata context, so int.TryParse's real
        // parameter rows prove the local-address argument is `out`. A verified
        // out call definitely assigns the local before the following return reads
        // it, so the initializer is a dead store the original IL did not carry.
        var function = ImportFixtureWithTrustedPlatformContext(nameof(CfgSampleClass.ParseOrZero));
        IrPasses.Run(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("int v;", output);
        Assert.Contains("out v", output);
        Assert.DoesNotContain("= default", output);
    }

    [Fact]
    public void Shadowed_QualifiesFieldLoadWhenParameterShadows()
    {
        // The parameter _shadowed has the same name as the field, so a bare
        // _shadowed binds to the parameter. The field load must spell
        // this._shadowed or the recompiled IL reads the parameter twice.
        var function = ImportFixture(nameof(CfgSampleClass.Shadowed));
        IrPasses.Run(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("this._shadowed", CSharpPrinter.PrintRaised(function).Output!);
    }

    [Fact]
    public void TryFinallyAdd_SinksReturnAccumulatorIntoTry()
    {
        // The result is computed inside the try and the ret sits after the
        // finally, so the importer reconstructs a synthetic accumulator local.
        // Sinking it back to `return x + 1;` inside the try keeps Roslyn from
        // re-zero-initing the temp (a dead leading ldc.i4.0/stloc the original
        // never emitted) — there must be no accumulator local in the output.
        var function = ImportFixture(nameof(CfgSampleClass.TryFinallyAdd));
        IrPasses.Run(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("return x + 1;", output);
        Assert.DoesNotContain("V_0", output);
    }

    [Fact]
    public void TryFinallyTwoReturns_SinksBothReturnsIntoTry()
    {
        // Both the guarded `return x` and the fall-through `return -1` are
        // spilled into one accumulator and returned after the finally. The
        // pass must sink both back into the try as distinct returns, leaving no
        // accumulator local behind.
        var function = ImportFixture(nameof(CfgSampleClass.TryFinallyTwoReturns));
        IrPasses.Run(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("return x;", output);
        Assert.Contains("return -1;", output);
        Assert.DoesNotContain("V_0", output);
    }

    [Fact]
    public void InParameter_SeesThroughModifier_ImportsAtFull()
    {
        // modreq(InAttribute) on the byref is a declaration-site concern that
        // never appears in the body; seeing through it lets a fully
        // representable method import at Full instead of being capped.
        var function = ImportFixture(nameof(CfgSampleClass.InParameterSum));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Diagnostics);
    }

    [Fact]
    public void VolatileField_SeesThroughModifier_ImportsAtFull()
    {
        // modreq(IsVolatile) on the field type is likewise transparent to the
        // body, which references the field by name.
        var function = ImportFixture(nameof(CfgSampleClass.ReadVolatileFlag));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Diagnostics);
    }

    [Fact]
    public void CoreLib_IsNullOrEmpty_ImportsAtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.String", "IsNullOrEmpty");

        Assert.NotNull(function);
        Assert.Equal(3, function.Body.Blocks.Count);
        var conditional = Assert.Single(function.Descendants.OfType<ConditionalBranch>());
        Assert.IsType<LogicalNot>(conditional.Condition);
        Assert.Single(function.Descendants.OfType<Comparison>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(function.Body.Blocks[2].StartOffset, conditional.TargetOffset);
    }

    [Fact]
    public void CoreLib_Ternary_ImportsViaStackSlots()
    {
        // 'TargetFrameworkName ??= ...' carries a value across block
        // boundaries — the canonical stack-carrying edge, materialized
        // through position-indexed slots so every predecessor of the join
        // stores to the same slot.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.AppContext", "get_TargetFrameworkName");

        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.NotEmpty(function.Descendants.OfType<StoreStackSlot>());
        Assert.NotEmpty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant();
    }

    [Fact]
    public void CoreLib_GenericMethodCall_ResolvesMethodSpecification()
    {
        // Array.get_Length calls Unsafe.As<...> — a MethodSpecification.
        // Unresolved, its arity-0 fallback mis-popped the stack and poisoned
        // everything downstream (2,484 false stops in the CoreLib sweep).
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Array", "get_Length");

        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var genericCall = function.Descendants.OfType<Call>().First(c => !c.Callee.TypeArguments.IsEmpty);
        Assert.NotEqual("?", genericCall.Callee.Name);
        // The call site reports instantiated types, not the callee's formal
        // !!N parameters (second-review fix).
        Assert.False(ContainsGenericParameter(genericCall.Callee.ReturnType));
        Assert.All(genericCall.Callee.ParameterTypes, p => Assert.False(ContainsGenericParameter(p)));
    }

    [Fact]
    public void NestedType_ImportsByFullyQualifiedName()
    {
        // A nested type's metadata name threads its declaring types
        // (CfgSampleClass.NestedSample), with a nil namespace and a leaf Name.
        // The importer must qualify it, or the body is unreachable. The dotted
        // metadata spelling differs from reflection's `+` separator.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string nestedName = typeof(CfgSampleClass.NestedSample).FullName!.Replace('+', '.');

        var nested = IrImporter.Import(source, nestedName, nameof(CfgSampleClass.NestedSample.Triple));
        Assert.NotNull(nested);
        Assert.Equal(DecompilationFidelity.Full, nested.Fidelity);

        // The unrelated top-level type shares the leaf name; keying on the leaf
        // alone would collide. Each resolves to its own method.
        var topLevel = IrImporter.Import(source, typeof(NestedSample).FullName!, nameof(NestedSample.Negate));
        Assert.NotNull(topLevel);
        Assert.Equal("Triple", nested.Name);
        Assert.Equal("Negate", topLevel.Name);

        // The bare leaf name must not resolve the nested type.
        Assert.Null(IrImporter.Import(source, "NestedSample", nameof(CfgSampleClass.NestedSample.Triple)));
    }

    static bool ContainsGenericParameter(TypeRef type)
        => type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
            || (type.ElementType is { } element && ContainsGenericParameter(element))
            || type.TypeArguments.Any(ContainsGenericParameter);

    [Fact]
    public void AddressOf_OutArgument_ImportsAsLocalAddress()
    {
        var function = ImportFixture(nameof(CfgSampleClass.ParseOrZero));

        // int.TryParse(s, out v) is a cross-assembly MemberRef: it carries no
        // parameter rows, so the out kind is unknown and the printer spells the
        // address as `ref` (CS1620 for an out parameter). That unverifiable
        // spelling lowers fidelity and records DEC0007 rather than claiming Full.
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var address = function.Descendants.OfType<LoadLocalAddress>().First();
        Assert.Equal("ref int", address.ResultType?.ToDisplayString());
        var call = function.Descendants.OfType<Call>().First(c => c.Callee.Name == "TryParse");
        Assert.Contains(call.Arguments, a => a is LoadLocalAddress);
        Assert.True(call.HasUnverifiedByRefArgument);

        // The diagnostics pass annotates the gap with DEC0007.
        IrPasses.Run(function);
        Assert.Contains(function.Diagnostics, d => d.Id == DiagnosticIds.UnverifiedByRefArgument);
    }

    [Fact]
    public void StaticAbstractInterfaceCall_SpellsTypeParameterReceiver()
    {
        // `constrained. T; call INumberBase<T>::IsNegative` — a static abstract
        // interface member invoked through a type parameter. C# spells the receiver
        // as the type parameter (`T.IsNegative(value)`), never the declaring
        // interface (`INumberBase<T>.IsNegative(value)` cannot invoke a static
        // abstract member). This recompiles to the same constrained call.
        var function = ImportFixture(nameof(CfgSampleClass.IsNegativeNumber));

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("T.IsNegative(value)", output);
        Assert.DoesNotContain("INumberBase", output);
    }

    [Fact]
    public void BoolValueIntoIntegerReturn_SpellsConditionalOneZero()
    {
        // `cgt.un; ret` from an int-returning method: a comparison result consumed
        // as a number. C# has no implicit bool->int, so the read must spell
        // `value > 0 ? 1 : 0` — not a bare `value > 0` (CS0029) — and Roslyn folds
        // that back to the bare comparison opcode.
        var function = ImportFixture(nameof(CfgSampleClass.IsPositiveAsInt));

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("? 1 : 0", output);
    }

    [Fact]
    public void ReinterpretRead_DerefsAsItsOwnPointerType_NotNativeInt()
    {
        // `ldarga; conv.u; ldind.u1` (reinterpret a generic value as a primitive
        // and read it). Deref-ing the native-int address — `*((nuint)(&value))` —
        // is CS0193; the read must spell its own pointer type: `*(byte*)(&value)`.
        var function = ImportFixture(nameof(CfgSampleClass.ReinterpretFirstByte));

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("*(byte*)(&value)", output);
        Assert.DoesNotContain("*((nuint)", output);
        Assert.DoesNotContain("*((nint)", output);
    }

    [Fact]
    public void NativeIntAddressSlot_DerefsAsItsOwnPointerType_NotNativeInt()
    {
        // Release can materialize a pointer through a native-int stack slot before
        // an indirect read/write. Deref-ing the slot itself (`*S_257`) is CS0193;
        // the access must cast the native int back to the opcode's pointer type.
        using var source = MetadataSource.Open(FixtureCatalog.DecompilerUnsafeChainA.AssemblyPath());
        var function = IrImporter.Import(
            source,
            typeof(ILInspector.Decompiler.Fixtures.UnsafeChainA.LibraryA).FullName!,
            nameof(ILInspector.Decompiler.Fixtures.UnsafeChainA.LibraryA.M1));
        Assert.NotNull(function);

        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("*(int*)S_", output);
        Assert.DoesNotContain("*S_", output);
    }

    [Fact]
    public void ElementAccess_ImportsTypedLoadAndStore()
    {
        var load = ImportFixture(nameof(CfgSampleClass.FirstElement));
        var store = ImportFixture(nameof(CfgSampleClass.SetFirstElement));

        Assert.Equal(DecompilationFidelity.Full, load.Fidelity);
        Assert.Equal("int", Assert.Single(load.Descendants.OfType<LoadElement>()).ResultType?.ToDisplayString());
        Assert.Equal(DecompilationFidelity.Full, store.Fidelity);
        Assert.Single(store.Descendants.OfType<StoreElement>());
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.SetByteElement), "byte", "(byte)")]
    [InlineData(nameof(CfgSampleClass.SetCharElement), "char", "(char)")]
    public void TypedStelem_TakesElementTypeFromArray_NotOpcodeSign(string method, string element, string expectedCast)
    {
        // stelem.i1/i2 encode width and a default sign (sbyte/short), but the
        // array's element type is authoritative: a `byte[]`/`char[]` store typed
        // from the opcode renders an `(sbyte)`/`(short)` cast that is CS0266
        // against the element. The store must take the array's element type.
        var function = ImportFixture(method);

        var store = Assert.Single(function.Descendants.OfType<StoreElement>());
        Assert.Equal(element, store.ElementType?.ToDisplayString());

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains(expectedCast, output);
        Assert.DoesNotContain("(sbyte)", output);
        Assert.DoesNotContain("(short)", output);
    }

    [Fact]
    public void Switch_ImportsWithTargetsAsLeaders()
    {
        var function = ImportFixture(nameof(CfgSampleClass.PowerOfTwo));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var switchBranch = Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.True(switchBranch.TargetOffsets.Length >= 4);
        // Every switch target starts a block.
        Assert.All(switchBranch.TargetOffsets,
            target => Assert.True(function.Body.IndexOfOffset(target) >= 0));
        function.CheckInvariant();
    }

    [Fact]
    public void TryFinally_ImportsWithEndFinally()
    {
        var function = ImportFixture(nameof(CfgSampleClass.TryFinallyAdd));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(HandlerKind.Finally, Assert.Single(function.Regions).Kind);
        Assert.Single(function.Descendants.OfType<EndFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void ExceptionFilter_ImportsWithEndFilter()
    {
        var function = ImportFixture(nameof(CfgSampleClass.FilteredLength));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Contains(FidelityRemarks.Collect(function),
            r => r.Code == DiagnosticIds.UnsupportedExceptionFilter);
        Assert.Equal(HandlerKind.Filter, Assert.Single(function.Regions).Kind);
        Assert.Single(function.Descendants.OfType<EndFilter>());
        Assert.NotEmpty(function.Descendants.OfType<CaughtException>());
        function.CheckInvariant();
    }

    [Fact]
    public void CoreLib_SimpleCorpusMethods_ImportAtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        (string Type, string Method)[] corpus =
        [
            ("System.String", "IsNullOrEmpty"),
            ("System.Math", "Max"),
            ("System.Math", "Clamp"),
            ("System.Text.StringBuilder", "Clear"),
            ("System.Collections.Generic.HashSet`1", "Contains"),
        ];

        foreach (var (type, method) in corpus)
        {
            var function = IrImporter.Import(source, type, method);
            Assert.NotNull(function);
            Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
            function.CheckInvariant();
        }
    }

    [Fact]
    public void CoreLib_StraightLineMethod_ImportsCallsAndFields()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        // get_Count: ldarg.0; ldfld _size; ret
        var function = IrImporter.Import(source, "System.Collections.Generic.List`1", "get_Count");

        Assert.NotNull(function);
        var block = (Block)Assert.Single(function.Body.Children);
        var ret = Assert.IsType<Return>(Assert.Single(block.Children));
        var field = Assert.IsType<LoadField>(ret.Value);
        Assert.Equal("_size", field.Field.Name);
        Assert.Equal("int", field.ResultType?.ToDisplayString());
        Assert.IsType<LoadArgument>(field.Instance);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}
