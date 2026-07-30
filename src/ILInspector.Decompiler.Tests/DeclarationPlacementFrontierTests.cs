using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Declaration placement for a local the source declared inside a nested block
/// (#3591). The portable PDB's <c>LocalScope</c> table records each local's true
/// extent, but <c>MetadataSource.LocalNames</c> keeps only the variable names and
/// drops <c>StartOffset</c>/<c>EndOffset</c>, so the printer hoists a narrowly
/// scoped local to the top of the method. These tests pin that frontier and, just
/// as importantly, pin the evidence that would close it: the PDB already
/// distinguishes the two shapes that today print identically.
/// </summary>
/// <remarks>
/// Closing #3591 must flip <see cref="NarrowLocal_IsHoistedAboveTheUsing_WithPdb"/>
/// and <see cref="NarrowLocal_PlacementIsIdenticalWithAndWithoutPdb"/>: with a PDB
/// the declaration should merge into its store inside the try, which is exactly the
/// masked equality this class currently asserts. The remaining tests are invariants
/// and should keep passing.
/// </remarks>
public class DeclarationPlacementFrontierTests
{
    static readonly IAssemblyReferenceResolver RuntimeResolver = TestAssemblyReferenceResolvers.TrustedPlatformAssemblies();

    /// <summary>
    /// The PDB names a local declared in the try block. The scope range that comes
    /// with the name is what the printer never reads.
    /// </summary>
    [Fact]
    public void NarrowLocal_IsHoistedAboveTheUsing_WithPdb()
    {
        var output = Print(nameof(DeclScopeClient.CreateNarrow));

        Assert.Contains("DeclScopeResult response;", output);
        Assert.Contains("response = _ops.Create(name, timeout);", output);
        AssertDeclarationPrecedesUsing(output, "DeclScopeResult response;");
    }

    /// <summary>
    /// Same method with symbols withheld. The local loses its source name, and
    /// nothing else moves.
    /// </summary>
    [Fact]
    public void NarrowLocal_IsHoistedAboveTheUsing_WithoutPdb()
    {
        var output = PrintWithoutSymbols(nameof(DeclScopeClient.CreateNarrow));

        var declaration = Assert.Single(Regex.Matches(output, @"DeclScopeResult V_\d+;")).Value;
        Assert.DoesNotContain("response", output);
        AssertDeclarationPrecedesUsing(output, declaration);
    }

    /// <summary>
    /// The headline claim of #3591: a present PDB changes the local's *name* and
    /// nothing about where it is declared. Masking every local identifier makes the
    /// two renderings byte-identical, which is only possible because the scope
    /// ranges are discarded.
    /// </summary>
    [Fact]
    public void NarrowLocal_PlacementIsIdenticalWithAndWithoutPdb()
    {
        var withPdb = Print(nameof(DeclScopeClient.CreateNarrow));
        var withoutPdb = PrintWithoutSymbols(nameof(DeclScopeClient.CreateNarrow));

        Assert.NotEqual(withPdb, withoutPdb);
        Assert.Equal(MaskLocalNames(withPdb), MaskLocalNames(withoutPdb));
    }

    /// <summary>
    /// Control: when the source really did declare above the try (the catch arm
    /// reads the local), today's emitter is correct. The gap is one-directional,
    /// so correct output on this method is not evidence that placement is derived.
    /// </summary>
    [Fact]
    public void HoistedLocal_RoundTripsAtMethodTop()
    {
        var output = Print(nameof(DeclScopeClient.CreateHoisted));

        Assert.Contains("DeclScopeResult response = null;", output);
        Assert.Contains("response = _ops.Create(name, timeout);", output);
        Assert.True(
            output.IndexOf("DeclScopeResult response = null;", StringComparison.Ordinal)
                < output.IndexOf("try", StringComparison.Ordinal),
            $"Expected the declaration above the try.{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Non-vacuity gate for the frontier above. If this fails, the two fixture
    /// shapes stopped being distinguishable in the PDB and the other tests here
    /// would be pinning a limitation that no longer has a knowable answer.
    /// </summary>
    [Fact]
    public void PdbScopes_DistinguishTheNarrowAndHoistedShapes()
    {
        var narrow = PdbScope(nameof(DeclScopeClient.CreateNarrow), "response");
        var hoisted = PdbScope(nameof(DeclScopeClient.CreateHoisted), "response");

        Assert.True(
            narrow.Start > 0 && narrow.End < narrow.IlLength,
            $"Expected a scope narrower than the method body, got IL_{narrow.Start:X4}..IL_{narrow.End:X4} of 0x{narrow.IlLength:X4}.");
        Assert.Equal(0, hoisted.Start);
        Assert.True(
            hoisted.End >= hoisted.IlLength,
            $"Expected a whole-method scope, got IL_{hoisted.Start:X4}..IL_{hoisted.End:X4} of 0x{hoisted.IlLength:X4}.");
    }

    /// <summary>
    /// A compiler temp carries no <c>LocalScope</c> entry at all, which is the same
    /// evidence in its degenerate form: absence of a scope marks a slot the source
    /// never declared. Read from the PDB directly, then cross-checked against the
    /// importer's view so the two cannot drift apart.
    /// </summary>
    [Fact]
    public void PdbScopes_OmitCompilerTemps()
    {
        var scoped = PdbScopedSlots(nameof(DeclScopeClient.CreateNarrow));
        Assert.Contains(scoped, slot => slot.Name == "response");

        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, null, RuntimeResolver);
        var function = IrImporter.Import(source, typeof(DeclScopeClient).FullName!, nameof(DeclScopeClient.CreateNarrow));
        Assert.NotNull(function);

        int[] unscoped = [.. Enumerable.Range(0, function!.Locals.Length).Where(index => !scoped.Any(slot => slot.Index == index))];
        Assert.NotEmpty(unscoped);
        Assert.All(unscoped, index => Assert.Null(function.LocalNames[index]));
    }

    static void AssertDeclarationPrecedesUsing(string output, string declaration)
    {
        int declarationIndex = output.IndexOf(declaration, StringComparison.Ordinal);
        int usingIndex = output.IndexOf("using (", StringComparison.Ordinal);

        Assert.True(declarationIndex >= 0, $"Missing declaration `{declaration}`.{Environment.NewLine}{output}");
        Assert.True(usingIndex >= 0, $"Missing using statement.{Environment.NewLine}{output}");
        Assert.True(
            declarationIndex < usingIndex,
            $"Expected `{declaration}` hoisted above the using.{Environment.NewLine}{output}");
    }

    // Every local identifier the two renderings can disagree on: the PDB source
    // names and the V_index fallbacks. Masking them isolates placement from naming.
    static string MaskLocalNames(string output)
        => Regex.Replace(output, @"\b(?:V_\d+|response|scope|ex)\b", "N");

    static string Print(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, null, RuntimeResolver);
        return Raise(source, methodName);
    }

    static string PrintWithoutSymbols(string methodName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location, RuntimeResolver);
        return Raise(source, methodName);
    }

    static string Raise(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, typeof(DeclScopeClient).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    static (int Start, int End, int IlLength) PdbScope(string methodName, string localName)
    {
        foreach (var (scope, ilLength, pdb) in Scopes(methodName))
        {
            foreach (var variableHandle in scope.GetLocalVariables())
            {
                if (pdb.GetString(pdb.GetLocalVariable(variableHandle).Name) == localName)
                    return (scope.StartOffset, scope.EndOffset, ilLength);
            }
        }

        throw new InvalidOperationException($"No PDB local scope for {methodName}/{localName}.");
    }

    static (int Index, string Name)[] PdbScopedSlots(string methodName)
    {
        var slots = new List<(int, string)>();
        foreach (var (scope, _, pdb) in Scopes(methodName))
        {
            foreach (var variableHandle in scope.GetLocalVariables())
            {
                var variable = pdb.GetLocalVariable(variableHandle);
                slots.Add((variable.Index, pdb.GetString(variable.Name)));
            }
        }

        return [.. slots];
    }

    static IEnumerable<(LocalScope Scope, int IlLength, MetadataReader Pdb)> Scopes(string methodName)
    {
        string assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(File.Exists(pdbPath), $"The test assembly must ship a portable PDB: {pdbPath}");

        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var metadata = pe.GetMetadataReader();
        using var pdbStream = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        var pdb = provider.GetMetadataReader();

        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (metadata.GetString(method.Name) != methodName)
                continue;
            if (metadata.GetString(metadata.GetTypeDefinition(method.GetDeclaringType()).Name) != nameof(DeclScopeClient))
                continue;

            int ilLength = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader().Length;
            foreach (var scopeHandle in pdb.GetLocalScopes(handle))
                yield return (pdb.GetLocalScope(scopeHandle), ilLength, pdb);
        }
    }
}
