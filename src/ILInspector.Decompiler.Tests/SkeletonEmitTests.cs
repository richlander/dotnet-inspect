using ILInspector.DecompilerHarness;

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

        var duplicateAssembly = CreateAssemblyWithDuplicateUnrelatedType();
        try
        {
            var duplicateType = Assert.Single(FidelityCheck.Evaluate(
                duplicateAssembly.Path,
                type => type == FixtureType,
                method => method.Method == "Sum"));

            Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, duplicateType.Status);
            Assert.Contains("CS0101", duplicateType.Detail);
        }
        finally
        {
            Directory.Delete(duplicateAssembly.Directory, recursive: true);
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

    static (string Directory, string Path) CreateAssemblyWithDuplicateUnrelatedType()
    {
        string sourcePath = typeof(SkeletonEmitFixture).Assembly.Location;
        string sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("The fixture assembly has no containing directory.");
        byte[] bytes = File.ReadAllBytes(sourcePath);
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

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-check-whole-module-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string targetFileName = Path.GetFileName(sourcePath);
        string path = Path.Combine(directory, targetFileName);
        File.WriteAllBytes(path, bytes);

        // FidelityCheck resolves sibling DLLs from the target directory. Mirror
        // the fixture's real output closure inside a private temp directory so
        // unrelated /tmp DLLs cannot poison this duplicate-type canary.
        foreach (string sibling in Directory.EnumerateFiles(sourceDirectory, "*.dll"))
        {
            if (Path.GetFileName(sibling).Equals(targetFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(sibling, Path.Combine(directory, Path.GetFileName(sibling)));
        }

        string sourceDeps = Path.ChangeExtension(sourcePath, ".deps.json");
        if (File.Exists(sourceDeps))
        {
            File.Copy(
                sourceDeps,
                Path.Combine(directory, Path.GetFileName(sourceDeps)));
        }

        return (directory, path);
    }
}

public sealed class WholeModuleHazardAlpha;
public sealed class WholeModuleHazardBravo;
