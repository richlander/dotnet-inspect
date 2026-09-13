using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Review (#2925): a *value read* of an unbox — ldobj T over unbox T — is the
// same operation as unbox.any T. UnboxValueReadPass normalizes it so the printer
// spells the universally valid value cast (T)o and reserves the ref-only
// Unsafe.Unbox<T> intrinsic for genuine ref/out/write places. Left as a ByRef
// unbox place, a boxed Nullable<T> value read spelled Unsafe.Unbox<Nullable<T>>
// is CS0453 (the intrinsic requires `where T : struct`), even though the cast
// (int?)o compiles to unbox.any and is correct.
[Trait("Area", "Pass")]
public class UnboxValueReadPassTests
{
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64Type = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef NullableInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Nullable`1"), [Int32Type]);

    static IrFunction Raise(IrExpression value, TypeRef returnType, params Parameter[] parameters)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container);
        IrPasses.Run(function, [new UnboxValueReadPass()]);
        function.CheckInvariant();
        return function;
    }

    [Fact]
    public void NullableValueRead_NormalizesToUnboxAnyCast_NoCS0453()
    {
        var function = Raise(
            new LoadIndirect(NullableInt, new Unbox(NullableInt, new LoadArgument(0, "o", ObjectType))),
            NullableInt,
            new Parameter("o", ObjectType));

        // The ByRef unbox place is gone; a single unbox.any value node remains.
        Assert.Single(function.Descendants.OfType<UnboxAny>());
        Assert.Empty(function.Descendants.OfType<Unbox>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("Unsafe.Unbox", output);  // the CS0453 spelling is gone
        AssertCompiles("public static int? M(object o)", output);
    }

    [Fact]
    public void StructValueRead_NormalizesToUnboxAnyCast()
    {
        // A concrete-struct value read normalizes the same way: (int)o over
        // unbox.any. Unsafe.Unbox<int> would also compile, but the cast is the
        // faithful value-read spelling a human (and csc) writes.
        var function = Raise(
            new LoadIndirect(Int32Type, new Unbox(Int32Type, new LoadArgument(0, "o", ObjectType))),
            Int32Type,
            new Parameter("o", ObjectType));

        Assert.Single(function.Descendants.OfType<UnboxAny>());
        Assert.Empty(function.Descendants.OfType<Unbox>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(int)o", output);
        Assert.DoesNotContain("Unsafe.Unbox", output);
        AssertCompiles("public static int M(object o)", output);
    }

    [Fact]
    public void SpilledSlotValueRead_ThroughFullPipeline_NormalizesNotUnsafeUnbox()
    {
        // A spilled `ref T S = unbox o` place, read as a value:
        //   S_0 = unbox Nullable<int>; return ldobj Nullable<int> S_0
        // The final slot-collapsing inliner (ExpressionInliningPass) re-forms
        // LoadIndirect(Unbox) only after the early passes run, so
        // UnboxValueReadPass must sit AFTER it in IrPasses.Default. If it runs
        // earlier, the re-formed value read spells Unsafe.Unbox<Nullable<int>>
        // (CS0453). This drives the FULL pipeline to lock that ordering — the
        // isolated-pass tests above cannot catch a late re-formed pair. (#2925
        // review: GPT 5.6 Sol + Gemini 3.1 Pro both reproduced this.)
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Unbox(NullableInt, new LoadArgument(0, "o", ObjectType))));
        block.Add(new Return(new LoadIndirect(NullableInt, new LoadStackSlot(0, TypeRef.ByRef(NullableInt)))));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(NullableInt, [new Parameter("o", ObjectType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container);

        IrPasses.Run(function);  // full Default pipeline, including the late inliner
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Unbox>());
        var output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("Unsafe.Unbox", output);
        AssertCompiles("public static int? M(object o)", output);
    }

    [Fact]
    public void ReinterpretingRead_IsNotNormalized()
    {
        // ldobj Int64 over unbox Int32 is a reinterpret, not unbox.any: the pass
        // must leave it explicit so the printer renders it honestly.
        var function = Raise(
            new LoadIndirect(Int64Type, new Unbox(Int32Type, new LoadArgument(0, "o", ObjectType))),
            Int64Type,
            new Parameter("o", ObjectType));

        Assert.Empty(function.Descendants.OfType<UnboxAny>());
        Assert.Single(function.Descendants.OfType<Unbox>());
    }

    [Fact]
    public void VolatileRead_IsNotNormalized()
    {
        // A volatile read carries acquire semantics a plain cast would drop, so
        // the normalization must skip it and leave the read explicit.
        var function = Raise(
            new LoadIndirect(Int32Type, new Unbox(Int32Type, new LoadArgument(0, "o", ObjectType))) { IsVolatile = true },
            Int32Type,
            new Parameter("o", ObjectType));

        Assert.Empty(function.Descendants.OfType<UnboxAny>());
        Assert.Single(function.Descendants.OfType<Unbox>());
    }

    static void AssertCompiles(string methodHeader, string body)
    {
        string source = $$"""
            using System;
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }
}
