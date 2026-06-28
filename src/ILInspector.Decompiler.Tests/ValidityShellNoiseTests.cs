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
        var (diagnostic, tree) = ReadOnlyInstanceDiagnostic();
        var function = StaticConstructorWithBackingStore(ReferenceEqualityComparerType, "Instance");

        Assert.True(ValidityCheck.IsDeclaringTypeStaticPropertyCtorAssignmentNoise(diagnostic, tree, function));
    }

    [Fact]
    public void DeclaringTypeStaticPropertyCtorAssignmentWithoutBackingStore_StaysReported()
    {
        var (diagnostic, tree) = ReadOnlyInstanceDiagnostic();
        var function = StaticConstructorWithBackingStore(ReferenceEqualityComparerType, backingPropertyName: null);

        Assert.False(ValidityCheck.IsDeclaringTypeStaticPropertyCtorAssignmentNoise(diagnostic, tree, function));
    }

    [Fact]
    public void OtherTypeStaticPropertyAssignment_StaysReported()
    {
        var (diagnostic, tree) = ReadOnlyInstanceDiagnostic();
        var otherType = TypeRef.Definition("fixture", "Fixture", "Holder");
        var function = StaticConstructorWithBackingStore(otherType, "Instance");

        Assert.False(ValidityCheck.IsDeclaringTypeStaticPropertyCtorAssignmentNoise(diagnostic, tree, function));
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

    static (Diagnostic Diagnostic, SyntaxTree Tree) ReadOnlyInstanceDiagnostic()
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
        var diagnostic = Assert.Single(Compile(tree), d => d.Id == "CS0200");
        return (diagnostic, tree);
    }

    static ImmutableArray<Diagnostic> Compile(string source)
        => Compile(CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)));

    static ImmutableArray<Diagnostic> Compile(SyntaxTree tree)
        => CSharpCompilation.Create(
                "validity-shell-noise",
                [tree],
                ValidityCheck.RuntimeReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
                    .WithMetadataImportOptions(MetadataImportOptions.All))
            .GetDiagnostics();

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
}
