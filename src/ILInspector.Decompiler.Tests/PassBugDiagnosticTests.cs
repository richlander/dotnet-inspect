using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class PassBugDiagnosticTests
{
    [Fact]
    public void DirectPassBugEmitters_UseSharedFormatter()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);

        string harnessDirectory = Path.Combine(directory.FullName, "tools", "DecompilerHarness");
        string[] offenders = Directory.EnumerateFiles(harnessDirectory, "*.cs")
            .Where(path => Path.GetFileName(path) != "PassBugDiagnostic.cs")
            .Where(path => File.ReadAllText(path).Contains("PASS BUG", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

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
            stringOverload);
        string otherAssembly = PassBugDiagnostic.Format(
            exception,
            "/packages/example/2.0.0/lib/Example.dll",
            "Example.Parser",
            "Parse",
            stringOverload);
        string otherOverload = PassBugDiagnostic.Format(
            exception,
            "/packages/example/1.0.0/lib/Example.dll",
            "Example.Parser",
            "Parse",
            integerOverload);

        Assert.Equal(
            "PASS BUG: KeyNotFoundException: missing block 3 " +
            "(/packages/example/1.0.0/lib/Example.dll!Example.Parser::Parse" +
            "(corelib:System.String) -> corelib:System.String)",
            first);
        Assert.NotEqual(first, otherAssembly);
        Assert.Contains("/packages/example/2.0.0/lib/Example.dll", otherAssembly);
        Assert.NotEqual(first, otherOverload);
        Assert.Contains(
            "Parse(corelib:System.Int32) -> corelib:System.String",
            otherOverload);
    }
}
