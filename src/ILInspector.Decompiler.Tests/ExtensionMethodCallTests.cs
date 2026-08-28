using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExtensionMethodCallTests
{
    // Extension-ness is a cross-assembly fact (System.Linq lives in the shared
    // framework, not beside the test assembly), so the default sibling resolver
    // cannot resolve it. Reach the running runtime directory, the same pattern
    // AllocationOccurrenceFactTests uses for its corelib base-chain checks.
    static readonly ILInspector.Metadata.IAssemblyReferenceResolver RuntimeResolver = TestAssemblyReferenceResolvers.RuntimeAssemblies();

    static string PrintRaised(string methodName)
        => PrintRaised(typeof(CfgSampleClass), methodName);

    static string PrintRaised(Type type, string methodName)
    {
        using var context = new MetadataContext(RuntimeResolver);
        using var source = MetadataSource.Open(type.Assembly.Location, null, RuntimeResolver, context);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void LinqCall_RendersAsInstanceExtensionSyntax()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        // The static spelling Enumerable.Where(items, pred) is rendered as the
        // instance form items.Where(pred) the source used.
        Assert.Contains(".Where", output);
        Assert.DoesNotContain("Enumerable.Where", output);
    }

    [Fact]
    public void LinqChain_RendersAsFluentInstanceChain()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        Assert.Contains(".Where", output);
        Assert.Contains(".Select", output);
        Assert.DoesNotContain("Enumerable.Where", output);
        Assert.DoesNotContain("Enumerable.Select", output);
        // The first call is the receiver of the second: ...Where(...).Select(...).
        int where = output.IndexOf(".Where", StringComparison.Ordinal);
        int select = output.IndexOf(".Select", StringComparison.Ordinal);
        Assert.True(where >= 0 && select > where, $"expected .Where(...).Select(...), got: {output}");
    }

    [Fact]
    public void SameAssemblyUserExtension_RendersAsInstanceSyntax()
    {
        // The [Extension] mark is read from the same-assembly MethodDef, so a user
        // extension sugars the same way the cross-assembly LINQ ones do.
        string output = PrintRaised(nameof(CfgSampleClass.CallsUserExtension));

        Assert.Contains("n.Doubled()", output);
        Assert.DoesNotContain("ExtensionMethodSamples.Doubled", output);
    }

    [Fact]
    public void NonExtensionStatic_KeepsStaticSpelling()
    {
        // A plain static with a first parameter is byte-identical in shape to an
        // extension call but carries no [Extension] mark, so it must NOT sugar to
        // receiver syntax — the precision guard on the IsExtension gate.
        string output = PrintRaised(nameof(CfgSampleClass.CallsNonExtensionStatic));

        Assert.Contains("ExtensionMethodSamples.Combine(a, b)", output);
        Assert.DoesNotContain("a.Combine", output);
    }

    [Fact]
    public void ShadowingInstanceMethod_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionMethodCollisionSamples),
            nameof(ExtensionMethodCollisionSamples.CallsShadowedExtension));

        Assert.Equal(
            "return Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>();",
            output);
    }

    [Fact]
    public void PlatformBaseInstanceMethod_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionMethodCollisionSamples),
            nameof(
                ExtensionMethodCollisionSamples
                    .CallsPlatformShadowedExtension));

        Assert.Contains(
            "CustomAttributeExtensions.GetCustomAttributes(typeInfo, typeof(Attribute), true)",
            output);
        Assert.DoesNotContain(
            "typeInfo.GetCustomAttributes",
            output);
    }

    [Fact]
    public void GenericExtensionShadowedByInstanceMethod_KeepsStaticSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionMethodCollisionSamples),
            nameof(
                ExtensionMethodCollisionSamples
                    .CallsShadowedGenericExtension));

        Assert.Equal(
            "return Enumerable.Contains<int>(values, value);",
            output);
    }

    [Fact]
    public void SameNamedProperty_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionPropertyCollisionSamples),
            nameof(
                ExtensionPropertyCollisionSamples
                    .CallsPropertyShadowedExtension));

        Assert.Equal(
            "return Values(receiver, typeof(Attribute), true).FirstOrDefault<Attribute>();",
            output);
    }

    [Fact]
    public void ByRefReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(RefExtensionCollisionSamples),
            nameof(
                RefExtensionCollisionSamples
                    .CallsShadowedRefExtension));

        Assert.Equal("return Value(ref receiver);", output);
    }

    [Fact]
    public void ArrayReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ArrayExtensionCollisionSamples),
            nameof(
                ArrayExtensionCollisionSamples
                    .CallsShadowedArrayExtension));

        Assert.Equal(
            "return Clone(values);",
            output);
    }

    [Fact]
    public void InterfaceReceiver_IncludesObjectMembers()
    {
        string output = PrintRaised(
            typeof(InterfaceExtensionCollisionSamples),
            nameof(
                InterfaceExtensionCollisionSamples
                    .CallsObjectShadowedExtension));

        Assert.Equal(
            "return Equals(receiver, other);",
            output);
    }

    [Fact]
    public void GenericParameterReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(GenericParameterExtensionCollisionSamples),
            nameof(
                GenericParameterExtensionCollisionSamples
                    .CallsConstraintUnknownExtension));

        Assert.Equal(
            "return Equals<T>(value, other);",
            output);
    }
}
