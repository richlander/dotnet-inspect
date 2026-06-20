using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An address-of node (ldloca/ldarga/ldflda/ldelema) leaves the IL as a managed
// pointer. Used as a ref-argument, ref-local initializer, or ref-return it
// spells `ref place`; in any other value position that `ref` is invalid C#
// (CS1525/CS1612). These tests pin the two value-position shapes that were
// rendering the bare `ref` form.
public class AddressInValuePositionTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void ValueTypeArrayElementMemberAccess_DropsRefOnTheReceiver()
    {
        // `pairs[0].A` reads a field off a struct array element by address
        // (ldelema; ldfld). The receiver is the element place, not `ref pairs[0]`
        // — `(ref pairs[0]).A` is CS1525.
        var output = Render(nameof(CfgSampleClass.FirstA));

        Assert.Contains("return pairs[0].A;", output);
        Assert.DoesNotContain("ref pairs[0]", output);
    }

    [Fact]
    public void LocalAddressConvertedToNativeUInt_RendersAddressOf()
    {
        // `(nuint)(&value)` lowers to `ldloca; conv.u`. Converting the address to
        // a native integer is C#'s address-of operator; `(nuint)(ref value)` is
        // CS1525.
        var output = Render(nameof(CfgSampleClass.AddressAsNativeUInt));

        Assert.Contains("(nuint)(&value)", output);
        Assert.DoesNotContain("ref value", output);
    }
}
