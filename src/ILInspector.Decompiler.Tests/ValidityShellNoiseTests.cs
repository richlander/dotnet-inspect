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

    [Fact]
    public void ShellThisConversionDiagnostic_IsFilteredFromStructuredEvidence()
    {
        var (diagnostic, tree, semanticModel) = DiagnosticFor("""
            class __Shell
            {
                static void TakeInt(int value) { }
                void __M() => TakeInt(this);
            }
            """, "CS1503");

        Assert.True(ValidityCheck.IsShellArtifact(diagnostic, tree, semanticModel));
    }

    [Fact]
    public void OtherReceiverConversionDiagnostic_StaysReported()
    {
        var (diagnostic, tree, semanticModel) = DiagnosticFor("""
            class Other { }
            class __Shell
            {
                static void TakeInt(int value) { }
                void __M() => TakeInt(new Other());
            }
            """, "CS1503");

        Assert.False(ValidityCheck.IsShellArtifact(diagnostic, tree, semanticModel));
    }

    [Fact]
    public void ShellReceiverWithInvalidArgumentDiagnostic_StaysReported()
    {
        var (diagnostic, tree, semanticModel) = DiagnosticFor("""
            class __Shell
            {
                void TakeInt(int value) { }
                void __M() => this.TakeInt("bad");
            }
            """, "CS1503");

        Assert.False(ValidityCheck.IsShellArtifact(diagnostic, tree, semanticModel));
    }

    [Fact]
    public void ShellReceiverInsideConversionDiagnostic_StaysReported()
    {
        var (diagnostic, tree, semanticModel) = DiagnosticFor("""
            class __Shell
            {
                int GetInt() => 1;
                void __M()
                {
                    string value = this.GetInt();
                }
            }
            """, "CS0029");

        Assert.False(ValidityCheck.IsShellArtifact(diagnostic, tree, semanticModel));
    }

    [Fact]
    public void NonSourceDiagnostic_StaysReported()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class __Shell { }",
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CreateCompilation(tree);
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
                "TEST001",
                "test",
                "mentions __Shell",
                "test",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true),
            Location.None);

        Assert.False(ValidityCheck.IsShellArtifact(
            diagnostic,
            tree,
            compilation.GetSemanticModel(tree)));
    }

    [Fact]
    public void BareStaticTypeCollisionWithModeledNonGenericType_IsFiltered()
    {
        var (diagnostic, tree, _) = DiagnosticFor("""
            using System;
            class __Shell
            {
                void __M(Convert value) { }
            }
            """, "CS0721");
        var function = FunctionReferencing(TypeRef.Definition("fixture", "Fixture", "Convert"));

        Assert.True(ValidityCheck.IsSimpleNameStaticTypeCollisionNoise(diagnostic, tree, function));
    }

    [Fact]
    public void BareStaticTypeCollisionWithModeledGenericType_StaysReported()
    {
        var (diagnostic, tree, _) = DiagnosticFor("""
            using System;
            class __Shell
            {
                void __M(Convert value) { }
            }
            """, "CS0721");
        var function = FunctionReferencing(TypeRef.Definition("fixture", "Fixture", "Convert`1"));

        Assert.False(ValidityCheck.IsSimpleNameStaticTypeCollisionNoise(diagnostic, tree, function));
    }

    static (Diagnostic Diagnostic, SyntaxTree Tree, SemanticModel SemanticModel) DiagnosticFor(
        string source,
        string diagnosticId)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CreateCompilation(tree);
        var diagnostic = Assert.Single(
            compilation.GetDiagnostics(TestContext.Current.CancellationToken),
            d => d.Id == diagnosticId);
        return (diagnostic, tree, compilation.GetSemanticModel(tree));
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

    static IrFunction FunctionReferencing(TypeRef type)
        => new(
            "M",
            TypeRef.Definition("fixture", "Fixture", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("value", type)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            new BlockContainer());
}
