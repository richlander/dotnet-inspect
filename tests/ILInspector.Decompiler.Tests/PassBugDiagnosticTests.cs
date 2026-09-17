using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class PassBugDiagnosticTests
{
    [Fact]
    public void Format_IdentifiesAssemblyAndExactMethodBody()
    {
        var exception = new KeyNotFoundException("missing block 3");
        var stringOverload = new MethodSignature(
            TypeRef.CoreLib("System", "String"),
            [new Parameter("value", TypeRef.CoreLib("System", "String"))],
            HasThis: false,
            GenericParameterCount: 0);
        var integerOverload = stringOverload with
        {
            Parameters = [new Parameter("value", TypeRef.CoreLib("System", "Int32"))],
        };

        string first = PassBugDiagnostic.Format(
            exception,
            "/packages/example/1.0.0/lib/Example.dll",
            "Example.Parser",
            "Parse",
            stringOverload,
            0x0600002A);
        string otherAssembly = PassBugDiagnostic.Format(
            exception,
            "/packages/example/2.0.0/lib/Example.dll",
            "Example.Parser",
            "Parse",
            stringOverload,
            0x0600002A);
        string otherOverload = PassBugDiagnostic.Format(
            exception,
            "/packages/example/1.0.0/lib/Example.dll",
            "Example.Parser",
            "Parse",
            integerOverload,
            0x0600002A);
        string otherToken = PassBugDiagnostic.Format(
            exception,
            "/packages/example/1.0.0/lib/Example.dll",
            "Example.Parser",
            "Parse",
            stringOverload,
            0x0600002B);

        Assert.Equal(
            "PASS BUG: KeyNotFoundException: missing block 3 " +
            "(/packages/example/1.0.0/lib/Example.dll!Example.Parser::Parse" +
            "(corelib:System.String) -> corelib:System.String [token 0x0600002A])",
            first);
        Assert.NotEqual(first, otherAssembly);
        Assert.Contains("/packages/example/2.0.0/lib/Example.dll", otherAssembly);
        Assert.NotEqual(first, otherOverload);
        Assert.Contains(
            "Parse(corelib:System.Int32) -> corelib:System.String",
            otherOverload);
        Assert.NotEqual(first, otherToken);
        Assert.Contains("[token 0x0600002B]", otherToken);
    }
}
