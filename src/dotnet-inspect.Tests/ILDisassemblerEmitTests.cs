using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// End-to-end roundtrip tests: emit exact IL via Reflection.Emit,
/// save to disk, then verify our disassembler reads it back correctly.
/// This eliminates compiler optimization surprises.
/// </summary>
public class ILDisassemblerEmitTests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"il-emit-{Guid.NewGuid():N}");

    public ILDisassemblerEmitTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Emit_SimpleReturn_RoundtripsCorrectly()
    {
        // IL: ldc.i4 42 / ret
        var instructions = EmitAndDisassemble("SimpleReturn", "ReturnFortyTwo", il =>
        {
            il.Emit(OpCodes.Ldc_I4, 42);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(int));

        // Emit may optimize ldc.i4 to ldc.i4.s for small values
        Assert.Equal(2, instructions.Count);
        Assert.Equal(0, instructions[0].Offset);
        Assert.True(instructions[0].OpCodeName is "ldc.i4" or "ldc.i4.s");
        Assert.Equal("42", instructions[0].Operand);
        Assert.Equal("ret", instructions[1].OpCodeName);
    }

    [Fact]
    public void Emit_BranchTarget_OffsetsResolveCorrectly()
    {
        // IL: ldc.i4.0 / brtrue.s SKIP / ldc.i4.1 / ret / SKIP: ldc.i4.2 / ret
        var instructions = EmitAndDisassemble("Branch", "BranchTest", il =>
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Brtrue_S, skipLabel);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skipLabel);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(int));

        Assert.Equal("ldc.i4.0", instructions[0].OpCodeName);
        Assert.Equal("brtrue.s", instructions[1].OpCodeName);
        // Branch target should point to ldc.i4.2
        var target = instructions.First(i => i.OpCodeName == "ldc.i4.2");
        Assert.Equal($"IL_{target.Offset:X4}", instructions[1].Operand);
    }

    [Fact]
    public void Emit_SwitchTable_DecodesAllTargets()
    {
        // IL: ldarg.0 / switch(L0, L1, L2) / ldc.i4.m1 / ret / L0: ldc.i4.0 / ret / ...
        var instructions = EmitAndDisassemble("Switch", "SwitchTest", il =>
        {
            var labels = new[] { il.DefineLabel(), il.DefineLabel(), il.DefineLabel() };
            var defaultLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Switch, labels);
            il.MarkLabel(defaultLabel);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Ret);

            for (int i = 0; i < 3; i++)
            {
                il.MarkLabel(labels[i]);
                il.Emit(OpCodes.Ldc_I4, i * 10);
                il.Emit(OpCodes.Ret);
            }
        }, returnType: typeof(int), parameterTypes: [typeof(int)]);

        var switchInstr = instructions.First(i => i.OpCodeName == "switch");
        Assert.NotNull(switchInstr.Operand);
        Assert.StartsWith("(", switchInstr.Operand);
        Assert.EndsWith(")", switchInstr.Operand);
        // Count IL_ targets — should have exactly 3
        int targetCount = switchInstr.Operand.Split("IL_").Length - 1;
        Assert.Equal(3, targetCount);
    }

    [Fact]
    public void Emit_StringOperand_ResolvesUserString()
    {
        var instructions = EmitAndDisassemble("StringOp", "LoadString", il =>
        {
            il.Emit(OpCodes.Ldstr, "hello world");
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(string));

        Assert.Equal("ldstr", instructions[0].OpCodeName);
        Assert.Equal("\"hello world\"", instructions[0].Operand);
    }

    [Fact]
    public void Emit_LongConstant_DecodesI8()
    {
        var instructions = EmitAndDisassemble("LongConst", "LoadLong", il =>
        {
            il.Emit(OpCodes.Ldc_I8, 0x7FFFFFFFFFFFFFFFL);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(long));

        Assert.Equal("ldc.i8", instructions[0].OpCodeName);
        Assert.Equal(long.MaxValue.ToString(), instructions[0].Operand);
    }

    [Fact]
    public void Emit_DoubleConstant_DecodesR8()
    {
        var instructions = EmitAndDisassemble("DoubleConst", "LoadDouble", il =>
        {
            il.Emit(OpCodes.Ldc_R8, 1.23456789);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(double));

        Assert.Equal("ldc.r8", instructions[0].OpCodeName);
        Assert.True(double.TryParse(instructions[0].Operand, out var val));
        Assert.Equal(1.23456789, val, 8);
    }

    [Fact]
    public void Emit_TwoBytePrefixOpcode_DecodesCorrectly()
    {
        // ceq is 0xFE01 (2-byte opcode)
        var instructions = EmitAndDisassemble("TwoByte", "CompareEq", il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(bool), parameterTypes: [typeof(int), typeof(int)]);

        Assert.Equal("ldarg.0", instructions[0].OpCodeName);
        Assert.Equal("ldarg.1", instructions[1].OpCodeName);
        Assert.Equal("ceq", instructions[2].OpCodeName);
        Assert.Equal("ret", instructions[3].OpCodeName);
        // ceq is at offset 2 (ldarg.0=1byte, ldarg.1=1byte)
        Assert.Equal(2, instructions[2].Offset);
    }

    [Fact]
    public void Emit_LocalVariables_DecodesStlocLdloc()
    {
        var instructions = EmitAndDisassemble("Locals", "UseLocals", il =>
        {
            il.DeclareLocal(typeof(int));    // local 0
            il.DeclareLocal(typeof(string)); // local 1

            il.Emit(OpCodes.Ldc_I4, 100);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldstr, "test");
            il.Emit(OpCodes.Stloc_1);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(int));

        Assert.Contains(instructions, i => i.OpCodeName == "stloc.0");
        Assert.Contains(instructions, i => i.OpCodeName == "stloc.1");
        Assert.Contains(instructions, i => i.OpCodeName == "ldloc.0");
    }

    [Fact]
    public void Emit_ShortInlineI_DecodesSByte()
    {
        // ldc.i4.s takes a signed byte operand
        var instructions = EmitAndDisassemble("ShortI", "LoadSmallInt", il =>
        {
            il.Emit(OpCodes.Ldc_I4_S, (sbyte)-5);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(int));

        Assert.Equal("ldc.i4.s", instructions[0].OpCodeName);
        Assert.Equal("-5", instructions[0].Operand);
    }

    [Fact]
    public void Emit_MethodCall_ResolvesTokenToMethodName()
    {
        var instructions = EmitAndDisassemble("MethodCall", "CallConsoleWriteLine", il =>
        {
            il.Emit(OpCodes.Ldstr, "emit test");
            il.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
            il.Emit(OpCodes.Ret);
        });

        var call = instructions.First(i => i.OpCodeName == "call");
        Assert.NotNull(call.Operand);
        Assert.Contains("WriteLine", call.Operand);
    }

    [Fact]
    public void Emit_FieldAccess_ResolvesFieldToken()
    {
        var instructions = EmitAndDisassemble("FieldAccess", "LoadField", il =>
        {
            il.Emit(OpCodes.Ldsfld, typeof(string).GetField("Empty")!);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(string));

        var ldsfld = instructions.First(i => i.OpCodeName == "ldsfld");
        Assert.NotNull(ldsfld.Operand);
        Assert.Contains("Empty", ldsfld.Operand);
    }

    [Fact]
    public void Emit_NewObj_ResolvesConstructorToken()
    {
        var instructions = EmitAndDisassemble("NewObj", "CreateObject", il =>
        {
            il.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(object));

        var newobj = instructions.First(i => i.OpCodeName == "newobj");
        Assert.NotNull(newobj.Operand);
        Assert.Contains(".ctor", newobj.Operand);
    }

    [Fact]
    public void Emit_AllShortFormLdcI4_DecodeCorrectly()
    {
        // ldc.i4.0 through ldc.i4.8, ldc.i4.m1 — all InlineNone, no operand
        var instructions = EmitAndDisassemble("ShortLdc", "AllShortConstants", il =>
        {
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_5);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_6);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_7);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Ret);
        }, returnType: typeof(int));

        string[] expected = ["ldc.i4.m1", "ldc.i4.0", "ldc.i4.1", "ldc.i4.2",
            "ldc.i4.3", "ldc.i4.4", "ldc.i4.5", "ldc.i4.6", "ldc.i4.7", "ldc.i4.8"];

        var ldcOps = instructions.Where(i => i.OpCodeName.StartsWith("ldc.i4")).ToList();
        Assert.Equal(expected.Length, ldcOps.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], ldcOps[i].OpCodeName);
            // Short form constants have no operand
            Assert.Null(ldcOps[i].Operand);
        }
    }

    [Fact]
    public void Emit_ConsecutiveOffsets_AreContiguous()
    {
        // Verify that emitted offsets match expected byte sizes
        var instructions = EmitAndDisassemble("Offsets", "VerifyOffsets", il =>
        {
            il.Emit(OpCodes.Nop);          // 1 byte, offset 0
            il.Emit(OpCodes.Ldc_I4_1);     // 1 byte, offset 1
            il.Emit(OpCodes.Ldc_I4_S, (sbyte)10); // 2 bytes, offset 2
            il.Emit(OpCodes.Ldc_I4, 1000); // 5 bytes, offset 4
            il.Emit(OpCodes.Pop);           // 1 byte, offset 9
            il.Emit(OpCodes.Pop);           // 1 byte, offset 10
            il.Emit(OpCodes.Pop);           // 1 byte, offset 11
            il.Emit(OpCodes.Ret);           // 1 byte, offset 12
        }, returnType: typeof(void));

        Assert.Equal(0, instructions[0].Offset);  // nop
        Assert.Equal(1, instructions[1].Offset);  // ldc.i4.1
        Assert.Equal(2, instructions[2].Offset);  // ldc.i4.s
        Assert.Equal(4, instructions[3].Offset);  // ldc.i4
        Assert.Equal(9, instructions[4].Offset);  // pop
        Assert.Equal(12, instructions[7].Offset);  // ret
    }

    // --- Helper: emit a method, save to disk, disassemble ---

    List<ILInstruction> EmitAndDisassemble(
        string typeName,
        string methodName,
        Action<ILGenerator> emitBody,
        Type? returnType = null,
        Type[]? parameterTypes = null)
    {
        returnType ??= typeof(void);
        parameterTypes ??= Type.EmptyTypes;

        var assemblyName = new AssemblyName($"EmitTest_{typeName}");
        var assemblyBuilder = new PersistedAssemblyBuilder(
            assemblyName, typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        var typeBuilder = moduleBuilder.DefineType(
            $"EmitTest.{typeName}",
            TypeAttributes.Public | TypeAttributes.Class);

        var methodBuilder = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            parameterTypes);

        var il = methodBuilder.GetILGenerator();
        emitBody(il);

        typeBuilder.CreateType();

        var dllPath = Path.Combine(_tempDir, $"{typeName}.dll");
        assemblyBuilder.Save(dllPath);

        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var result = ILDisassembler.DisassembleMethod(
            peReader, $"EmitTest.{typeName}", methodName);

        Assert.NotNull(result);
        return result;
    }
}
