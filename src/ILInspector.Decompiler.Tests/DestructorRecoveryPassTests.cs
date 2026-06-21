using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A C# destructor lowers to a Finalize override whose body is
// try { BODY } finally { base.Finalize(); }. DestructorRecoveryPass strips that
// scaffold back to BODY and marks the function a destructor.
public class DestructorRecoveryPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void FinalizeOverride_RecoversAsDestructorBody()
    {
        var function = Raised("Finalize");

        Assert.True(function.IsDestructor);
        // The try/finally scaffold and the base.Finalize() call are gone — the body
        // is the destructor's own statements.
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        Assert.DoesNotContain(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Finalize");

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("s_finalized = true;", output);
        Assert.DoesNotContain("base.Finalize", output);
        Assert.DoesNotContain("finally", output);
    }
}
