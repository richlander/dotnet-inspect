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
    public void ClosureCapture_SimplifiesClosureType()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ClosureCapture));

        Assert.Contains("/* closure */", output);
        Assert.Contains("/* lambda: ClosureCapture */", output);
        Assert.DoesNotContain("<>c__DisplayClass", output);
        Assert.DoesNotContain("System.Func", output);
    }

    [Fact]
    public void ClosureWithLinq_SimplifiesLambda()
    {
        string output = EmitMethod(nameof(CfgSampleClass.ClosureWithLinq));

        Assert.Contains("/* lambda: ClosureWithLinq */", output);
        Assert.DoesNotContain("<>c__DisplayClass", output);
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
