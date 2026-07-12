using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class ValidityShellNoiseTests
{
    static readonly TypeRef ReferenceEqualityComparerType =
        TypeRef.Definition("Microsoft.CodeAnalysis", "System.Collections.Generic", "ReferenceEqualityComparer");

    static readonly TypeRef NonGenericConvertType =
        TypeRef.Definition("ILInspector.Decompiler", "ILInspector.Decompiler.Pipeline", "Convert");

    [Theory]
    [InlineData("CS0029", "class Real {} sealed class __Shell { Real M() => this; }")]
    [InlineData("CS0019", "sealed class Real {} sealed class __Shell { bool M(Real value) => this == value; }")]
    [InlineData("CS0030", "sealed class Real {} sealed class __Shell { Real M() => (Real)this; }")]
    [InlineData("CS0023", "sealed class __Shell { bool M() => !this; }")]
    [InlineData("CS8121", "class Real {} sealed class Derived : Real {} sealed class __Shell { bool M() => this is Derived; }")]
    public void SyntheticShellThisDiagnostics_AreFiltered(string diagnosticId, string source)
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel(source);

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics.Where(d => d.Id == diagnosticId),
            tree,
            Function(TypeRef.Definition("fixture", "", "Real")),
            semanticModel);

        Assert.Empty(defects);
    }

    [Fact]
    public void ShellNameInMethodLevelDiagnostic_StaysReported()
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel(
            "sealed class __Shell { bool M() { } }");

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics,
            tree,
            Function(TypeRef.Definition("fixture", "", "Real")),
            semanticModel);

        Assert.Equal(["CS0161"], defects.Select(d => d.Id));
    }

    [Theory]
    [InlineData(
        "CS0266",
        "sealed class __Shell { byte M(long value) => this.Equals(null) ? value : value; }")]
    [InlineData(
        "CS0029",
        "sealed class __Shell { void M() { (string, object) value; value = (1, this); } }")]
    public void ShellThisInSiblingSubexpression_DoesNotHideDefect(
        string diagnosticId,
        string source)
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel(source);

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics.Where(d => d.Id == diagnosticId),
            tree,
            Function(TypeRef.Definition("fixture", "", "Real")),
            semanticModel);

        Assert.Equal([diagnosticId], defects.Select(d => d.Id));
    }

    [Fact]
    public void ProtectedAccessThroughDeclaringTypeReceiver_IsFiltered()
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel("""
            class Base { protected object Clone() => new(); }
            class Real : Base {}
            class __Shell : Base
            {
                object M(Real value) => value.Clone();
            }
            """);

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics,
            tree,
            Function(TypeRef.Definition("fixture", "", "Real")),
            semanticModel);

        Assert.Empty(defects);
    }

    [Fact]
    public void ProtectedAccessThroughOtherTypeReceiver_StaysReported()
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel("""
            class Base { protected object Clone() => new(); }
            class Real : Base {}
            class __Shell : Base
            {
                object M(Real value) => value.Clone();
            }
            """);

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics,
            tree,
            Function(TypeRef.Definition("fixture", "", "Other")),
            semanticModel);

        Assert.Equal(["CS1540"], defects.Select(d => d.Id));
    }

    [Theory]
    [InlineData("CS0712", "class __Shell { object M() => new Convert(); }")]
    [InlineData("CS0721", "class __Shell { void M(Convert value) {} }")]
    [InlineData(
        "CS0721",
        "class __Shell { System.Threading.Tasks.Task M(System.Convert value) => null; }")]
    [InlineData("CS0722", "class __Shell { Convert M() => null; }")]
    [InlineData("CS0723", "class __Shell { void M() { Convert value = null; } }")]
    public void StaticTypeNameCollisions_UseDiagnosticSyntax(
        string diagnosticId,
        string source)
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel(
            "using System;" + Environment.NewLine + source);

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics.Where(d => d.Id == diagnosticId),
            tree,
            Function(
                TypeRef.Definition("fixture", "", "Host"),
                [NonGenericConvertType]),
            semanticModel);

        Assert.Empty(defects);
    }

    [Fact]
    public void StaticTypeDiagnosticWithoutModelCollision_StaysReported()
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel(
            "using System; class __Shell { void M() { Convert value = null; } }");

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            diagnostics.Where(d => d.Id == "CS0723"),
            tree,
            Function(TypeRef.Definition("fixture", "", "Host")),
            semanticModel);

        Assert.Equal(["CS0723"], defects.Select(d => d.Id));
    }

    [Theory]
    [InlineData("CS0029")]
    [InlineData("CS0723")]
    public void SupportedDiagnosticWithoutSourceLocation_IsFailVisible(string diagnosticId)
    {
        var (diagnostics, tree, semanticModel) = CompileWithModel("class __Shell {}");
        var descriptor = new DiagnosticDescriptor(
            diagnosticId,
            "Synthetic",
            "Synthetic",
            "Compiler",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, Location.None);

        var defects = ValidityCheck.ClassifySemanticDiagnostics(
            [diagnostic],
            tree,
            Function(TypeRef.Definition("fixture", "", "Host")),
            semanticModel);

        Assert.Equal(
            [diagnosticId, ValidityCheck.StructuredEvidenceMissId],
            defects.Select(d => d.Id));
    }

    [Fact]
    public void DeclaringTypeStaticPropertyCtorAssignmentCollision_IsFiltered()
    {
        var (diagnostic, tree, semanticModel) = ReadOnlyInstanceDiagnostic();
        var function = StaticConstructorWithBackingStore(ReferenceEqualityComparerType, "Instance");

        Assert.True(ValidityCheck.IsDeclaringTypeStaticPropertyCtorAssignmentNoise(diagnostic, tree, function, semanticModel));
    }

    [Fact]
    public void DeclaringTypeStaticPropertyCtorAssignmentWithoutBackingStore_StaysReported()
    {
        var (diagnostic, tree, semanticModel) = ReadOnlyInstanceDiagnostic();
        var function = StaticConstructorWithBackingStore(ReferenceEqualityComparerType, backingPropertyName: null);

        Assert.False(ValidityCheck.IsDeclaringTypeStaticPropertyCtorAssignmentNoise(diagnostic, tree, function, semanticModel));
    }

    [Fact]
    public void OtherTypeStaticPropertyAssignment_StaysReported()
    {
        var (diagnostic, tree, semanticModel) = ReadOnlyInstanceDiagnostic();
        var otherType = TypeRef.Definition("fixture", "Fixture", "Holder");
        var function = StaticConstructorWithBackingStore(otherType, "Instance");

        Assert.False(ValidityCheck.IsDeclaringTypeStaticPropertyCtorAssignmentNoise(diagnostic, tree, function, semanticModel));
    }

    [Fact]
    public void DeclaringTypeGetOnlyPropertyAssignment_BindsInsideStaticConstructor()
    {
        var diagnostics = Compile("""
            public sealed class Rq
            {
                public static Rq Instance { get; }

                static Rq()
                {
                    Rq.Instance = new Rq();
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    static (Diagnostic Diagnostic, SyntaxTree Tree, SemanticModel SemanticModel) ReadOnlyInstanceDiagnostic()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            #pragma warning disable
            using System.Collections.Generic;

            class __Shell
            {
                void __M()
                {
                    ReferenceEqualityComparer.Instance = null!;
                }
            }
            """, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CreateCompilation(tree);
        var diagnostic = Assert.Single(compilation.GetDiagnostics(), d => d.Id == "CS0200");
        return (diagnostic, tree, compilation.GetSemanticModel(tree));
    }

    static (ImmutableArray<Diagnostic> Diagnostics, SyntaxTree Tree, SemanticModel SemanticModel)
        CompileWithModel(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CreateCompilation(tree);
        return (
            compilation.GetDiagnostics(TestContext.Current.CancellationToken),
            tree,
            compilation.GetSemanticModel(tree));
    }

    static ImmutableArray<Diagnostic> Compile(string source)
        => CreateCompilation(CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)))
            .GetDiagnostics();

    static CSharpCompilation CreateCompilation(SyntaxTree tree)
        => CSharpCompilation.Create(
                "validity-shell-noise",
                [tree],
                ValidityCheck.RuntimeReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
                    .WithMetadataImportOptions(MetadataImportOptions.All));

    static IrFunction StaticConstructorWithBackingStore(TypeRef declaringType, string? backingPropertyName)
    {
        var field = new FieldRef(declaringType, "<Instance>k__BackingField", declaringType)
        {
            BackingPropertyName = backingPropertyName,
        };
        var block = new Block();
        block.Add(new StoreField(field, instance: null, new Constant(null, declaringType)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            ".cctor",
            declaringType,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction Function(
        TypeRef declaringType,
        ImmutableArray<TypeRef> locals = default)
        => new(
            "M",
            declaringType,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            locals.IsDefault ? [] : locals,
            new BlockContainer());
}
