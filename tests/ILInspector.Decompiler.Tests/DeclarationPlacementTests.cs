using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Declaration placement for a local the source declared inside a nested block
/// (#3591). The portable PDB's <c>LocalScope</c> table records each local's true
/// extent; <c>MetadataSource.LocalScopes</c> reads it and the printer sinks the
/// declaration onto its store when the IR independently proves that is valid.
/// </summary>
/// <remarks>
/// The two halves of that rule each have a dedicated negative here. Without a PDB
/// there is no evidence of intent, so
/// <see cref="NarrowLocal_StaysHoisted_WithoutPdb"/> pins the unchanged hoisted
/// shape rather than a guess. With a PDB but a store the IR cannot prove dominates
/// its uses, <see cref="BranchedStore_StaysHoisted_DespiteNestedPdbScope"/> pins the
/// decline. This class was <c>DeclarationPlacementFrontierTests</c> in #3593, where
/// it pinned the gap and named the two tests that closing it had to flip.
/// </remarks>
public class DeclarationPlacementTests
{
    static readonly IAssemblyReferenceResolver RuntimeResolver = TestAssemblyReferenceResolvers.TrustedPlatformAssemblies();

    /// <summary>
    /// The PDB scopes the local to the try block, and every reference stays in that
    /// block after the store, so the declaration merges into the store.
    /// </summary>
    [Fact]
    public void NarrowLocal_DeclarationSinksToItsStore_WithPdb()
    {
        var output = Print(nameof(DeclScopeClient.CreateNarrow));

        Assert.Contains("DeclScopeResult response = _ops.Create(name, timeout);", output);
        Assert.DoesNotContain("DeclScopeResult response;", output);
        Assert.True(
            output.IndexOf("using (", StringComparison.Ordinal)
                < output.IndexOf("DeclScopeResult response =", StringComparison.Ordinal),
            $"Expected the declaration inside the using.{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Non-vacuity gate for the PDB evidence. With symbols withheld the same IR
    /// reaches the printer with no scope facts, and the local keeps its hoisted
    /// declaration: absent evidence the tool does not guess.
    /// </summary>
    [Fact]
    public void NarrowLocal_StaysHoisted_WithoutPdb()
    {
        var output = PrintWithoutSymbols(nameof(DeclScopeClient.CreateNarrow));

        var declaration = Assert.Single(Regex.Matches(output, @"DeclScopeResult V_\d+;")).Value;
        Assert.DoesNotContain("response", output);
        int declarationIndex = output.IndexOf(declaration, StringComparison.Ordinal);
        int usingIndex = output.IndexOf("using (", StringComparison.Ordinal);
        Assert.True(usingIndex >= 0, $"Missing using statement.{Environment.NewLine}{output}");
        Assert.True(
            declarationIndex < usingIndex,
            $"Expected `{declaration}` hoisted above the using.{Environment.NewLine}{output}");
    }

    /// <summary>
    /// The headline claim of #3591, inverted. A present PDB used to change the
    /// local's name and nothing about where it is declared — masking every local
    /// identifier made the two renderings byte-identical. Now placement differs too,
    /// and masking no longer collapses them.
    /// </summary>
    [Fact]
    public void NarrowLocal_PlacementDiffersWithAndWithoutPdb()
    {
        var withPdb = Print(nameof(DeclScopeClient.CreateNarrow));
        var withoutPdb = PrintWithoutSymbols(nameof(DeclScopeClient.CreateNarrow));

        Assert.NotEqual(MaskLocalNames(withPdb), MaskLocalNames(withoutPdb));
    }

    /// <summary>
    /// A loop body is the same evidence in its commonest form: the source declared
    /// the local inside the body, so it belongs at its store rather than above the
    /// loop where every iteration would appear to share it.
    /// </summary>
    [Fact]
    public void LoopBodyLocal_DeclarationSinksToItsStore()
    {
        var output = Print<DeclScopeLoopClient>(nameof(DeclScopeLoopClient.SumNarrow));

        Assert.Contains("DeclScopeResult step = _ops.Create(\"s\", i);", output);
        Assert.DoesNotContain("DeclScopeResult step;", output);
    }

    /// <summary>
    /// Non-vacuity gate for the IR guard. The PDB scopes this local to the using
    /// block just as narrowly, but the first store is inside one arm of the if while
    /// the read is after it, so sinking the declaration onto that store would not
    /// compile. The scope is evidence of intent, not of validity, and the printer
    /// declines.
    /// </summary>
    [Fact]
    public void BranchedStore_StaysHoisted_DespiteNestedPdbScope()
    {
        var scope = PdbScope<DeclScopeLoopClient>(nameof(DeclScopeLoopClient.CreateBranched), "response");
        Assert.True(
            scope.Start > 0 || scope.End < scope.IlLength,
            $"Expected a nested scope, got IL_{scope.Start:X4}..IL_{scope.End:X4} of 0x{scope.IlLength:X4}.");

        var output = Print<DeclScopeLoopClient>(nameof(DeclScopeLoopClient.CreateBranched));

        Assert.Contains("DeclScopeResult response;", output);
        Assert.True(
            output.IndexOf("DeclScopeResult response;", StringComparison.Ordinal)
                < output.IndexOf("using (", StringComparison.Ordinal),
            $"Expected the declaration to stay above the using.{Environment.NewLine}{output}");
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

    // Every local identifier the two renderings can disagree on: the PDB source
    // names and the V_index fallbacks. Masking them isolates placement from naming.
    static string MaskLocalNames(string output)
        => Regex.Replace(output, @"\b(?:V_\d+|response|scope|ex|step|total)\b", "N");

    static string Print(string methodName) => Print<DeclScopeClient>(methodName);

    static string Print<T>(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, null, RuntimeResolver);
        return Raise<T>(source, methodName);
    }

    static string PrintWithoutSymbols(string methodName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location, RuntimeResolver);
        return Raise<DeclScopeClient>(source, methodName);
    }

    static string Raise<T>(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, typeof(T).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    static (int Start, int End, int IlLength) PdbScope(string methodName, string localName)
        => PdbScope<DeclScopeClient>(methodName, localName);

    static (int Start, int End, int IlLength) PdbScope<T>(string methodName, string localName)
    {
        foreach (var (scope, ilLength, pdb) in Scopes<T>(methodName))
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
        foreach (var (scope, _, pdb) in Scopes<DeclScopeClient>(methodName))
        {
            foreach (var variableHandle in scope.GetLocalVariables())
            {
                var variable = pdb.GetLocalVariable(variableHandle);
                slots.Add((variable.Index, pdb.GetString(variable.Name)));
            }
        }

        return [.. slots];
    }

    static IEnumerable<(LocalScope Scope, int IlLength, MetadataReader Pdb)> Scopes<T>(string methodName)
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
            if (metadata.GetString(metadata.GetTypeDefinition(method.GetDeclaringType()).Name) != typeof(T).Name)
                continue;

            int ilLength = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader().Length;
            foreach (var scopeHandle in pdb.GetLocalScopes(handle))
                yield return (pdb.GetLocalScope(scopeHandle), ilLength, pdb);
        }
    }
}
