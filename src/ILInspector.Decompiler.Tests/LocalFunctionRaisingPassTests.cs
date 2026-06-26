using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LocalFunctionRaisingPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [s_int, s_int]);

    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void StaticLocalFunctionCalledTwice_CollapsesToOneDeclaration()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StaticLocalFunctionCalledTwice));

        Assert.Contains("Twice(x)", output);                              // both calls rendered unqualified
        Assert.Contains("Twice(x + 1)", output);
        Assert.Contains("static int Twice(int v) => v * 2;", output);     // declaration emitted
        // Each call site carries its own (reference-unequal) MethodRef, so a per-Callee
        // grouping would emit one declaration per call. Recovery must collapse to one.
        Assert.Equal(1, CountOccurrences(output, "int Twice(int v)"));
        Assert.DoesNotContain("g__", output);
        Assert.DoesNotContain("CfgSampleClass.Twice", output);
    }

    [Fact]
    public void StaticLocalFunctionWithLocal_RaisesWithNestedLocalScope()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StaticLocalFunctionWithLocal));

        Assert.Contains("return SquarePlusOne(x);", output);
        Assert.Contains("static int SquarePlusOne(int v)", output);
        Assert.Contains(" = v + 1;", output);
        Assert.Contains("return ", output);
        Assert.Contains(" * ", output);
        Assert.DoesNotContain("g__", output);
        Assert.DoesNotContain("CfgSampleClass.SquarePlusOne", output);
    }

    [Fact]
    public void CapturingLocalFunctionCalledTwice_RecoversSingleDeclarationAcrossBothCalls()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingCalledTwice));

        Assert.Contains("Add(5)", output);                       // both calls drop the ref-env argument
        Assert.Contains("Add(7)", output);
        Assert.Contains("int Add(int v) => v + n;", output);     // captured `n` substituted; env param gone
        Assert.Equal(1, CountOccurrences(output, "int Add(int v)"));
        Assert.DoesNotContain("static int Add", output);         // capturing local function is not static (CS8421)
        Assert.DoesNotContain("DisplayClass", output);           // environment elided
    }

    [Fact]
    public void CapturingLocalFunctionWithLocal_StaysLowered()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingLocalFunctionWithLocal));

        Assert.Contains("DisplayClass", output);
        Assert.DoesNotContain("int AddSquare(int v)", output);
    }

    [Fact]
    public void SharedEnvironmentLocalFunctions_StayLowered()
    {
        // Two local functions share one display-class environment, so neither
        // resolves to a clean single-use environment — recovery must not fire.
        string output = PrintRaised(nameof(CfgSampleClass.SharedEnvironmentLocalFunctions));

        Assert.Contains("DisplayClass", output);                 // environment not elided
        Assert.DoesNotContain("int Add(int v) =>", output);      // no recovered declaration
        Assert.DoesNotContain("int Mul(int v) =>", output);
    }

    [Fact]
    public void RecursiveLocalFunction_StaysLowered()
    {
        // The body calls itself; keeping the import non-recursive bars recovery.
        string output = PrintRaised(nameof(CfgSampleClass.RecursiveLocalFunction));

        Assert.DoesNotContain("int Fact(int v) =>", output);     // no recovered declaration
        Assert.Contains("Fact(n)", output);                      // call left as the lowered invocation
    }

    [Fact]
    public void CapturingAfterMutation_StaysLowered()
    {
        // The captured local is mutated, so the environment field is written from a
        // read of itself (not a clean capture value) — recovery must not fire.
        string output = PrintRaised(nameof(CfgSampleClass.CapturingAfterMutation));

        Assert.Contains("DisplayClass", output);                 // environment not elided
        Assert.DoesNotContain("int Add(int v) =>", output);      // no recovered declaration
    }

    [Fact]
    public void LocalFunctionNameLookalikeWithoutCompilerGeneratedMetadata_StaysCall()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var method = new MethodRef(
            TypeRef.Definition("UserAssembly", "Samples", "Owner"),
            "<M>g__Helper|0_0",
            intType,
            [intType],
            HasThis: false)
        {
            CompilerGenerated = MetadataFactState.No,
        };
        var function = FunctionReturningCall(method, intType);
        var body = LocalFunctionBody(method, intType);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: m => m == method ? body : null);

        new LocalFunctionRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<LocalFunctionStatement>());
        Assert.Empty(function.Descendants.OfType<LocalFunctionInvocation>());
        Assert.Single(function.Descendants.OfType<Call>());
        function.CheckInvariant();
    }

    [Fact]
    public void EnclosingMethodNameContainingGMarker_DemanglesLocalFunctionName()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var method = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            "<Ag__B>g__Local|0_0",
            intType,
            [intType],
            HasThis: false)
        {
            CompilerGenerated = MetadataFactState.Yes,
        };
        var function = FunctionReturningCall(method, intType);
        var body = LocalFunctionBody(method, intType);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: m => m == method ? body : null);

        new LocalFunctionRaisingPass().Run(function, context);

        var invocation = Assert.Single(function.Descendants.OfType<LocalFunctionInvocation>());
        Assert.Equal("Local", invocation.Name);
        var declaration = Assert.Single(function.Descendants.OfType<LocalFunctionStatement>());
        Assert.Equal("Local", declaration.Name);

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("return Local(x);", output);
        Assert.Contains("static int Local(int x)", output);
        Assert.DoesNotContain("B>g__Local", output);
        function.CheckInvariant();
    }

    [Fact]
    public void LambdaLocalFunctionImportCycle_DeclinesInsteadOfReenteringPipeline()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var holder = TypeRef.Definition("Synthetic", "Samples", "Owner+<>c");
        var localMethod = new MethodRef(
            owner,
            "<M>g__Helper|0_0",
            s_int,
            [s_int],
            HasThis: false)
        {
            CompilerGenerated = MetadataFactState.Yes,
        };
        var lambdaMethod = new MethodRef(
            holder,
            "<M>b__0_0",
            s_int,
            [s_int],
            HasThis: true)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
        var function = FunctionReturningCall(localMethod, s_int);
        int localImports = 0;
        int lambdaImports = 0;
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method =>
            {
                if (method == localMethod)
                {
                    localImports++;
                    return LocalFunctionBodyCreatingLambda(localMethod, lambdaMethod);
                }
                if (method == lambdaMethod)
                {
                    lambdaImports++;
                    return LambdaBodyCallingLocalFunction(lambdaMethod, localMethod);
                }
                return null;
            });

        new LocalFunctionRaisingPass().Run(function, context);

        Assert.Equal(1, localImports);
        Assert.Equal(1, lambdaImports);
        function.CheckInvariant();
    }

    [Fact]
    public void CapturingLocalFunctionCallBeforeCaptureStore_StaysLowered()
    {
        var (function, context) = CapturingLocalFunctionOrderFixture(storeBeforeCall: false);

        new LocalFunctionRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<LocalFunctionStatement>());
        Assert.Empty(function.Descendants.OfType<LocalFunctionInvocation>());
        Assert.Single(function.Descendants.OfType<StoreField>());
        Assert.Single(function.Descendants.OfType<Call>(), call => GeneratedCodeIdentity.IsLocalFunctionMethod(call.Callee));
        function.CheckInvariant();
    }

    [Fact]
    public void CapturingLocalFunctionCaptureStoreBeforeCall_StillRaises()
    {
        var (function, context) = CapturingLocalFunctionOrderFixture(storeBeforeCall: true);

        new LocalFunctionRaisingPass().Run(function, context);

        Assert.Single(function.Descendants.OfType<LocalFunctionStatement>());
        Assert.Single(function.Descendants.OfType<LocalFunctionInvocation>());
        Assert.Empty(function.Descendants.OfType<StoreField>());
        function.CheckInvariant();
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    static (IrFunction Function, PassContext Context) CapturingLocalFunctionOrderFixture(bool storeBeforeCall)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var envType = TypeRef.Definition("Synthetic", "Samples", "<>c__DisplayClass0_0", ValueTypeHint.ValueType);
        var byRefEnv = TypeRef.ByRef(envType);
        var field = new FieldRef(envType, "x", s_int);
        var method = new MethodRef(
            owner,
            "<M>g__Local|0_0",
            s_int,
            [byRefEnv],
            HasThis: false)
        {
            CompilerGenerated = MetadataFactState.Yes,
        };

        var captureStore = new StoreField(field, new LoadLocalAddress(0, envType), new LoadLocal(1, s_int));
        var call = new ExpressionStatement(new Call(method, isVirtual: false, [new LoadLocalAddress(0, envType)]));
        var block = new Block();
        block.Add(new StoreLocal(1, s_int, new Constant(42, s_int)));
        if (storeBeforeCall)
        {
            block.Add(captureStore);
            block.Add(call);
        }
        else
        {
            block.Add(call);
            block.Add(captureStore);
        }
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [envType, s_int],
            body);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: imported => imported == method ? CapturingLocalFunctionBody(method, field, byRefEnv) : null);
        return (function, context);
    }

    static IrFunction CapturingLocalFunctionBody(MethodRef method, FieldRef field, TypeRef byRefEnv)
    {
        var block = new Block();
        block.Add(new Return(new LoadField(field, new LoadArgument(0, "env", byRefEnv))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            method.Name,
            method.DeclaringType,
            new MethodSignature(method.ReturnType, [new Parameter("env", byRefEnv)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    [Fact]
    public void StaticLocalFunction_RecoveredAsDeclarationAndUnqualifiedCall()
    {
        string output = PrintRaised(nameof(CfgSampleClass.DoubleViaLocalFunction));

        Assert.Contains("return Twice(x);", output);                  // call rendered unqualified
        Assert.Contains("static int Twice(int v) => v * 2;", output); // declaration emitted
        Assert.DoesNotContain("g__", output);                          // no synthesized name
        Assert.DoesNotContain("CfgSampleClass.Twice", output);         // not the qualified mis-binding
    }

    [Fact]
    public void CapturingLocalFunction_SubstitutesEnvironmentAndDropsRefParameter()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingLocalFunction));

        Assert.Contains("return Add(5);", output);              // call drops the ref-env argument
        Assert.Contains("int Add(int v) => v + n;", output);    // captured `n` substituted; env param gone
        Assert.DoesNotContain("static int Add", output);        // capturing local function is not static (CS8421)
        Assert.DoesNotContain("DisplayClass", output);          // environment elided
    }

    static IrFunction FunctionReturningCall(MethodRef method, TypeRef intType)
    {
        var block = new Block();
        block.Add(new Return(new Call(method, isVirtual: false, [new LoadArgument(0, "x", intType)])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            method.DeclaringType,
            new MethodSignature(method.ReturnType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction LocalFunctionBody(MethodRef method, TypeRef intType)
    {
        var block = new Block();
        block.Add(new Return(new Binary(
            BinaryKind.Multiply,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "x", intType),
            new Constant(2, intType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            method.Name,
            method.DeclaringType,
            new MethodSignature(method.ReturnType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction LocalFunctionBodyCreatingLambda(MethodRef localMethod, MethodRef lambdaMethod)
    {
        var block = new Block();
        block.Add(new ExpressionStatement(new DelegateCreation(
            s_func,
            lambdaMethod,
            isVirtual: false,
            new Constant(null, TypeRef.CoreLib("System", "Object")))));
        block.Add(new Return(new LoadArgument(0, "x", s_int)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            localMethod.Name,
            localMethod.DeclaringType,
            new MethodSignature(s_int, [new Parameter("x", s_int)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction LambdaBodyCallingLocalFunction(MethodRef lambdaMethod, MethodRef localMethod)
    {
        var block = new Block();
        block.Add(new Return(new Call(
            localMethod,
            isVirtual: false,
            [new LoadArgument(1, "x", s_int)])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            lambdaMethod.Name,
            lambdaMethod.DeclaringType,
            new MethodSignature(s_int, [new Parameter("x", s_int)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    [Fact]
    public void CapturingSecondParameter_SubstitutesDespiteIndexCollision()
    {
        // The captured value is host argument 1, which shares the numeric index of
        // the synthesized env parameter; the substituted value must not be mistaken
        // for an unresolved environment read.
        string output = PrintRaised(nameof(CfgSampleClass.CaptureSecondParam));

        Assert.Contains("return Add(5);", output);
        Assert.Contains("int Add(int v) => v + n;", output);
        Assert.DoesNotContain("DisplayClass", output);
        Assert.DoesNotContain("ref ", output);
    }

    [Fact]
    public void CapturingTwoVariables_SubstitutesEveryCapturedField()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CaptureTwoVariables));

        Assert.Contains("return Add(5);", output);
        Assert.Contains("v + a", output);
        Assert.Contains("b", output);
        Assert.DoesNotContain("DisplayClass", output);
        Assert.DoesNotContain("ref ", output);
    }

    [Fact]
    public void CaptureReassignedAfterCall_DeclinesToRaise()
    {
        // Reassigning the captured variable after the call means no single
        // substituted value is live at the call site — the raise must stay honest.
        string output = PrintRaised(nameof(CfgSampleClass.CaptureReassignedAfterCall));

        Assert.DoesNotContain("int Add(int v) =>", output);     // not raised to a local function
        Assert.Contains("DisplayClass", output);                // honest fallback to the lowered form
    }

    [Fact]
    public void CaptureReassignedBeforeCall_DeclinesToRaise()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CaptureReassignedBeforeCall));

        Assert.DoesNotContain("int Add(int v) =>", output);
        Assert.Contains("DisplayClass", output);
    }
}
