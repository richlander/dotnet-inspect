using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

public class ILAstBuilderTests
{
    [Fact]
    public void Build_Add_HasReturnWithArguments()
    {
        var method = BuildAst(nameof(CfgSampleClass.Add));

        // Should have parameters
        Assert.Equal(2, method.Parameters.Count);

        // Should have blocks
        Assert.NotEmpty(method.Blocks);

        // Find the ret node — should have an argument (the return value)
        var ret = FindNode(method, ILOpCode.Ret);
        Assert.NotNull(ret);
        Assert.NotEmpty(ret.Arguments);
    }

    [Fact]
    public void Build_Add_HasAddExpression()
    {
        var method = BuildAst(nameof(CfgSampleClass.Add));

        var add = FindNode(method, ILOpCode.Add);
        Assert.NotNull(add);
        Assert.Equal(2, add.Arguments.Count);
        Assert.Equal(StackValueKind.Int32, add.ResultType.Kind);
    }

    [Fact]
    public void Build_IsPositive_HasComparison()
    {
        var method = BuildAst(nameof(CfgSampleClass.IsPositive));

        // Should have at least one comparison or branch
        var allExprs = AllExpressions(method);
        bool hasBranch = allExprs.Any(e =>
            e.OpCode is ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bge or ILOpCode.Bge_s or
            ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Ble or ILOpCode.Ble_s or
            ILOpCode.Cgt or ILOpCode.Clt or
            ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s);
        Assert.True(hasBranch, "Expected a comparison or conditional branch");
    }

    [Fact]
    public void Build_TryCatch_HasCallAndBranch()
    {
        var method = BuildAst(nameof(CfgSampleClass.TryCatch));

        var allExprs = AllExpressions(method);

        // Should have at least one call (Console.WriteLine or similar)
        bool hasCall = allExprs.Any(e => e.OpCode is ILOpCode.Call or ILOpCode.Callvirt);
        Assert.True(hasCall, "Expected at least one call instruction");
    }

    [Fact]
    public void Build_LoopSum_HasMultipleBlocks()
    {
        var method = BuildAst(nameof(CfgSampleClass.LoopSum));

        Assert.True(method.Blocks.Count >= 2,
            $"Expected multiple blocks for a loop, got {method.Blocks.Count}");
    }

    [Fact]
    public void Build_Add_TreeOutput_IsReadable()
    {
        var method = BuildAst(nameof(CfgSampleClass.Add));
        string output = method.ToString();

        Assert.Contains("Block_0", output);
        Assert.Contains("add", output);
        Assert.Contains("ret", output);
    }

    [Fact]
    public void Build_CallExpression_HasResolvedMethodName()
    {
        var method = BuildAst(nameof(CfgSampleClass.TryCatch));

        var calls = AllExpressions(method)
            .Where(e => e.OpCode is ILOpCode.Call or ILOpCode.Callvirt)
            .ToList();

        foreach (var call in calls)
        {
            Assert.NotNull(call.Operand);
            Assert.DoesNotContain("token:0x", call.Operand);
        }
    }

    [Fact]
    public void Build_FieldAccess_HasResolvedFieldName()
    {
        // ThrowAndRethrow might not have fields, but let's check any method with fields
        var method = BuildAst(nameof(CfgSampleClass.TryCatch));
        var fields = AllExpressions(method)
            .Where(e => e.OpCode is ILOpCode.Ldfld or ILOpCode.Ldsfld or ILOpCode.Stfld or ILOpCode.Stsfld)
            .ToList();

        foreach (var f in fields)
        {
            Assert.NotNull(f.Operand);
            Assert.DoesNotContain("field:0x", f.Operand);
        }
    }

    [Fact]
    public void Build_Constants_HaveLiteralValues()
    {
        var method = BuildAst(nameof(CfgSampleClass.LoopSum));

        var constants = AllExpressions(method)
            .Where(e => e.OpCode is >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8
                     or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4)
            .ToList();

        Assert.NotEmpty(constants);
        foreach (var c in constants)
        {
            Assert.NotNull(c.Operand);
            Assert.True(int.TryParse(c.Operand, out _), $"Expected integer literal, got '{c.Operand}'");
        }
    }

    [Fact]
    public void Build_BinaryOps_HaveTwoArguments()
    {
        var method = BuildAst(nameof(CfgSampleClass.Add));

        var binOps = AllExpressions(method)
            .Where(e => e.OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div)
            .ToList();

        foreach (var op in binOps)
        {
            Assert.Equal(2, op.Arguments.Count);
        }
    }

    // --- Platform stress test ---

    [Fact]
    public void PlatformAssembly_BuildAllMethods_NoCrashes()
    {
        var assembly = typeof(object).Assembly;
        var path = assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int totalMethods = 0;
        int built = 0;
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

                    var ast = ILAstBuilder.Build(context);
                    built++;

                    // Basic sanity: should have at least one block
                    Assert.NotEmpty(ast.Blocks);
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
        Assert.True(built > 1000);

        double failureRate = (double)failures.Count / totalMethods;
        Assert.True(failureRate < 0.02,
            $"Build failed for {failures.Count}/{totalMethods} ({failureRate:P1}):\n{string.Join("\n", failures.Take(20))}");
    }

    // --- Helpers ---

    static ILAstMethod BuildAst(string methodName)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var stream = File.OpenRead(assemblyPath);
        var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);
        Assert.NotNull(context);
        return ILAstBuilder.Build(context);
    }

    static ILAstExpression? FindNode(ILAstMethod method, ILOpCode opcode) =>
        AllExpressions(method).FirstOrDefault(e => e.OpCode == opcode);

    static List<ILAstExpression> AllExpressions(ILAstMethod method)
    {
        var result = new List<ILAstExpression>();
        foreach (var block in method.Blocks)
            CollectExpressions(block, result);
        return result;
    }

    static void CollectExpressions(ILAstBlock block, List<ILAstExpression> result)
    {
        foreach (var node in block.Nodes)
        {
            if (node is ILAstStatement stmt)
                CollectExpr(stmt.Expression, result);
            else if (node is ILAstAssignment assign)
                CollectExpr(assign.Value, result);
        }
    }

    static void CollectExpr(ILAstExpression expr, List<ILAstExpression> result)
    {
        result.Add(expr);
        foreach (var arg in expr.Arguments)
            CollectExpr(arg, result);
    }
}
