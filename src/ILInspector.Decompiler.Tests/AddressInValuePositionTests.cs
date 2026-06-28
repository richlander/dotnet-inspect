using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// An address-of node (ldloca/ldarga/ldflda/ldelema) leaves the IL as a managed
// pointer. Used as a ref-argument, ref-local initializer, or ref-return it
// spells `ref place`; in any other value position that `ref` is invalid C#
// (CS1525/CS1612). These tests pin the two value-position shapes that were
// rendering the bare `ref` form.
public class AddressInValuePositionTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef NInt = TypeRef.CoreLib("System", "IntPtr");

    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    static string RenderSyntheticNarrowedAddress()
    {
        var block = new Block(0);
        block.Add(new StoreLocal(
            0,
            NInt,
            new ILInspector.Decompiler.Pipeline.Convert(
                Int,
                isChecked: false,
                isUnsigned: false,
                new LoadArgumentAddress(0, "value", Int))));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("value", Int)], HasThis: false, GenericParameterCount: 0),
            [NInt],
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    static string RenderSyntheticRawAddress()
    {
        var block = new Block(0);
        block.Add(new StoreLocal(0, NInt, new LoadArgumentAddress(0, "value", Int)));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("value", Int)], HasThis: false, GenericParameterCount: 0),
            [NInt],
            body);
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

    [Fact]
    public void ArgumentAddressConvertedToNativeUInt_RendersAddressOf()
    {
        // EventSource payload helpers lower DataPointer assignments as
        // `ldarga; conv.u`; value-position argument addresses use `&`, not `ref`.
        var output = Render(nameof(CfgSampleClass.ArgumentAddressAsNativeUInt));

        Assert.Contains("(nuint)(&value)", output);
        Assert.DoesNotContain("ref value", output);
    }

    [Fact]
    public void ArgumentAddressStoredToNativeInt_RendersAddressOf()
    {
        // Some store/call boundaries consume the converted native-int type and see
        // the raw address node; that value-position spelling is still `&value`.
        var output = Render(nameof(CfgSampleClass.StoreArgumentAddressAsNativeInt));

        Assert.Contains("s_argumentAddress = (nint)(&value);", output);
        Assert.DoesNotContain("ref value", output);
    }

    [Fact]
    public void NonNativeAddressConversion_PreservesIntermediateConversion()
    {
        var output = RenderSyntheticNarrowedAddress();

        Assert.Contains("nint V_0 = (int)(&value);", output);
        Assert.DoesNotContain("= (nint)(&value);", output);
        Assert.DoesNotContain("ref value", output);
    }

    [Fact]
    public void RawArgumentAddressStoredToNativeInt_RendersAddressOf()
    {
        var output = RenderSyntheticRawAddress();

        Assert.Contains("nint V_0 = (nint)(&value);", output);
        Assert.DoesNotContain("ref value", output);
    }
}
