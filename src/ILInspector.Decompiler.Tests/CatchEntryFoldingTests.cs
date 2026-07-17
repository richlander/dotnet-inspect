using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// EhStructuringPass.FoldEntryConsumption folds a catch-entry `stloc` of the
// caught exception into the clause variable — but only when that local is
// confined to the handler. Issue #2828: when the destination local is declared
// before the try and read after the catch, folding it emits a duplicate catch
// variable (CS0136) and drops the `local = ex` assignment. Because the folded
// shape is load-bearing for the using/foreach/async scaffold passes, the fix is
// a late pass (CatchVariableScopePass) that runs after those consumers and
// un-folds only surviving plain clauses whose folded local escapes the clause:
// it rebinds a fresh catch variable and restores the `local = ex` entry store.
// Ordinary handler-local folding is preserved.
public class CatchEntryFoldingTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    [Fact]
    public void HandlerLocalCatchVariable_FoldsIntoClause()
    {
        var function = Raised(nameof(CfgSampleClass.CatchFoldsHandlerLocal));

        var clause = Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses.Single();
        Assert.NotNull(clause.VariableIndex);
        // The fold consumed the entry store: no bare caught exception, and no
        // re-introduced `local = caught` assignment inside the handler body.
        Assert.Empty(clause.Body.Descendants.OfType<CaughtException>());

        string? output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("catch (Exception", output);
        AssertCompiles("int", "CatchFoldsHandlerLocal", output);
    }

    [Fact]
    public void PredeclaredLocalAssignedInCatch_DeclinesFoldAndBindsFreshVariable()
    {
        var function = Raised(nameof(CfgSampleClass.CatchAssignsPredeclaredLocal));

        var clause = Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses.Single();
        Assert.NotNull(clause.VariableIndex);

        // The predeclared local (slot 0, `captured`) is initialized before the
        // try, so the clause variable must be a distinct fresh slot, not slot 0.
        Assert.NotEqual(0, clause.VariableIndex);

        // The `captured = ex` assignment survives: a store into slot 0 inside the
        // handler, reading the freshly bound catch variable.
        var assignment = Assert.Single(
            clause.Body.Descendants.OfType<StoreLocal>(), s => s.Index == 0);
        var read = Assert.IsType<LoadLocal>(assignment.Value);
        Assert.Equal(clause.VariableIndex, read.Index);

        // No bare caught exception leaks, and the catch variable name differs
        // from the predeclared local's name — so the emitted C# binds (no CS0136).
        Assert.Empty(clause.Body.Descendants.OfType<CaughtException>());

        string? output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("captured = ", output);
        AssertCompiles("string", "CatchAssignsPredeclaredLocal", output);
    }

    static void AssertCompiles(string returnType, string name, string body)
    {
        string source = $$"""
            using System;
            static class __Gate
            {
                static {{returnType}} M(string s)
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(
            errors.Length == 0,
            $"Rendered {name} must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }
}
