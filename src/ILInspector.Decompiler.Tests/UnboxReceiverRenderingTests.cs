using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An `unbox` opcode yields a managed pointer to the value inside a box; when it
// is the receiver of a field/property/method access, C# spells it as the cast
// itself — `((T)x).Member` — because the compiler auto-takes the address. The
// by-ref argument spelling `ref (T)x` is CS1525 "Invalid expression term 'ref'"
// in that value position, so the printer must drop the `ref` for receivers.
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
    public void UnboxFieldReceiver_DropsRefPrefix()
    {
        var function = Raised(typeof(CfgBoxed).FullName!, nameof(CfgBoxed.FieldEquals));
        // The field-off-unbox pattern must import as an Unbox receiver (the read
        // off a box uses `unbox` + `ldfld`, not a copying `unbox.any`).
        Assert.Single(function.Descendants.OfType<Unbox>());
        var output = CSharpPrinter.Print(function).Output ?? "";

        // The unbox-into-field-access must read as a parenthesized cast, never `ref`.
        Assert.Contains("((CfgBoxed)other).Value", output);
        Assert.DoesNotContain("ref (", output);
    }
}
