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
        string[] files = Directory.EnumerateFiles(harnessDirectory, "*.cs").ToArray();
        string[] callSites = files
            .SelectMany(path => Enumerable.Repeat(
                Path.GetFileName(path)!,
                File.ReadAllText(path).Split(
                    "PassBugDiagnostic.Format(",
                    StringSplitOptions.None).Length - 1))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "Program.cs",
                "Program.cs",
                "Program.cs",
                "Program.cs",
                "SlotResidualCensus.cs",
                "SlotUnifierCensus.cs",
            ],
            callSites);

        string[] offenders = files
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
