using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LambdaRaisingPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [s_int, s_int]);

    static string PrintRaised(string methodName, Type? fixtureType = null)
    {
        var type = fixtureType ?? typeof(CfgSampleClass);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void NonCapturingExpressionBody_RaisesSimpleLambda()
        => Assert.Equal("return x => x + 1;", PrintRaised(nameof(CfgSampleClass.NonCapturingLambda)));

    [Fact]
    public void NonCapturingStatementBody_RaisesBlockLambda()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StatementBodyLambda));

        Assert.Contains("return x => {", output);
        Assert.Contains("Console.WriteLine(x);", output);
        Assert.Contains("return x + 1;", output);
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void CapturingParameterLocalBearingBody_RaisesBlockLambdaWithNestedLocalScope()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingLocalBodyLambda));

        Assert.Contains("return x => {", output);
        Assert.Contains(" = x + n;", output);
        Assert.Contains("return ", output);
        Assert.Contains(" * ", output);
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void CapturingOuterLocalBearingBody_StaysLowered()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingOuterLocalBodyLambda));

        Assert.DoesNotContain("=>", output);
        Assert.Contains("new Func", output);
    }

    // A captured variable mutated after the lambda is created: the display-class
    // field is stored twice. The environment is shared by reference, so a call
    // before the second store must see the first value. Eliding both stores and
    // substituting one value would miscompile (every call reads the last value,
    // and the mutation itself vanishes), so the environment must stay lowered.
    // A branch/loop keeps the display class in a local even in Release, so this
    // reproduces in the shipped compiler mode, not only in Debug.
    [Fact]
    public void CaptureMutatedInBranch_StaysLowered()
    {
        string output = PrintRaised(
            nameof(ClosureMutationAdversarialSamples.MutatedCaptureInBranch),
            typeof(ClosureMutationAdversarialSamples));

        Assert.DoesNotContain("=>", output);   // not raised to a lambda...
        Assert.Contains("new Func", output);   // ...the delegate creation survives
        Assert.Contains("= q", output);        // and the mutating store is preserved, not elided
    }

    [Fact]
    public void CaptureMutatedInLoop_StaysLowered()
    {
        string output = PrintRaised(
            nameof(ClosureMutationAdversarialSamples.MutatedCaptureInLoop),
            typeof(ClosureMutationAdversarialSamples));

        Assert.DoesNotContain("=>", output);
        Assert.Contains("new Func", output);
        Assert.Contains("= q", output);
    }

    [Fact]
    public void CapturingExpressionBody_SubstitutesCaptureAndRaisesLambda()
        => Assert.Equal("return x => x + n;", PrintRaised(nameof(CfgSampleClass.CapturingLambda)));

    [Fact]
    public void MultipleCaptures_AllSubstitutedIntoRaisedBody()
        => Assert.Equal("return x => (x + a) - b;", PrintRaised(nameof(CfgSampleClass.TwoCaptureLambda)));

    [Fact]
    public void LocalBearingBody_RaisesBlockLambdaWithNestedLocalScope()
    {
        string output = PrintRaised(nameof(CfgSampleClass.LocalBodyLambda));

        Assert.Contains("return x => {", output);
        Assert.Contains(" = x + 1;", output);
        Assert.Contains("return ", output);
        Assert.Contains(" * ", output);
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void LocalDisplayClassEnvironment_RaisesLambdaAndElidesSetup()
    {
        string output = PrintRaised(nameof(CfgSampleClass.InvokeLocalCapture));

        Assert.Contains("x => x + n", output);
        Assert.DoesNotContain("DisplayClass", output);   // allocation + capture store elided
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void SharedDisplayClassEnvironment_RaisesEveryLambda()
    {
        string output = PrintRaised(nameof(CfgSampleClass.SharedCaptureLambdas));

        Assert.Contains("x => x + n", output);
        Assert.Contains("y => y - n", output);
        Assert.DoesNotContain("DisplayClass", output);
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void LambdaNameLookalikeWithoutCompilerGeneratedMetadata_IsNotRaised()
    {
        var lambdaMethod = new MethodRef(
            TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c"),
            "<M>b__0_0",
            s_int,
            [s_int],
            HasThis: true);
        var function = FunctionReturningDelegate(lambdaMethod);
        var lambdaBody = LambdaBody(lambdaMethod);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => method == lambdaMethod ? lambdaBody : null);

        new LambdaRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        function.CheckInvariant();
    }

    [Fact]
    public void RecursiveLambdaImport_DeclinesInsteadOfReenteringPipeline()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Outer+<>c");
        var lambdaMethod = new MethodRef(
            holder,
            "<M>b__0_0",
            s_int,
            [s_int],
            HasThis: true)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
        var function = FunctionReturningDelegate(lambdaMethod);
        int imports = 0;
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method =>
            {
                if (method != lambdaMethod)
                    return null;
                imports++;
                return RecursiveLambdaBody(lambdaMethod);
            });

        new LambdaRaisingPass().Run(function, context);

        Assert.Equal(1, imports);
        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        function.CheckInvariant();
    }

    // #1358: a local <>c__DisplayClass whose capture store runs AFTER the delegate
    // is created. Eliding the store and substituting its value would make the raised
    // lambda read the later value, but the lowered delegate observes the field's
    // prior value (the environment is shared by reference). The setup is not a
    // straight-line prefix, so the environment must stay lowered.
    [Fact]
    public void DelegateCreatedBeforeCaptureStore_StaysLowered()
    {
        var (function, lambdaMethod, lambdaBody) = BuildLocalCaptureSetup(storeBeforeCreate: false);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => method == lambdaMethod ? lambdaBody : null);

        new LambdaRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<Lambda>());          // not raised...
        Assert.Single(function.Descendants.OfType<DelegateCreation>()); // ...creation survives
        Assert.Single(function.Descendants.OfType<StoreField>());      // ...capture store preserved
        function.CheckInvariant();
    }

    // Positive twin: the same nodes in the normal compiler order (alloc; capture
    // store; create delegate) are a straight-line prefix and still raise + elide.
    [Fact]
    public void CaptureStoreBeforeDelegateCreation_RaisesLambda()
    {
        var (function, lambdaMethod, lambdaBody) = BuildLocalCaptureSetup(storeBeforeCreate: true);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => method == lambdaMethod ? lambdaBody : null);

        new LambdaRaisingPass().Run(function, context);

        Assert.Single(function.Descendants.OfType<Lambda>());        // raised...
        Assert.Empty(function.Descendants.OfType<DelegateCreation>()); // ...creation replaced
        Assert.Empty(function.Descendants.OfType<StoreField>());     // ...capture store elided
        function.CheckInvariant();
    }

    static readonly TypeRef s_func1 = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`1"), [s_int]);

    static (IrFunction Function, MethodRef Lambda, IrFunction LambdaBody) BuildLocalCaptureSetup(bool storeBeforeCreate)
    {
        var outer = TypeRef.Definition("Synthetic", "Samples", "Outer");
        var dcType = TypeRef.Definition("Synthetic", "Samples", "Outer+<>c__DisplayClass0_0");
        var lambdaMethod = new MethodRef(dcType, "<M>b__0", s_int, [], HasThis: true)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
        var dcCtor = new MethodRef(dcType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var xField = new FieldRef(dcType, "x", s_int);
        var invokeMethod = new MethodRef(s_func1, "Invoke", s_int, [], HasThis: true);

        var storeValue = new StoreLocal(1, s_int, new Constant(42, s_int));
        var alloc = new StoreLocal(0, dcType, new NewObject(dcCtor, []));
        var captureStore = new StoreField(xField, new LoadLocal(0, dcType), new LoadLocal(1, s_int));
        var creation = new StoreLocal(2, s_func1,
            new DelegateCreation(s_func1, lambdaMethod, isVirtual: false, new LoadLocal(0, dcType)));
        var invoke = new ExpressionStatement(new Call(invokeMethod, isVirtual: true, [new LoadLocal(2, s_func1)]));

        var block = new Block();
        block.Add(storeValue);
        block.Add(alloc);
        if (storeBeforeCreate)
        {
            block.Add(captureStore);
            block.Add(creation);
            block.Add(invoke);
        }
        else
        {
            block.Add(creation);
            block.Add(invoke);
            block.Add(captureStore);
        }
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            outer,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [dcType, s_int, s_func1],
            body);

        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(new LoadField(xField, new LoadArgument(0, "this", dcType))));
        var lambdaContainer = new BlockContainer();
        lambdaContainer.Add(lambdaBlock);
        var lambdaBody = new IrFunction(
            lambdaMethod.Name,
            dcType,
            new MethodSignature(s_int, [], HasThis: true, GenericParameterCount: 0),
            [],
            lambdaContainer);

        return (function, lambdaMethod, lambdaBody);
    }

    static IrFunction FunctionReturningDelegate(MethodRef method)
    {
        var block = new Block();
        block.Add(new Return(new DelegateCreation(
            s_func,
            method,
            isVirtual: false,
            new Constant(null, TypeRef.CoreLib("System", "Object")))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            new MethodSignature(s_func, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction LambdaBody(MethodRef method)
    {
        var block = new Block();
        block.Add(new Return(new LoadArgument(1, "x", s_int)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            method.Name,
            method.DeclaringType,
            new MethodSignature(s_int, [new Parameter("x", s_int)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction RecursiveLambdaBody(MethodRef method)
    {
        var block = new Block();
        block.Add(new ExpressionStatement(new DelegateCreation(
            s_func,
            method,
            isVirtual: false,
            new Constant(null, TypeRef.CoreLib("System", "Object")))));
        block.Add(new Return(new LoadArgument(0, "this", method.DeclaringType)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            method.Name,
            method.DeclaringType,
            new MethodSignature(s_int, [new Parameter("x", s_int)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }
}

// Negative fixtures for LambdaRaisingPass: a captured variable mutated after the
// lambda is created. The display class must stay lowered — substituting one
// captured value and eliding the stores would lose the closure's by-reference
// semantics. Kept out of CfgSampleClass because the lowered form renders the
// raw <>c__DisplayClass names the fidelity gate cannot recompile.
public static class ClosureMutationAdversarialSamples
{
    public static int MutatedCaptureInBranch(int p, int q, bool c)
    {
        int x = p;
        System.Func<int> f = () => x;
        int a = f();
        if (c) x = q;
        int b = f();
        return a * 100 + b;
    }

    public static int MutatedCaptureInLoop(int p, int q)
    {
        int x = p;
        System.Func<int> f = () => x;
        int sum = 0;
        for (int i = 0; i < 2; i++) { sum += f(); x = q; }
        return sum;
    }
}
