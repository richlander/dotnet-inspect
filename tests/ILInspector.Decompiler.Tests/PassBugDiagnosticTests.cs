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
        // Recursive so a new emitter cannot evade the gate by living in a
        // subdirectory; build output is not harness source.
        string[] files = Directory
            .EnumerateFiles(harnessDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(harnessDirectory, path))
            .ToArray();
        string[] callSites = files
            .SelectMany(path => Enumerable.Repeat(
                Relative(harnessDirectory, path),
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
            .Where(path => Relative(harnessDirectory, path) != "PassBugDiagnostic.cs")
            .Where(path => File.ReadAllText(path).Contains("PASS BUG", StringComparison.Ordinal))
            .Select(path => Relative(harnessDirectory, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(offenders);
    }

    static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    static bool IsBuildOutput(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
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
