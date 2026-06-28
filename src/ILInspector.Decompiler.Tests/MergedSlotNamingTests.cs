using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Issue #1767: a stack slot whose producer (an object-merged `cond ? null :
// value` ternary) and consumer (a string-typed field store) carry different
// static types must render with one unified local name. Keying names on
// (slot, type) split them — `object S_1` declared/assigned, `string S_1_1`
// read — so the consumer used an unassigned local (CS0165) and the produced
// value was silently dropped.
public class MergedSlotNamingTests
{
    [Fact]
    public void ProducerAndConsumerOfMergedSlot_ShareOneLocalName()
    {
        string body = RenderFixture(typeof(MergedSlotProbe).FullName!, "set_MergedFormat");

        // The store value reaches the consumer: one declaration, assigned and read
        // under the same name, with no phantom second-ordinal local.
        Assert.Contains("string S_1 = ", body);
        Assert.Contains("_fmt = S_1;", body);
        Assert.DoesNotContain("S_1_1", body);
        Assert.DoesNotContain("object S_1", body);
    }

    static string RenderFixture(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(MergedSlotProbe).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }
}
