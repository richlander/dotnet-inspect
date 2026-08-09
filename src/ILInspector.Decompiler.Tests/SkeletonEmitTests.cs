using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the changed-method fidelity skeleton against whole-module emit hazards.
/// A non-generic explicit interface implementation and a const enum field
/// previously emitted invalid C# (CS0106 / CS0266) into the reconstructed module
/// (#1282); and the bare `using System;` skeleton recompile-failed any body whose
/// short type names needed a wider using set (CS0246, the changed-method
/// missing-symbol bucket). The fidelity check emits the whole module, so a method
/// on the offending type only compiles back when every hazard is handled.
/// </summary>
[Trait("Speed", "Slow")]
[Trait("Area", "Fidelity")]
[Collection(FidelityGateCollection.Name)]
public class SkeletonEmitTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.SkeletonEmitFixture";

    [Fact]
    public void SkeletonCompilesPastExplicitImplAndConstEnum()
    {
        var sum = Assert.Single(FidelityCheck.Evaluate(
            typeof(SkeletonEmitFixture).Assembly.Location,
            type => type == FixtureType,
            method => method.Method == "Sum"));

        // The point is that the whole-module skeleton compiles: an unhandled
        // explicit impl (CS0106) or const enum (CS0266) would surface here as a
        // RecompileFail/ContextFail, not as the clean opcode comparison below.
        Assert.False(sum.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile for {FixtureType}.Sum: {sum.Status} / {sum.Detail}");
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, sum.Status);

        string path = CreateAssemblyWithDuplicateUnrelatedType();
        try
        {
            var duplicateType = Assert.Single(FidelityCheck.Evaluate(
                path,
                type => type == FixtureType,
                method => method.Method == "Sum"));

            Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, duplicateType.Status);
            Assert.Contains("CS0101", duplicateType.Detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    // Bodies the product printer spells with short names that need a using the
    // bare `using System;` skeleton lacked: System.Collections.Generic
    // (Dictionary) and System.Linq (Enumerable). Before the skeleton's widened
    // using set these recompile-failed with CS0246; now they bind and compare.
    [InlineData("CompoundAssignDictionaryIndexer")]
    [InlineData("CachedStaticMethodGroup")]
    public void SkeletonImportsUsingsForShortNamedFrameworkTypes(string method)
    {
        var result = FidelityCheck.Evaluate(
                typeof(CfgSampleClass).Assembly.Location,
                type => type == "ILInspector.Decompiler.Tests.CfgSampleClass",
                candidate => candidate.Method == method)
            .Single();

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile CfgSampleClass.{method} (a missing using would surface here): "
            + $"{result.Status} / {result.Detail}");
    }

    [Fact]
    public void SkeletonSkipsCompilerEmbeddedAttributeDefinitions()
    {
        var result = FidelityCheck.Evaluate(
                typeof(EmbeddedAttributeSkeletonFixture).Assembly.Location,
                type => type == "ILInspector.Decompiler.Tests.EmbeddedAttributeSkeletonFixture",
                method => method.Method == "Value")
            .Single();

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile with Microsoft.CodeAnalysis.EmbeddedAttribute present: {result.Status} / {result.Detail}");
    }

    [Fact]
    public void SkeletonSkipsFixedBufferBackingFieldTypes()
    {
        var result = FidelityCheck.Evaluate(
                typeof(FixedBufferSkeletonFixture).Assembly.Location,
                type => type == "ILInspector.Decompiler.Tests.FixedBufferSkeletonFixture",
                method => method.Method == "Value")
            .Single();

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile with a fixed-buffer backing type present: {result.Status} / {result.Detail}");
    }

    [Fact]
    public void SkeletonDeclaresNestedTypeWithoutInheritedGenericParameters()
    {
        // GenericNestedUser.UseNested returns GenericNestedHolder<int>.Nested. The
        // skeleton reconstructs the nested Nested struct; emitting its inherited T
        // (struct Nested<T>) makes the GenericNestedHolder<int>.Nested reference
        // CS0305. Only the own (zero) parameters may be restated.
        var result = FidelityCheck.Evaluate(
                typeof(GenericNestedHolder<>).Assembly.Location,
                type => type == "ILInspector.Decompiler.Tests.GenericNestedUser",
                method => method.Method == "UseNested")
            .Single();

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton mis-declared a nested generic type (CS0305): {result.Status} / {result.Detail}");
    }

    [Theory]
    // Bodies whose referenced constrained generic (Nullable<T> needs struct;
    // Exception members need the type constraint) only binds when the skeleton
    // restates the method's where clause — otherwise CS0453 / CS1061.
    [InlineData("WrapNullable")]
    [InlineData("DescribeException")]
    public void SkeletonRestatesGenericConstraints(string method)
    {
        var result = FidelityCheck.Evaluate(
                typeof(ConstraintFixture).Assembly.Location,
                type => type == "ILInspector.Decompiler.Tests.ConstraintFixture",
                candidate => candidate.Method == method)
            .Single();

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton dropped the generic constraint on {method}: {result.Status} / {result.Detail}");
    }

    [Fact]
    public void SkeletonRetainsCrossAssemblyBaseContext()
    {
        const string typeName =
            "ILInspector.Decompiler.Tests.CrossAssemblyCompileBackFixture";
        var results = FidelityCheck.Evaluate(
            typeof(CrossAssemblyCompileBackFixture).Assembly.Location,
            type => type == typeName);

        Assert.Equal(7, results.Count);
        Assert.All(
            results,
            result => Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{typeName}.{result.Method} did not retain its external base context: "
                + $"{result.Status} / {result.Detail}"));
    }

    [Fact]
    public void SkeletonRetainsCrossAssemblyBaseForAccessorOnlySelection()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(CrossAssemblyAccessorCompileBackFixture).Assembly.Location,
            type => type ==
                "ILInspector.Decompiler.Tests.CrossAssemblyAccessorCompileBackFixture",
            method => method.Method == "get_Value"));

        Assert.Equal(
            FidelityCheck.CompileBackStatus.Exact,
            result.Status);
    }

    [Fact]
    public void SkeletonKeepsExtensionMethodOnItsDeclaringType()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(CrossAssemblyCompileBackExtensions).Assembly.Location,
            type => type ==
                "ILInspector.Decompiler.Tests.CrossAssemblyCompileBackExtensions",
            method => method.Method == "Twice"));

        Assert.False(
            result.Status is FidelityCheck.CompileBackStatus.RecompileFail
                or FidelityCheck.CompileBackStatus.ContextFail,
            $"Extension target used its receiver's base type: "
                + $"{result.Status} / {result.Detail}");
    }

    [Fact]
    public void SkeletonOmitsUnconstructibleExternalBaseForPlainMethod()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            typeof(CrossAssemblyNeedsArgumentCompileBackFixture).Assembly.Location,
            type => type ==
                "ILInspector.Decompiler.Tests.CrossAssemblyNeedsArgumentCompileBackFixture",
            method => method.Method == "Sum"));

        Assert.Equal(
            FidelityCheck.CompileBackStatus.Exact,
            result.Status);
    }

    [Fact]
    public void SkeletonDoesNotSubstituteSameNamedBaseFromWrongAssembly()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-check-base-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string goodPath = Path.Combine(root, "Good.dll");
        string wrongPath = Path.Combine(root, "Wrong.dll");
        string targetPath = Path.Combine(root, "IdentityTarget.dll");

        try
        {
            EmitLibrary(
                goodPath,
                "Good",
                """
                namespace N;
                public class Base
                {
                    public virtual int M() => 1;
                }
                """);
            EmitLibrary(
                wrongPath,
                "Wrong",
                """
                namespace N;
                public class Base
                {
                    public int Value => 2;
                    public virtual int M() => 2;
                }
                """);

            MetadataReference good =
                MetadataReference.CreateFromFile(goodPath);
            MetadataReference wrong =
                MetadataReference.CreateFromFile(
                    wrongPath,
                    new MetadataReferenceProperties(
                        MetadataImageKind.Assembly,
                        aliases:
                            System.Collections.Immutable.ImmutableArray
                                .Create("wrong")));
            EmitLibrary(
                targetPath,
                "IdentityTarget",
                """
                extern alias wrong;
                namespace IdentityTarget;
                public sealed class Derived : N.Base
                {
                    public override int M() => 42;
                    public static int TouchWrong(wrong::N.Base value) => value.Value;
                }
                """,
                [good, wrong]);

            File.Delete(goodPath);
            var result = Assert.Single(FidelityCheck.Evaluate(
                targetPath,
                type => type == "IdentityTarget.Derived",
                method => method.Method == "M"));

            Assert.Equal(
                FidelityCheck.CompileBackStatus.RecompileFail,
                result.Status);
            Assert.Contains("CS0115", result.Detail);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static void EmitLibrary(
        string path,
        string assemblyName,
        string source,
        IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        List<MetadataReference> references = (AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        if (additionalReferences is not null)
            references.AddRange(additionalReferences);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        using FileStream stream = File.Create(path);
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(
                    diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error)));
    }

    static string CreateAssemblyWithDuplicateUnrelatedType()
    {
        byte[] bytes = File.ReadAllBytes(typeof(SkeletonEmitFixture).Assembly.Location);
        byte[] original = "WholeModuleHazardBravo\0"u8.ToArray();
        byte[] replacement = "WholeModuleHazardAlpha\0"u8.ToArray();
        Assert.Equal(original.Length, replacement.Length);

        int replacements = 0;
        int searchStart = 0;
        while (bytes.AsSpan(searchStart).IndexOf(original) is var relative && relative >= 0)
        {
            int match = searchStart + relative;
            replacement.CopyTo(bytes, match);
            replacements++;
            searchStart = match + original.Length;
        }
        Assert.True(replacements > 0, "Expected the unrelated hazard type name in the assembly metadata.");

        string path = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-check-whole-module-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

public sealed class WholeModuleHazardAlpha;
public sealed class WholeModuleHazardBravo;
