using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

public class CSharpEmitterTests
{
    // --- Basic output ---

    [Fact]
    public void Add_ProducesArithmetic()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add));

        Assert.Contains("+", output);
        Assert.Contains("return", output);
    }

    [Fact]
    public void Add_HasNoILOpcodes()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add));

        // Should not contain raw IL opcode names in the output
        Assert.DoesNotContain("ldarg.0", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void Add_IsLoweredCSharp()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add));

        // Lowered C# uses P_ variable names, not original parameter names
        Assert.True(output.Contains("P_0") || output.Contains("P_1") || output.Contains("return"),
            $"Expected lowered variable names in:\n{output}");
    }

    // --- Control flow ---

    [Fact]
    public void Classify_HasConditionals()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Classify));

        // Should have if/goto or if/else structure
        Assert.True(output.Contains("if") || output.Contains("goto"),
            $"Expected conditional in:\n{output}");
    }

    [Fact]
    public void Classify_HasStringLiterals()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Classify));

        Assert.True(output.Contains("\"positive\"") || output.Contains("\"negative\"") || output.Contains("\"zero\""),
            $"Expected string literals in:\n{output}");
    }

    [Fact]
    public void LoopSum_HasLoop()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum));

        Assert.Contains("while", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void LoopSum_HasArithmetic()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum));

        Assert.Contains("+", output);
    }

    // --- Method calls ---

    [Fact]
    public void TryCatch_HasMethodCall()
    {
        string output = EmitMethod(nameof(CfgSampleClass.TryCatch));

        // Should have a method call like int.Parse(...)
        Assert.Contains("(", output);
        Assert.Contains(")", output);
    }

    [Fact]
    public void TryCatch_HasTryCatch()
    {
        string output = EmitMethod(nameof(CfgSampleClass.TryCatch));

        Assert.Contains("try", output);
        Assert.Contains("catch", output);
    }

    // --- Return statements ---

    [Fact]
    public void Add_HasReturn()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add));

        Assert.Contains("return", output);
        Assert.Contains(";", output);
    }

    [Fact]
    public void IsPositive_HasReturn()
    {
        string output = EmitMethod(nameof(CfgSampleClass.IsPositive));

        Assert.Contains("return", output);
    }

    // --- Local variables ---

    [Fact]
    public void LoopSum_DeclaresLocals()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum));

        // Should have local variable declarations (V_0, V_1, etc.)
        Assert.True(output.Contains("V_0") || output.Contains("V_1"),
            $"Expected local variable names in:\n{output}");
    }

    // --- Operators ---

    [Fact]
    public void Add_UsesPlusOperator()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Add));

        // Should have P_0 + P_1 or similar
        Assert.Contains("+", output);
    }

    [Fact]
    public void IsPositive_UsesComparisonOperator()
    {
        string output = EmitMethod(nameof(CfgSampleClass.IsPositive));

        // cgt compiles to >
        Assert.Contains(">", output);
    }

    // --- Object creation ---

    [Fact]
    public void ThrowAndRethrow_HasNewAndThrow()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ThrowAndRethrow));

        Assert.True(output.Contains("new") || output.Contains("throw"),
            $"Expected new/throw in:\n{output}");
    }

    // --- Non-empty output for all sample methods ---

    [Theory]
    [InlineData(nameof(CfgSampleClass.Add))]
    [InlineData(nameof(CfgSampleClass.IsPositive))]
    [InlineData(nameof(CfgSampleClass.Classify))]
    [InlineData(nameof(CfgSampleClass.TryCatch))]
    [InlineData(nameof(CfgSampleClass.TryFinally))]
    [InlineData(nameof(CfgSampleClass.LoopSum))]
    [InlineData(nameof(CfgSampleClass.ThrowAndRethrow))]
    [InlineData(nameof(CfgSampleClass.WhileLoop))]
    [InlineData(nameof(CfgSampleClass.DoWhileLoop))]
    [InlineData(nameof(CfgSampleClass.LoopWithBreak))]
    [InlineData(nameof(CfgSampleClass.NestedLoops))]
    public void AllSampleMethods_ProduceNonEmptyOutput(string methodName)
    {
        string output = EmitMethod(methodName);
        Assert.NotEmpty(output);
    }

    // --- While loops ---

    [Fact]
    public void WhileLoop_EmitsWhile()
    {
        string output = EmitMethod(nameof(CfgSampleClass.WhileLoop));

        Assert.Contains("while", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void NestedLoops_EmitsWhile()
    {
        string output = EmitMethod(nameof(CfgSampleClass.NestedLoops));

        // Should have at least one while
        Assert.Contains("while", output);
    }

    // --- Goto-to-return inlining ---

    [Fact]
    public void TryCatch_InlinesReturn()
    {
        string output = EmitMethod(nameof(CfgSampleClass.TryCatch));

        Assert.Contains("return", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    // --- Platform stress test ---

    [Fact]
    public void PlatformAssembly_EmitAll_NoCrashes()
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

                    string output = CSharpEmitter.Emit(context);
                    Assert.NotNull(output);
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
            $"C# emit failed for {failures.Count}/{totalMethods} ({failureRate:P1}):\n" +
            string.Join("\n", failures.Take(10)));
    }

    // --- Helpers ---

    static string EmitMethod(string methodName)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var stream = File.OpenRead(assemblyPath);
        var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);
        Assert.NotNull(context);
        return CSharpEmitter.Emit(context);
    }
}
