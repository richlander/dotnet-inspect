using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

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

        var methodRef = IrImporter.ResolveMethod(reader, methodHandle, GenericScope.Empty);

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
        // ParseOrZero reaches its local first through a by-ref out-argument,
        // which the conservative analysis does not treat as a proven assignment,
        // so the `= default` stays — dropping it would be CS0165.
        var function = ImportFixture(nameof(CfgSampleClass.ParseOrZero));
        IrPasses.Run(function);

        Assert.Contains("= default", CSharpPrinter.PrintRaised(function).Output!);
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
        using var source = MetadataSource.Open(typeof(ILInspector.Decompiler.Fixtures.UnsafeChainA.LibraryA).Assembly.Location);
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

public class JoinTypeConflictTests : IDisposable
{
    readonly Stack<IDisposable> _disposables = new();

    public void Dispose()
    {
        while (_disposables.Count > 0)
            _disposables.Pop().Dispose();
    }

    IrFunction BuildSynthetic(byte[] il)
    {
        var source = MetadataSource.Open(typeof(object).Assembly.Location);
        _disposables.Push(source);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var method = new ImportedMethod(
            TypeRef.CoreLib("Synthetic", "T"), "M", signature,
            new MethodBody([.. il], MaxStack: 8, Locals: [], LocalNames: [], Handlers: []));
        return IrImporter.Build(source, method, GenericScope.Empty);
    }

    [Fact]
    public void TrailingLabeledReturn_IsNotTrimmedToADanglingLabel()
    {
        // br.s to the final 'return;' — the return is a branch target's only
        // statement, so trimming it would strand the label as invalid C#.
        var function = BuildSynthetic([0x2B, 0x00, 0x2A]);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("IL_0002:\nreturn;", output);
    }

    [Fact]
    public void JoinTypeConflict_BeforeJoinIsBuilt_MergesToHonestUnknown()
    {
        // ldc.i4.1; brtrue.s L1; ldc.i4.0; br.s J; L1: ldnull; J: pop; ret
        // Two forward edges carry int and object into J: pre-build conflict
        // merges to null (honest unknown), never a guessed type.
        var function = BuildSynthetic([0x17, 0x2D, 0x03, 0x16, 0x2B, 0x01, 0x14, 0x26, 0x2A]);

        // The join-type diagnostic records the disagreement; the only
        // consumer of the unknown value was a pop of a pure load, which
        // elides — so no unknown-typed expression survives into the tree
        // and fidelity stays Full while the diagnostic preserves the trace.
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("(join-type)", diagnostic.Message);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(),
            l => l.Parent is ExpressionStatement);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void DisjointStackCarryTargets_DoNotSharePositionSlot()
    {
        // Two unrelated forward targets each carry one stack value. They both use
        // stack position 0, but they are different entry stacks: sharing S_0 would
        // force the later long store into the earlier int slot (CS0266).
        var function = BuildSynthetic(
        [
            0x17,                         // ldc.i4.1
            0x2B, 0x00,                   // br.s IL_0003
            0x26,                         // pop
            0x21, 0x02, 0, 0, 0, 0, 0, 0, 0, // ldc.i8 2
            0x2B, 0x00,                   // br.s IL_000F
            0x26,                         // pop
            0x2A,                         // ret
        ]);

        var stores = function.Descendants.OfType<StoreStackSlot>().ToArray();

        Assert.Contains(stores, s => s is { Slot: 0, Value.ResultType.Name: "Int32" });
        Assert.Contains(stores, s => s is { Slot: 1, Value.ResultType.Name: "Int64" });
        Assert.DoesNotContain(stores, s => s is { Slot: 0, Value.ResultType.Name: "Int64" });

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("int S_0 = 1;", output);
        Assert.Contains("long S_1;", output);
        Assert.DoesNotContain("S_0 = 2L;", output);
        function.CheckInvariant();
    }

    IrFunction BuildSyntheticWithRegion(byte[] il, HandlerRegion region)
    {
        var source = MetadataSource.Open(typeof(object).Assembly.Location);
        _disposables.Push(source);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var method = new ImportedMethod(
            TypeRef.CoreLib("Synthetic", "T"), "M", signature,
            new MethodBody([.. il], MaxStack: 8, Locals: [], LocalNames: [], Handlers: [region]));
        return IrImporter.Build(source, method, GenericScope.Empty);
    }

    [Fact]
    public void Endfinally_NonEmptyStack_IsMalformed_StopsHonestly()
    {
        // try { leave } finally { ldc.i4.1; endfinally }  — ECMA requires an
        // empty stack at endfinally; the stray value must not import as Full.
        var function = BuildSyntheticWithRegion(
            [0xDE, 0x02, 0x17, 0xDC, 0x2A],
            new HandlerRegion(HandlerKind.Finally, 0, 2, 2, 2, 0, null));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("endfinally", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void Endfilter_MoreThanVerdict_IsMalformed_StopsHonestly()
    {
        // Filter code that never consumes the CLR-pushed exception: after
        // popping the verdict the exception remains — malformed per ECMA.
        var function = BuildSyntheticWithRegion(
            [0xDE, 0x06, 0x17, 0xFE, 0x11, 0x26, 0xDE, 0x00, 0x2A],
            new HandlerRegion(HandlerKind.Filter, 0, 2, 5, 3, 2, null));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("filter verdict", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void JoinTypeConflict_AfterJoinIsBuilt_StopsHonestly()
    {
        // ldc.i4.0; br.s J; J: pop; ret; (unreachable) ldnull; br.s J
        // The join is built with int before the object-carrying edge arrives;
        // already-emitted loads cannot be retyped, so the import stops.
        var function = BuildSynthetic([0x16, 0x2B, 0x00, 0x26, 0x2A, 0x14, 0x2B, 0xFB]);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedConstruct, diagnostic.Id);
        Assert.Contains("types disagree", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_ToCommonBase_ImportsAtFullFidelity()
    {
        // Two ternary branches carry JoinDerived and JoinBase into one slot.
        // The merge resolves to JoinBase — an ancestor of JoinDerived reached
        // by walking the same-assembly base chain — so the body imports Full
        // with no join-type diagnostic, unlike the unresolvable conflicts above.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedReferenceSlot))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_ToImplementedInterface_ImportsAtFullFidelity()
    {
        // One ternary arm is cast to IJoinShape; the other implements it. The
        // merge resolves to IJoinShape — an interface one arm already carries
        // and the other implements — so the body imports Full with no
        // join-type diagnostic, exercising the interface arm of the merge.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedInterfaceSlot))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_NullLiteralAdoptsCrossAssemblyReferenceType()
    {
        // The null arm of a ?. lowering is the null literal, not a hard object
        // value. When the other arm carries a cross-assembly reference type such
        // as System.Type, the join keeps that type instead of reporting
        // object-vs-Type as an unknown join.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullConditionalCrossAssemblyReference))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }
}

public class CSharpPrinterTests
{
    static string PrintFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void MergedTernary_DeclaresCommonBase_NotWhenTrueArm()
    {
        // A folded ternary feeding a declared slot: the arms are JoinDerived
        // and JoinBase, so the slot must declare as the common base JoinBase.
        // Without carrying the importer-merged type the ternary would narrow to
        // the WhenTrue arm (JoinDerived) and assign JoinBase to it — unsound.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedTernaryDeclaration));
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function);
        Assert.True(result.Succeeded);
        string output = result.Output!.ReplaceLineEndings("\n");

        Assert.Contains("new JoinDerived()", output);
        Assert.Contains("new JoinBase()", output);
        // The slot/local carrying the ternary declares as the common base,
        // never the WhenTrue arm — config-agnostic (Release spills a stack
        // slot, Debug keeps a named local).
        Assert.Matches(@"JoinBase \w+ = !flag \?", output);
        Assert.DoesNotContain("JoinDerived S_", output);
        Assert.DoesNotContain("JoinDerived V_", output);
    }

    [Fact]
    public void NullConditional_RaisesQuestionDot_AndSoundlyTypesSlot()
    {
        // The ?. lowering spills the receiver into a stack slot, null-tests it,
        // and reloads the spill for the member access — reusing one slot for the
        // receiver (JoinBase) and the result (string). NullConditionalPass raises
        // it to node?.Member, so the slot carries only the string result and the
        // receiver type never declares a slot/local.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var property = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullConditionalProperty));
        Assert.NotNull(property);
        var propertyResult = CSharpPrinter.PrintRaised(property);
        Assert.True(propertyResult.Succeeded);
        string propertyOutput = propertyResult.Output!.ReplaceLineEndings("\n");
        Assert.Contains("node?.Label", propertyOutput);
        // The reused slot must not declare as the receiver type — that is the
        // unsound slot-conflation the raise removes (config-agnostic spelling).
        Assert.DoesNotContain("JoinBase S_", propertyOutput);
        Assert.DoesNotContain("JoinBase V_", propertyOutput);

        var call = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullConditionalCall));
        Assert.NotNull(call);
        var callResult = CSharpPrinter.PrintRaised(call);
        Assert.True(callResult.Succeeded);
        string callOutput = callResult.Output!.ReplaceLineEndings("\n");
        Assert.Contains("node?.Shape()", callOutput);
    }

    [Fact]
    public void StraightLine_PrintsCurrentStyle()
    {
        Assert.Equal("return a + b;\n", PrintFixture(nameof(CfgSampleClass.Add)));
    }

    [Fact]
    public void Branches_PrintAsHonestLabelsAndGotos()
    {
        string output = PrintFixture(nameof(CfgSampleClass.AbsShort));

        Assert.Contains("goto IL_", output);
        Assert.Contains(":", output);
        Assert.DoesNotContain("/* ", output);  // every node has a rendering
    }

    [Fact]
    public void UnsignedConversion_CastsSignedSource()
    {
        // conv.r.un on a signed int: the source reads as unsigned, so the
        // C# spelling needs the (uint) cast or the value is wrong for
        // negative inputs.
        var convert = new Pipeline.Convert(
            TypeRef.CoreLib("System", "Double"), isChecked: false, isUnsigned: true,
            new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32")));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(convert));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Double"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (double)(uint)a;", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void TypedConstants_BoxedAndElementConstants_Retype()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var boxed = new Box(boolType, new Constant(1, TypeRef.CoreLib("System", "Int32")));
        block.Add(new ExpressionStatement(boxed));
        block.Add(new StoreElement(boolType,
            new LoadArgument(0, "flags", TypeRef.SzArray(boolType)),
            new Constant(0, TypeRef.CoreLib("System", "Int32")),
            new Constant(1, TypeRef.CoreLib("System", "Int32"))));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flags", TypeRef.SzArray(boolType))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        new TypedConstantsPass().Run(function, PassContext.None);

        Assert.Equal(true, ((Constant)boxed.Operand).Value);
        var store = function.Descendants.OfType<StoreElement>().Single();
        Assert.Equal(true, ((Constant)store.Value).Value);
        // The index constant stays int — element typing applies to the value.
        Assert.Equal(0, ((Constant)store.Index).Value);
        function.CheckInvariant();
    }

    [Fact]
    public void UnsignedOperations_CastSignedOperands_PlainWhenAlreadyUnsigned()
    {
        var signedInt = new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32"));
        var signedInt2 = new LoadArgument(1, "b", TypeRef.CoreLib("System", "Int32"));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: true, signedInt, signedInt2)));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "UInt32"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32")), new Parameter("b", TypeRef.CoreLib("System", "Int32"))],
            HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (uint)a / (uint)b;", CSharpPrinter.Print(function).Output!.Trim());

        // Already-unsigned operands print plain — div.un's semantics are
        // already conveyed by the types.
        var unsignedArg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "UInt32"));
        var unsignedArg2 = new LoadArgument(1, "b", TypeRef.CoreLib("System", "UInt32"));
        var container2 = new BlockContainer();
        var block2 = new Block(0);
        container2.Add(block2);
        block2.Add(new Return(new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: true, unsignedArg, unsignedArg2)));
        var function2 = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container2);

        Assert.Equal("return a / b;", CSharpPrinter.Print(function2).Output!.Trim());
    }

    [Fact]
    public void NumericBoundary_SameFamilySignChange_Casts()
    {
        // A uint value returned from an int-returning method needs (int): same
        // 4-byte stack family, but not implicitly convertible (CS0266).
        Assert.Equal("return (int)a;",
            ReturnOf("UInt32", "Int32"));
        // long into ulong return — same I8 family, sign change.
        Assert.Equal("return (ulong)a;",
            ReturnOf("Int64", "UInt64"));
    }

    [Fact]
    public void NumericBoundary_ImplicitWidening_NoCast()
    {
        // byte → int and ushort → int are implicit C# conversions; no cast.
        Assert.Equal("return a;", ReturnOf("Byte", "Int32"));
        Assert.Equal("return a;", ReturnOf("UInt16", "Int32"));
    }

    [Fact]
    public void NumericBoundary_StoreNarrowsToSlotType_Casts()
    {
        // A ushort value stored into a char local — both 2-byte, ushort→char is
        // not implicit, so the declaring store casts to the slot type.
        var arg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "UInt16"));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, TypeRef.CoreLib("System", "Char"), arg));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", TypeRef.CoreLib("System", "UInt16"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [TypeRef.CoreLib("System", "Char")], container);

        Assert.Contains("char V_0 = (char)a;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void NumericBoundary_InRangeConstant_RendersBare()
    {
        // 5 is in uint's range, so C#'s constant-expression conversion applies —
        // no cast needed.
        Assert.Equal("return 5;", ReturnConstant(5, "Int32", "UInt32"));
    }

    [Fact]
    public void NumericBoundary_OutOfRangeConstant_UncheckedCast()
    {
        // -1 is out of uint's range (uint.MaxValue's ldc.i4.m1); a bare literal
        // is CS0031, so reinterpret the bits with an unchecked cast.
        Assert.Equal("return unchecked((uint)(-1));", ReturnConstant(-1, "Int32", "UInt32"));
    }

    [Fact]
    public void NumericBoundary_SameWidthConv_SingleCast()
    {
        // A conv.u2 (→ ushort) feeding a char slot renders as one (char) cast on
        // the conversion's operand, not the redundant (char)((ushort)x).
        var arg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32"));
        var conv = new ILInspector.Decompiler.Pipeline.Convert(TypeRef.CoreLib("System", "UInt16"), isChecked: false, isUnsigned: false, arg);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, TypeRef.CoreLib("System", "Char"), conv));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [TypeRef.CoreLib("System", "Char")], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("char V_0 = (char)a;", output);
        Assert.DoesNotContain("(ushort)", output);
    }

    [Fact]
    public void BackingField_RendersAsProperty()
    {
        // An auto-property backing field <Count>k__BackingField has no spellable
        // C# name; a store/load on `this` renders as this.Count (the property),
        // which also disambiguates a constructor parameter that shadows it.
        var intType = TypeRef.CoreLib("System", "Int32");
        var declType = TypeRef.CoreLib("Synthetic", "T");
        var backing = new FieldRef(declType, "<Count>k__BackingField", intType)
        {
            BackingPropertyName = "Count",
        };
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreField(backing, new LoadArgument(0, "this", declType), new LoadArgument(1, "Count", intType)));
        block.Add(new Return(new LoadField(backing, new LoadArgument(0, "this", declType))));
        var signature = new MethodSignature(intType,
            [new Parameter("Count", intType)], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("M", declType, signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("this.Count = Count;", output);
        Assert.Contains("return this.Count;", output);
        Assert.DoesNotContain("k__BackingField", output);
    }

    [Fact]
    public void ImportedAutoPropertyBackingField_RendersAsProperty()
    {
        string output = PrintFixture("get_CompoundProperty");

        Assert.Contains("return this.CompoundProperty;", output);
        Assert.DoesNotContain("k__BackingField", output);
    }

    [Fact]
    public void CallArgument_NumericMismatch_Casts()
    {
        // M(uint) into an int parameter: the argument casts to the parameter
        // type, the call-site counterpart of the return/store boundary casts.
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var callee = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "M",
            TypeRef.CoreLib("System", "Void"), [intType], HasThis: false);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false, [new LoadArgument(0, "a", uintType)])));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", uintType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("F", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Contains("T.M((int)a);", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void ConvertOfOutOfRangeConstant_UsesUnchecked()
    {
        // conv.u8 of ldc.i4.m1 (ulong.MaxValue): the plain (ulong)-1 is CS0221,
        // so the conversion reinterprets the bits with unchecked.
        var conv = new ILInspector.Decompiler.Pipeline.Convert(
            TypeRef.CoreLib("System", "UInt64"), isChecked: false, isUnsigned: false,
            new Constant(-1, TypeRef.CoreLib("System", "Int32")));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(conv));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "UInt64"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return unchecked((ulong)(-1));", CSharpPrinter.Print(function).Output!.Trim());
    }

    static string ReturnConstant(int value, string constType, string returnType)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new Constant(value, TypeRef.CoreLib("System", constType))));
        var signature = new MethodSignature(TypeRef.CoreLib("System", returnType), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string ReturnOf(string sourceType, string returnType)
    {
        var arg = new LoadArgument(0, "a", TypeRef.CoreLib("System", sourceType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(arg));
        var signature = new MethodSignature(TypeRef.CoreLib("System", returnType),
            [new Parameter("a", TypeRef.CoreLib("System", sourceType))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    [Fact]
    public void Constructor_ImplicitBaseCall_IsSuppressed()
    {
        // A no-argument base-constructor call is implicit in C#, and the field
        // initializer (`_shadowed = 1`) the compiler emits before that base call
        // lifts to the field declaration — not the body, where it would
        // recompile to AFTER the base call.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, ".ctor");

        Assert.NotNull(function);
        var result = CSharpPrinter.Print(function);
        string output = result.Output!;
        Assert.DoesNotContain("base(", output);
        Assert.DoesNotContain(".ctor", output);
        Assert.DoesNotContain("_shadowed", output);
        Assert.Contains(("_shadowed", "1"), result.FieldInitializers);
    }

    [Fact]
    public void UnsignedComparison_InverseCondition_KeepsUnsignedCasts()
    {
        // brfalse over an unsigned comparison folds to the inverse operator;
        // the unsigned operand casts must survive the fold.
        var intType = TypeRef.CoreLib("System", "Int32");
        var comparison = new Comparison(ComparisonKind.LessThan, isUnsigned: true,
            new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LogicalNot(comparison), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("if ((uint)a >= (uint)b) goto IL_0004;", output);
    }

    [Fact]
    public void StraightLineCoreLibMethod_RendersExpected()
    {
        // The simplest class: a method needing no raising at all.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Collections.Generic.List`1", "get_Count");
        Assert.NotNull(function);

        string candidate = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Equal("return _size;", candidate);
    }
}

public class RaisingPassTests
{
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
        // assignment of a fresh value, never the illegal handler..ctor(...).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var result = CSharpPrinter.Print(RunThroughStructConstructor(nameof(CfgSampleClass.InterpolatedStruct), source));
        Assert.True(result.Succeeded);
        string output = result.Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Contains("new DefaultInterpolatedStringHandler(", output);
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
        Assert.Equal("Carrier V_0 = new Carrier(42);", CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").Trim());
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
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SelectBoolReturn), source);

        Assert.Contains("bool S_0", output);
        Assert.DoesNotContain("int S_0", output);
        Assert.Contains("false", output);
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
        // `target = S_0` (CS0029).
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
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flag", boolType), new Parameter("other", boolType), new Parameter("target", TypeRef.ByRef(boolType))],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("bool S_0", output);
        Assert.Contains("? false :", output);
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
    public void StackSlotLiveRange_ReusedStringListCount_SplitsTypedCarriers()
    {
        // One edge slot can carry unrelated straight-line values: a string
        // property value, then a nullable list receiver, then the int Count. The
        // final C# needs distinct synthetic carriers rather than assigning the
        // list/int values through the earlier string slot (CS0029).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReusedSlotStringListCount), source);

        Assert.Contains(".Missing", output);
        Assert.Contains(".Count", output);
        Assert.DoesNotContain("string S_0 = missing", output);
        Assert.DoesNotContain("string S_0 = S_", output);
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
        Assert.Contains("= ref a[CfgSampleClass.RecordIndex()];", output);
        Assert.Contains(") + v;", output);
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
    public void StackAlloc_ReturningPointer_PrintsPointerLocal()
    {
        // A stackalloc expression cannot be returned directly as a pointer, nor
        // explicitly cast in expression position (CS8346). Route it through a
        // pointer local and cast that local to the signature's pointer type.
        using var source = MetadataSource.Open(typeof(ILInspector.Decompiler.Fixtures.UnsafeChainA.LibraryA).Assembly.Location);
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
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

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
        Assert.Contains("ref int V_0 = ref T.A();", output);
        Assert.Contains("V_0 = ref T.B();", output);
        Assert.DoesNotContain("V_0 = T.A();", output);
    }

    [Fact]
    public void RefLocal_DeclaredBeforeStore_InitializesToNullRef()
    {
        // A ref local whose defining store is not the entry-block first
        // reference declares up front. A bare `ref int V_0;` is CS8174, so it
        // spells IL's null-reference zero-init via Unsafe.NullRef<T>().
        var intType = TypeRef.CoreLib("System", "Int32");
        var refInt = TypeRef.ByRef(intType);
        var container = new BlockContainer();
        var entry = new Block(0);
        container.Add(entry);
        entry.Add(new Branch(1));
        var body = new Block(1);
        container.Add(body);
        var getRef = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "A", refInt, [], HasThis: false);
        body.Add(new StoreLocal(0, refInt, new Call(getRef, isVirtual: false, [])));
        body.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [refInt], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int V_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();", output);
        Assert.DoesNotContain("ref int V_0;", output);
    }

    [Fact]
    public void RefSlot_DeclaredUpFront_InitializesToNullRef()
    {
        // The same null-ref zero-init applies to a ref-typed stack slot whose
        // store is not its declaring site.
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

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int S_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();", output);
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
    public void ExternalEntryIntoMultiBlockDoWhile_StaysFlat()
    {
        var function = ExternalEntryIntoMultiBlockLoop();

        new DoWhileLoopPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DoWhileLoop>());
        Assert.NotEmpty(function.Descendants.OfType<Branch>());
        function.CheckInvariant();
    }

    static IrFunction ExternalEntryIntoMultiBlockLoop()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var body = new BlockContainer();

        var entry = new Block(0);
        entry.Add(new Branch(20));
        body.Add(entry);

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
            new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0),
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
    public void LocalFunctionCall_DemanglesToSourceName()
    {
        // A call to the compiler-generated local function <Outer>g__Helper|0_0
        // renders its source name Helper, not the invalid mangled identifier.
        var voidType = TypeRef.CoreLib("System", "Void");
        var callee = new MethodRef(TypeRef.CoreLib("Synthetic", "Owner"),
            "<Outer>g__Helper|0_0", voidType, [], HasThis: false);

        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false, [])));
        block.Add(new Return(null));
        container.Add(block);
        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "Owner"), signature, [], container);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("Helper()", output);
        Assert.DoesNotContain("g__", output);
        Assert.DoesNotContain("<", output);
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
        // CSharpPrinter.IsUnsafeOperation is a real #921 nonnested-forward-guards
        // representative: csc lowers a type-test dispatch to guard branches whose
        // arms either return directly or feed a tiny stack-slot return diamond.
        // Folding the inner return diamonds and isolated return tail exposes
        // return leaves that the structurer can inline, eliminating the goto soup.
        using var source = MetadataSource.Open(typeof(CSharpPrinter).Assembly.Location);
        var function = IrImporter.Import(
            source,
            "ILInspector.Decompiler.Pipeline.CSharpPrinter",
            "IsUnsafeOperation");
        Assert.NotNull(function);

        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain(function.Descendants.OfType<ConditionalBranch>(), _ => true);
        Assert.Contains("if (node is Call c)", output);
        Assert.Contains("return c.Callee.RequiresUnsafe ? true : CSharpPrinter.SignatureRequiresUnsafe(c.Callee);", output);
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
        // raises — no surviving goto, and the folded ternary present.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SlotDiamondDispatch));
        Assert.NotNull(function);
        IrPasses.Run(function!);
        string output = CSharpPrinter.Print(function!).Output!;

        Assert.DoesNotContain("goto", output);
        Assert.Contains("?", output);   // a folded conditional arm survives
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
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

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
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

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

/// <summary>
/// The EH structuring slice: flat regions raise to TryCatch/TryFinally with
/// consumed regions, entry stores fold into clause variables, tail leaves
/// trim to fallthrough, and the supported typed filter shape raises to
/// catch-when — while out-of-slice EH shapes keep the flat form with regions
/// intact.
/// </summary>
public class EhStructuringTests
{
    static (IrFunction Function, string Output) RaiseFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return (function, result.Output!.ReplaceLineEndings("\n").TrimEnd());
    }

    [Fact]
    public void TryFinally_RaisesToStructuredForm()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TryFinallyAdd));

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("finally", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("endfinally", output);
    }

    [Fact]
    public void TryFinally_MultipleReturns_InlineThroughFinally()
    {
        // The two in-try returns leave to distinct return blocks; the EH pass
        // inlines them so the try/finally raises with the returns in the body
        // and no leftover goto/leave.
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TryFinallyTwoReturns));

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("finally", output);
        Assert.Equal(2, function.Descendants.OfType<Return>().Count());
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("endfinally", output);
    }

    [Fact]
    public void Catch_EntryStore_FoldsIntoClauseVariable()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.CatchLogs));

        // Debug stores the catch variable at handler entry, folding the store into
        // the clause header; Release leaves the once-used exception on the stack and
        // EhStructuring synthesizes the variable back. Either way the region is
        // consumed and the try/catch binds a variable used by the handler body.
        Assert.Empty(function.Regions);
        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.NotNull(clause.VariableIndex);
        Assert.Matches(@"catch \(FormatException V_\d+\)", output);
        Assert.DoesNotContain("__exception", output);
        // The clause owns the declaration — nothing declares the local up front.
        Assert.DoesNotMatch(@"FormatException V_\d+;", output);
        // ldc.i4.m1 must print -1, not the ushort-wrapped 65535.
        Assert.Contains("-1;", output);
        Assert.DoesNotContain("65535", output);
    }

    [Fact]
    public void Catch_DiscardedException_PrintsBareType()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.CatchDiscards));

        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.Null(clause.VariableIndex);
        Assert.Matches(@"(?m)^catch \(FormatException\)$", output);
    }

    [Fact]
    public void CatchAll_PrintsBareCatch()
    {
        var (_, output) = RaiseFixture(nameof(CfgSampleClass.CatchEverything));

        Assert.Matches(@"(?m)^catch$", output);
    }

    [Fact]
    public void Rethrow_PrintsBareThrow()
    {
        var (_, output) = RaiseFixture(nameof(CfgSampleClass.LogAndRethrow));

        Assert.Contains("throw;", output);
        Assert.DoesNotContain("__exception", output);
    }

    [Fact]
    public void MultiCatch_PreservesClauseOrder()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TwoCatches));

        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.Equal(2, tryCatch.Clauses.Count);
        Assert.True(output.IndexOf("FormatException", StringComparison.Ordinal)
            < output.IndexOf("OverflowException", StringComparison.Ordinal));
    }

    [Fact]
    public void TryCatchFinally_TailLeaves_TrimThroughNestedConstructs()
    {
        // The arms of the inner try/catch leave straight past the outer
        // finally; in tail position that is plain fallthrough, so no goto
        // and no label survive.
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.ParseWithCleanup));

        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void Filter_RaisesToCatchWhen()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.FilteredLength));

        Assert.Empty(function.Regions);
        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        var clause = Assert.Single(tryCatch.Clauses);
        Assert.NotNull(clause.Filter);
        Assert.Empty(function.Descendants.OfType<EndFilter>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("catch (Exception e) when (e.Message.Length > 0)", output);
        Assert.DoesNotContain("endfilter", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void Overloads_EnumeratesEverySameNameMethod_IndexAlignsWithImport()
    {
        // Overloads is the harness's --dump disambiguator: it must list every
        // same-name method in metadata order, and each reported index must select
        // that same method through Import(overloadIndex).
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        var overloads = IrImporter.Overloads(source, typeof(CtorChainSamples).FullName!, ".ctor");

        Assert.True(overloads.Count > 1, "fixture should have multiple .ctor overloads");
        // Indices are dense and sequential from 0.
        Assert.Equal(Enumerable.Range(0, overloads.Count), overloads.Select(o => o.Index));
        // Instance constructors: HasThis, and each has a body that Import resolves
        // at the matching index.
        foreach (var overload in overloads)
        {
            Assert.True(overload.HasThis);
            Assert.True(overload.HasBody);
            Assert.NotNull(IrImporter.Import(source, typeof(CtorChainSamples).FullName!, ".ctor", overload.Index));
            Assert.StartsWith("(", overload.Describe());
        }

        // A distinct parameter arity proves the overloads are not aliased.
        Assert.Contains(overloads, o => o.ParameterTypes.Length != overloads[0].ParameterTypes.Length);
    }

    [Fact]
    public void Overloads_UnknownTypeOrMethod_IsEmpty()
    {
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        Assert.Empty(IrImporter.Overloads(source, "No.Such.Type", "Nope"));
        Assert.Empty(IrImporter.Overloads(source, typeof(CtorChainSamples).FullName!, "NoSuchMethod"));
    }
}

/// <summary>
/// Constructor-chain rendering: base/this calls print as body statements, the
/// spilled-this receiver (control-flow argument shapes) canonicalizes back to
/// <c>this</c>, and the implicit parameterless base call is suppressed.
/// </summary>
public class ConstructorChainTests
{
    static (string Body, string? Chain) RaiseCtor(int overloadIndex)
    {
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CtorChainSamples).FullName!, ".ctor", overloadIndex);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return (result.Output!.ReplaceLineEndings("\n").TrimEnd(), result.ConstructorChain);
    }

    [Fact]
    public void ImplicitParameterlessBase_IsSuppressed()
    {
        // ctor#0: public CtorChainSamples() { } — base() is implicit: no body
        // statement and no initializer.
        var (body, chain) = RaiseCtor(0);
        Assert.Equal("", body);
        Assert.Null(chain);
    }

    [Fact]
    public void BaseCall_LiftsToInitializer()
    {
        // ctor#1: : base(message) — the explicit chain lifts out of the body to
        // a signature initializer (a base(...) body statement is CS0175).
        var (body, chain) = RaiseCtor(1);
        Assert.Equal("base(message)", chain);
        Assert.DoesNotContain("base(", body);
    }

    [Fact]
    public void SpilledCoalesceArgument_LiftsToInitializer()
    {
        // ctor#3: base(message ?? "default") — the ?? spill keeps the base call
        // off the first statement; the constructor-chain argument pass inlines
        // the spill so the call lifts to the signature initializer instead of
        // leaking as an invalid base(temp); body statement (CS0175).
        var (body, chain) = RaiseCtor(3);

        Assert.Equal("base(message ?? \"default\")", chain);
        Assert.DoesNotContain("base(", body);
        Assert.DoesNotContain("..ctor", body);
    }

    [Fact]
    public void SpilledTernaryArgument_LiftsWithArgumentInlined()
    {
        // ctor#2: base(code > 0 ? "positive" : null) — the ternary argument
        // spills to a temp; the pass inlines it into the lifted initializer so
        // the base argument survives (it would otherwise drop on recompile).
        var (body, chain) = RaiseCtor(2);

        Assert.NotNull(chain);
        Assert.StartsWith("base(", chain);
        Assert.Contains("\"positive\"", chain);
        Assert.DoesNotContain("base(", body);
    }

    [Fact]
    public void ThisDelegation_LiftsToInitializer()
    {
        // ctor#4: : this(value.ToString()) — lifts to the signature initializer.
        var (_, chain) = RaiseCtor(4);

        Assert.StartsWith("this(", chain);
        Assert.DoesNotContain("base(", chain);
        Assert.DoesNotContain("..ctor", chain);
    }

    static DecompilerResult RaiseDefaultCtor(Type type)
    {
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, ".ctor");
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function);
        Assert.True(result.Succeeded);
        return result;
    }

    [Fact]
    public void ConstantFieldInitializer_LiftsToFieldDeclaration()
    {
        // CfgSampleClass declares `int _shadowed = 1;`. The compiler emits that
        // store before the (implicit) base call, so it is a field initializer,
        // not a body assignment — lift it to the field declaration and drop it
        // from the body (where it would recompile to AFTER the base call).
        var result = RaiseDefaultCtor(typeof(CfgSampleClass));

        Assert.Contains(("_shadowed", "1"), result.FieldInitializers);
        Assert.DoesNotContain("_shadowed", result.Output);
    }

    [Fact]
    public void NewObjectFieldInitializer_LiftsToFieldDeclaration()
    {
        // LockFixtureSamples declares `readonly object _root = new();`. The
        // `new object()` initializer is self-contained (no this/parameter/local
        // load), so it is legal in field-declaration context and lifts there.
        var result = RaiseDefaultCtor(typeof(LockFixtureSamples));

        Assert.Contains(("_root", "new object()"), result.FieldInitializers);
        Assert.DoesNotContain("_root", result.Output);
    }
}

public class IdentityConvertTests
{
    [Fact]
    public void ArrayLengthConversion_IsElided()
    {
        // ldlen yields the int-typed ArrayLength; the trailing conv.i4 is an
        // identity conversion and must not print as a cast.
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.SzArray(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var length = new ArrayLength(new LoadArgument(0, "a", arrayType));
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: false, isUnsigned: false, length)));
        var signature = new MethodSignature(intType, [new Parameter("a", arrayType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return a.Length;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void GenuineNarrowing_IsKept()
    {
        // conv.i4 of a long is a real narrowing — the cast stays.
        var intType = TypeRef.CoreLib("System", "Int32");
        var longType = TypeRef.CoreLib("System", "Int64");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: false, isUnsigned: false, new LoadArgument(0, "x", longType))));
        var signature = new MethodSignature(intType, [new Parameter("x", longType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (int)x;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void CheckedUnsignedConversion_AtEqualType_IsKept()
    {
        // conv.ovf.i4.un of an int is Int32 -> Int32, but it reinterprets the
        // source as unsigned and throws for negative bit patterns — eliding it
        // would drop the overflow check. The cast must survive equal types.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: true, isUnsigned: true, new LoadArgument(0, "x", intType))));
        var signature = new MethodSignature(intType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return checked((int)(uint)x);", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }
}

/// <summary>
/// The lock-sugar pass: the csc Monitor lockTaken lowering raises to a
/// lock (obj) { ... } statement, the synthetic V_object/V_taken locals
/// disappear, and the lock object is the original expression.
/// </summary>
public class LockSugarTests
{
    static string RaiseLock(string methodName)
    {
        using var source = MetadataSource.Open(typeof(LockFixtureSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LockFixtureSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n").TrimEnd();
    }

    [Fact]
    public void VoidLock_RaisesToLockStatement()
    {
        var (function, output) = (IrImportFor(nameof(LockFixtureSamples.IncrementUnderLock)), RaiseLock(nameof(LockFixtureSamples.IncrementUnderLock)));

        Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());            // the try/finally is consumed
        Assert.DoesNotContain("Monitor", output);                          // no Monitor.Enter/Exit left
        Assert.Matches(@"lock \(_root\)", output);
        Assert.DoesNotContain("bool V_", output);                          // the lockTaken local is gone
    }

    [Fact]
    public void LockOnParameter_UsesParameterExpression()
    {
        Assert.Contains("lock (gate)", RaiseLock(nameof(LockFixtureSamples.LockOnParameter)));
    }

    [Fact]
    public void LockBody_IsStillRaised()
    {
        // The body inside the lock continues through later passes.
        string output = RaiseLock(nameof(LockFixtureSamples.ReadUnderLock));
        Assert.Contains("lock (_root)", output);
        Assert.DoesNotContain("Monitor", output);
    }

    [Fact]
    public void CoreLibStaticFieldLock_PreservesCopiedReceiverLocal()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.AppContext", "GetData");
        Assert.NotNull(function);
        IrPasses.Run(function!);

        var output = CSharpPrinter.Print(function!).Output;

        Assert.NotNull(output);
        Assert.Contains("lock (V_1)", output);
        Assert.DoesNotContain("lock (AppContext.s_dataStore)", output);
    }

    static IrFunction IrImportFor(string methodName)
    {
        using var source = MetadataSource.Open(typeof(LockFixtureSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LockFixtureSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        return function;
    }
}

/// <summary>
/// Soundness of the lock-sugar match, on shapes the C# compiler never emits
/// but hand-written or obfuscated IL could: a lockTaken local read after the
/// try/finally, a same-named Monitor from a non-BCL assembly, a wrong Enter
/// signature, extra trailing work in the finally, and an Exit whose object does
/// not match the Enter. Each must leave the construct flat. Built directly in the
/// post-structuring shape and run through LockSugarPass alone.
/// </summary>
public class LockSugarSoundnessTests
{
    static IrFunction BuildLock(string monitorAssembly, bool strayTakenRef, bool malformedEnterSignature = false, bool staticFieldLockObject = false, bool extraFinallyWork = false, bool mismatchedExitObject = false)
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var objType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var monitor = monitorAssembly == TypeRef.CoreLibrary
            ? TypeRef.CoreLib("System.Threading", "Monitor")
            : TypeRef.Definition(monitorAssembly, "System.Threading", "Monitor");
        // A malformed Enter returns object instead of void — same name, type,
        // and argument node shapes, wrong signature.
        var enterReturn = malformedEnterSignature ? objType : voidType;
        var enterRef = new MethodRef(monitor, "Enter", enterReturn, [objType, TypeRef.ByRef(boolType)], HasThis: false);
        var exitRef = new MethodRef(monitor, "Exit", voidType, [objType], HasThis: false);
        // A second object local (index 2) so the finally can Exit a DIFFERENT
        // object than Enter locked — Enter(V_0)/Exit(V_2): not a real lock.
        System.Collections.Immutable.ImmutableArray<TypeRef> locals = mismatchedExitObject ? [objType, boolType, objType] : [objType, boolType];
        int exitObjectIndex = mismatchedExitObject ? 2 : 0;

        var tryBlock = new Block(0);
        tryBlock.Add(new ExpressionStatement(new Call(enterRef, false,
            [new LoadLocal(0, objType), new LoadLocalAddress(1, boolType)])));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);

        var thenBlock = new Block(0);
        thenBlock.Add(new ExpressionStatement(new Call(exitRef, false, [new LoadLocal(exitObjectIndex, objType)])));
        var finallyBlock = new Block(0);
        finallyBlock.Add(new IfStatement(new LoadLocal(1, boolType), thenBlock, null));
        if (extraFinallyWork)
            finallyBlock.Add(new ExpressionStatement(new LoadLocal(0, objType)));   // finally does more than guarded Exit
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);

        var entry = new Block(0);
        IrExpression lockObject = staticFieldLockObject
            ? new LoadField(new FieldRef(TypeRef.Definition("SyntheticAssembly", "Samples", "Owner"), "Gate", objType), instance: null)
            : new Constant(null, objType);
        entry.Add(new StoreLocal(0, objType, lockObject));
        entry.Add(new StoreLocal(1, boolType, new Constant(0, boolType)));
        entry.Add(new TryFinally(tryBody, finallyBody));
        if (strayTakenRef)
            entry.Add(new ExpressionStatement(new LoadLocal(1, boolType)));   // reads V_1 after the lock
        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, locals, body);
    }

    [Fact]
    public void CleanShape_Raises()   // positive control: the synthetic shape is well-formed
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void StaticFieldReceiver_RaisesButKeepsObjectCopy()
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, staticFieldLockObject: true);
        new LockSugarPass().Run(function, PassContext.None);

        var lockNode = Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        var lockObject = Assert.IsType<LoadLocal>(lockNode.LockObject);
        Assert.Equal(0, lockObject.Index);
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => store.Index == 0);
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void LockTakenReadAfterTryFinally_StaysFlat()
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: true);
        new LockSugarPass().Run(function, PassContext.None);

        // Detaching the stores would strand the later read of V_1.
        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void MonitorFromOtherAssembly_StaysFlat()
    {
        var function = BuildLock("SomeUserAssembly", strayTakenRef: false);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void WrongMonitorSignature_StaysFlat()
    {
        // Right name, type, and argument shapes — but Enter returns object,
        // not void. The signature check rejects it.
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, malformedEnterSignature: true);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ExtraFinallyWork_StaysFlat()
    {
        // The finally does more than the guarded Monitor.Exit. csc's lock never
        // emits trailing finally work, so the single-statement finally check
        // must reject it — raising would drop that extra statement.
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, extraFinallyWork: true);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void ExitObjectMismatch_StaysFlat()
    {
        // Enter locks V_0 but the finally Exits a different object (V_2). The two
        // are not a matched lock acquire/release, so the object-identity check
        // must reject it rather than invent lock (V_0).
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, mismatchedExitObject: true);
        new LockSugarPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }
}

/// <summary>
/// The printer's shape-driven truthiness: given a resolved TypeShape for a
/// non-generic definition branch operand, an enum zero-tests and a reference
/// null-tests. Built directly with a TypeShapes map so the rendering is tested
/// independent of csc codegen (enum brfalse is Release-only).
/// </summary>
public class TypeShapeTruthinessTests
{
    static string PrintConditionOn(TypeRef conditionType, TypeShape shape, bool negated = false)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        IrExpression condition = new LoadLocal(0, conditionType);
        if (negated)
            condition = new LogicalNot(condition);
        var entry = new Block(0);
        entry.Add(new IfStatement(condition, then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [conditionType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [conditionType] = shape },
        };
        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void EnumShape_ZeroTests()
    {
        var enumType = TypeRef.Definition("asm", "NS", "MyEnum");
        Assert.Contains("if (V_0 != 0)", PrintConditionOn(enumType, TypeShape.Enum));
    }

    [Fact]
    public void ReferenceShape_NullTests()
    {
        var classType = TypeRef.Definition("asm", "NS", "MyClass");
        Assert.Contains("if (V_0 is not null)", PrintConditionOn(classType, TypeShape.Reference));
    }

    [Fact]
    public void ReferenceHint_NullTests()
    {
        var classType = TypeRef.Definition("other-asm", "NS", "MyClass", ValueTypeHint.ReferenceType);

        Assert.Contains("if (V_0 is not null)", PrintConditionOn(classType, TypeShape.Unknown));
        Assert.Contains("if (V_0 is null)", PrintConditionOn(classType, TypeShape.Unknown, negated: true));
    }

    [Fact]
    public void ValueTypeHint_ZeroTests()
    {
        var enumType = TypeRef.Definition("other-asm", "NS", "MyEnum", ValueTypeHint.ValueType);
        Assert.Contains("if (V_0 != 0)", PrintConditionOn(enumType, TypeShape.Unknown));
    }

    [Fact]
    public void UnknownShapeWithoutHint_StaysRaw()
    {
        // A cross-assembly definition with no CLASS/VALUETYPE hint resolves to
        // Unknown — print raw, no guess.
        var classType = TypeRef.Definition("other-asm", "NS", "Mystery");
        string output = PrintConditionOn(classType, TypeShape.Unknown);

        Assert.DoesNotContain("is null", output);
        Assert.DoesNotContain("!= 0", output);
        Assert.Contains("V_0", output);   // still references the operand, raw
    }
}

/// <summary>
/// Enum-constant naming: an integer flowing into an enum position retypes to
/// the enum and prints as EnumType.Member from the resolved same-assembly
/// member map. A value with no single member casts to the enum rather than
/// rendering a bare int (which would be CS0266).
/// </summary>
public class EnumConstantTests
{
    [Fact]
    public void GenericArgumentEnumShape_RegistersEnumMembers()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReadOnlySpanEnumFirst));
        Assert.NotNull(function);

        var enumType = function!.Signature.Parameters[0].Type.TypeArguments[0];

        Assert.Equal(TypeShape.Enum, function.TypeShapes.GetValueOrDefault(enumType));
        Assert.True(function.EnumMembers.TryGetValue(enumType, out var members));
        Assert.Contains(members!, member => member.Value == nameof(CfgPriority.High));
    }

    [Fact]
    public void EnumArgument_RendersMemberName()
    {
        // TakesPriority(CfgPriority.High) — the ldc.i4.2 names as High, not 2.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CallWithHighPriority), source);

        Assert.Contains("CfgPriority.High", output);
        Assert.DoesNotContain("TakesPriority(2)", output);
    }

    [Fact]
    public void HighBitUnsignedEnumMember_Names()
    {
        // CfgFlags.Top = 0x80000000 (uint) emits as int -2147483648. The
        // member-map key must reinterpret the uint as a signed int to match,
        // or this falls back to the raw -2147483648.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CallWithTopFlag), source);

        Assert.Contains("CfgFlags.Top", output);
        Assert.DoesNotContain("-2147483648", output);
    }

    [Fact]
    public void ExactMember_Names_UnmatchedValue_Casts()
    {
        var enumType = TypeRef.Definition("asm", "NS", "Color");
        var members = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
        {
            [enumType] = new Dictionary<long, string> { [1] = "Red", [2] = "Green" },
        };

        Assert.Equal("Color.Red", PrintEnumConstant(1, enumType, members));
        Assert.Equal("Color.Green", PrintEnumConstant(2, enumType, members));
        // 3 names no member (a composite flag value); a bare int would be CS0266,
        // so it casts. Naming flag combinations is a later slice.
        Assert.Equal("(Color)3", PrintEnumConstant(3, enumType, members));
        // A negative high-bit value must be parenthesized after the cast (CS0075).
        Assert.Equal("(Color)(-2147483648)", PrintEnumConstant(int.MinValue, enumType, members));
    }

    [Fact]
    public void EnumWithNoResolvedMembers_Casts()
    {
        // A cross-assembly enum absent from the member map still casts (the
        // value can't be named, but a bare int into the enum is CS0266).
        var enumType = TypeRef.Definition("other", "NS", "Mystery");
        Assert.Equal("(Mystery)5", PrintEnumConstant(5, enumType, new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()));
    }

    [Fact]
    public void EnumReturn_NamesMember_FromSeededSignature()
    {
        // The enum is reached only via the return-type signature; the constant
        // names CfgPriority.High only because ResolveTypeInfo seeds it.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReturnsHighPriority), source);

        Assert.Contains("return CfgPriority.High;", output);
        Assert.DoesNotContain("return 2;", output);
    }

    [Fact]
    public void EnumBitwiseOperand_NamesMember()
    {
        // p & CfgPriority.High — the ldc.i4.2 beside the enum retypes, so the
        // mask is not the CS0019 `p & 2`.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MaskHighPriority), source);

        Assert.Contains("CfgPriority.High", output);
        Assert.DoesNotContain("& 2", output);
    }

    [Fact]
    public void NonConstantIntToEnum_InsertsCast()
    {
        // (CfgPriority)value — a non-constant int into an enum-typed position has
        // no IL conv to lean on, so the printer must spell the explicit cast;
        // a bare `return value;` is CS0266 (int is not implicitly an enum).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ToPriority), source);

        Assert.Contains("return (CfgPriority)value;", output);
    }

    [Fact]
    public void ValueTypeThisRead_RendersAsThis()
    {
        // `ldarg.0; ldobj` reads the value through the `this` managed pointer;
        // C#'s `this` already denotes the value, so it renders bare — `*this`
        // would be CS0193 (`this` is not an unmanaged pointer).
        using var source = MetadataSource.Open(typeof(CfgSelf).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSelf).FullName!, nameof(CfgSelf.Identity), source);

        Assert.Contains("return this;", output);
        Assert.DoesNotContain("*this", output);
    }

    [Fact]
    public void TernaryIntoEnum_CastsWholeTernary()
    {
        // `StringComparison V_0 = flag ? 1 : 0;` is int->enum (CS0266). A ternary
        // of integer arms into an enum local must take the enum cast on the whole
        // merge — `(MyEnum)(flag ? 1 : 0)` — even though CastValue otherwise
        // leaves merge nodes uncast (the bail's CS0030 risk is type-parameter-only,
        // not a concrete enum).
        var enumType = TypeRef.Definition("asm", "NS", "MyEnum");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var ternary = new Conditional(new LoadArgument(0, "flag", boolType),
            new Constant(1, intType), new Constant(0, intType));
        var block = new Block(0);
        block.Add(new StoreLocal(0, enumType, ternary));
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [enumType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains(")(flag ? 1 : 0)", output);
        Assert.DoesNotContain("= flag ? 1 : 0;", output);
    }

    [Fact]
    public void IndirectStoreThroughTypedPointer_CastsToPointerElement()
    {
        // `*(uint*)p = (int)x` is int->uint (CS0266): a primitive `stind.i4`
        // carries `int`, but the C# lvalue `*p` is typed by the pointer (uint).
        // The store must cast to the pointer's element type, not the opcode's —
        // here a uint value into a uint* needs no cast at all.
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var address = new LoadLocal(0, TypeRef.Pointer(uintType));
        var value = new LoadLocal(1, uintType);
        var block = new Block(0);
        block.Add(new StoreIndirect(intType, address, value));
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [TypeRef.Pointer(uintType), uintType], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("*V_0 = V_1;", output);
        Assert.DoesNotContain("(int)", output);
    }

    [Fact]
    public void NotOverOperatorCall_Parenthesizes()
    {
        // !(a != b) — an operator-spelled call renders as a compound `a != b`,
        // so the enclosing `!` must parenthesize it; a bare `!a != b` binds as
        // `(!a) != b` (CS0023). The C# compiler folds this source form, so the
        // node is built directly.
        var type = TypeRef.CoreLib("System", "Type");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var callee = new MethodRef(type, "op_Inequality", boolType, [type, type], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var inequality = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", type), new LoadArgument(1, "b", type)]);
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(inequality)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("!(a != b)", output);
        Assert.DoesNotContain("!a != b", output);
    }

    [Fact]
    public void ResolvedNonOperatorOpAddition_RendersAsMethodCall()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "OperatorLikeLibrary");
        var callee = new MethodRef(declaring, "op_Addition", intType, [intType, intType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.No,
        };
        var call = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains(".op_Addition(a, b)", output);
        Assert.DoesNotContain("a + b", output);
    }

    [Fact]
    public void ResolvedNonOperatorOpImplicit_RendersAsMethodCall()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "OperatorLikeLibrary");
        var callee = new MethodRef(declaring, "op_Implicit", intType, [intType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.No,
        };
        var call = new Call(callee, isVirtual: false, [new LoadArgument(0, "value", intType)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains(".op_Implicit(value)", output);
        Assert.DoesNotContain("return (int)value;", output);
    }

    [Fact]
    public void UnresolvedNameInferredOpAddition_PreservesOperatorFallback()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "OperatorLikeLibrary");
        var callee = new MethodRef(declaring, "op_Addition", intType, [intType, intType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Unknown,
        };
        var call = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("return a + b;", output);
        Assert.DoesNotContain("op_Addition", output);
    }

    [Fact]
    public void IsInstanceValueType_RendersAsIs()
    {
        // `isinst <valuetype>` is `obj is T`, not `obj as T`: `as` on a
        // non-nullable value type is CS0077. Built directly so the rendering
        // is tested independent of csc codegen.
        var objType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var block = new Block(0);
        block.Add(new Return(new IsInstance(byteType, new LoadArgument(0, "obj", objType))));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [byteType] = TypeShape.ValueType },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("obj is byte", output);
        Assert.DoesNotContain(" as byte", output);
    }

    [Fact]
    public void IsInstanceValueType_AsCondition_NoIntCompare()
    {
        // A value-type `isinst` used as a branch test is already boolean
        // (`obj is byte`); wrapping it in `!= 0` would be `bool != int`
        // (CS0019), so the condition must render bare.
        var objType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new IsInstance(byteType, new LoadArgument(0, "obj", objType)), then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [byteType] = TypeShape.ValueType },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("obj is byte", output);
        Assert.DoesNotContain("!= 0", output);
    }

    [Fact]
    public void IsInstanceUnknownShape_AsCondition_RendersIs()
    {
        // A cross-assembly value type (e.g. BigInteger, Guid) whose shape the
        // printer could not resolve is still `obj is T` when tested as a branch
        // condition — `as` would be CS0077. The bug in issue #932: with no shape
        // entry the printer fell back to `as`, producing `if (obj as BigInteger)`.
        var objType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var bigIntType = TypeRef.Definition("System.Runtime.Numerics", "System.Numerics", "BigInteger");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new IsInstance(bigIntType, new LoadArgument(0, "obj", objType)), then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        // No TypeShapes entry for BigInteger: shape is unresolved, as for a real
        // cross-assembly struct.
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("obj is BigInteger", output);
        Assert.DoesNotContain(" as BigInteger", output);
    }

    static string PrintEnumConstant(int value, TypeRef enumType, IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>> members)
    {
        var block = new Block(0);
        block.Add(new Return(new Constant(value, enumType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container)
        {
            EnumMembers = members,
            // The real pipeline only gives a constant an enum type when that
            // type resolved as an enum shape; mirror that so the cast path fires.
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
        };
        // Strip the leading "return " and trailing ";" to get the operand text.
        string output = CSharpPrinter.Print(function).Output!.Trim();
        return output["return ".Length..].TrimEnd(';');
    }

    sealed class RaisingPassTestsAccessor
    {
        public string Print(string typeName, string methodName, MetadataSource source)
        {
            var function = IrImporter.Import(source, typeName, methodName);
            Assert.NotNull(function);
            IrPasses.Run(function);
            return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        }
    }
}

/// <summary>
/// String and char literal escaping: control characters, quotes, and
/// backslashes are escaped so the rendered literal always compiles. A raw
/// newline or tab in the output would be invalid C#.
/// </summary>
public class StringEscapingTests
{
    static string PrintStringConstant(string value)
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, stringType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(stringType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    [Fact]
    public void ControlCharacters_QuotesAndBackslashes_AreEscaped()
    {
        Assert.Equal("return \"a\\nb\";", PrintStringConstant("a\nb"));
        Assert.Equal("return \"\\t\\r\\0\";", PrintStringConstant("\t\r\0"));
        Assert.Equal("return \"say \\\"hi\\\"\";", PrintStringConstant("say \"hi\""));
        Assert.Equal("return \"c:\\\\tmp\";", PrintStringConstant("c:\\tmp"));
    }

    [Fact]
    public void OtherControlChar_UsesUnicodeEscape()
    {
        //  has no recognized short escape.
        Assert.Equal("return \"x\\u0001y\";", PrintStringConstant("xy"));
    }
}
