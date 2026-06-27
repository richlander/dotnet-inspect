using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Narrow rung 2 guard for #1624: C# 3 object/collection initializer lowerings
/// flowing through compiler temporaries must render as initializer syntax rather
/// than explicit property-set/Add temp chains.
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
        "Main", "MakeCollectionInitializer", "MakeDirectReturn",
    ];

    [Fact]
    public void Rung2Fixture_ExposesExactScopedMemberSet_AllFull()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 2 initializer slice requires every scoped fixture member to render Full; not Full: " + string.Join(", ", notFull));
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
    }

    [Fact]
    public void Rung2Fixture_HasNoMalformedFullMethods()
    {
        var malformed = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName == FixtureType && r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(malformed.Length == 0,
            "Rung 2 initializer slice requires zero malformed Full methods; malformed: " + string.Join("; ", malformed));
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
