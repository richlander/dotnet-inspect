using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
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

    [Theory]
    [InlineData("Microsoft.CodeAnalysis.CSharp.ConversionsBase", "GetExplicitTupleLiteralConversion")]
    [InlineData("Microsoft.CodeAnalysis.CSharp.ConversionsBase", "GetImplicitTupleLiteralConversion")]
    [InlineData("Microsoft.CodeAnalysis.CSharp.MethodTypeInferrer", "FixDependentParameters")]
    [InlineData("Microsoft.CodeAnalysis.CSharp.MethodTypeInferrer", "FixNondependentParameters")]
    public void RoslynCachedLambdaDiamonds_RemainFullyRaised(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(CSharpCompilation).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(
            function!,
            method => IrImporter.Import(source, method));

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        Assert.Contains("=>", result.Output);
        Assert.DoesNotContain("___c", result.Output);
    }

    [Fact]
    public void NonCapturingExpressionBody_RaisesSimpleLambda()
        => Assert.Equal("return x => x + 1;", PrintRaised(nameof(CfgSampleClass.NonCapturingLambda)));

    [Fact]
    public void NonCapturingStatementBody_RaisesBlockLambda()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StatementBodyLambda));

        // A multi-statement block body now expands across lines, matching how
        // every other statement block prints (issue #2952), instead of staying
        // collapsed onto the lambda's own line.
        Assert.Contains("return x =>\n{", output);
        Assert.Contains("Console.WriteLine(x);", output);
        Assert.Contains("return x + 1;", output);
        Assert.DoesNotContain("new Func", output);
    }

    [Fact]
    public void CapturingParameterLocalBearingBody_RaisesBlockLambdaWithNestedLocalScope()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingLocalBodyLambda));

        Assert.Contains("return x =>\n{", output);
        Assert.Contains(" = x + n;", output);
        Assert.Contains("return ", output);
        Assert.Contains(" * ", output);
        Assert.DoesNotContain("new Func", output);
    }

    // The printer #2952 shape at a deeper nesting level: a multi-statement
    // lambda block body returned from inside an `if`, one indent level below
    // the method body. The expanded block's braces must align to the
    // *enclosing statement's* own indentation (4 spaces, matching the `if`
    // body) rather than always aligning to column 0.
    [Fact]
    public void MultiStatementLambda_InsideNestedIf_AlignsBracesToEnclosingStatementIndent()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StatementBodyLambdaInsideIf));

        Assert.Contains(
            "    return x =>\n" +
            "    {\n" +
            "        Console.WriteLine(x);\n" +
            "        return x + 1;\n" +
            "    };",
            output);
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
        => Assert.Equal("return x => x + a - b;", PrintRaised(nameof(CfgSampleClass.TwoCaptureLambda)));

    [Fact]
    public void LocalBearingBody_RaisesBlockLambdaWithNestedLocalScope()
    {
        string output = PrintRaised(nameof(CfgSampleClass.LocalBodyLambda));

        Assert.Contains("return x =>\n{", output);
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

    // #2945: a hoisted field read in the outer body (`if (map is null)`) is
    // substituted back to the captured source, so the whole environment elides
    // and the lambda raises — the SetAction/GetCompletions CS1001 closure-stall.
    [Fact]
    public void CapturedFieldReadInOuterBody_ElidesEnvironmentAndSubstitutesRead()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturedParamReadInOuterBody));

        Assert.Contains("if (map is null)", output);        // outer read substituted map <- V_0.map
        Assert.Contains("=>", output);                       // lambda raised
        Assert.DoesNotContain("DisplayClass", output);       // environment elided
        Assert.DoesNotContain("new Func", output);
        Assert.DoesNotContain("b__", output);                // no un-raised lambda-method reference
    }

    [Fact]
    public void CapturingVoidExpressionBody_RaisesAndElidesLocalEnvironment()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.CapturingVoidExpressionLambda),
            typeof(VoidLambdaRaisingSamples));

        Assert.Contains("Action<int> action = x => Console.WriteLine(x + n);", output);
        Assert.Contains("consume.Invoke(action, n);", output);
        Assert.DoesNotContain("DisplayClass", output);
        Assert.DoesNotContain("new Action", output);
        Assert.DoesNotContain("b__", output);
    }

    [Fact]
    public void CapturingVoidStatementBody_RaisesWithoutImplicitReturn()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.CapturingVoidStatementLambda),
            typeof(VoidLambdaRaisingSamples));

        Assert.Contains("return x =>\n{", output);
        Assert.Contains("Console.WriteLine(x);", output);
        Assert.Contains("Console.WriteLine(n);", output);
        Assert.DoesNotContain("return;", output);
        Assert.DoesNotContain("new Action", output);
    }

    [Fact]
    public void EmptyVoidBody_RaisesAsEmptyLambda()
        => Assert.Equal(
            "return () => { };",
            PrintRaised(nameof(VoidLambdaRaisingSamples.EmptyVoidLambda), typeof(VoidLambdaRaisingSamples)));

    [Fact]
    public void DiscardedNonVoidCall_StaysBlockBodied()
        => Assert.Equal(
            "return () => { Value(); };",
            PrintRaised(nameof(VoidLambdaRaisingSamples.DiscardedNonVoidCall), typeof(VoidLambdaRaisingSamples)));

    [Fact]
    public void DiscardedPropertyRead_StaysBlockBodiedAndExplicit()
        => Assert.Equal(
            "return () => { _ = Environment.ProcessId; };",
            PrintRaised(nameof(VoidLambdaRaisingSamples.DiscardedPropertyRead), typeof(VoidLambdaRaisingSamples)));

    [Fact]
    public void CustomDelegateInObjectSink_PreservesDelegateIdentity()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.CustomDelegateInObjectSink),
            typeof(VoidLambdaRaisingSamples));

        Assert.Contains("return (VoidCallback)(() => { });", output);
        Assert.DoesNotContain("new VoidCallback", output);
    }

    [Fact]
    public void ActionInObjectSink_PreservesDelegateIdentityAndTargetTyping()
        => Assert.Equal(
            "return (Action<int>)(x => Console.WriteLine(x));",
            PrintRaised(nameof(VoidLambdaRaisingSamples.ActionInObjectSink), typeof(VoidLambdaRaisingSamples)));

    [Fact]
    public void ActionOverloadArgument_PreservesExactDelegateType()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.ActionOverloadArgument),
            typeof(VoidLambdaRaisingSamples));

        Assert.Contains("Pick((Action<int>)(x => Console.WriteLine(x)));", output);
    }

    [Fact]
    public void NestedWeakReturn_UsesEnclosingLambdaReturnType()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.NestedWeakReturn),
            typeof(VoidLambdaRaisingSamples));

        Assert.Contains("() => (Action<int>)(x => Console.WriteLine(x))", output);
    }

    [Fact]
    public void AsyncVoidLambda_StaysLoweredWithoutAsyncLambdaSupport()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.AsyncVoidLambda),
            typeof(VoidLambdaRaisingSamples));

        Assert.DoesNotContain("=>", output);
        Assert.Contains("new Action", output);
    }

    [Fact]
    public void ByRefVoidLambda_RaisesWithExplicitRefParameter()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.ByRefVoidLambda),
            typeof(VoidLambdaRaisingSamples));

        Assert.Equal("return (ref int value) => Console.WriteLine(value);", output);
        AssertRefLambdaCompiles(output);
    }

    [Fact]
    public void RefReadonlyLambda_StaysLoweredUntilDeclarationKindIsRepresentable()
    {
        using var source = MetadataSource.Open(typeof(VoidLambdaRaisingSamples).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(VoidLambdaRaisingSamples).FullName!,
            nameof(VoidLambdaRaisingSamples.RefReadonlyVoidLambda));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(
            function!,
            method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        var creation = Assert.Single(function!.Descendants.OfType<DelegateCreation>());
        Assert.Equal(ParameterRefKindFacts.Known, creation.Method.ParameterRefKindsFacts);
        Assert.True(creation.Method.HasRefReadOnlyParameters);
        Assert.DoesNotContain("=>", result.Output);
        Assert.Contains("new RefReadonlyCallback", result.Output);
    }

    [Fact]
    public void ByRefLambdaWithUnknownRefKind_StaysLowered()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Outer+<>c");
        var delegateType = TypeRef.Definition("Synthetic", "Samples", "RefCallback");
        var byRefInt = TypeRef.ByRef(s_int);
        var lambdaMethod = new MethodRef(
            holder,
            "<M>b__0",
            TypeRef.CoreLib("System", "Void"),
            [byRefInt],
            HasThis: true)
        {
            CompilerGenerated = MetadataFactState.Yes,
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
        var singleton = new LoadField(new FieldRef(holder, "<>9", holder), instance: null);
        var hostBlock = new Block();
        hostBlock.Add(new Return(new DelegateCreation(delegateType, lambdaMethod, isVirtual: false, singleton)));
        var hostBody = new BlockContainer();
        hostBody.Add(hostBlock);
        var host = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            new MethodSignature(delegateType, [], HasThis: false, GenericParameterCount: 0),
            [],
            hostBody);

        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(null));
        var lambdaContainer = new BlockContainer();
        lambdaContainer.Add(lambdaBlock);
        var lambdaBody = new IrFunction(
            lambdaMethod.Name,
            holder,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("value", byRefInt)],
                HasThis: true,
                GenericParameterCount: 0),
            [],
            lambdaContainer);

        new LambdaRaisingPass().Run(
            host,
            new PassContext(
                new Stepper(enabled: false),
                importMethodBody: _ => lambdaBody));

        Assert.Empty(host.Descendants.OfType<Lambda>());
        Assert.Single(host.Descendants.OfType<DelegateCreation>());
    }

    static void AssertRefLambdaCompiles(string body)
    {
        string source = $$"""
            using System;
            static class Gate
            {
                public delegate void RefCallback(ref int value);
                public static RefCallback M()
                {
                    {{body}}
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "lambda-ref-kind-gate",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();
        Assert.Empty(errors);
    }

    [Fact]
    public void VoidBodyWithNestedControlFlow_StaysLowered()
    {
        string output = PrintRaised(
            nameof(VoidLambdaRaisingSamples.VoidLambdaWithConditional),
            typeof(VoidLambdaRaisingSamples));

        Assert.DoesNotContain("=>", output);
        Assert.Contains("new Action", output);
        Assert.Contains("b__", output);
    }

    // Negative: the captured parameter is reassigned in the outer body, a second
    // store to the hoisted field. The single-store guard must keep the
    // environment lowered so the mutation is not lost to value substitution.
    [Fact]
    public void CapturedFieldReassignedInOuterBody_StaysLowered()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturedParamReassignedInOuterBody));

        Assert.Contains("DisplayClass", output);   // environment kept
        Assert.Contains("new Func", output);        // delegate creation survives
        Assert.Contains("b__", output);             // lambda method reference not raised
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

    // #1358 (adversarial review): two lambdas capture disjoint fields, interleaved
    // with their creations (store a; create g1; store b; create g2). Each lambda
    // reads only its own field, stored before its own creation, so both must still
    // raise — a global "all stores precede all creations" gate would wrongly decline
    // because store b follows g1's creation.
    [Fact]
    public void SyntheticDisjointInterleavedCaptures_RaiseEveryLambda()
    {
        var (function, importBody) = BuildDisjointInterleavedSetup();
        var context = new PassContext(new Stepper(enabled: false), importMethodBody: importBody);

        new LambdaRaisingPass().Run(function, context);

        Assert.Equal(2, function.Descendants.OfType<Lambda>().Count());
        Assert.Empty(function.Descendants.OfType<DelegateCreation>());
        Assert.Empty(function.Descendants.OfType<StoreField>());
        function.CheckInvariant();
    }

    // #1358 (adversarial review nit): the capture store is nested in an if-block —
    // control flow, not a straight-line statement. When the branch is not taken the
    // field keeps its default, so eliding the store and substituting its value is
    // unsound. StatementIndex returns -1 for the nested store, declining the env.
    [Fact]
    public void ConditionalCaptureStore_StaysLowered()
    {
        var outer = TypeRef.Definition("Synthetic", "Samples", "Outer");
        var dcType = TypeRef.Definition("Synthetic", "Samples", "Outer+<>c__DisplayClass0_0");
        var xField = new FieldRef(dcType, "x", s_int);
        var dcCtor = new MethodRef(dcType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var lambdaMethod = new MethodRef(dcType, "<M>b__0", s_int, [], HasThis: true) { DeclaringTypeCompilerGenerated = MetadataFactState.Yes };

        var thenArm = new Block();
        thenArm.Add(new StoreField(xField, new LoadLocal(0, dcType), new LoadLocal(1, s_int)));
        var block = new Block();
        block.Add(new StoreLocal(1, s_int, new Constant(42, s_int)));
        block.Add(new StoreLocal(0, dcType, new NewObject(dcCtor, [])));
        block.Add(new IfStatement(new Constant(true, TypeRef.CoreLib("System", "Boolean")), thenArm, null));
        block.Add(new StoreLocal(2, s_func1, new DelegateCreation(s_func1, lambdaMethod, isVirtual: false, new LoadLocal(0, dcType))));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M", outer,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [dcType, s_int, s_func1], body);

        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(new LoadField(xField, new LoadArgument(0, "this", dcType))));
        var lambdaContainer = new BlockContainer();
        lambdaContainer.Add(lambdaBlock);
        var lambdaBody = new IrFunction(
            lambdaMethod.Name, dcType,
            new MethodSignature(s_int, [], HasThis: true, GenericParameterCount: 0), [], lambdaContainer);
        var context = new PassContext(new Stepper(enabled: false), importMethodBody: m => m == lambdaMethod ? lambdaBody : null);

        new LambdaRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        Assert.Single(function.Descendants.OfType<StoreField>());
        function.CheckInvariant();
    }

    static (IrFunction Function, Func<MethodRef, IrFunction?> ImportBody) BuildDisjointInterleavedSetup()
    {
        var outer = TypeRef.Definition("Synthetic", "Samples", "Outer");
        var dcType = TypeRef.Definition("Synthetic", "Samples", "Outer+<>c__DisplayClass0_0");
        var aField = new FieldRef(dcType, "a", s_int);
        var bField = new FieldRef(dcType, "b", s_int);
        var dcCtor = new MethodRef(dcType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var lambdaA = new MethodRef(dcType, "<M>b__0", s_int, [], HasThis: true) { DeclaringTypeCompilerGenerated = MetadataFactState.Yes };
        var lambdaB = new MethodRef(dcType, "<M>b__1", s_int, [], HasThis: true) { DeclaringTypeCompilerGenerated = MetadataFactState.Yes };

        var block = new Block();
        block.Add(new StoreLocal(1, s_int, new Constant(10, s_int)));                                   // value for a
        block.Add(new StoreLocal(0, dcType, new NewObject(dcCtor, [])));                                // alloc
        block.Add(new StoreField(aField, new LoadLocal(0, dcType), new LoadLocal(1, s_int)));           // store a
        block.Add(new StoreLocal(3, s_func1, new DelegateCreation(s_func1, lambdaA, isVirtual: false, new LoadLocal(0, dcType)))); // create g1
        block.Add(new StoreLocal(2, s_int, new Constant(20, s_int)));                                   // value for b
        block.Add(new StoreField(bField, new LoadLocal(0, dcType), new LoadLocal(2, s_int)));           // store b (after g1)
        block.Add(new StoreLocal(4, s_func1, new DelegateCreation(s_func1, lambdaB, isVirtual: false, new LoadLocal(0, dcType)))); // create g2
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            outer,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [dcType, s_int, s_int, s_func1, s_func1],
            body);

        IrFunction LambdaBodyReading(MethodRef method, FieldRef field)
        {
            var lambdaBlock = new Block();
            lambdaBlock.Add(new Return(new LoadField(field, new LoadArgument(0, "this", dcType))));
            var container = new BlockContainer();
            container.Add(lambdaBlock);
            return new IrFunction(
                method.Name, dcType,
                new MethodSignature(s_int, [], HasThis: true, GenericParameterCount: 0),
                [], container);
        }

        Func<MethodRef, IrFunction?> importBody = method =>
            method == lambdaA ? LambdaBodyReading(lambdaA, aField)
            : method == lambdaB ? LambdaBodyReading(lambdaB, bField)
            : null;
        return (function, importBody);
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

public static class VoidLambdaRaisingSamples
{
    public delegate void VoidCallback();
    public delegate void RefCallback(ref int value);
    public delegate void RefReadonlyCallback(ref readonly int value);

    // The captured parameter is also consumed by the outer body, keeping the
    // display class in a local like NLOptNet.AddLessOrEqualZeroConstraints.
    public static void CapturingVoidExpressionLambda(
        int n,
        System.Action<System.Action<int>, int> consume)
    {
        System.Action<int> action = x => System.Console.WriteLine(x + n);
        consume(action, n);
    }

    public static System.Action<int> CapturingVoidStatementLambda(int n)
        => x =>
        {
            System.Console.WriteLine(x);
            System.Console.WriteLine(n);
        };

    public static System.Action EmptyVoidLambda() => () => { };

    public static System.Action DiscardedNonVoidCall() => () => { Value(); };

    public static System.Action DiscardedPropertyRead() => () => { _ = System.Environment.ProcessId; };

    public static object CustomDelegateInObjectSink() => (VoidCallback)(() => { });

    public static object ActionInObjectSink() => (System.Action<int>)(x => System.Console.WriteLine(x));

    public static void ActionOverloadArgument()
        => Pick((System.Action<int>)(x => System.Console.WriteLine(x)));

    public static System.Action<int> NestedWeakReturn(System.Action<System.Func<object>> consume)
    {
        System.Func<object> callback = () => (System.Action<int>)(x => System.Console.WriteLine(x));
        consume(callback);
        return null!;
    }

    static void Pick(System.Action<int> action) { }
    static void Pick(System.Action<long> action) { }

    public static System.Action<System.Threading.Tasks.Task> AsyncVoidLambda()
        => async task => await task;

    public static RefCallback ByRefVoidLambda() => (ref int value) => System.Console.WriteLine(value);

    public static RefReadonlyCallback RefReadonlyVoidLambda()
        => (ref readonly int value) => System.Console.WriteLine(value);

    static int Value() => 1;

    // A close negative: the current lambda slice admits straight-line bodies,
    // not nested control flow.
    public static System.Action<int> VoidLambdaWithConditional()
        => x =>
        {
            if (x > 0)
                System.Console.WriteLine(x);
        };
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
