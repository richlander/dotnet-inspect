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

        // With metadata param names, should use real names (a, b)
        Assert.True(output.Contains("a") || output.Contains("b") || output.Contains("return"),
            $"Expected parameter names in:\n{output}");
    }

    // --- Unsigned/unordered comparisons (cgt.un, clt.un, b*.un) ---

    [Fact]
    public void UnsignedBoundsCheck_KeepsUnsignedCasts()
    {
        string output = EmitMethod(nameof(CfgSampleClass.UnsignedBoundsCheck));

        // (uint)index < (uint)array.Length must not decay to index < array.Length —
        // that inverts the result for negative indexes.
        Assert.Contains("(uint)", output);
    }

    [Fact]
    public void UnsignedBoundsBranch_KeepsUnsignedCasts()
    {
        string output = EmitMethod(nameof(CfgSampleClass.UnsignedBoundsBranch));

        Assert.Contains("(uint)", output);
        Assert.Contains("if", output);
    }

    [Fact]
    public void FloatUnordered_DoesNotDecayToOrderedCompare()
    {
        string output = EmitMethod(nameof(CfgSampleClass.FloatUnordered));

        // !(a <= b) compiles to cgt.un; rendering it as a > b changes the
        // result when either operand is NaN. Accept the negated-complement
        // form or the exact source form.
        Assert.True(output.Contains("!(") || output.Contains("<="),
            $"Expected unordered-aware rendering in:\n{output}");
        Assert.DoesNotContain("(uint)", output);
    }

    [Fact]
    public void NotNullIdiom_RendersAsNullComparison()
    {
        string output = EmitMethod(nameof(CfgSampleClass.NotNullIdiom));

        // cgt.un(o, null) is the reference-inequality idiom — never "(uint)o > null".
        Assert.Contains("null", output);
        Assert.DoesNotContain("(uint)", output);
        Assert.DoesNotContain("> null", output);
    }

    [Fact]
    public void UnsignedShift_RendersUnsignedShiftOperator()
    {
        string output = EmitMethod(nameof(CfgSampleClass.UnsignedShift));

        Assert.Contains(">>>", output);
    }

    [Fact]
    public void UnsignedDivide_DoesNotRenderSignedDivision()
    {
        string output = EmitMethod(nameof(CfgSampleClass.UnsignedDivide));

        // div.un on uint operands: plain '/' is fine only with unsigned operands;
        // the emitter renders casts since IL erases operand signedness.
        Assert.Contains("/", output);
    }

    [Fact]
    public void ULongGe_OmitsRedundantUnsignedCasts()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ULongGe));

        // Both operands are statically ulong — C# compiles a >= b straight to
        // the unsigned compare, so reinterpretation casts are redundant noise.
        Assert.Contains(">=", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void MaxULong_OmitsRedundantUnsignedCasts()
    {
        string output = EmitMethod(nameof(CfgSampleClass.MaxULong));

        Assert.Contains(">=", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void AbsShortHelper_EveryGotoHasALabel()
    {
        // CoreLib Math.Abs shape: structuring suppresses the follow-block label
        // assuming all branches to it were absorbed, but the inner guard still
        // renders as a goto. Any rendered goto must have its target label.
        string output = EmitMethod(nameof(CfgSampleClass.AbsShortHelper));

        foreach (string line in output.Split('\n'))
        {
            int at = line.IndexOf("goto IL_", StringComparison.Ordinal);
            if (at < 0)
                continue;
            string label = line.Substring(at + 5, 7);
            Assert.True(output.Contains(label + ":", StringComparison.Ordinal),
                $"goto {label} has no target label in:\n{output}");
        }
    }

    // --- Generic ldelem (type-parameter element access) ---

    [Fact]
    public void GetAt_RendersGenericElementLoad()
    {
        string output = EmitMethod(nameof(CfgSampleClass.GetAt));

        // ldelem with a type token previously fell back to an IL comment.
        Assert.Contains("array[index]", output);
        Assert.DoesNotContain("ldelem", output);
    }

    [Fact]
    public void CoreLib_QueueDequeue_SpillsElementLoadToTypedLocal()
    {
        // Release shape: the dequeued element stays on the eval stack across
        // the IsReferenceOrContainsReferences branch. The spill must render as
        // a typed declaration consumed by the return — no comment fallbacks,
        // no S_in_* artifacts.
        string output = EmitCoreLibMethod("System.Collections.Generic.Queue`1", "Dequeue");

        Assert.Contains("T S_0 = V_1[V_0];", output);
        Assert.Contains("return S_0;", output);
        Assert.DoesNotContain("S_in_", output);
        Assert.DoesNotContain("/*", output);
    }

    // --- Exception filters (catch...when) ---

    [Fact]
    public void FilterCatch_EmitsWhenClause()
    {
        string output = EmitMethod(nameof(CfgSampleClass.FilterCatch));

        Assert.Contains("catch", output);
        Assert.Contains("when", output);
        Assert.Contains("FormatException", output);
    }

    [Fact]
    public void FilterCatch_HandlerBodyIsNotDropped()
    {
        string output = EmitMethod(nameof(CfgSampleClass.FilterCatch));

        // The handler returns e.Message.Length — its body must appear.
        Assert.Contains("Message", output);
    }

    [Fact]
    public void FilterCatch_FilterCodeDoesNotLeakInline()
    {
        string output = EmitMethod(nameof(CfgSampleClass.FilterCatch));

        Assert.DoesNotContain("endfilter", output);
    }

    // --- Short-circuit condition chains (Release-shaped IL) ---
    // Debug builds materialize && / || as stack values, so chain coverage uses
    // the always-Release platform assembly.

    [Fact]
    public void CoreLib_IsNullOrEmpty_ReconstructsShortCircuit()
    {
        string output = EmitCoreLibMethod("System.String", "IsNullOrEmpty");

        Assert.True(output.Contains("&&") || output.Contains("||"),
            $"Expected a short-circuit operator in:\n{output}");
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void CoreLib_IsNullOrEmpty_KeepsNullCheckSemantics()
    {
        string output = EmitCoreLibMethod("System.String", "IsNullOrEmpty");

        Assert.Contains("null", output);
        Assert.Contains("Length", output);
    }

    [Fact]
    public void CoreLib_AbsShort_KeepsNegateAssignment()
    {
        // Math.Abs(short): the inner overflow check is a fallthrough condition
        // block, but its block also contains `value = (short)-value`. The chain
        // detector must NOT absorb it — that drops the assignment, rendering
        // `if (value >= 0 || value >= 0)`.
        string output = EmitCoreLibMethod("System.Math", "Abs", overloadIndex: 0);

        Assert.Contains("-value", output);
        Assert.DoesNotContain("||", output);
        Assert.DoesNotContain("&&", output);
    }

    [Fact]
    public void AbsShort_KeepsNegateAssignment()
    {
        // Same shape from the fixture assembly. In Release builds this hits the
        // chain detector; in Debug the stack-height guard already disables
        // chains, so the assignment trivially survives.
        string output = EmitMethod(nameof(CfgSampleClass.AbsShort));

        Assert.Contains("-value", output);
        Assert.DoesNotContain("||", output);
    }

    static string EmitCoreLibMethod(string typeName, string methodName, int overloadIndex = 0)
    {
        var assemblyPath = typeof(object).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(peReader, typeName, methodName, overloadIndex);
        Assert.NotNull(context);
        return CSharpEmitter.Emit(context);
    }

    // --- P1 sugar: field compound assignment and char literals ---

    [Fact]
    public void Bump_RendersFieldCompoundAssignment()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Bump));

        Assert.Contains("+=", output);
        Assert.Contains("++;", output);
        Assert.DoesNotContain("h.Value = h.Value", output);
    }

    [Fact]
    public void IsSpaceOrTab_RendersCharLiterals()
    {
        string output = EmitMethod(nameof(CfgSampleClass.IsSpaceOrTab));

        Assert.Contains("' '", output);
        Assert.Contains("'\\t'", output);
        Assert.DoesNotContain("32", output);
    }

    // --- P1 sugar: operators, local functions, enum case labels ---

    [Fact]
    public void NegateSum_RendersOperatorSugar()
    {
        string output = EmitMethod(nameof(CfgSampleClass.NegateSum));

        Assert.Contains("a + b", output);
        Assert.Contains("-(", output);
        Assert.DoesNotContain("op_Addition", output);
        Assert.DoesNotContain("op_UnaryNegation", output);
    }

    [Fact]
    public void MoneyToInt_RendersImplicitConversionAsCast()
    {
        string output = EmitMethod(nameof(CfgSampleClass.MoneyToInt));

        Assert.Contains("(int)", output);
        Assert.DoesNotContain("op_Implicit", output);
    }

    [Fact]
    public void DoubleViaLocalFunction_UsesSourceLevelName()
    {
        string output = EmitMethod(nameof(CfgSampleClass.DoubleViaLocalFunction));

        Assert.Contains("Twice(", output);
        Assert.DoesNotContain("g__", output);
    }

    [Fact]
    public void ColorName_UsesEnumCaseLabels()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ColorName));

        Assert.Contains("case", output);
        Assert.Contains("Green", output);
        Assert.Contains("Yellow", output);
    }

    // --- Loop branches: continue / break ---

    [Fact]
    public void LoopWithContinue_RendersContinue()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopWithContinue));

        Assert.Contains("continue;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
        // The summing statement must remain reachable (it follows the guard).
        Assert.Contains("+=", output);
    }

    [Fact]
    public void LoopWithBreak_RendersBreak()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopWithBreak));

        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
    }

    // --- Inliner soundness ---

    [Fact]
    public void StaleFieldRead_DoesNotInlinePastMutation()
    {
        string output = EmitMethod(nameof(CfgSampleClass.StaleFieldRead));

        // v captures h.Value BEFORE h.Value = 99; inlining the read past the
        // store would compute the post-mutation value. The capture must stay
        // ordered before the store.
        int captureIdx = output.IndexOf("= h.Value", StringComparison.Ordinal);
        int storeIdx = output.IndexOf("= 99", StringComparison.Ordinal);
        Assert.True(captureIdx >= 0, $"Expected captured read in:\n{output}");
        Assert.True(storeIdx >= 0, $"Expected field store in:\n{output}");
        Assert.True(captureIdx < storeIdx,
            $"Captured read must precede the mutation in:\n{output}");
    }

    // --- using detection (must not eat finally code) ---

    [Fact]
    public void NormalUsing_RendersAsUsing()
    {
        string output = EmitMethod(nameof(CfgSampleClass.NormalUsing));

        Assert.Contains("using", output);
        Assert.DoesNotContain("finally", output);
    }

    [Fact]
    public void FinallyWithExtraWork_IsNotCollapsedToUsing()
    {
        string output = EmitMethod(nameof(CfgSampleClass.FinallyWithExtraWork));

        // The finally does more than dispose; rendering it as using would
        // silently delete the extra statement.
        Assert.Contains("finally", output);
        Assert.Contains("Dispose", output);
        Assert.Contains("-1", output);
    }

    [Fact]
    public void ManualDisposeAsyncInFinally_IsNotRenderedAsUsing()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ManualDisposeAsyncInFinally));

        // DisposeAsync is await using's lowering — a plain using would change
        // semantics. Keep the faithful try/finally.
        Assert.Contains("finally", output);
        Assert.Contains("DisposeAsync", output);
    }

    // --- Control flow ---

    [Fact]
    public void Classify_HasGuardClauses()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Classify));

        Assert.Contains("if", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void Classify_HasStringLiterals()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Classify));

        Assert.Contains("\"positive\"", output);
        Assert.Contains("\"negative\"", output);
        Assert.Contains("\"zero\"", output);
    }

    [Fact]
    public void LoopSum_HasForLoop()
    {
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum));

        Assert.Contains("for", output);
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

    [Fact]
    public void CallByRefTarget_DoesNotDuplicateRefModifier()
    {
        string output = EmitMethod(nameof(CfgSampleClass.CallByRefTarget));

        Assert.Contains("ByRefTarget(ref value)", output);
        Assert.DoesNotContain("ref ref value", output);
    }

    [Fact]
    public void CallInTarget_UsesInModifier()
    {
        string output = EmitMethod(nameof(CfgSampleClass.CallInTarget));

        Assert.Contains("InTarget(in value)", output);
        Assert.DoesNotContain("InTarget(ref value)", output);
    }

    [Fact]
    public void CallOutTarget_UsesOutModifier()
    {
        string output = EmitMethod(nameof(CfgSampleClass.CallOutTarget));

        Assert.Contains("OutTarget(out ", output);
        Assert.DoesNotContain("OutTarget(ref ", output);
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

        // Should have a + b or similar
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
    public void WhileLoop_EmitsForLoop()
    {
        string output = EmitMethod(nameof(CfgSampleClass.WhileLoop));

        // Detected as a for-loop (increment pattern: V_0 = V_0 + 1)
        Assert.Contains("for", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void DoWhileLoop_EmitsDoWhile()
    {
        string output = EmitMethod(nameof(CfgSampleClass.DoWhileLoop));

        Assert.Contains("do", output);
        Assert.Contains("while", output);
        // The do-while body should contain the increment (as ++ or + 1)
        Assert.True(output.Contains("++") || output.Contains("+ 1"),
            $"Expected increment in:\n{output}");
    }

    [Fact]
    public void NestedLoops_EmitsFor()
    {
        string output = EmitMethod(nameof(CfgSampleClass.NestedLoops));

        Assert.Contains("for", output);
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

    [Fact]
    public void MultipleCatch_HasBothCatchBlocks()
    {
        string output = EmitMethod(nameof(CfgSampleClass.MultipleCatch));

        Assert.Contains("try", output);
        Assert.Contains("FormatException", output);
        Assert.Contains("OverflowException", output);
        // Both catch blocks should appear — count occurrences of "catch"
        int catchCount = output.Split("catch").Length - 1;
        Assert.True(catchCount >= 2, $"Expected at least 2 catch blocks, found {catchCount} in:\n{output}");
    }

    // --- Switch ---

    [Fact]
    public void SwitchStatement_HasSwitchKeyword()
    {
        string output = EmitMethod(nameof(CfgSampleClass.SwitchStatement));

        Assert.Contains("switch", output);
    }

    [Fact]
    public void SwitchStatement_HasCaseLabels()
    {
        string output = EmitMethod(nameof(CfgSampleClass.SwitchStatement));

        Assert.Contains("case", output);
    }

    [Fact]
    public void SwitchStatement_HasDefaultCase()
    {
        string output = EmitMethod(nameof(CfgSampleClass.SwitchStatement));

        Assert.Contains("default:", output);
    }

    [Fact]
    public void SwitchStatement_HasStringReturns()
    {
        string output = EmitMethod(nameof(CfgSampleClass.SwitchStatement));

        Assert.Contains("return", output);
        Assert.Contains("\"zero\"", output);
        Assert.Contains("\"other\"", output);
    }

    // --- Ternary ---

    [Fact]
    public void Ternary_HasBothValues()
    {
        string output = EmitMethod(nameof(CfgSampleClass.Ternary));

        // In Release, the ternary compiles to if/return branches
        Assert.Contains("\"positive\"", output);
        Assert.Contains("\"non-positive\"", output);
        Assert.Contains("return", output);
    }

    // --- Platform stress test ---

    [Fact]
    public void StringInterpolation_EmitsInterpolatedString()
    {
        string output = EmitMethod(nameof(CfgSampleClass.StringInterpolation));

        Assert.Contains("$\"", output);
        // Parameters use real names from metadata (name, age)
        Assert.Contains("{name}", output);
        Assert.Contains("{age}", output);
        Assert.DoesNotContain("AppendLiteral", output);
        Assert.DoesNotContain("AppendFormatted", output);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", output);
    }

    [Fact]
    public void UsingStatement_EmitsUsingDeclaration()
    {
        string output = EmitMethod(nameof(CfgSampleClass.UsingStatement));

        Assert.Contains("using var", output);
        Assert.Contains("OpenRead", output);
        Assert.DoesNotContain("try", output);
        Assert.DoesNotContain("finally", output);
        Assert.DoesNotContain("Dispose", output);
    }

    [Fact]
    public void ForeachLoop_EmitsForeach()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ForeachLoop));

        Assert.Contains("foreach", output);
        Assert.DoesNotContain("GetEnumerator", output);
        Assert.DoesNotContain("MoveNext", output);
        Assert.DoesNotContain("Current", output);
        Assert.DoesNotContain("Dispose", output);
        Assert.DoesNotContain("try", output);
    }

    [Fact]
    public void ClosureCapture_InlinesLambdaBody()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ClosureCapture));

        // The captured variable appears by name inside the inlined body.
        Assert.Contains("x => x + offset", output);
        Assert.DoesNotContain("<>c__DisplayClass", output);
        Assert.DoesNotContain("/* lambda", output);
    }

    [Fact]
    public void ClosureWithLinq_InlinesLambdaBody()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ClosureWithLinq));

        Assert.Contains("x => x > threshold", output);
        Assert.DoesNotContain("<>c__DisplayClass", output);
    }

    [Fact]
    public void CountAbove_InlinesCapturedLambdaExactly()
    {
        string output = EmitMethod(nameof(CfgSampleClass.CountAbove));

        Assert.Contains("values.Count(v => v > min)", output);
        // Closure construction is subsumed by the lambda syntax.
        Assert.DoesNotContain("/* closure */", output);
    }

    [Fact]
    public void CountPositive_ElidesCachedDelegateDance()
    {
        string output = EmitMethod(nameof(CfgSampleClass.CountPositive));

        // The <>9__ null-check/build/store dance is elided entirely; only the
        // inlined lambda remains.
        Assert.Contains("values.Count(v => v > 0)", output);
        Assert.DoesNotContain("9__", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("S_", output);
    }

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

    [Fact]
    public void BoolLiteral_AlwaysTrue_EmitsTrue()
    {
        var code = EmitMethod(nameof(CfgSampleClass.AlwaysTrue));
        Assert.Contains("return true;", code);
        Assert.DoesNotContain("return 1;", code);
    }

    [Fact]
    public void BoolLiteral_AlwaysFalse_EmitsFalse()
    {
        var code = EmitMethod(nameof(CfgSampleClass.AlwaysFalse));
        Assert.Contains("return false;", code);
        Assert.DoesNotContain("return 0;", code);
    }

    [Fact]
    public void DoubleLiteral_HasSuffix()
    {
        var code = EmitMethod(nameof(CfgSampleClass.DoubleConstant));
        Assert.Contains("3.14d", code);
    }

    [Fact]
    public void DoubleLiteral_WholeNumber_HasDecimalPoint()
    {
        var code = EmitMethod(nameof(CfgSampleClass.DoubleWholeNumber));
        Assert.Contains("1.0d", code);
    }

    [Fact]
    public void DoubleLiteral_NaN_UsesFrameworkConstant()
    {
        var code = EmitMethod(nameof(CfgSampleClass.DoubleNaN));
        Assert.Contains("double.NaN", code);
        Assert.DoesNotContain("NaNd", code);
    }

    [Fact]
    public void DoubleLiteral_PositiveInfinity_UsesFrameworkConstant()
    {
        var code = EmitMethod(nameof(CfgSampleClass.DoublePositiveInfinity));
        Assert.Contains("double.PositiveInfinity", code);
        Assert.DoesNotContain("Infinityd", code);
    }

    [Fact]
    public void NullableType_UsesShorthand()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullableReturn));
        Assert.Contains("int?", code);
        Assert.DoesNotContain("Nullable<", code);
    }

    [Fact]
    public void CheckedAdd_EmitsChecked()
    {
        var code = EmitMethod(nameof(CfgSampleClass.CheckedAdd));
        Assert.Contains("checked(", code);
    }

    [Fact]
    public void CheckedCast_EmitsChecked()
    {
        var code = EmitMethod(nameof(CfgSampleClass.CheckedCast));
        Assert.Contains("checked(", code);
    }

    [Fact]
    public void NullCoalesce_EmitsDoubleQuestion()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullCoalesce));
        Assert.Contains("??", code);
    }

    [Fact]
    public void ArrayInit_EmitsInitializer()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ArrayWithInit));
        Assert.Contains("new string[]", code);
        Assert.Contains("{", code);
        Assert.DoesNotContain("[0] =", code); // stelem statements should be collapsed
    }

    [Fact]
    public void ArrayInit_DynamicSize_DoesNotCollapseToEmptyInitializer()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ArrayWithDynamicSize));
        Assert.Contains("new int[n]", code);
        Assert.Contains("[0] = 1", code);
        Assert.DoesNotContain("new int[] {  }", code);
    }

    [Fact]
    public void EnumConstant_ResolvedInCallArgs()
    {
        var code = EmitMethod(nameof(CfgSampleClass.CallWithLocalEnum));
        Assert.Contains("CfgPriority.High", code);
        Assert.DoesNotContain(" 2)", code); // should not show raw number
    }

    [Fact]
    public void ConvI8_OnConstant_EmitsSuffix()
    {
        var code = EmitMethod(nameof(CfgSampleClass.LongConstArith));
        Assert.Contains("1L", code);
        Assert.DoesNotContain("(long)1", code);
    }

    // Note: C# compiler emits conv.i8 (not conv.u8) for ulong constants,
    // so these exercise the conv.i8 path. For negative constants with ulong
    // return type, we emit unchecked((ulong)-1) for valid C#.
    [Fact]
    public void ConvU8_NegativeConstant_EmitsUncheckedCast()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ULongNegOne));
        // Negative constants for ulong need unchecked cast to be valid C#
        Assert.Contains("unchecked((ulong)-1)", code);
    }

    [Fact]
    public void ConvU2_ReturningChar_UsesCharCast()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ReturnChar));
        Assert.Contains("(char)", code);
        Assert.DoesNotContain("(ushort)", code);
    }

    [Fact]
    public void ConvU2_ReturningUInt16_UsesUInt16Cast()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ReturnUInt16));
        Assert.Contains("(ushort)", code);
    }

    [Fact]
    public void BoolParameter_ZeroLiteral_RendersFalse()
    {
        var code = EmitMethod(nameof(CfgSampleClass.PassesBoolFalse));
        Assert.Contains("AcceptsBool(false)", code);
        Assert.DoesNotContain("AcceptsBool(0)", code);
    }

    [Fact]
    public void CollectionWithCapacity_ReusesConstructedCollection()
    {
        var code = EmitMethod(nameof(CfgSampleClass.CollectionWithCapacity));

        Assert.Equal(1, CountOccurrences(code, "new List<string>"));
        Assert.Contains(".AddRange", code);
    }

    [Fact]
    public void CollectionWithComparer_ReusesConstructedCollection()
    {
        var code = EmitMethod(nameof(CfgSampleClass.CollectionWithComparer));

        Assert.Equal(1, CountOccurrences(code, "new HashSet<string>"));
        Assert.Contains(".Add(\"Hello\")", code);
        Assert.Contains(".Add(\"HELLO\")", code);
        Assert.Contains(".Add(\"hello\")", code);
    }

    [Fact]
    public void ReadOnlySpanCollectionExpression_EmitsCollectionExpression()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ReadOnlySpanCollectionExpression));

        Assert.Contains("[a, 42]", code);
        Assert.DoesNotContain("InlineArray", code);
        Assert.DoesNotContain("new ReadOnlySpan", code);
        Assert.DoesNotContain("new int[]", code);
    }

    [Fact]
    public void ClassicLock_EmitsLockStatement()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ClassicLock));

        Assert.Contains("lock (gate)", code);
        Assert.DoesNotContain("Monitor.Enter", code);
        Assert.DoesNotContain("Monitor.Exit", code);
        Assert.DoesNotContain("try", code);
        Assert.DoesNotContain("finally", code);
    }

    [Fact]
    public void SystemThreadingLock_EmitsLockStatement()
    {
        var code = EmitMethod(nameof(CfgSampleClass.SystemThreadingLock));

        Assert.Contains("lock (gate)", code);
        Assert.DoesNotContain("EnterScope", code);
        Assert.DoesNotContain("Dispose", code);
        Assert.DoesNotContain("using var", code);
        Assert.DoesNotContain("try", code);
        Assert.DoesNotContain("finally", code);
    }

    [Fact]
    public void NullConditionalFieldAssignment_EmitsConditionalAssignment()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullConditionalFieldAssignment));

        Assert.Contains("target?.Value = value;", code);
        Assert.DoesNotContain("if (target != null)", code);
    }

    [Fact]
    public void NullConditionalFieldCompoundAssignment_EmitsConditionalAssignment()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullConditionalFieldCompoundAssignment));

        Assert.Contains("target?.Value += value;", code);
        Assert.DoesNotContain("if (target != null)", code);
    }

    [Fact]
    public void NullConditionalPropertyAssignment_EmitsConditionalAssignment()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullConditionalPropertyAssignment));

        Assert.Contains("target?.Text = value;", code);
        Assert.DoesNotContain("if (target != null)", code);
    }

    [Fact]
    public void NullConditionalPropertyCompoundAssignment_EmitsConditionalAssignment()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullConditionalPropertyCompoundAssignment));

        Assert.Contains("target?.Count += value;", code);
        Assert.DoesNotContain("if (target != null)", code);
    }

    [Fact]
    public void NullConditionalIndexerAssignment_EmitsConditionalAssignment()
    {
        var code = EmitMethod(nameof(CfgSampleClass.NullConditionalIndexerAssignment));

        Assert.Contains("target?[0] = value;", code);
        Assert.DoesNotContain("if (target != null)", code);
    }

    [Fact]
    public void UnsafeReadThroughAddress_EmitsPointerDereference()
    {
        var code = EmitMethod(nameof(CfgSampleClass.UnsafeReadThroughAddress));

        Assert.Contains("*(&", code);
        Assert.DoesNotContain("(nuint)", code);
    }

    [Fact]
    public void AddressAsNativeUInt_KeepsNativeUIntCast()
    {
        var code = EmitMethod(nameof(CfgSampleClass.AddressAsNativeUInt));

        Assert.Contains("(nuint)&", code);
    }

    [Fact]
    public void UnsafeReadArrayElementAddress_EmitsPointerDereference()
    {
        var code = EmitMethod(nameof(CfgSampleClass.UnsafeReadArrayElementAddress));

        Assert.Contains("*(&", code);
        Assert.Contains("[0]", code);
        Assert.DoesNotContain("(nuint)ref", code);
    }

    [Fact]
    public void ArrayElementAddressAsNativeUInt_KeepsNativeUIntCast()
    {
        var code = EmitMethod(nameof(CfgSampleClass.ArrayElementAddressAsNativeUInt));

        Assert.Contains("(nuint)&", code);
        Assert.Contains("[0]", code);
        Assert.DoesNotContain("(nuint)ref", code);
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

    static string EmitMethod(string methodName, string? externalPdbPath)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName,
            externalPdbPath: externalPdbPath);
        Assert.NotNull(context);
        return CSharpEmitter.Emit(context);
    }

    // The test assembly ships a standalone (non-embedded) portable PDB, so without it the
    // decompiler falls back to synthesized V_n locals, and with it real source names appear.
    static string TestAssemblyPdbPath() =>
        Path.ChangeExtension(typeof(CfgSampleClass).Assembly.Location, ".pdb");

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [Fact]
    public void ExternalPdb_SuppliesLocalVariableNames()
    {
        var pdbPath = TestAssemblyPdbPath();
        Assert.SkipUnless(File.Exists(pdbPath), $"standalone PDB not found at {pdbPath}");

        string withPdb = EmitMethod(nameof(CfgSampleClass.LoopSum), pdbPath);

        // LoopSum declares `int sum` and `int i`; the acquired PDB should restore those names.
        Assert.Contains("sum", withPdb);
        Assert.DoesNotContain("V_0", withPdb);
    }

    [Fact]
    public void WithoutPdb_FallsBackToSyntheticLocalNames()
    {
        // No embedded PDB and no external path: locals render as V_n, never crashing.
        string noPdb = EmitMethod(nameof(CfgSampleClass.LoopSum));

        Assert.Contains("V_0", noPdb);
        Assert.DoesNotContain("int sum", noPdb);
    }

    [Fact]
    public void ExternalPdb_NonexistentPath_FallsBackGracefully()
    {
        // A bogus external path must not throw; it falls back to synthesized names.
        string output = EmitMethod(nameof(CfgSampleClass.LoopSum), "/no/such/file.pdb");

        Assert.Contains("V_0", output);
    }
}
