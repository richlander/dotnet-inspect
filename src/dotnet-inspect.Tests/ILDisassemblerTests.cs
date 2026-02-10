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
        // Match any branch opcode (br, brtrue, brfalse, beq, bne, bge, bgt, ble, blt, and .s variants)
        var branch = instructions.FirstOrDefault(i =>
            i.OpCodeName.StartsWith("br") || i.OpCodeName.StartsWith("be") ||
            i.OpCodeName.StartsWith("bg") || i.OpCodeName.StartsWith("bl"));
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

    // --- Edge case tests borrowed from ILSpy/runtime test patterns ---

    [Fact]
    public void Disassemble_SwitchStatement_HasSwitchOpcode()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.SwitchCase));
        Assert.NotNull(instructions);

        var switchInstr = instructions.FirstOrDefault(i => i.OpCodeName == "switch");
        Assert.NotNull(switchInstr);
        // Switch operand should list branch targets in parentheses
        Assert.NotNull(switchInstr.Operand);
        Assert.StartsWith("(", switchInstr.Operand);
        Assert.EndsWith(")", switchInstr.Operand);
        // Should have at least 4 targets for cases 0-3
        Assert.True(switchInstr.Operand.Split("IL_").Length > 4,
            $"Expected multiple branch targets, got: {switchInstr.Operand}");
    }

    [Fact]
    public void Disassemble_TryCatch_HasExpectedStructure()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.TryCatch));
        Assert.NotNull(instructions);

        // Should contain call to int.Parse (or Int32::Parse)
        Assert.Contains(instructions, i =>
            i.OpCodeName is "call" or "callvirt" &&
            i.Operand?.Contains("Parse") == true);

        // Should have leave or leave.s (exits try block)
        Assert.Contains(instructions, i =>
            i.OpCodeName is "leave" or "leave.s");
    }

    [Fact]
    public void Disassemble_TryFinally_HasLeaveInstruction()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.TryFinally));
        Assert.NotNull(instructions);

        Assert.Contains(instructions, i =>
            i.OpCodeName is "leave" or "leave.s");

        // Should have endfinally
        Assert.Contains(instructions, i => i.OpCodeName == "endfinally");
    }

    [Fact]
    public void Disassemble_GenericMethodCall_ResolvesMethodSpec()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.CallGeneric));
        Assert.NotNull(instructions);

        // Should have at least one call/callvirt/newobj
        Assert.Contains(instructions, i =>
            i.OpCodeName is "call" or "callvirt" or "newobj" &&
            i.Operand is not null);
    }

    [Fact]
    public void Disassemble_LongConstant_DecodesI8Operand()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.LongConstant));
        Assert.NotNull(instructions);

        // Should contain ldc.i8 with the constant value
        var ldcI8 = instructions.FirstOrDefault(i => i.OpCodeName == "ldc.i8");
        Assert.NotNull(ldcI8);
        Assert.Equal("1311768467463790320", ldcI8.Operand); // 0x123456789ABCDEF0
    }

    [Fact]
    public void Disassemble_DoubleConstant_DecodesR8Operand()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.DoubleConstant));
        Assert.NotNull(instructions);

        var ldcR8 = instructions.FirstOrDefault(i => i.OpCodeName == "ldc.r8");
        Assert.NotNull(ldcR8);
        Assert.NotNull(ldcR8.Operand);
        // Value should parse back to approximately pi
        Assert.True(double.TryParse(ldcR8.Operand, out var val));
        Assert.Equal(3.14159265358979, val, 10);
    }

    [Fact]
    public void Disassemble_FloatConstant_DecodesR4Operand()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.FloatConstant));
        Assert.NotNull(instructions);

        var ldcR4 = instructions.FirstOrDefault(i => i.OpCodeName == "ldc.r4");
        Assert.NotNull(ldcR4);
        Assert.NotNull(ldcR4.Operand);
        Assert.True(float.TryParse(ldcR4.Operand, out var val));
        Assert.Equal(2.718f, val, 2);
    }

    [Fact]
    public void Disassemble_StringWithEscapes_ResolvesUserString()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.StringWithEscapes));
        Assert.NotNull(instructions);

        var ldstr = instructions.FirstOrDefault(i => i.OpCodeName == "ldstr");
        Assert.NotNull(ldstr);
        Assert.NotNull(ldstr.Operand);
        // Should contain escaped characters
        Assert.Contains("\\t", ldstr.Operand);
        Assert.Contains("\\n", ldstr.Operand);
    }

    [Fact]
    public void Disassemble_NestedGenerics_ResolvesComplexTypes()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.NestedGenerics));
        Assert.NotNull(instructions);

        // Should contain a newobj for Dictionary<string, List<int>>
        var newobj = instructions.FirstOrDefault(i => i.OpCodeName == "newobj");
        Assert.NotNull(newobj);
        Assert.NotNull(newobj.Operand);
    }

    [Fact]
    public void Disassemble_CompareEquals_Has2ByteOpcode()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.CompareEquals));
        Assert.NotNull(instructions);

        // ceq is 0xFE01 (2-byte opcode)
        Assert.Contains(instructions, i => i.OpCodeName == "ceq");
    }

    [Fact]
    public void Disassemble_ManyLocals_HandlesLocalVariableAccess()
    {
        var instructions = DisassembleTestMethod(nameof(ILSampleClass.ManyLocals));
        Assert.NotNull(instructions);

        // Should have stloc/stloc.s instructions for storing to locals
        Assert.Contains(instructions, i =>
            i.OpCodeName.StartsWith("stloc"));

        // Should end with ret
        Assert.Equal("ret", instructions[^1].OpCodeName);
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
        if (x < 0) return "negative";
        return "zero";
    }

    public int GetValue() => _value;

    public static object BoxInt(int x) => x;

    // Edge case: switch statement (generates InlineSwitch opcode)
    public static string SwitchCase(int x) => x switch
    {
        0 => "zero",
        1 => "one",
        2 => "two",
        3 => "three",
        _ => "other"
    };

    // Edge case: try/catch (exception handler regions)
    public static int TryCatch(string s)
    {
        try
        {
            return int.Parse(s);
        }
        catch (FormatException)
        {
            return -1;
        }
    }

    // Edge case: try/finally
    public static void TryFinally(Action action)
    {
        try
        {
            action();
        }
        finally
        {
            Console.WriteLine("done");
        }
    }

    // Edge case: generic method (generates MethodSpec token)
    public static T GenericMethod<T>(T value) => value;

    // Edge case: generic method call (callvirt with MethodSpec)
    public static int CallGeneric()
    {
        var list = new List<int> { 1, 2, 3 };
        return list.Count;
    }

    // Edge case: long constant (InlineI8 operand)
    public static long LongConstant() => 0x123456789ABCDEF0L;

    // Edge case: double constant (InlineR operand)
    public static double DoubleConstant() => 3.14159265358979;

    // Edge case: float constant (ShortInlineR operand)
    public static float FloatConstant() => 2.718f;

    // Edge case: ldstr with escapes (special characters in user strings)
    public static string StringWithEscapes() => "hello\tworld\n\"quotes\"";

    // Edge case: nested generics (complex type resolution)
    public static Dictionary<string, List<int>> NestedGenerics()
        => new();

    // Edge case: 2-byte opcode (ceq, cgt, clt are 0xFE prefixed)
    public static bool CompareEquals(int a, int b) => a == b;

    // Edge case: multiple local variables
    public static int ManyLocals()
    {
        int a = 1, b = 2, c = 3, d = 4, e = 5;
        return a + b + c + d + e;
    }
}

/// <summary>
/// Abstract class for testing abstract method disassembly (should return null).
/// </summary>
public abstract class ILSampleAbstract
{
    public abstract void AbstractMethod();
}
