using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

using LegacyFixtures = ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
using NewFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Drives the unsafe-context emitter. The two fixture assemblies compile to
/// identical IL; the only difference the decompiler can observe is the
/// new-rules module's <c>MemorySafetyRulesAttribute</c>. The printer must use
/// that signal to choose between a member <c>unsafe</c> modifier (legacy) and
/// explicit <c>unsafe { }</c> blocks (new rules).
/// </summary>
public class UnsafeEmitterTests
{
    static string Decompile(string assemblyPath, string typeFullName, string method)
    {
        var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, typeFullName, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static string DecompileNew(string method) =>
        Decompile(typeof(NewFixtures).Assembly.Location, typeof(NewFixtures).FullName!, method);

    static string DecompileLegacy(string method) =>
        Decompile(typeof(LegacyFixtures).Assembly.Location, typeof(LegacyFixtures).FullName!, method);

    [Fact]
    public void NewRulesModule_PointerDeref_EmitsExplicitUnsafeContext()
    {
        // RED until the emitter lands: under the new rules the member modifier
        // no longer provides a body context, so `*p` must be wrapped in an
        // explicit `unsafe { }` block. The printer emits no unsafe context today.
        var output = DecompileNew(nameof(NewFixtures.DerefPointer));

        Assert.Contains("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_FunctionPointerInvoke_EmitsExplicitUnsafeContext()
    {
        var output = DecompileNew(nameof(NewFixtures.InvokeFunctionPointer));

        Assert.Contains("unsafe", output);
    }
}
