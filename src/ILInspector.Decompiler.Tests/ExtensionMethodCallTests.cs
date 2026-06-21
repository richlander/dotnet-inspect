using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExtensionMethodCallTests
{
    // Extension-ness is a cross-assembly fact (System.Linq lives in the shared
    // framework, not beside the test assembly), so the default sibling locator
    // cannot resolve it. Reach the running runtime directory, the same pattern
    // AllocationClassifierTests uses for its corelib base-chain checks.
    static readonly ILInspector.Metadata.AssemblyLocator RuntimeLocator = (name, trust) =>
    {
        string dir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string path = System.IO.Path.Combine(dir, name + ".dll");
        return System.IO.File.Exists(path) ? path : null;
    };

    static string PrintRaised(string methodName)
    {
        using var context = new MetadataContext(RuntimeLocator);
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, null, RuntimeLocator, context);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
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
}
