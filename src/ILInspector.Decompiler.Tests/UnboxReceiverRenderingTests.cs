using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An `unbox` opcode yields a managed pointer into the box; when it is the
// receiver of a field/property/method access, that access must reach the in-box
// place, not a copy. C# spells it as the `Unsafe.Unbox<T>(o)` intrinsic (a
// `ref T`), which reads faithfully and — unlike the bare cast `((T)x)` — also
// carries mutations back to the box and is a valid assignment target. The
// by-ref argument spelling `ref (T)x` is CS1525 in a value position, and the
// copy `((T)x)` silently drops mutation (`((T)x).Mutate()`) and is CS0445 as an
// assignment target (`((T)x).Field = v`).
public class UnboxReceiverRenderingTests
{
    static IrFunction Raised(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    [Fact]
    public void UnboxFieldReceiver_SpellsUnsafeUnbox()
    {
        var function = Raised(typeof(CfgBoxed).FullName!, nameof(CfgBoxed.FieldEquals));
        // The field-off-unbox pattern must import as an Unbox receiver (the read
        // off a box uses `unbox` + `ldfld`, not a copying `unbox.any`).
        Assert.Single(function.Descendants.OfType<Unbox>());
        var output = CSharpPrinter.Print(function).Output ?? "";

        // The unbox receiver must spell as the Unsafe.Unbox<T>(o) intrinsic, never
        // the value-copy cast `((CfgBoxed)other)` and never a bare `ref`.
        Assert.Contains("Unsafe.Unbox<CfgBoxed>(other).Value", output);
        Assert.DoesNotContain("((CfgBoxed)other)", output);
        Assert.DoesNotContain("ref (", output);
    }
}
