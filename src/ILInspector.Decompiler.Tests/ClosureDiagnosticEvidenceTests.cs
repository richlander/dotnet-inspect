using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class ClosureDiagnosticEvidenceTests
{
    [Theory]
    [InlineData("@class", "class")]
    [InlineData("@for.@class", "class")]
    [InlineData("Namespace.Generic<T>", "Generic")]
    [InlineData("Namespace.Metadata`1", "Metadata")]
    public void NormalizeTypeName_ReturnsRawMetadataLeaf(string name, string expected)
        => Assert.Equal(expected, ClosureDiagnosticEvidence.NormalizeTypeName(name));

    [Theory]
    [InlineData("CS0246", "class C { MissingType Field; }", "MissingType", null, null)]
    [InlineData("CS0234", "class C { Root.Missing.Value Field; } namespace Root { public class Existing {} }", "Missing", null, "Root")]
    [InlineData("CS0234", "class C { void M() { Root.Missing.Helper.Do(); } } namespace Root { public class Existing {} }", "Missing", null, "Root")]
    [InlineData("CS0234", "class C { @for.Missing.Value Field; } namespace @for { public class Existing {} }", "Missing", null, "for")]
    [InlineData("CS0103", "class C { void M() { MissingName(); } }", "MissingName", null, null)]
    [InlineData("CS0122", "class Holder { private class Hidden {} } class C { Holder.Hidden Field; }", "Holder.Hidden", null, null)]
    [InlineData("CS1061", "class Receiver {} class C { void M(Receiver value) { value.Missing(); } }", "Missing", "Receiver", null)]
    [InlineData("CS1061", "class Receiver {} class C { void M(Receiver value) { value?.Missing(); } }", "Missing", "Receiver", null)]
    [InlineData("CS1061", "class Receiver {} class C { object M(Receiver value) => value?.Missing; }", "Missing", "Receiver", null)]
    [InlineData("CS1061", "class Receiver {} class C { async System.Threading.Tasks.Task M(Receiver value) { await value; } }", "GetAwaiter", "Receiver", null)]
    [InlineData("CS1061", "class Receiver {} class C { async System.Threading.Tasks.Task M(Receiver value) { await value.Missing(); } }", "Missing", "Receiver", null)]
    [InlineData("CS1061", "class Receiver {} class Holder { public Receiver Value => null; } class C { async System.Threading.Tasks.Task M(Holder holder) { await holder.Value; } }", "GetAwaiter", "Receiver", null)]
    [InlineData("CS1061", "class Receiver : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; } class C { Receiver M() => new Receiver { 1 }; }", "Add", "Receiver", null)]
    [InlineData("CS1061", "class Receiver {} class C { System.Collections.Generic.List<object> M(Receiver value) => new() { value.Missing }; }", "Missing", "Receiver", null)]
    [InlineData("CS1061", "class Receiver : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; } class Item { public int Value => 1; } class C { Receiver M(Item item) => new Receiver { item.Value }; }", "Add", "Receiver", null)]
    [InlineData("CS1061", "class Receiver : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; } class C { async System.Threading.Tasks.Task<Receiver> M(System.Threading.Tasks.Task<int> task) => new Receiver { await task }; }", "Add", "Receiver", null)]
    [InlineData("CS1061", "class Receiver : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; } class C { async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.FromResult(new Receiver { 1 }); } }", "Add", "Receiver", null)]
    [InlineData("CS1061", "namespace @for { class @class {} class C { void M(@class value) { value.Missing(); } } }", "Missing", "@for.@class", null)]
    [InlineData("CS0117", "class Receiver {} class C { void M() { Receiver.Missing(); } }", "Missing", "Receiver", null)]
    [InlineData("CS0117", "class Receiver {} class C { Receiver M() => new Receiver { Missing = 1 }; }", "Missing", "Receiver", null)]
    public void Extract_UsesStructuredSyntaxAndSemanticEvidence(
        string diagnosticId,
        string source,
        string expectedName,
        string? expectedContainingType,
        string? expectedContainingNamespace)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "closure-diagnostic-evidence",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostic = Assert.Single(
            compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(candidate => candidate.Id == diagnosticId));

        var reference = ClosureDiagnosticEvidence.Extract(
            diagnostic,
            compilation.GetSemanticModel(tree));

        Assert.NotNull(reference);
        Assert.Equal(expectedName, reference.Name);
        Assert.Equal(expectedContainingType, reference.ContainingType);
        Assert.Equal(expectedContainingNamespace, reference.ContainingNamespace);
    }
}
