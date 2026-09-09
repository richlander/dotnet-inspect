using System.Collections.Immutable;
using System.IO;
using System.Linq;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using LegacyFixtures = ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
using LegacyDynamicStackallocFixtures = ILInspector.Decompiler.Fixtures.LegacyUnsafe.DynamicStackallocFixtures;
using NewFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;
using NewDynamicStackallocFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.DynamicStackallocFixtures;
using NewStackallocFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.StackallocInitializerResiduals;
using ChainB = ILInspector.Decompiler.Fixtures.UnsafeChainB.LibraryB;
using ChainC = ILInspector.Decompiler.Fixtures.UnsafeChainC.Program;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The unsafe-context emitter. The two fixture assemblies compile to identical
/// IL; the only difference the decompiler can observe is the new-rules module's
/// <c>MemorySafetyRulesAttribute</c>. The printer uses that signal to wrap
/// unsafe operations in explicit, minimally scoped <c>unsafe { }</c> blocks for
/// a new-rules module when one expression cannot contain the obligation, and in
/// <c>unsafe(expr)</c> otherwise. Legacy modules emit neither form because their
/// member <c>unsafe</c> modifier — rendered at the signature, not by this body
/// printer — still supplies the context.
/// </summary>
public class UnsafeEmitterTests
{
    static DecompilerResult DecompileResult(string assemblyPath, string typeFullName, string method)
    {
        var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, typeFullName, method);
        Assert.NotNull(function);
        return CSharpPrinter.PrintRaised(function!);
    }

    static string Decompile(string assemblyPath, string typeFullName, string method)
    {
        var result = DecompileResult(assemblyPath, typeFullName, method);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    /// <summary>Decompile in optimistic ("simulate") mode — force new-rules rendering.</summary>
    static string DecompileSimulate(string assemblyPath, string typeFullName, string method)
    {
        var source = MetadataSource.Open(assemblyPath);
        source.SimulateNewRules = true;
        var function = IrImporter.Import(source, typeFullName, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static string DecompileNew(string method) =>
        Decompile(typeof(NewFixtures).Assembly.Location, typeof(NewFixtures).FullName!, method);

    static string DecompileNewDynamicStackalloc(string method) =>
        Decompile(typeof(NewDynamicStackallocFixtures).Assembly.Location, typeof(NewDynamicStackallocFixtures).FullName!, method);

    static string DecompileLegacy(string method) =>
        Decompile(typeof(LegacyFixtures).Assembly.Location, typeof(LegacyFixtures).FullName!, method);

    static string DecompileLegacyDynamicStackalloc(string method) =>
        Decompile(typeof(LegacyDynamicStackallocFixtures).Assembly.Location, typeof(LegacyDynamicStackallocFixtures).FullName!, method);

    static string DecompileNewStackalloc(string method) =>
        Decompile(typeof(NewStackallocFixtures).Assembly.Location, typeof(NewStackallocFixtures).FullName!, method);

    static string DecompileChainB(string method) =>
        Decompile(typeof(ChainB).Assembly.Location, typeof(ChainB).FullName!, method);

    static string DecompileLegacySimulate(string method) =>
        DecompileSimulate(typeof(LegacyFixtures).Assembly.Location, typeof(LegacyFixtures).FullName!, method);

    static (IrFunction Function, IReadOnlyList<IAnnotation> Annotations) ClassifyNew(string method)
    {
        var source = MetadataSource.Open(typeof(NewFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(NewFixtures).FullName!, method);
        Assert.NotNull(function);
        return (function!, ResearchViews.CollectFacts(source, function!));
    }

    /// <summary>The body of the first <c>unsafe { }</c> block, by brace matching.</summary>
    static string FirstUnsafeBlockBody(string output)
    {
        int keyword = output.IndexOf("unsafe", StringComparison.Ordinal);
        Assert.True(keyword >= 0, "no unsafe block in output:\n" + output);
        int open = output.IndexOf('{', keyword);
        Assert.True(open >= 0);
        int depth = 0;
        for (int i = open; i < output.Length; i++)
        {
            if (output[i] == '{') depth++;
            else if (output[i] == '}' && --depth == 0)
                return output[(open + 1)..i];
        }
        throw new Xunit.Sdk.XunitException("unbalanced unsafe block:\n" + output);
    }

    [Fact]
    public void NewRulesModule_PointerDeref_UsesUnsafeExpression()
    {
        var output = DecompileNew(nameof(NewFixtures.DerefPointer));

        Assert.Contains("return unsafe(*(int*)(&value));", output);
        Assert.DoesNotContain("unsafe\n{", output);
    }

    [Fact]
    public void NewRulesModule_FunctionPointerInvoke_UsesUnsafeExpression()
    {
        var output = DecompileNew(nameof(NewFixtures.InvokeFunctionPointer));

        Assert.Contains("return unsafe(callback(x));", output);
        Assert.DoesNotContain("unsafe\n{", output);
    }

    [Fact]
    public void UpdatedRules_UnavailableCalleeContractDoesNotInventUnsafeContext()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var pointer = TypeRef.Pointer(int32);
        var callee = new MethodRef(
            TypeRef.Definition("Dependency", "Fixtures", "Library"),
            "GetPointer",
            pointer,
            [],
            HasThis: false)
        {
            MemorySafetyRulesState = MemorySafetyRulesState.Updated,
            MemorySafetyContractUnavailable = true,
        };
        var block = new Block(0);
        block.Add(new ExpressionStatement(
            new Call(callee, isVirtual: false, [])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Call",
            TypeRef.Definition("Consumer", "Fixtures", "Consumer"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.DoesNotContain("unsafe", result.Output);
        Assert.False(result.RequiresUnsafeBodyModifier);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
    }

    [Fact]
    public void NewRulesModule_VoidUnsafeInvocation_FallsBackToBlock()
    {
        // An unsafe expression is not itself a legal statement expression, and
        // a void invocation cannot be assigned to a discard. The block is
        // therefore the smallest valid context and cannot become an
        // expression-bodied member.
        var result = DecompileResult(
            typeof(NewFixtures).Assembly.Location,
            typeof(NewFixtures).FullName!,
            nameof(NewFixtures.FreePointer));

        Assert.NotNull(result.Output);
        Assert.Contains("unsafe\n{", result.Output);
        Assert.EndsWith("}", result.Output!.TrimEnd());
        Assert.False(result.BodyIsSingleExpressionBody);
    }

    [Fact]
    public void NewRulesModule_UnsafeCatchFilter_UsesUnsafeExpression()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var pointer = TypeRef.Pointer(int32);
        var tryBody = new BlockContainer();
        tryBody.Add(new Block(1));
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(2));
        var filter = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new LoadIndirect(int32, new LoadArgument(0, "p", pointer)),
            new Constant(0, int32));
        var block = new Block(0);
        block.Add(new TryCatch(
            tryBody,
            [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody, filter)]));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Holder"),
            new MethodSignature(voidType, [new Parameter("p", pointer)], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var result = CSharpPrinter.Print(function);

        Assert.NotNull(result.Output);
        Assert.Contains("catch (Exception) when (unsafe((*p) != 0))", result.Output);
        Assert.DoesNotContain("unsafe\n{", result.Output);
        AssertNoWarningsOrErrors(
            Recompile("static void M(int* p)", result.Output!),
            result.Output!);
    }

    [Theory]
    [InlineData("if", true)]
    [InlineData("while", true)]
    [InlineData("do", true)]
    [InlineData("catch", true)]
    [InlineData("lock", true)]
    [InlineData("using", true)]
    [InlineData("fixed", true)]
    [InlineData("for", false)]
    [InlineData("foreach", false)]
    [InlineData("switch", false)]
    public void NewRulesModule_RequiresUnsafePropertyHeader_UsesCompilerSupportedForm(
        string position,
        bool expectsBlock)
    {
        var boolean = TypeRef.CoreLib("System", "Boolean");
        var int32 = TypeRef.CoreLib("System", "Int32");
        var @object = TypeRef.CoreLib("System", "Object");
        var disposable = TypeRef.CoreLib("System", "IDisposable");
        var intArray = TypeRef.SzArray(int32);
        var owner = TypeRef.Definition("Synthetic", "", "Holder");
        TypeRef propertyType = position switch
        {
            "lock" => @object,
            "using" => disposable,
            "fixed" or "foreach" => intArray,
            "switch" => int32,
            _ => boolean,
        };
        string propertyName = position switch
        {
            "lock" => "RiskyObject",
            "using" => "RiskyDisposable",
            "fixed" or "foreach" => "RiskyArray",
            "switch" => "RiskyInt",
            _ => "Risky",
        };
        var getter = new MethodRef(
            owner,
            $"get_{propertyName}",
            propertyType,
            [],
            HasThis: false)
        {
            IsSpecialName = true,
            RequiresUnsafe = true,
        };
        LoadProperty Property() => new(getter, instance: null, []);
        Block EmptyBlock() => new();
        BlockContainer EmptyContainer()
        {
            var empty = new BlockContainer();
            empty.Add(new Block());
            return empty;
        }
        Block ForBody()
        {
            var body = new Block();
            body.Add(new ExpressionStatement(new LoadLocal(0, int32)));
            return body;
        }
        BlockContainer SwitchBody()
        {
            var body = new BlockContainer();
            var block = new Block();
            block.Add(new Break());
            body.Add(block);
            return body;
        }

        IrNode statement = position switch
        {
            "if" => new IfStatement(Property(), EmptyBlock(), elseArm: null),
            "while" => new WhileLoop(Property(), EmptyBlock()),
            "do" => new DoWhileLoop(EmptyContainer(), Property()),
            "catch" => new TryCatch(
                EmptyContainer(),
                [new CatchClause(
                    TypeRef.CoreLib("System", "Exception"),
                    EmptyContainer(),
                    Property())]),
            "lock" => new ILInspector.Decompiler.Pipeline.Lock(
                Property(),
                EmptyContainer()),
            "using" => new UsingStatement(
                localIndex: 0,
                disposable,
                Property(),
                EmptyContainer()),
            "fixed" => new Fixed(
                int32,
                localIndex: 0,
                Property(),
                EmptyContainer(),
                sourceIsAddress: false),
            "for" => new ForLoop(
                new StoreLocal(0, int32, new Constant(0, int32)),
                Property(),
                new StoreLocal(0, int32, new Constant(1, int32)),
                ForBody()),
            "foreach" => new ForeachStatement(
                localIndex: 0,
                int32,
                Property(),
                EmptyBlock()),
            "switch" => new Switch(
                Property(),
                [new SwitchSection([], isDefault: true, SwitchBody())]),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };
        var block = new Block();
        block.Add(statement);
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        ImmutableArray<TypeRef> locals = position switch
        {
            "using" => [disposable],
            "fixed" => [TypeRef.Pointer(int32)],
            "for" or "foreach" => [int32],
            _ => [],
        };
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "__Gate"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            locals,
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var body = CSharpPrinter.Print(function).Output!;

        if (expectsBlock)
        {
            Assert.Contains("unsafe\n{", body);
            Assert.DoesNotContain($"unsafe(Holder.{propertyName})", body);
        }
        else
        {
            Assert.DoesNotContain("unsafe\n{", body);
            Assert.Contains($"unsafe(Holder.{propertyName})", body);
        }
        AssertNoWarningsOrErrors(
            Recompile(
                "static void M()",
                body,
                """
                public sealed class Resource : IDisposable { public void Dispose() { } }
                public static class Holder
                {
                    public static bool Risky { unsafe get => true; }
                    public static object RiskyObject { unsafe get => new object(); }
                    public static IDisposable RiskyDisposable { unsafe get => new Resource(); }
                    public static int[] RiskyArray { unsafe get => new int[1]; }
                    public static int RiskyInt { unsafe get => 1; }
                }
                """),
            body);
    }

    [Fact]
    public void NewRulesModule_FixedAddressInitializer_RetainsUnsafeBlock()
    {
        var body = Decompile(
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals).Assembly.Location,
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals).FullName!,
            nameof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals.ReadAtThroughFixedAddress));

        Assert.Contains("unsafe\n{", body);
        Assert.Contains("fixed (int* ", body);
        Assert.Contains(" = &Data[index])", body);
        Assert.DoesNotContain("unsafe(&Data[index])", body);
        AssertNoWarningsOrErrors(
            Recompile(
                "public int M(int index)",
                body,
                containingTypeHeader: "public struct __Gate",
                typeMembers: "public fixed int Data[4];"),
            body);
    }

    [Fact]
    public void NewRulesModule_PointerElementAccessInLoop_UsesUnsafeExpression()
    {
        // The pointer element access is one statement inside the loop body, so
        // the unsafe block must wrap only that statement — not the surrounding
        // loop control. A whole-loop wrap would swallow the increment.
        var output = DecompileNew(nameof(NewFixtures.SumPinned));
        Assert.Contains("sum += unsafe(p[i]);", output);
        Assert.DoesNotContain("unsafe\n{", output);
        Assert.Contains("i++", output);
    }

    [Fact]
    public void LegacyModule_PointerDeref_EmitsNoUnsafeBlock()
    {
        // A legacy module relies on the member `unsafe` modifier for its body
        // context, so the body printer emits no block.
        var output = DecompileLegacy(nameof(LegacyFixtures.DerefPointer));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void LegacyModule_PointerElementAccessInLoop_EmitsNoUnsafeBlock()
    {
        var output = DecompileLegacy(nameof(LegacyFixtures.SumPinned));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_RequiresUnsafeCall_UsesUnsafeExpression()
    {
        // Risky() has no pointers but is declared `unsafe`, so the compiler
        // stamps it requires-unsafe. Every call site needs an unsafe context
        // even though no pointer crosses the boundary.
        var output = DecompileNew(nameof(NewFixtures.CallRisky));

        Assert.Contains("return unsafe(Risky());", output);
        Assert.DoesNotContain("unsafe\n{", output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewRulesModule_ResidualSwitchSelector_UsesUnsafeExpression(
        bool raised)
    {
        string assemblyPath = CompileUpdatedRulesAssembly(
            """
            public static class Probe
            {
                public static unsafe int Risky() => 2;

                public static int SharedSwitch(bool flag)
                {
                    if (flag) goto Shared;
                    unsafe
                    {
                        switch (Risky())
                        {
                            case 0: goto Shared;
                            case 1: return 2;
                            case 2: return 9;
                            case 3: return 7;
                            default: return 0;
                        }
                    }
                Shared:
                    return 8;
                }
            }
            """);
        try
        {
            using var source = MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, "Probe", "SharedSwitch");
            Assert.NotNull(function);
            Assert.NotEmpty(function!.Descendants.OfType<SwitchBranch>());

            var result = raised
                ? CSharpPrinter.PrintRaised(function)
                : CSharpPrinter.PrintLowered(function);
            string output = result.Output!;

            Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
            Assert.Contains(
                "__switchValue0 = (int)(unsafe(Risky()));",
                output);
            Assert.DoesNotContain("__switchValue0 = (int)(Risky());", output);
            AssertNoWarningsOrErrors(
                Recompile(
                    "static int M(bool flag)",
                    output,
                    typeMembers: "static unsafe int Risky() => 2;"),
                output);
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public void NewRulesModule_CrossAssemblyRequiresUnsafeCall_UsesUnsafeExpression()
    {
        // B.M2 calls A.M1 — a pointerless requires-unsafe method in another
        // assembly. The RequiresUnsafeAttribute lives on A.M1's MethodDef, so it
        // is invisible in B's MemberRef and the signature carries no pointer; the
        // wrap is possible only by resolving A cross-assembly (MetadataContext).
        var output = DecompileChainB(nameof(ChainB.M2));

        Assert.Contains("return unsafe(LibraryA.M1());", output);
        Assert.DoesNotContain("unsafe\n{", output);
    }

    [Fact]
    public void NewRulesModule_CrossAssemblySafePointerCall_NeedsNoUnsafeBlock()
    {
        var result = DecompileResult(
            typeof(ChainB).Assembly.Location,
            typeof(ChainB).FullName!,
            nameof(ChainB.AwaitSafePointer));

        Assert.True(
            result.Fidelity == DecompilationFidelity.Full,
            $"{result.Fidelity}: {result.Output}{Environment.NewLine}{string.Join(Environment.NewLine, result.Diagnostics)}");
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("return await LibraryA.SafePointerTask", result.Output);
        Assert.DoesNotContain("unsafe", result.Output);
    }

    [Fact]
    public void LegacyModule_RequiresUnsafeCall_EmitsNoUnsafeBlock()
    {
        // A legacy module relies on the member `unsafe` modifier for its body
        // context, so the call to the unsafe member needs no block here.
        var output = DecompileLegacy(nameof(LegacyFixtures.CallRisky));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_CompatPointerSignatureCall_WrapsInUnsafeBlock()
    {
        // NativeMemory.Free has a pointer in its signature, so under compat mode
        // it is requires-unsafe even though its attributes are cross-assembly.
        // The call must be wrapped; the pointer parameter itself is safe.
        var output = DecompileNew(nameof(NewFixtures.FreePointer));

        Assert.Contains("Free", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void NewRulesModule_ByRefReturn_UsesUnsafeExpression()
    {
        var output = Decompile(
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals).Assembly.Location,
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals).FullName!,
            nameof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals.RefAt));

        Assert.Contains("return ref unsafe(Data[index]);", output);
        Assert.DoesNotContain("unsafe\n{", output);
        AssertNoWarningsOrErrors(
            Recompile(
                "public ref int M(int index)",
                output,
                containingTypeHeader: "public struct __Gate",
                typeMembers: "public fixed int Data[4];"),
            output);
    }

    [Fact]
    public void LegacyModule_CompatPointerSignatureCall_EmitsNoUnsafeBlock()
    {
        var output = DecompileLegacy(nameof(LegacyFixtures.FreePointer));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_StackAllocSpanSkipInit_UsesUnsafeInitializer()
    {
        // stackalloc -> Span under [SkipLocalsInit] is unsafe. An unsafe
        // expression keeps the declaration inline and the local in its original
        // scope, so no forward declaration or explicit scoped modifier is needed.
        var output = DecompileNew(nameof(NewFixtures.StackAllocSkipInit));

        Assert.Contains("Span<int> s = unsafe(stackalloc int[n]);", output);
        Assert.DoesNotContain("new Span", output);
        Assert.DoesNotContain("stackalloc byte[", output);
        Assert.DoesNotContain("scoped Span<int> s", output);
        Assert.DoesNotContain("unsafe\n{", output);
        Assert.Contains("s.Length", output);
    }

    [Fact]
    public void UnsafeBodyModifierFact_UpdatedSkipLocalsInitUsesExplicitBlock()
    {
        var unsafeResult = DecompileResult(
            typeof(NewFixtures).Assembly.Location,
            typeof(NewFixtures).FullName!,
            nameof(NewFixtures.StackAllocSkipInit));
        var safeResult = DecompileResult(
            typeof(NewFixtures).Assembly.Location,
            typeof(NewFixtures).FullName!,
            nameof(NewFixtures.StackAllocDefault));

        Assert.Contains("unsafe", unsafeResult.Output);
        Assert.False(unsafeResult.RequiresUnsafeBodyModifier);
        Assert.False(safeResult.RequiresUnsafeBodyModifier);
    }

    [Fact]
    public void UnsafeBodyModifierFact_LegacyNestedLambdaOperationRequiresMemberContext()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var pointer = TypeRef.Pointer(int32);
        var callback = TypeRef.Definition(
            "Synthetic",
            "Synthetic",
            "Callback");
        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(new LoadIndirect(
            int32,
            new LoadArgument(0, "pointer", pointer))));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var block = new Block();
        block.Add(new Return(new Lambda(
            callback,
            [new Parameter("pointer", pointer)],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Synthetic", "Owner"),
            new MethodSignature(
                callback,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var result = CSharpPrinter.Print(function);

        Assert.True(result.RequiresUnsafeBodyModifier);
        Assert.DoesNotContain("unsafe\n{", result.Output);
    }

    [Fact]
    public void UnsafeBodyModifierFact_ExcludesStructThisInitialization()
    {
        var structType = TypeRef.Definition(
            "Synthetic",
            "Synthetic",
            "Value",
            ValueTypeHint.ValueType);
        var voidType = TypeRef.CoreLib("System", "Void");
        var block = new Block(0);
        block.Add(new InitObject(
            structType,
            new LoadArgument(0, "this", structType)));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Reset",
            structType,
            new MethodSignature(
                voidType,
                [],
                HasThis: true,
                GenericParameterCount: 0),
            [],
            body);

        var result = CSharpPrinter.Print(function);

        Assert.NotNull(result.Output);
        Assert.False(result.RequiresUnsafeBodyModifier);
    }

    [Fact]
    public void NewRulesModule_StackAllocPointerInitializer_FallsBackToBlock()
    {
        var output = DecompileNewStackalloc(nameof(NewStackallocFixtures.StackallocPointerInitializer));
        var block = FirstUnsafeBlockBody(output);

        Assert.Contains("int* values = stackalloc int[] { 1, 2, 3 };", block);
        Assert.Contains("return *values + values[2];", block);
        Assert.DoesNotContain("return unsafe(", output);

        var diagnostics = Recompile("static int M()", output);
        AssertNoWarningsOrErrors(diagnostics, output);
    }

    [Fact]
    public void NewRulesModule_UnsafeStackSlotReadInLaterBlockDeclaresUpFront()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        var second = new Block(1);
        second.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.True(
            output.IndexOf("* S_", StringComparison.Ordinal)
                < output.IndexOf("unsafe", StringComparison.Ordinal),
            "the stack-slot declaration must be hoisted above the unsafe block:\n" + output);
        Assert.Contains("_ = S_", output);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunKeepsInitObjectDeclarationInScope()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var guid = TypeRef.CoreLib("System", "Guid");
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");
        var getHashCode = new MethodRef(guid, "GetHashCode", int32, [], HasThis: true);

        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        block.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        block.Add(new ExpressionStatement(new Call(getHashCode, isVirtual: true, [new LoadLocal(0, guid)])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [guid],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;
        var unsafeBody = FirstUnsafeBlockBody(output);

        Assert.True(
            output.IndexOf("Guid V_0;", StringComparison.Ordinal)
                < output.IndexOf("unsafe", StringComparison.Ordinal),
            "the InitObject declaration must be hoisted above the unsafe block:\n" + output);
        Assert.Contains("V_0 = default;", unsafeBody);
        Assert.Contains("V_0.GetHashCode()", output);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunHoistsSafeLocalReadInLaterBlock()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        first.Add(new StoreLocal(0, int32, new Constant(1, int32)));
        first.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        var second = new Block(1);
        second.Add(new ExpressionStatement(new LoadLocal(0, int32)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [int32],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.True(
            output.IndexOf("int V_0;", StringComparison.Ordinal)
                < output.IndexOf("unsafe", StringComparison.Ordinal),
            "the safe local declaration must be hoisted above the unsafe block:\n" + output);
        Assert.Contains("_ = V_0;", output);
    }

    [Fact]
    public void NewRulesModule_SafeCrossBlockLocalWithoutUnsafeStaysInline()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var first = new Block(0);
        first.Add(new StoreLocal(0, int32, new Constant(1, int32)));
        var second = new Block(1);
        second.Add(new ExpressionStatement(new LoadLocal(0, int32)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [int32],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("int V_0 = 1;", output);
        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunHoistsSafeLocalBetweenUnsafeDeclarationAndUse()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        block.Add(new StoreLocal(0, int32, new Constant(1, int32)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        block.Add(new ExpressionStatement(new LoadLocal(0, int32)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [int32],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;
        var unsafeBody = FirstUnsafeBlockBody(output);

        Assert.DoesNotContain("int V_0 = 1;", unsafeBody);
        Assert.Contains("V_0 = 1;", unsafeBody);
        Assert.Contains("_ = V_0;", output);
    }

    [Fact]
    public void NewRulesModule_SafeLocalAfterClosedUnsafeRunStaysInline()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        block.Add(new StoreLocal(0, int32, new Constant(42, int32)));
        block.Add(new ExpressionStatement(new LoadLocal(0, int32)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [int32],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("int V_0 = 42;", output);
        Assert.DoesNotContain("\nV_0 = 42;", output);
    }

    [Fact]
    public void NewRulesModule_UnsafePointerLocalReadInLaterBlockDeclaresUpFront()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var first = new Block(0);
        first.Add(new StoreLocal(0, bytePointer, new StackAllocate(new Constant(8, int32))));
        var second = new Block(1);
        second.Add(new ExpressionStatement(new LoadLocal(0, bytePointer)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [bytePointer],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.True(
            output.IndexOf("byte* V_0;", StringComparison.Ordinal)
                < output.IndexOf("unsafe", StringComparison.Ordinal),
            "the pointer local declaration must be hoisted above the unsafe block:\n" + output);
        Assert.Contains("_ = V_0;", output);
    }

    [Fact]
    public void LegacyPointerLocalWhoseScopeCrossesAwait_DeclinesVisibly()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var task = TypeRef.CoreLib("System.Threading.Tasks", "Task");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");

        var block = new Block(0);
        block.Add(new StoreLocal(
            0,
            bytePointer,
            new StackAllocate(new Constant(8, int32))));
        block.Add(new ExpressionStatement(
            new AwaitExpression(
                new LoadArgument(0, "task", task),
                resultType: null)));
        block.Add(new ExpressionStatement(new LoadLocal(0, bytePointer)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(
                voidType,
                [new Parameter("task", task)],
                HasThis: false,
                GenericParameterCount: 0),
            [bytePointer],
            body)
        {
            RequiresAsyncBodyModifier = true,
        };

        new UnsafeAwaitBoundaryPass().Run(function, PassContext.None);
        var result = CSharpPrinter.Print(function);
        string output = Assert.IsType<string>(result.Output);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(result.RequiresUnsafeBodyModifier);
        Assert.False(result.ContainsAwaitExpression);
        Assert.Contains("legacy pointer lifetime cannot be scoped outside await", output);
        Assert.DoesNotContain("unsafe\n{", output);
        Assert.DoesNotContain("await task", output);
        Assert.Equal(output, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunHoistsInitObjectCapturedByLaterLambda()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var guid = TypeRef.CoreLib("System", "Guid");
        var action = TypeRef.CoreLib("System", "Action");
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");
        var getHashCode = new MethodRef(guid, "GetHashCode", int32, [], HasThis: true);

        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new ExpressionStatement(new Call(getHashCode, isVirtual: true, [new LoadLocal(0, guid)])));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);

        var first = new Block(0);
        first.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        first.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        first.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        var second = new Block(1);
        second.Add(new StoreLocal(1, action, new Lambda(action, [], [], [], usesUpdatedMemorySafetyRules: true, skipLocalsInit: false, lambdaBody)));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [guid, action],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.True(
            output.IndexOf("Guid V_0;", StringComparison.Ordinal)
                < output.IndexOf("unsafe", StringComparison.Ordinal),
            "the captured InitObject local declaration must be hoisted above the unsafe block:\n" + output);
        Assert.Contains("V_0.GetHashCode()", output);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunKeepsCapturedOuterLocalLambdaInScope()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var guid = TypeRef.CoreLib("System", "Guid");
        var action = TypeRef.CoreLib("System", "Action");
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");
        var getHashCode = new MethodRef(guid, "GetHashCode", int32, [], HasThis: true);

        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new ExpressionStatement(new Call(getHashCode, isVirtual: true, [new LoadLocal(0, guid)])));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);

        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        block.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        block.Add(new StoreLocal(1, action, new Lambda(action, [], [], [], usesUpdatedMemorySafetyRules: true, skipLocalsInit: false, lambdaBody)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [guid, action],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var unsafeBody = FirstUnsafeBlockBody(CSharpPrinter.Print(function).Output!);

        Assert.Contains("V_0 = default;", unsafeBody);
        Assert.DoesNotContain("=>", unsafeBody);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunIgnoresNestedLambdaLocalIndexCollisions()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var guid = TypeRef.CoreLib("System", "Guid");
        var action = TypeRef.CoreLib("System", "Action");
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");
        var getHashCode = new MethodRef(guid, "GetHashCode", int32, [], HasThis: true);

        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        lambdaBlock.Add(new ExpressionStatement(new Call(getHashCode, isVirtual: true, [new LoadLocal(0, guid)])));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);

        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        block.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        block.Add(new StoreLocal(1, action, new Lambda(action, [], [guid], [], usesUpdatedMemorySafetyRules: true, skipLocalsInit: false, lambdaBody)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [guid, action],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var unsafeBody = FirstUnsafeBlockBody(CSharpPrinter.Print(function).Output!);

        Assert.DoesNotContain("=>", unsafeBody);
    }

    [Fact]
    public void NewRulesModule_UnsafeRunIgnoresNestedLocalFunctionLocalIndexCollisions()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var guid = TypeRef.CoreLib("System", "Guid");
        var owner = TypeRef.Definition("Synthetic", "Holder", "Class1");
        var getHashCode = new MethodRef(guid, "GetHashCode", int32, [], HasThis: true);

        var localBlock = new Block(0);
        localBlock.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        localBlock.Add(new Return(new Call(getHashCode, isVirtual: true, [new LoadLocal(0, guid)])));
        var localBody = new BlockContainer();
        localBody.Add(localBlock);

        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new StackAllocate(new Constant(8, int32))));
        block.Add(new InitObject(guid, new LoadLocalAddress(0, guid)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, bytePointer)));
        block.Add(new LocalFunctionStatement(
            "Local",
            int32,
            [],
            isStatic: false,
            [guid],
            [],
            usesUpdatedMemorySafetyRules: true,
            skipLocalsInit: false,
            localBody));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0),
            [guid],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var unsafeBody = FirstUnsafeBlockBody(CSharpPrinter.Print(function).Output!);

        Assert.DoesNotContain("Local()", unsafeBody);
    }

    [Fact]
    public void NewRulesModule_StackAllocSpanDefault_EmitsNoUnsafeBlock()
    {
        // Without [SkipLocalsInit] the same stackalloc -> Span is safe under the
        // new rules; the pointer in the Span constructor's signature must not
        // trigger the compat heuristic here.
        var output = DecompileNew(nameof(NewFixtures.StackAllocDefault));

        Assert.DoesNotContain("unsafe", output);
        // Safe case keeps the inline `Span<int> s = stackalloc int[n]` form.
        Assert.Contains("stackalloc int[", output);
        Assert.DoesNotContain("new Span", output);
    }

    [Theory]
    [InlineData(nameof(NewDynamicStackallocFixtures.ByteCount), "stackalloc byte[n]")]
    [InlineData(nameof(NewDynamicStackallocFixtures.GuidCount), "stackalloc Guid[n]")]
    [InlineData(nameof(NewDynamicStackallocFixtures.ByteExpression), "stackalloc byte[n + 1]")]
    [InlineData(nameof(NewDynamicStackallocFixtures.ByteEffectful), "stackalloc byte[Math.Abs(n)]")]
    [InlineData(nameof(NewDynamicStackallocFixtures.ByteExplicitLocal), "stackalloc byte[count]")]
    [InlineData(nameof(NewDynamicStackallocFixtures.ByteTwoCounts), "stackalloc byte[m]")]
    [InlineData(nameof(NewDynamicStackallocFixtures.ByteTwoEffectfulCounts), "stackalloc byte[Math.Abs(n + 1)]")]
    public void NewRulesModule_DynamicStackAllocCompilerShapes_Raise(string method, string expected)
    {
        var output = DecompileNewDynamicStackalloc(method);

        Assert.DoesNotContain("unsafe", output);
        Assert.Contains(expected, output);
        Assert.DoesNotContain("new Span", output);
        Assert.DoesNotContain("int V_", output);
    }

    [Theory]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.ByteCount), "stackalloc byte[n]")]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.GuidCount), "stackalloc Guid[n]")]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.ByteExpression), "stackalloc byte[n + 1]")]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.ByteEffectful), "stackalloc byte[Math.Abs(n)]")]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.ByteExplicitLocal), "stackalloc byte[count]")]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.ByteTwoCounts), "stackalloc byte[m]")]
    [InlineData(nameof(LegacyDynamicStackallocFixtures.ByteTwoEffectfulCounts), "stackalloc byte[Math.Abs(n + 1)]")]
    public void LegacyModule_DynamicStackAllocCompilerShapes_Raise(string method, string expected)
    {
        var output = DecompileLegacyDynamicStackalloc(method);

        Assert.DoesNotContain("unsafe", output);
        Assert.Contains(expected, output);
        Assert.DoesNotContain("new Span", output);
        Assert.DoesNotContain("int V_", output);
    }

    [Fact]
    public void LegacyModule_StackAllocSpan_EmitsNoUnsafeBlock()
    {
        // The stackalloc->Span raise is mode-independent correctness (the lowered
        // ctor shape never compiled), so legacy output raises too — just without
        // the unsafe wrapping the new rules require.
        var skipInit = DecompileLegacy(nameof(LegacyFixtures.StackAllocSkipInit));
        Assert.DoesNotContain("unsafe", skipInit);
        Assert.Contains("stackalloc int[", skipInit);
        // Legacy keeps the inline `Span<int> s = stackalloc int[n]` form, which
        // infers `scoped` on its own — no split declaration, so no `scoped`.
        Assert.DoesNotContain("scoped", skipInit);
        Assert.DoesNotContain("unsafe", DecompileLegacy(nameof(LegacyFixtures.StackAllocDefault)));
    }

    [Fact]
    public void NewRulesModule_StackAllocEventData_UsesBlockOnlyForDependentStores()
    {
        var output = DecompileNew(nameof(NewFixtures.StackAllocEventData));
        var block = FirstUnsafeBlockBody(output);

        Assert.Contains("byte* __stackalloc = stackalloc byte[", block);
        Assert.Contains("values = (int*)__stackalloc;", block);
        Assert.Contains("*values = eventId;", block);
        // `values[1]` is byte offset 4. The printer now recognizes the canonical
        // scaled offset and emits element arithmetic instead of the lower-altitude
        // byte-pointer fallback.
        Assert.Contains("*(values + 1)", block);
        Assert.Contains("return unsafe(*values + values[1]);", output);
        Assert.DoesNotContain("return", block);
        Assert.DoesNotContain("*(values + 4)", block);
        Assert.DoesNotContain("Span", output);
    }

    [Fact]
    public void LegacyModule_StackAllocEventData_EmitsNoUnsafeBlock()
    {
        var output = DecompileLegacy(nameof(LegacyFixtures.StackAllocEventData));

        Assert.DoesNotContain("unsafe", output);
        Assert.Contains("stackalloc byte[", output);
        Assert.Contains("int* values = (int*)__stackalloc;", output);
        Assert.Contains("*values", output);
    }

    [Fact]
    public void StackAllocEventData_AnnotationSurvivesImportedClassificationAndRaising()
    {
        var (function, annotations) = ClassifyNew(nameof(NewFixtures.StackAllocEventData));

        var stackAlloc = Assert.Single(annotations, a => a.Descriptor.Id == "unsafe.stackalloc");
        Assert.Equal("byte*", stackAlloc.Detail);
        Assert.True(stackAlloc.SourceOffset >= 0);
        Assert.Single(function.Descendants.OfType<StackAllocate>());

        var output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Contains("stackalloc byte[", output);
        Assert.Single(function.Descendants.OfType<StackAllocate>());
    }

    // ---- Optimistic ("simulate") mode: render new-rules contexts for legacy input ----

    [Fact]
    public void OptimisticMode_LegacyPointerDeref_UsesUnsafeExpression()
    {
        // The pointer dereference leaves an IL trace (ldind), so simulate mode can
        // recover the context the new rules would require even though the legacy
        // module carries no MemorySafetyRulesAttribute. Matches conservative(New).
        var output = DecompileLegacySimulate(nameof(LegacyFixtures.DerefPointer));

        Assert.Contains("return unsafe(", output);
        Assert.DoesNotContain("unsafe\n{", output);
    }

    [Fact]
    public void OptimisticMode_LegacyCompatPointerSignatureCall_FallsBackToBlock()
    {
        // NativeMemory.Free has a pointer in its signature — recoverable from the
        // MemberRef — so simulate mode wraps the call for legacy input too.
        var output = DecompileLegacySimulate(nameof(LegacyFixtures.FreePointer));

        Assert.Contains("Free", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void OptimisticMode_LegacyPointerlessRequiresUnsafe_NotRecoverable_EmitsNoBlock()
    {
        // A legacy same-assembly `unsafe` method with no pointers leaves NO trace:
        // legacy compilation stamps no RequiresUnsafeAttribute and the call carries
        // no pointer. There is nothing to recover, so even simulate mode emits no
        // block — the principled limit of optimistic rendering.
        var output = DecompileLegacySimulate(nameof(LegacyFixtures.CallRisky));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void OptimisticMode_LegacyCrossAssemblyRequiresUnsafeCall_UsesUnsafeExpression()
    {
        // App C is NOT opted into the new rules, yet it calls A.M1 — a pointerless
        // requires-unsafe method whose attribute lives in opted-in assembly A.
        // That attribute is readable cross-assembly, so simulate mode recovers the
        // context and wraps the call; conservative mode (legacy) leaves it bare.
        var conservative = Decompile(typeof(ChainC).Assembly.Location, typeof(ChainC).FullName!, nameof(ChainC.CallChain));
        Assert.DoesNotContain("unsafe", conservative);

        var optimistic = DecompileSimulate(typeof(ChainC).Assembly.Location, typeof(ChainC).FullName!, nameof(ChainC.CallChain));
        Assert.Contains("return unsafe(LibraryB.M2() + LibraryA.M1());", optimistic);
        Assert.DoesNotContain("unsafe\n{", optimistic);
    }

    // ---- Recompile rail: the new-rules output is valid, warning-free C# ----
    //
    // The ordinary fidelity/compile-back rails derive the feature from the source
    // module. These focused tests compile the actual decompiled new-rules output
    // with the feature explicitly enabled, so both unsafe-context failures and
    // ref-safety warnings such as CS9081 remain observable.

    [Fact]
    public void NewRulesModule_StackAllocSkipInit_UnsafeExpressionRecompilesWithoutWarning()
    {
        var body = DecompileNew(nameof(NewFixtures.StackAllocSkipInit));
        Assert.Contains("Span<int> s = unsafe(stackalloc int[n]);", body);
        Assert.DoesNotContain("scoped", body);

        var diagnostics = Recompile("[SkipLocalsInit] static int M(int n)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void OptimisticMode_LegacyStackAllocSkipInit_UnsafeExpressionRecompilesWithoutWarning()
    {
        var body = DecompileLegacySimulate(nameof(LegacyFixtures.StackAllocSkipInit));
        Assert.Contains("Span<int> s = unsafe(stackalloc int[n]);", body);
        Assert.DoesNotContain("scoped", body);

        var diagnostics = Recompile("[SkipLocalsInit] static int M(int n)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void NewRulesModule_PointerDeref_UnsafeExpressionRecompilesWithoutWarning()
    {
        var body = DecompileNew(nameof(NewFixtures.DerefPointer));
        Assert.Contains("return unsafe(", body);

        var diagnostics = Recompile("static int M(int value)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void NewRulesModule_ByRefPointerDeref_UnsafeExpressionRecompilesWithoutWarning()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var pointer = TypeRef.Pointer(int32);
        var block = new Block(0);
        block.Add(new Return(new LoadIndirect(
            int32,
            new LoadArgument(0, "pointer", pointer))));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Synthetic", "Owner"),
            new MethodSignature(
                TypeRef.ByRef(int32),
                [new Parameter("pointer", pointer)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var body = CSharpPrinter.Print(function).Output!;
        Assert.Equal("return ref unsafe(*pointer);\n", body);

        var diagnostics = Recompile("static ref int M(int* pointer)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void NewRulesModule_VoidRequiresUnsafeCall_BlockFallbackRecompilesWithoutWarning()
    {
        var body = DecompileNew(nameof(NewFixtures.FreePointer));
        Assert.Contains("unsafe\n{", body);

        var diagnostics = Recompile("static void M(void* p)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void NewRulesModule_RequiresUnsafePropertyInLambda_FallsBackToBlock()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var getter = new MethodRef(holder, "get_Risky", int32, [], HasThis: false)
        {
            IsSpecialName = true,
            RequiresUnsafe = true,
        };
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(new LoadProperty(getter, instance: null, [])));
        lambdaBody.Add(lambdaBlock);
        var func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`1"), [int32]);
        var block = new Block();
        block.Add(new Return(new Lambda(
            func,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: true,
            skipLocalsInit: false,
            lambdaBody)));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "__Gate"),
            new MethodSignature(
                func,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var body = CSharpPrinter.Print(function).Output!;

        Assert.Contains("() =>\n{\n    unsafe", body);
        Assert.DoesNotContain("=> unsafe(Holder.Risky)", body);
        AssertNoWarningsOrErrors(
            Recompile(
                "static Func<int> M()",
                body,
                "public static class Holder { public static int Risky { unsafe get => 42; } }"),
            body);
    }

    [Fact]
    public void NewRulesModule_RequiresUnsafePropertyNestedInValue_UsesUnsafeExpression()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var getter = new MethodRef(holder, "get_Risky", int32, [], HasThis: false)
        {
            IsSpecialName = true,
            RequiresUnsafe = true,
        };
        var value = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadProperty(getter, instance: null, []),
            new Constant(1, int32));
        var block = new Block();
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "__Gate"),
            new MethodSignature(
                int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var body = CSharpPrinter.Print(function).Output!;

        Assert.Equal("return unsafe(Holder.Risky + 1);\n", body);
        AssertNoWarningsOrErrors(
            Recompile(
                "static int M()",
                body,
                "public static class Holder { public static int Risky { unsafe get => 42; } }"),
            body);
    }

    [Fact]
    public void NewRulesModule_CheckedUnsafeIncrementStatement_UsesUnsafeExpression()
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var counter = TypeRef.Definition(
            "Synthetic",
            "",
            "Counter",
            ValueTypeHint.ValueType);
        var increment = new MethodRef(
            counter,
            "op_CheckedIncrement",
            counter,
            [counter],
            HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
            RequiresUnsafe = true,
        };
        var block = new Block();
        block.Add(new ExpressionStatement(new IncrementDecrement(
            new LoadArgument(0, "value", counter),
            isIncrement: true,
            isPrefix: false,
            isUserDefined: true,
            isChecked: true,
            consumedMethod: increment)));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "__Gate"),
            new MethodSignature(
                voidType,
                [new Parameter("value", counter)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("_ = unsafe(checked(value++));", output);
        Assert.DoesNotContain("unsafe\n{", output);
        AssertNoWarningsOrErrors(
            Recompile(
                "static void M(Counter value)",
                output,
                """
                public struct Counter
                {
                    public int Value;
                    public static Counter operator ++(Counter value) => value;
                    public static unsafe Counter operator checked ++(Counter value)
                    {
                        value.Value++;
                        return value;
                    }
                }
                """),
            output);
    }

    [Fact]
    public void NewRulesModule_SharedScopeLambdaReturn_UsesUnsafeExpression()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var probe = TypeRef.Definition("Synthetic", "", "Probe");
        var ping = new MethodRef(probe, "Ping", voidType, [], HasThis: false);
        var risky = new MethodRef(probe, "Risky", int32, [], HasThis: false)
        {
            RequiresUnsafe = true,
        };
        var lambdaBlock = new Block();
        lambdaBlock.Add(new ExpressionStatement(new Call(
            ping,
            isVirtual: false,
            [])));
        lambdaBlock.Add(new Return(new Call(
            risky,
            isVirtual: false,
            [])));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var func = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`1"),
            [int32]);
        var block = new Block();
        block.Add(new Return(new Lambda(
            func,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: true,
            skipLocalsInit: false,
            lambdaBody)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "__Gate"),
            new MethodSignature(
                func,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("return unsafe(Probe.Risky());", output);
        Assert.DoesNotContain("return Probe.Risky();", output);
        AssertNoWarningsOrErrors(
            Recompile(
                "static Func<int> M()",
                output,
                """
                public static class Probe
                {
                    public static void Ping() { }
                    public static unsafe int Risky() => 42;
                }
                """),
            output);
    }

    [Fact]
    public void NewRulesModule_RequiresUnsafePropertyInLocalFunction_FallsBackToBlock()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var getter = new MethodRef(holder, "get_Risky", int32, [], HasThis: false)
        {
            IsSpecialName = true,
            RequiresUnsafe = true,
        };
        var localBody = new BlockContainer();
        var localBlock = new Block();
        localBlock.Add(new Return(new LoadProperty(getter, instance: null, [])));
        localBody.Add(localBlock);
        var block = new Block();
        block.Add(new LocalFunctionStatement(
            "Read",
            int32,
            [],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: true,
            skipLocalsInit: false,
            localBody));
        block.Add(new Return(new LocalFunctionInvocation("Read", int32, [])));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "__Gate"),
            new MethodSignature(
                int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var body = CSharpPrinter.Print(function).Output!;

        Assert.Contains("static int Read()\n{\n    unsafe", body);
        Assert.DoesNotContain("Read() => unsafe(Holder.Risky)", body);
        AssertNoWarningsOrErrors(
            Recompile(
                "static int M()",
                body,
                "public static class Holder { public static int Risky { unsafe get => 42; } }"),
            body);
    }

    [Fact]
    public void NewRulesModule_StackAllocEventData_RecompilesWithoutWarning()
    {
        var body = DecompileNew(nameof(NewFixtures.StackAllocEventData));

        var diagnostics = Recompile("static int M(int eventId)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    /// <summary>
    /// Recompile a decompiled method body inside a minimal non-<c>unsafe</c>
    /// method shell (so the output's explicit <c>unsafe { }</c> blocks are
    /// actually required, not masked by an outer modifier) with no warning
    /// suppression, so a CS9081 (or any other) warning is observable.
    /// </summary>
    static ImmutableArray<Diagnostic> Recompile(
        string methodHeader,
        string body,
        string declarations = "",
        string containingTypeHeader = "static class __Gate",
        string typeMembers = "")
    {
        string source = $$"""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            {{declarations}}
            {{containingTypeHeader}}
            {
                {{typeMembers}}
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        return CreateUpdatedRulesCompilation("__gate", source).GetDiagnostics();
    }

    static string CompileUpdatedRulesAssembly(string source)
    {
        var compilation = CreateUpdatedRulesCompilation(
            "__fixture",
            source,
            OptimizationLevel.Release);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-unsafe-switch-{Guid.NewGuid():N}.dll");
        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    static CSharpCompilation CreateUpdatedRulesCompilation(
        string assemblyName,
        string source,
        OptimizationLevel optimizationLevel = OptimizationLevel.Debug)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("updated-memory-safety-rules", "true")]);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        return CSharpCompilation.Create(
            assemblyName,
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimizationLevel,
                allowUnsafe: true));
    }

    static void AssertNoWarningsOrErrors(ImmutableArray<Diagnostic> diagnostics, string body)
    {
        var relevant = diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
        Assert.True(
            relevant.Count == 0,
            "decompiled new-rules output must recompile clean, got:\n  "
                + string.Join("\n  ", relevant) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;
}
