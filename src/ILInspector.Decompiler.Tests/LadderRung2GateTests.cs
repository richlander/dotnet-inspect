using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Narrow rung 2 guard for #1624/#1625/#1626: C# 3 object/collection initializer
/// lowerings flowing through compiler temporaries must render as initializer
/// syntax, and anonymous type locals must use source-level local spelling rather
/// than generated metadata type names, and nullable value-type coalesce must use
/// source-level ?? syntax.
/// </summary>
public class LadderRung2GateTests
{
    static string FixturePath => typeof(LadderRung2.Program).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung2.Program).FullName!;

    static readonly Lazy<IReadOnlyDictionary<string, string>> s_runtimeAssemblies = new(() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase));

    static readonly AssemblyLocator RuntimeLocator = (name, trust) =>
        trust == AssemblyTrust.Platform && s_runtimeAssemblies.Value.TryGetValue(name, out var path)
            ? path
            : null;

    static readonly string[] ExpectedMembers =
    [
        "AnonymousSummary", "Main", "MakeCollectionInitializer", "MakeDirectReturn", "NullableOrDefault",
    ];

    static readonly string[] ExpectedFullMembers =
    [
        "Main", "MakeCollectionInitializer", "MakeDirectReturn", "NullableOrDefault",
    ];

    [Fact]
    public void Rung2Fixture_ExposesExactScopedMemberSet_InitializerMembersAllFull()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => ExpectedFullMembers.Contains(m.Name) && m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 2 initializer slice requires every initializer fixture member to render Full; not Full: " + string.Join(", ", notFull));
    }

    [Fact]
    public void Rung2Fixture_RendersInitializerChainsRecognizably()
    {
        var members = LoadRaisedMembers();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        var main = Body("Main");
        Assert.Contains("new Box<int> { Value = 3, Label = \"three\" }", main);
        Assert.Contains("new List<int> { 1, 2, 3 }", main);
        Assert.DoesNotContain(".Value = 3;", main);
        Assert.DoesNotContain(".Label = \"three\";", main);
        Assert.DoesNotContain(".Add(1);", main);

        Assert.Contains(
            "return new List<int> { 1, 2, 3 };",
            Body("MakeCollectionInitializer"));
        Assert.Contains(
            "return new Box<string> { Value = text, Label = text.OrFallback(\"empty\") };",
            Body("MakeDirectReturn"));

        var anonymous = Body("AnonymousSummary");
        Assert.Contains("var info = new { Name = name, Count = count };", anonymous);
        Assert.Contains("info.Name", anonymous);
        Assert.Contains("info.Count", anonymous);
        Assert.DoesNotContain("AnonymousType", anonymous);

        var nullable = Body("NullableOrDefault");
        Assert.Contains("return value ?? 42;", nullable);
        Assert.DoesNotContain("GetValueOrDefault", nullable);
    }

    [Fact]
    public void Rung2Fixture_HasNoMalformedScopedMethods()
    {
        var malformed = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName == FixtureType && ExpectedMembers.Contains(r.MethodName) && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(malformed.Length == 0,
            "Rung 2 fixture slice requires zero malformed scoped methods; malformed: " + string.Join("; ", malformed));
    }

    static List<(string Name, IrFunction Function)> LoadRaisedMembers()
    {
        var members = new List<(string Name, IrFunction Function)>();
        using var source = MetadataSource.Open(FixturePath, locator: RuntimeLocator);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;
            IrPasses.Run(function);
            members.Add((methodName, function));
        }
        return members;
    }
}
