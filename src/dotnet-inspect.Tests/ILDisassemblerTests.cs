using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for IL disassembly of method bodies.
/// </summary>
public class ILDisassemblerTests
{
    [Fact]
    public void Disassemble_SimpleMethod_ReturnsInstructions()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.Add));

        Assert.NotNull(instructions);
        Assert.NotEmpty(instructions);

        // Should end with ret
        Assert.Equal("ret", instructions[^1].OpCodeName);
    }

    [Fact]
    public void Disassemble_SimpleMethod_HasCorrectOffsets()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.Add));

        Assert.NotNull(instructions);
        // First instruction starts at offset 0
        Assert.Equal(0, instructions[0].Offset);
        // Offsets should be monotonically increasing
        for (int i = 1; i < instructions.Count; i++)
            Assert.True(instructions[i].Offset > instructions[i - 1].Offset);
    }

    [Fact]
    public void Disassemble_MethodWithStringLiteral_ResolvesString()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.GetGreeting));

        Assert.NotNull(instructions);
        var ldstr = instructions.FirstOrDefault(i => i.OpCodeName == "ldstr");
        Assert.NotNull(ldstr);
        Assert.Contains("hello", ldstr.Operand);
    }

    [Fact]
    public void Disassemble_MethodWithMethodCall_ResolvesMethodToken()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.CallToString));

        Assert.NotNull(instructions);
        var call = instructions.FirstOrDefault(i => i.OpCodeName is "call" or "callvirt");
        Assert.NotNull(call);
        Assert.Contains("ToString", call.Operand);
    }

    [Fact]
    public void Disassemble_MethodWithNewObject_ResolvesConstructor()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.CreateList));

        Assert.NotNull(instructions);
        var newobj = instructions.FirstOrDefault(i => i.OpCodeName == "newobj");
        Assert.NotNull(newobj);
        Assert.Contains(".ctor", newobj.Operand);
    }

    [Fact]
    public void Disassemble_MethodWithBranch_ResolvesBranchTarget()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.Classify));

        Assert.NotNull(instructions);
        var branch = instructions.FirstOrDefault(i => i.OpCodeName.StartsWith("br"));
        Assert.NotNull(branch);
        Assert.NotNull(branch.Operand);
        Assert.StartsWith("IL_", branch.Operand);
    }

    [Fact]
    public void Disassemble_MethodWithFieldAccess_ResolvesField()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.GetValue));

        Assert.NotNull(instructions);
        var ldfld = instructions.FirstOrDefault(i => i.OpCodeName is "ldfld" or "ldsfld");
        Assert.NotNull(ldfld);
        Assert.Contains("_value", ldfld.Operand);
    }

    [Fact]
    public void Disassemble_MethodWithTypeOperand_ResolvesType()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.BoxInt));

        Assert.NotNull(instructions);
        var box = instructions.FirstOrDefault(i => i.OpCodeName == "box");
        Assert.NotNull(box);
        Assert.Contains("System.Int32", box.Operand);
    }

    [Fact]
    public void Disassemble_AbstractMethod_ReturnsNull()
    {
        var instructions = DisassembleTestMethod(
            nameof(ILSampleAbstract.AbstractMethod),
            typeof(ILSampleAbstract));

        Assert.Null(instructions);
    }

    [Fact]
    public void DisassembleMethod_ByName_FindsAndDisassembles()
    {
        var assemblyPath = typeof(ILDisassemblerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var instructions = ILDisassembler.DisassembleMethod(
            peReader,
            "DotnetInspector.Tests.ILSampleClass",
            nameof(ILSampleClass.Add));

        Assert.NotNull(instructions);
        Assert.Equal("ret", instructions[^1].OpCodeName);
    }

    [Fact]
    public void DisassembleMethod_NonexistentType_ReturnsNull()
    {
        var assemblyPath = typeof(ILDisassemblerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var instructions = ILDisassembler.DisassembleMethod(peReader, "NoSuch.Type", "Method");

        Assert.Null(instructions);
    }

    [Fact]
    public void DisassembleMethod_NonexistentMethod_ReturnsNull()
    {
        var assemblyPath = typeof(ILDisassemblerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var instructions = ILDisassembler.DisassembleMethod(
            peReader,
            "DotnetInspector.Tests.ILSampleClass",
            "NonexistentMethod");

        Assert.Null(instructions);
    }

    [Fact]
    public void ILInstruction_ToString_FormatsCorrectly()
    {
        var instruction = new ILInstruction(0x0A, "call", "System.String::Concat");
        Assert.Equal("IL_000A: call         System.String::Concat", instruction.ToString());
    }

    [Fact]
    public void ILInstruction_ToString_NoOperand_OmitsTrailingSpace()
    {
        var instruction = new ILInstruction(0x00, "nop");
        Assert.Equal("IL_0000: nop", instruction.ToString());
    }

    // Helper to disassemble a method from the test assembly's ILSampleClass
    static List<ILInstruction>? DisassembleTestMethod(string methodName, Type? declaringType = null)
    {
        declaringType ??= typeof(ILSampleClass);
        var assemblyPath = declaringType.Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        return ILDisassembler.DisassembleMethod(
            peReader,
            declaringType.FullName!,
            methodName);
    }

    /// <summary>
    /// Disassembles every method in every dotnet-inspect assembly to verify
    /// the decoder handles all real-world IL patterns without crashing.
    /// </summary>
    [Fact]
    public void Disassemble_AllMethodsInProjectAssemblies_NoCrashes()
    {
        var testDir = Path.GetDirectoryName(typeof(ILDisassemblerTests).Assembly.Location)!;
        string[] assemblyNames = [
            "DotnetInspector.Core.dll",
            "DotnetInspector.Metadata.dll",
            "DotnetInspector.Packages.dll",
            "DotnetInspector.Services.dll",
            "dotnet-inspect.dll",
        ];

        int totalMethods = 0;
        int totalInstructions = 0;
        List<string> failures = [];

        foreach (var assemblyName in assemblyNames)
        {
            var path = Path.Combine(testDir, assemblyName);
            if (!File.Exists(path))
                continue;

            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                continue;

            var reader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                string typeName = reader.GetString(typeDef.Name);

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    string methodName = reader.GetString(method.Name);
                    totalMethods++;

                    try
                    {
                        var instructions = ILDisassembler.Disassemble(peReader, reader, method);
                        if (instructions is not null)
                            totalInstructions += instructions.Count;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{assemblyName} {typeName}::{methodName}: {ex.Message}");
                    }
                }
            }
        }

        Assert.True(totalMethods > 100, $"Expected to scan many methods, got {totalMethods}");
        Assert.True(totalInstructions > 1000, $"Expected many instructions, got {totalInstructions}");
        Assert.True(failures.Count == 0,
            $"Disassembly failed for {failures.Count} method(s):\n{string.Join("\n", failures.Take(20))}");
    }
}

/// <summary>
/// Sample class with various IL patterns for testing disassembly.
/// </summary>
public class ILSampleClass
{
    private int _value = 42;

    public static int Add(int a, int b) => a + b;

    public static string GetGreeting() => "hello";

    public static string CallToString(int x) => x.ToString();

    public static List<int> CreateList() => new();

    public static bool IsPositive(int x) => x > 0;

    public static string Classify(int x)
    {
        if (x > 0) return "positive";
        return "non-positive";
    }

    public int GetValue() => _value;

    public static object BoxInt(int x) => x;
}

/// <summary>
/// Abstract class for testing abstract method disassembly (should return null).
/// </summary>
public abstract class ILSampleAbstract
{
    public abstract void AbstractMethod();
}
