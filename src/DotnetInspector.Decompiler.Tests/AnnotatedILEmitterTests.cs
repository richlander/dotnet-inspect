using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

public class AnnotatedILEmitterTests
{
    // --- Raw depth ---

    [Fact]
    public void Raw_Add_ContainsExpectedInstructions()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Raw);

        Assert.Contains("IL_0000:", output);
        Assert.Contains("ldarg.0", output);
        Assert.Contains("ldarg.1", output);
        Assert.Contains("add", output);
        Assert.Contains("ret", output);
    }

    [Fact]
    public void Raw_Add_HasNoStackAnnotations()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Raw);

        Assert.DoesNotContain("// stack:", output);
        Assert.DoesNotContain("// [", output);
    }

    [Fact]
    public void Raw_LoopSum_ContainsBranchTargets()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum), ILAnnotationDepth.Raw);

        Assert.Contains("IL_", output);
        // Should have at least one branch instruction
        Assert.True(output.Contains("br.s") || output.Contains("br ") || output.Contains("blt") || output.Contains("bge"),
            $"Expected branch instruction in:\n{output}");
    }

    // --- Typed depth ---

    [Fact]
    public void Typed_Add_HasStackAnnotations()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Typed);

        // Should have stack type annotations
        Assert.Contains("// stack:", output);
    }

    [Fact]
    public void Typed_Add_ShowsInt32StackTypes()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Typed);

        // After loading int args, stack should show Int32
        Assert.Contains("Int32", output);
    }

    [Fact]
    public void Typed_HasLocalsHeader()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum), ILAnnotationDepth.Typed);

        // Should have a locals or parameters header
        Assert.True(output.Contains("// Parameters:") || output.Contains("// Locals:"),
            $"Expected header in:\n{output}");
    }

    // --- Structured depth ---

    [Fact]
    public void Structured_Add_HasBlockLabels()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Structured);

        Assert.Contains("Block_0:", output);
    }

    [Fact]
    public void Structured_LoopSum_HasMultipleBlocks()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum), ILAnnotationDepth.Structured);

        Assert.Contains("Block_0:", output);
        Assert.Contains("Block_1:", output);
    }

    [Fact]
    public void Structured_HasMethodHeader()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Structured);

        Assert.Contains("// Method IL", output);
        Assert.Contains("MaxStack:", output);
        Assert.Contains("IL size:", output);
    }

    [Fact]
    public void Structured_TryCatch_HasExceptionRegions()
    {
        string output = EmitMethod(nameof(CfgSampleClass.TryCatch), ILAnnotationDepth.Structured);

        Assert.Contains(".try", output);
        Assert.Contains("catch", output);
    }

    [Fact]
    public void Structured_HasStackAnnotations()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Structured);

        // Structured mode includes stack annotations
        Assert.Contains("// [", output);
    }

    [Fact]
    public void Structured_HasIndentation()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), ILAnnotationDepth.Structured);

        // Instructions should be indented under blocks
        var lines = output.Split('\n');
        bool hasIndentedInstruction = lines.Any(l => l.StartsWith("        IL_"));
        Assert.True(hasIndentedInstruction, $"Expected indented instructions in:\n{output}");
    }

    [Fact]
    public void Structured_ExternalPdb_AnnotatesVariableNames()
    {
        var pdbPath = TestAssemblyPdbPath();
        Assert.SkipUnless(File.Exists(pdbPath), $"standalone PDB not found at {pdbPath}");

        string output = EmitMethod(nameof(CfgSampleClass.LoopSum), ILAnnotationDepth.Structured, pdbPath);

        Assert.Matches(@"//\s+Parameters: .* n", output);
        Assert.Contains("arg: n", output);
        Assert.Contains("local: sum", output);
        Assert.Contains("local: i", output);
    }

    // --- Token resolution ---

    [Fact]
    public void Raw_TryCatch_ResolvesMethodNames()
    {
        string output = EmitMethod(nameof(CfgSampleClass.TryCatch), ILAnnotationDepth.Raw);

        // Should have resolved method names, not raw tokens
        Assert.DoesNotContain("method:0x", output);
    }

    [Fact]
    public void Raw_CallExpression_ShowsQualifiedName()
    {
        string output = EmitMethod(nameof(CfgSampleClass.TryCatch), ILAnnotationDepth.Raw);

        // Should have :: separator for qualified method names
        Assert.Contains("::", output);
    }

    // --- Default depth ---

    [Fact]
    public void DefaultDepth_IsTyped()
    {
        string output = EmitMethodDefault(nameof(CfgSampleClass.Add));

        // Default should be Typed (has stack annotations but no block labels)
        Assert.Contains("// stack:", output);
    }

    // --- All three depths produce output ---

    [Theory]
    [InlineData(ILAnnotationDepth.Raw)]
    [InlineData(ILAnnotationDepth.Typed)]
    [InlineData(ILAnnotationDepth.Structured)]
    public void AllDepths_ProduceNonEmptyOutput(ILAnnotationDepth depth)
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), depth);
        Assert.NotEmpty(output);
    }

    [Theory]
    [InlineData(ILAnnotationDepth.Raw)]
    [InlineData(ILAnnotationDepth.Typed)]
    [InlineData(ILAnnotationDepth.Structured)]
    public void AllDepths_ContainILOffsets(ILAnnotationDepth depth)
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add), depth);
        Assert.Contains("IL_0000:", output);
    }

    // --- Platform stress test ---

    [Theory]
    [InlineData(ILAnnotationDepth.Raw)]
    [InlineData(ILAnnotationDepth.Typed)]
    [InlineData(ILAnnotationDepth.Structured)]
    public void PlatformAssembly_EmitAllMethods_NoCrashes(ILAnnotationDepth depth)
    {
        var assembly = typeof(object).Assembly;
        var path = assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int totalMethods = 0;
        int emitted = 0;
        List<string> failures = [];

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                totalMethods++;

                try
                {
                    var context = MethodBodyContext.Create(peReader, reader, method);
                    if (context is null) continue;

                    string output = AnnotatedILEmitter.Emit(context, depth);
                    Assert.NotEmpty(output);
                    emitted++;
                }
                catch (Exception ex)
                {
                    string typeName = reader.GetString(typeDef.Name);
                    string methodName = reader.GetString(method.Name);
                    failures.Add($"{typeName}::{methodName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Assert.True(totalMethods > 1000);
        Assert.True(emitted > 1000);

        double failureRate = (double)failures.Count / totalMethods;
        Assert.True(failureRate < 0.02,
            $"Emit ({depth}) failed for {failures.Count}/{totalMethods} ({failureRate:P1}):\n" +
            string.Join("\n", failures.Take(10)));
    }

    // --- Helpers ---

    static string EmitMethod(string methodName, ILAnnotationDepth depth, string? externalPdbPath = null)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var stream = File.OpenRead(assemblyPath);
        var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName,
            externalPdbPath: externalPdbPath);
        Assert.NotNull(context);
        return AnnotatedILEmitter.Emit(context, depth);
    }

    static string TestAssemblyPdbPath() =>
        Path.ChangeExtension(typeof(CfgSampleClass).Assembly.Location, ".pdb");

    static string EmitMethodDefault(string methodName)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var stream = File.OpenRead(assemblyPath);
        var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);
        Assert.NotNull(context);
        return AnnotatedILEmitter.Emit(context);
    }
}
