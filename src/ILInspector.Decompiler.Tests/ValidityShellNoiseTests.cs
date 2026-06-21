using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guards the validity-check's binding-noise filtering — the predicates that
/// distinguish a real "claimed Full but won't bind" defect from an artifact of
/// the sibling-free <c>__Shell</c> the decompiled body is compiled inside.
/// </summary>
public class ValidityShellNoiseTests
{
    static Diagnostic SingleError(string source, string id, out SyntaxTree tree)
    {
        tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "noise-test", [tree], ValidityCheck.RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return Assert.Single(compilation.GetDiagnostics().Where(d => d.Id == id));
    }

    [Fact]
    public void GenericArityCollisionNoise_FiltersBareSiblingTypeName()
    {
        // The decompiler emits its own non-generic `Comparison` IR node by simple
        // name; in the sibling-free shell the bare name resolves to the
        // using-imported System.Comparison<T> and reports CS0305. In the real
        // namespace the local type binds, so this is shell noise.
        var diagnostic = SingleError(
            """
            using System;
            class __Shell { void __M(object o) { var c = o as Comparison; } }
            """, "CS0305", out var tree);

        Assert.True(ValidityCheck.IsGenericArityCollisionNoise(diagnostic, tree));
    }

    [Fact]
    public void GenericArityCollisionNoise_FiltersBareMemberAccessReceiver()
    {
        // A member access whose receiver is a property in the real type (`Lookup`)
        // collides with using System.Linq's Lookup<TKey, TElement> in the shell.
        var diagnostic = SingleError(
            """
            using System.Linq;
            class __Shell { object __M() { return Lookup.Count; } }
            """, "CS0305", out var tree);

        Assert.True(ValidityCheck.IsGenericArityCollisionNoise(diagnostic, tree));
    }

    [Fact]
    public void GenericArityCollisionNoise_KeepsExplicitWrongArity()
    {
        // A genuine wrong-arity spelling — the type arguments ARE written (a
        // GenericNameSyntax), so it is a real defect, not a bare-name collision.
        var diagnostic = SingleError(
            """
            using System.Collections.Generic;
            class __Shell { void __M() { Dictionary<int> d; } }
            """, "CS0305", out var tree);

        Assert.False(ValidityCheck.IsGenericArityCollisionNoise(diagnostic, tree));
    }

    [Fact]
    public void GenericArityCollisionNoise_IgnoresOtherCodes()
    {
        // Only CS0305 is in scope; an unrelated code is never treated as this noise.
        var diagnostic = SingleError(
            """
            class __Shell { int __M() { return Unknown; } }
            """, "CS0103", out var tree);

        Assert.False(ValidityCheck.IsGenericArityCollisionNoise(diagnostic, tree));
    }
}
