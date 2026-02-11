using System.Reflection.Metadata;
using System.Text;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Emits C# source code from the ILAst representation. Maps IL opcodes to
/// C# operators, method calls, field access, casts, and control flow constructs.
/// </summary>
public static class CSharpEmitter
{
    /// <summary>
    /// Emit C# source for a method.
    /// </summary>
    public static string Emit(MethodBodyContext context)
    {
        var cfg = ControlFlowGraph.Create(context);
        var simResult = StackSimulator.Simulate(context, cfg);
        var ast = ILAstBuilder.Build(context, cfg, simResult);
        var structure = StructuredControlFlow.Analyze(context, cfg);
        return Emit(ast, structure, context.Reader);
    }

    /// <summary>
    /// Emit C# source from pre-computed ILAst and control flow structure.
    /// </summary>
    public static string Emit(ILAstMethod ast, StructuredControlFlow structure, MetadataReader? reader = null)
    {
        var sb = new StringBuilder();
        var emitter = new EmitterContext(ast, structure, sb, reader);
        emitter.EmitMethod();
        return sb.ToString();
    }

    sealed class EmitterContext
    {
        readonly ILAstMethod _ast;
        readonly StructuredControlFlow _structure;
        readonly StringBuilder _sb;
        readonly MetadataReader? _reader;

        // Map block index → ILAstBlock for quick lookup
        readonly Dictionary<int, ILAstBlock> _blockMap;

        // Blocks consumed by structured constructs (don't emit separately)
        readonly HashSet<int> _consumedBlocks;

        // Current block nodes being emitted (for dup resolution)
        List<ILAstNode>? _currentBlockNodes;

        public EmitterContext(ILAstMethod ast, StructuredControlFlow structure, StringBuilder sb, MetadataReader? reader = null)
        {
            _ast = ast;
            _structure = structure;
            _sb = sb;
            _reader = reader;

            _blockMap = [];
            for (int i = 0; i < ast.Blocks.Count; i++)
                _blockMap[i] = ast.Blocks[i];

            _consumedBlocks = [];
        }

        public void EmitMethod()
        {
            // Emit local variable declarations
            if (_ast.Locals.Count > 0)
            {
                foreach (var local in _ast.Locals)
                {
                    string typeName = SimplifyTypeName(local.TypeName ?? "var");
                    _sb.AppendLine($"{typeName} {local.Name};");
                }
                _sb.AppendLine();
            }

            // Emit the structured body
            EmitStructuredBlock(_structure.Root, 0);
        }

        void EmitStructuredBlock(StructuredBlock block, int indent)
        {
            switch (block.Kind)
            {
                case StructuredBlockKind.Sequence:
                    foreach (var child in block.Children)
                        EmitStructuredBlock(child, indent);
                    break;

                case StructuredBlockKind.BasicBlock:
                    EmitBasicBlock(block.BlockIndex, indent);
                    break;

                case StructuredBlockKind.IfThenElse:
                    EmitIfThenElse(block, indent);
                    break;

                case StructuredBlockKind.Loop:
                    EmitLoop(block, indent);
                    break;

                case StructuredBlockKind.TryCatchFinally:
                    EmitTryCatchFinally(block, indent);
                    break;

                case StructuredBlockKind.Switch:
                    EmitSwitch(block, indent);
                    break;
            }
        }

        void EmitBasicBlock(int blockIndex, int indent)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var astBlock))
                return;

            _currentBlockNodes = astBlock.Nodes;

            foreach (var node in astBlock.Nodes)
            {
                switch (node)
                {
                    case ILAstAssignment assign:
                        WriteIndent(indent);
                        string assignType = SimplifyTypeName(assign.Variable.TypeName ?? "var");
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                        break;

                    case ILAstStatement stmt:
                        EmitStatement(stmt.Expression, indent);
                        break;
                }
            }

            _currentBlockNodes = null;
        }

        void EmitIfThenElse(StructuredBlock block, int indent)
        {
            // The condition block's last expression is the branch condition
            string condition = "/* condition */";
            if (block.ConditionBlockIndex >= 0 && _blockMap.TryGetValue(block.ConditionBlockIndex, out var condBlock))
            {
                _currentBlockNodes = condBlock.Nodes;

                // Emit any statements before the branch
                for (int i = 0; i < condBlock.Nodes.Count - 1; i++)
                {
                    if (condBlock.Nodes[i] is ILAstStatement stmt)
                        EmitStatement(stmt.Expression, indent);
                    else if (condBlock.Nodes[i] is ILAstAssignment assign)
                    {
                        WriteIndent(indent);
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                    }
                }

                // Extract condition from the last node (branch)
                var lastNode = condBlock.Nodes.LastOrDefault();
                if (lastNode is ILAstStatement branchStmt)
                    condition = ExpressionToString(ExtractCondition(branchStmt.Expression));
            }

            // Apply negation if the conditional detector swapped then/else
            if (block.NegateCondition)
                condition = NegateConditionString(condition);

            WriteIndent(indent);
            _sb.AppendLine($"if ({condition})");
            WriteIndent(indent);
            _sb.AppendLine("{");
            if (block.ThenBlock is not null)
                EmitStructuredBlock(block.ThenBlock, indent + 1);
            WriteIndent(indent);
            _sb.AppendLine("}");

            if (block.ElseBlock is not null)
            {
                WriteIndent(indent);
                _sb.AppendLine("else");
                WriteIndent(indent);
                _sb.AppendLine("{");
                EmitStructuredBlock(block.ElseBlock, indent + 1);
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
        }

        void EmitLoop(StructuredBlock block, int indent)
        {
            WriteIndent(indent);
            _sb.AppendLine("while (true)");
            WriteIndent(indent);
            _sb.AppendLine("{");

            foreach (var child in block.Children)
                EmitStructuredBlock(child, indent + 1);

            WriteIndent(indent);
            _sb.AppendLine("}");
        }

        void EmitTryCatchFinally(StructuredBlock block, int indent)
        {
            if (block.ExceptionRegion is not { } region) return;

            WriteIndent(indent);
            _sb.AppendLine("try");
            WriteIndent(indent);
            _sb.AppendLine("{");

            // Emit all try body blocks
            if (block.TryChildren.Count > 0)
            {
                foreach (var child in block.TryChildren)
                    EmitStructuredBlock(child, indent + 1);
            }
            else
            {
                EmitBasicBlock(block.BlockIndex, indent + 1);
            }

            WriteIndent(indent);
            _sb.AppendLine("}");

            if (region.Kind == ExceptionRegionKind.Catch)
            {
                string exType = "Exception";
                if (!region.CatchType.IsNil && _reader is not null)
                {
                    var resolved = Metadata.TypeResolver.GetTypeName(
                        _reader, region.CatchType);
                    if (resolved is not null)
                        exType = SimplifyTypeName(resolved);
                }

                WriteIndent(indent);
                _sb.AppendLine($"catch ({exType})");
                WriteIndent(indent);
                _sb.AppendLine("{");
                if (block.HandlerChildren.Count > 0)
                {
                    foreach (var child in block.HandlerChildren)
                        EmitStructuredBlock(child, indent + 1);
                }
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
            else if (region.Kind == ExceptionRegionKind.Finally)
            {
                WriteIndent(indent);
                _sb.AppendLine("finally");
                WriteIndent(indent);
                _sb.AppendLine("{");
                if (block.HandlerChildren.Count > 0)
                {
                    foreach (var child in block.HandlerChildren)
                        EmitStructuredBlock(child, indent + 1);
                }
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
        }

        void EmitSwitch(StructuredBlock block, int indent)
        {
            WriteIndent(indent);
            _sb.AppendLine("switch (/* value */)");
            WriteIndent(indent);
            _sb.AppendLine("{");
            WriteIndent(indent + 1);
            _sb.AppendLine("// cases");
            WriteIndent(indent);
            _sb.AppendLine("}");
        }

        // --- Expression emission ---

        void EmitStatement(ILAstExpression expr, int indent)
        {
            switch (expr.OpCode)
            {
                case ILOpCode.Ret:
                    WriteIndent(indent);
                    if (expr.Arguments.Count > 0)
                    {
                        _sb.Append("return ");
                        EmitExpression(expr.Arguments[0]);
                        _sb.AppendLine(";");
                    }
                    else
                    {
                        _sb.AppendLine("return;");
                    }
                    break;

                case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or
                     ILOpCode.Stloc_s or ILOpCode.Stloc:
                {
                    string varName = expr.Operand ?? GetLocalName(expr.OpCode);
                    WriteIndent(indent);
                    _sb.Append($"{varName} = ");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    else
                        _sb.Append("/* value */");
                    _sb.AppendLine(";");
                    break;
                }

                case ILOpCode.Stfld or ILOpCode.Stsfld:
                {
                    WriteIndent(indent);
                    if (expr.OpCode == ILOpCode.Stfld && expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append($".{ExtractMemberName(expr.Operand)} = ");
                        EmitExpression(expr.Arguments[1]);
                    }
                    else if (expr.OpCode == ILOpCode.Stsfld && expr.Arguments.Count >= 1)
                    {
                        _sb.Append($"{expr.Operand} = ");
                        EmitExpression(expr.Arguments[0]);
                    }
                    else
                    {
                        _sb.Append($"{expr.Operand} = /* value */");
                    }
                    _sb.AppendLine(";");
                    break;
                }

                case ILOpCode.Call or ILOpCode.Callvirt:
                    WriteIndent(indent);
                    EmitCallExpression(expr);
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Throw:
                    WriteIndent(indent);
                    _sb.Append("throw ");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Rethrow:
                    WriteIndent(indent);
                    _sb.AppendLine("throw;");
                    break;

                case ILOpCode.Starg_s or ILOpCode.Starg:
                    WriteIndent(indent);
                    _sb.Append($"{expr.Operand} = ");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or
                     ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
                     ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref:
                    WriteIndent(indent);
                    if (expr.Arguments.Count >= 3)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append("] = ");
                        EmitExpression(expr.Arguments[2]);
                    }
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Initobj:
                    WriteIndent(indent);
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.AppendLine($" = default({SimplifyTypeName(expr.Operand ?? "?")});");
                    }
                    else
                    {
                        _sb.AppendLine($"/* initobj {SimplifyTypeName(expr.Operand ?? "?")} */");
                    }
                    break;

                case ILOpCode.Pop:
                    // Typically a discarded expression — emit as expression statement
                    if (expr.Arguments.Count > 0)
                    {
                        WriteIndent(indent);
                        EmitExpression(expr.Arguments[0]);
                        _sb.AppendLine(";");
                    }
                    break;

                // Branches become comments in the initial output
                case ILOpCode.Br or ILOpCode.Br_s:
                    WriteIndent(indent);
                    _sb.AppendLine($"goto {expr.Operand};");
                    break;

                case ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s:
                case ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s:
                case ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bgt or ILOpCode.Bgt_s:
                case ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Blt or ILOpCode.Blt_s:
                case ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s:
                case ILOpCode.Ble_un or ILOpCode.Ble_un_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s:
                    // Conditional branches — when not consumed by structuring
                    WriteIndent(indent);
                    _sb.Append($"if (");
                    EmitBranchCondition(expr);
                    _sb.AppendLine($") goto {expr.Operand};");
                    break;

                case ILOpCode.Leave or ILOpCode.Leave_s:
                    // leave becomes implicit control flow in C#
                    break;

                case ILOpCode.Endfinally:
                    break;

                default:
                    WriteIndent(indent);
                    _sb.Append("/* ");
                    expr.WriteTo(_sb, 0);
                    _sb.AppendLine(" */");
                    break;
            }
        }

        void EmitExpression(ILAstExpression expr)
        {
            switch (expr.OpCode)
            {
                // Constants
                case ILOpCode.Ldc_i4_m1: _sb.Append("-1"); break;
                case ILOpCode.Ldc_i4_0: _sb.Append('0'); break;
                case ILOpCode.Ldc_i4_1: _sb.Append('1'); break;
                case ILOpCode.Ldc_i4_2: _sb.Append('2'); break;
                case ILOpCode.Ldc_i4_3: _sb.Append('3'); break;
                case ILOpCode.Ldc_i4_4: _sb.Append('4'); break;
                case ILOpCode.Ldc_i4_5: _sb.Append('5'); break;
                case ILOpCode.Ldc_i4_6: _sb.Append('6'); break;
                case ILOpCode.Ldc_i4_7: _sb.Append('7'); break;
                case ILOpCode.Ldc_i4_8: _sb.Append('8'); break;
                case ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4:
                    _sb.Append(expr.Operand ?? "0");
                    break;
                case ILOpCode.Ldc_i8:
                    _sb.Append($"{expr.Operand ?? "0"}L");
                    break;
                case ILOpCode.Ldc_r4:
                    _sb.Append($"{expr.Operand ?? "0"}f");
                    break;
                case ILOpCode.Ldc_r8:
                    _sb.Append(expr.Operand ?? "0.0");
                    break;
                case ILOpCode.Ldnull:
                    _sb.Append("null");
                    break;
                case ILOpCode.Ldstr:
                    _sb.Append(expr.Operand ?? "\"\"");
                    break;

                // Argument/local loads
                case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or
                     ILOpCode.Ldarg_s or ILOpCode.Ldarg:
                    _sb.Append(expr.Operand ?? GetArgName(expr.OpCode));
                    break;
                case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or
                     ILOpCode.Ldloc_s or ILOpCode.Ldloc:
                    _sb.Append(expr.Operand ?? GetLocalName(expr.OpCode));
                    break;

                // Binary arithmetic/logic operators
                case ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un:
                    EmitBinary(expr, "+"); break;
                case ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un:
                    EmitBinary(expr, "-"); break;
                case ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un:
                    EmitBinary(expr, "*"); break;
                case ILOpCode.Div or ILOpCode.Div_un:
                    EmitBinary(expr, "/"); break;
                case ILOpCode.Rem or ILOpCode.Rem_un:
                    EmitBinary(expr, "%"); break;
                case ILOpCode.And: EmitBinary(expr, "&"); break;
                case ILOpCode.Or: EmitBinary(expr, "|"); break;
                case ILOpCode.Xor: EmitBinary(expr, "^"); break;
                case ILOpCode.Shl: EmitBinary(expr, "<<"); break;
                case ILOpCode.Shr or ILOpCode.Shr_un:
                    EmitBinary(expr, ">>"); break;

                // Comparison operators
                case ILOpCode.Ceq: EmitBinary(expr, expr.Operand ?? "=="); break;
                case ILOpCode.Cgt or ILOpCode.Cgt_un: EmitBinary(expr, ">"); break;
                case ILOpCode.Clt or ILOpCode.Clt_un: EmitBinary(expr, "<"); break;

                // Unary operators
                case ILOpCode.Neg:
                    _sb.Append('-');
                    EmitParenthesized(expr, 0);
                    break;
                case ILOpCode.Not:
                    _sb.Append('~');
                    EmitParenthesized(expr, 0);
                    break;

                // Conversions
                case ILOpCode.Conv_i1 or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i1_un:
                    EmitCast(expr, "sbyte"); break;
                case ILOpCode.Conv_u1 or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un:
                    EmitCast(expr, "byte"); break;
                case ILOpCode.Conv_i2 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i2_un:
                    EmitCast(expr, "short"); break;
                case ILOpCode.Conv_u2 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u2_un:
                    EmitCast(expr, "char"); break;
                case ILOpCode.Conv_i4 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i4_un:
                    EmitCast(expr, "int"); break;
                case ILOpCode.Conv_u4 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u4_un:
                    EmitCast(expr, "uint"); break;
                case ILOpCode.Conv_i8 or ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_i8_un:
                    EmitCast(expr, "long"); break;
                case ILOpCode.Conv_u8 or ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_u8_un:
                    EmitCast(expr, "ulong"); break;
                case ILOpCode.Conv_r4: EmitCast(expr, "float"); break;
                case ILOpCode.Conv_r8 or ILOpCode.Conv_r_un: EmitCast(expr, "double"); break;
                case ILOpCode.Conv_i or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_i_un:
                    EmitCast(expr, "nint"); break;
                case ILOpCode.Conv_u or ILOpCode.Conv_ovf_u or ILOpCode.Conv_ovf_u_un:
                    EmitCast(expr, "nuint"); break;

                // Type casts
                case ILOpCode.Castclass:
                    _sb.Append($"({SimplifyTypeName(expr.Operand ?? "object")})");
                    EmitParenthesized(expr, 0);
                    break;
                case ILOpCode.Isinst:
                    EmitParenthesized(expr, 0);
                    _sb.Append($" as {SimplifyTypeName(expr.Operand ?? "object")}");
                    break;

                // Object creation
                case ILOpCode.Newobj:
                {
                    string typeName = ExtractTypeName(expr.Operand);
                    _sb.Append($"new {SimplifyTypeName(typeName)}(");
                    // Skip 'this' argument (first arg for instance constructor)
                    for (int i = 0; i < expr.Arguments.Count; i++)
                    {
                        if (i > 0) _sb.Append(", ");
                        EmitExpression(expr.Arguments[i]);
                    }
                    _sb.Append(')');
                    break;
                }

                case ILOpCode.Newarr:
                    _sb.Append($"new {SimplifyTypeName(expr.Operand ?? "object")}[");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.Append(']');
                    break;

                // Field access
                case ILOpCode.Ldfld:
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('.');
                    }
                    _sb.Append(ExtractMemberName(expr.Operand));
                    break;
                case ILOpCode.Ldsfld:
                    _sb.Append(expr.Operand ?? "/* field */");
                    break;

                // Method calls
                case ILOpCode.Call or ILOpCode.Callvirt:
                    EmitCallExpression(expr);
                    break;

                // Boxing
                case ILOpCode.Box:
                    EmitParenthesized(expr, 0);
                    _sb.Append($" /* box {SimplifyTypeName(expr.Operand ?? "?")} */");
                    break;
                case ILOpCode.Unbox_any:
                    _sb.Append($"({SimplifyTypeName(expr.Operand ?? "object")})");
                    EmitParenthesized(expr, 0);
                    break;

                // Array operations
                case ILOpCode.Ldlen:
                    EmitParenthesized(expr, 0);
                    _sb.Append(".Length");
                    break;
                case ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or
                     ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or
                     ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref or
                     ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4:
                    if (expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append(']');
                    }
                    break;

                // Dup (pass through, or reconstruct from preceding expression in block)
                case ILOpCode.Dup:
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    else if (_currentBlockNodes is not null)
                    {
                        // Find the preceding expression that produced the dup'd value
                        var preceding = FindPrecedingValue(_currentBlockNodes, expr);
                        if (preceding is not null)
                            EmitExpression(preceding);
                        else
                            _sb.Append("/* dup */");
                    }
                    else
                        _sb.Append("/* dup */");
                    break;

                // Address operations
                case ILOpCode.Ldarga_s or ILOpCode.Ldarga:
                    _sb.Append($"ref {expr.Operand}");
                    break;
                case ILOpCode.Ldloca_s or ILOpCode.Ldloca:
                    _sb.Append($"ref {expr.Operand}");
                    break;
                case ILOpCode.Ldflda:
                    _sb.Append("ref ");
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('.');
                    }
                    _sb.Append(ExtractMemberName(expr.Operand));
                    break;

                case ILOpCode.Sizeof:
                    _sb.Append($"sizeof({SimplifyTypeName(expr.Operand ?? "?")})");
                    break;

                case ILOpCode.Ldtoken:
                    _sb.Append($"typeof({SimplifyTypeName(expr.Operand ?? "?")})");
                    break;

                case ILOpCode.Localloc:
                    _sb.Append("stackalloc byte[");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.Append(']');
                    break;

                // Block-entry stack values (synthetic ldloc for cross-block values)
                case ILOpCode.Nop when expr.Operand is not null:
                    _sb.Append(expr.Operand);
                    break;

                default:
                    _sb.Append("/* ");
                    expr.WriteTo(_sb, 0);
                    _sb.Append(" */");
                    break;
            }
        }

        void EmitCallExpression(ILAstExpression expr)
        {
            string? methodName = expr.Operand;
            if (methodName is null)
            {
                _sb.Append("/* call */");
                return;
            }

            // Parse "TypeName::MethodName()" format
            string typePart = "";
            string memberPart = methodName;
            int colonIdx = methodName.IndexOf("::", StringComparison.Ordinal);
            if (colonIdx >= 0)
            {
                typePart = methodName[..colonIdx];
                memberPart = methodName[(colonIdx + 2)..].TrimEnd('(', ')');
            }

            bool isStatic = expr.IsStaticCall;

            if (isStatic && expr.Arguments.Count > 0 && typePart.Length > 0)
            {
                // Static call: TypeName.Method(args)
                _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart}(");
                for (int i = 0; i < expr.Arguments.Count; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    EmitExpression(expr.Arguments[i]);
                }
                _sb.Append(')');
            }
            else if (!isStatic && expr.Arguments.Count > 0)
            {
                // Instance call: receiver.Method(args)
                EmitExpression(expr.Arguments[0]);
                _sb.Append($".{memberPart}(");
                for (int i = 1; i < expr.Arguments.Count; i++)
                {
                    if (i > 1) _sb.Append(", ");
                    EmitExpression(expr.Arguments[i]);
                }
                _sb.Append(')');
            }
            else
            {
                _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart}()");
            }
        }

        void EmitBranchCondition(ILAstExpression expr)
        {
            switch (expr.OpCode)
            {
                case ILOpCode.Brfalse or ILOpCode.Brfalse_s:
                    if (expr.Arguments.Count > 0)
                    {
                        _sb.Append('!');
                        EmitParenthesized(expr, 0);
                    }
                    break;
                case ILOpCode.Brtrue or ILOpCode.Brtrue_s:
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    break;
                case ILOpCode.Beq or ILOpCode.Beq_s:
                    EmitBinaryCondition(expr, "=="); break;
                case ILOpCode.Bne_un or ILOpCode.Bne_un_s:
                    EmitBinaryCondition(expr, "!="); break;
                case ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s:
                    EmitBinaryCondition(expr, ">="); break;
                case ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s:
                    EmitBinaryCondition(expr, ">"); break;
                case ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s:
                    EmitBinaryCondition(expr, "<="); break;
                case ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s:
                    EmitBinaryCondition(expr, "<"); break;
                default:
                    EmitExpression(expr);
                    break;
            }
        }

        void EmitBinaryCondition(ILAstExpression expr, string op)
        {
            if (expr.Arguments.Count >= 2)
            {
                EmitExpression(expr.Arguments[0]);
                _sb.Append($" {op} ");
                EmitExpression(expr.Arguments[1]);
            }
        }

        // --- Helpers ---

        void EmitBinary(ILAstExpression expr, string op)
        {
            if (expr.Arguments.Count >= 2)
            {
                EmitParenthesized(expr, 0);
                _sb.Append($" {op} ");
                EmitParenthesized(expr, 1);
            }
            else
            {
                _sb.Append($"/* {op} */");
            }
        }

        void EmitParenthesized(ILAstExpression parent, int argIndex)
        {
            if (argIndex >= parent.Arguments.Count) return;
            var arg = parent.Arguments[argIndex];
            bool needsParens = NeedsParentheses(arg);
            if (needsParens) _sb.Append('(');
            EmitExpression(arg);
            if (needsParens) _sb.Append(')');
        }

        void EmitCast(ILAstExpression expr, string typeName)
        {
            _sb.Append($"({typeName})");
            EmitParenthesized(expr, 0);
        }

        void WriteIndent(int indent)
        {
            for (int i = 0; i < indent; i++)
                _sb.Append("    ");
        }

        /// <summary>
        /// Find the value expression that a dup instruction duplicated by looking
        /// at the preceding node in the same block. Returns the load/field expression
        /// if found, or null.
        /// </summary>
        static ILAstExpression? FindPrecedingValue(List<ILAstNode> nodes, ILAstExpression dupExpr)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var expr = nodes[i] switch
                {
                    ILAstStatement s => s.Expression,
                    ILAstAssignment a => a.Value,
                    _ => null
                };

                if (expr is null) continue;

                // Look for the dup inside this expression's argument tree
                if (ContainsDup(expr, dupExpr))
                {
                    // The preceding node's stored value is what was dup'd
                    if (i > 0)
                    {
                        return nodes[i - 1] switch
                        {
                            ILAstAssignment prevAssign => new ILAstExpression
                            {
                                OpCode = ILOpCode.Ldloc_0,
                                Operand = prevAssign.Variable.Name,
                                ResultType = prevAssign.Value.ResultType,
                                Offset = dupExpr.Offset
                            },
                            ILAstStatement prevStmt when prevStmt.Expression.OpCode is
                                ILOpCode.Ldfld or ILOpCode.Ldsfld or
                                ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or
                                ILOpCode.Ldloc_s or ILOpCode.Ldloc or
                                ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or
                                ILOpCode.Ldarg_s or ILOpCode.Ldarg
                                => prevStmt.Expression,
                            _ => null
                        };
                    }
                }

                // Also check if this expression IS a branch with a dup argument
                if (expr.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                {
                    if (expr.Arguments.Count > 0 && expr.Arguments[0].OpCode == ILOpCode.Dup && expr.Arguments[0] == dupExpr)
                    {
                        // The dup's source is whatever was loaded before in this block
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (nodes[j] is ILAstAssignment a)
                            {
                                return new ILAstExpression
                                {
                                    OpCode = ILOpCode.Ldloc_0,
                                    Operand = a.Variable.Name,
                                    ResultType = a.Value.ResultType,
                                    Offset = dupExpr.Offset
                                };
                            }
                        }
                    }
                }
            }

            return null;
        }

        static bool ContainsDup(ILAstExpression expr, ILAstExpression target)
        {
            if (ReferenceEquals(expr, target)) return true;
            foreach (var arg in expr.Arguments)
            {
                if (ContainsDup(arg, target)) return true;
            }
            return false;
        }

        string ExpressionToString(ILAstExpression expr)
        {
            var sb = new StringBuilder();
            var saved = _sb;
            // Use a temp context with the new StringBuilder
            var tempCtx = new EmitterContext(_ast, _structure, sb);
            tempCtx.EmitExpression(expr);
            return sb.ToString();
        }

        static string NegateConditionString(string condition)
        {
            // Flip comparison operators if present
            if (condition.Contains(" != "))
                return condition.Replace(" != ", " == ");
            if (condition.Contains(" == "))
                return condition.Replace(" == ", " != ");
            if (condition.Contains(" > ") && !condition.Contains(" >= "))
                return condition.Replace(" > ", " <= ");
            if (condition.Contains(" < ") && !condition.Contains(" <= "))
                return condition.Replace(" < ", " >= ");
            if (condition.Contains(" >= "))
                return condition.Replace(" >= ", " < ");
            if (condition.Contains(" <= "))
                return condition.Replace(" <= ", " > ");

            // Simple negation
            if (condition.StartsWith("!(") && condition.EndsWith(')'))
                return condition[2..^1];
            if (condition.StartsWith('!'))
                return condition[1..];
            return $"!{condition}";
        }

        static ILAstExpression ExtractCondition(ILAstExpression branchExpr)
        {
            // For conditional branches, the condition is the arguments
            if (branchExpr.Arguments.Count == 1)
            {
                var arg = branchExpr.Arguments[0];

                // For brfalse/brtrue on reference types, emit explicit null comparison
                bool isRefType = arg.ResultType.Kind is StackValueKind.ObjRef
                    || (arg.ResultType.TypeName is not null && !IsPrimitiveType(arg.ResultType.TypeName));

                if (isRefType && branchExpr.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s
                    or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                {
                    // Produce a synthetic != null comparison using Ceq + Operand override
                    return new ILAstExpression
                    {
                        OpCode = ILOpCode.Ceq,
                        ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                        Operand = "!=",
                        Arguments =
                        {
                            arg,
                            new ILAstExpression
                            {
                                OpCode = ILOpCode.Ldnull,
                                ResultType = StackValue.CreateObjRef(null),
                                Offset = arg.Offset
                            }
                        }
                    };
                }

                return arg;
            }
            if (branchExpr.Arguments.Count == 2)
            {
                // Binary comparison branch — reconstruct as comparison expression
                return new ILAstExpression
                {
                    OpCode = branchExpr.OpCode switch
                    {
                        ILOpCode.Beq or ILOpCode.Beq_s => ILOpCode.Ceq,
                        ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s => ILOpCode.Cgt,
                        ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s => ILOpCode.Clt,
                        _ => ILOpCode.Ceq
                    },
                    ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                    Arguments = { branchExpr.Arguments[0], branchExpr.Arguments[1] }
                };
            }
            return branchExpr;
        }

        static bool IsPrimitiveType(string typeName) => typeName is
            "System.Boolean" or "bool" or "System.Byte" or "byte" or
            "System.SByte" or "sbyte" or "System.Int16" or "short" or
            "System.UInt16" or "ushort" or "System.Int32" or "int" or "Int32" or
            "System.UInt32" or "uint" or "System.Int64" or "long" or
            "System.UInt64" or "ulong" or "System.Single" or "float" or
            "System.Double" or "double" or "System.Char" or "char" or
            "System.IntPtr" or "nint" or "System.UIntPtr" or "nuint";

        static bool NeedsParentheses(ILAstExpression expr) => expr.OpCode switch
        {
            ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or
            ILOpCode.Rem or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
            ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Ceq or ILOpCode.Cgt or
            ILOpCode.Clt => true,
            _ => false
        };

        static string GetArgName(ILOpCode opcode) => opcode switch
        {
            ILOpCode.Ldarg_0 => "P_0",
            ILOpCode.Ldarg_1 => "P_1",
            ILOpCode.Ldarg_2 => "P_2",
            ILOpCode.Ldarg_3 => "P_3",
            _ => "arg"
        };

        static string GetLocalName(ILOpCode opcode) => opcode switch
        {
            ILOpCode.Ldloc_0 or ILOpCode.Stloc_0 => "V_0",
            ILOpCode.Ldloc_1 or ILOpCode.Stloc_1 => "V_1",
            ILOpCode.Ldloc_2 or ILOpCode.Stloc_2 => "V_2",
            ILOpCode.Ldloc_3 or ILOpCode.Stloc_3 => "V_3",
            _ => "loc"
        };

        static string ExtractTypeName(string? qualifiedName)
        {
            if (qualifiedName is null) return "object";
            int colonIdx = qualifiedName.IndexOf("::", StringComparison.Ordinal);
            return colonIdx >= 0 ? qualifiedName[..colonIdx] : qualifiedName;
        }

        static string ExtractMemberName(string? qualifiedName)
        {
            if (qualifiedName is null) return "member";
            int colonIdx = qualifiedName.IndexOf("::", StringComparison.Ordinal);
            if (colonIdx >= 0)
            {
                string name = qualifiedName[(colonIdx + 2)..];
                return name.TrimEnd('(', ')');
            }
            return qualifiedName;
        }

        static string SimplifyTypeName(string typeName) => typeName switch
        {
            "System.Void" or "void" => "void",
            "System.Boolean" or "bool" => "bool",
            "System.Byte" or "byte" => "byte",
            "System.SByte" or "sbyte" => "sbyte",
            "System.Int16" or "short" => "short",
            "System.UInt16" or "ushort" => "ushort",
            "System.Int32" or "int" => "int",
            "System.UInt32" or "uint" => "uint",
            "System.Int64" or "long" => "long",
            "System.UInt64" or "ulong" => "ulong",
            "System.Single" or "float" => "float",
            "System.Double" or "double" => "double",
            "System.Decimal" or "decimal" => "decimal",
            "System.Char" or "char" => "char",
            "System.String" or "string" => "string",
            "System.Object" or "object" => "object",
            "System.IntPtr" or "nint" => "nint",
            "System.UIntPtr" or "nuint" => "nuint",
            _ => typeName
        };
    }
}
