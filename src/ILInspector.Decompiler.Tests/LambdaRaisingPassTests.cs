using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ArrayShape = System.Reflection.Metadata.ArrayShape;
using SignatureAttributes = System.Reflection.Metadata.SignatureAttributes;
using SignatureCallingConvention = System.Reflection.Metadata.SignatureCallingConvention;
using SignatureHeader = System.Reflection.Metadata.SignatureHeader;
using SignatureKind = System.Reflection.Metadata.SignatureKind;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class LambdaRaisingPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [s_int, s_int]);

    static string PrintRaised(
        string methodName,
        Type? fixtureType = null,
        Action<IrFunction>? inspectFunction = null)
    {
        var type = fixtureType ?? typeof(CfgSampleClass);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        inspectFunction?.Invoke(function!);
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
    public void ParameterReusingOuterLocal_PreservesBothNamesAndFullFidelity()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.LambdaParameterReusesOuterLocal),
            inspectFunction: function =>
                Assert.Equal(DecompilationFidelity.Full, function.Fidelity));

        Assert.Contains("int value = input;", output);
        Assert.Contains("value => value + 1", output);
        Assert.DoesNotContain("int num = input;", output);
    }

    [Fact]
    public void LocalReusingOuterParameter_PreservesBothNamesAndFullFidelity()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.LambdaLocalReusesOuterParameter),
            inspectFunction: function =>
                Assert.Equal(DecompilationFidelity.Full, function.Fidelity));

        Assert.Contains("int value = 1;", output);
        Assert.Contains("RefHelper(ref value);", output);
        Assert.DoesNotContain("int num = 1;", output);
    }

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
    public void BodyOnlyInterfaceFact_ReachesInlineLambdaPrinter()
    {
        string output = PrintRaised(nameof(CfgSampleClass.InterfaceCastLambda));

        Assert.Contains("consumer => ((CfgDimFace)consumer).Value()", output);
        Assert.DoesNotContain("consumer => (consumer).Value()", output);
    }

    [Fact]
    public void BodyOnlyInterfaceFact_ReachesNestedLambdaPrinter()
    {
        string output = PrintRaised(nameof(CfgSampleClass.InterfaceCastLocalBodyLambda));

        Assert.Contains("((CfgDimFace)consumer).Value()", output);
        Assert.DoesNotContain("(consumer).Value()", output);
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
    public void ByRefLambdaWithAnonymousSibling_DeclinesAtExplicitParameterBoundary()
    {
        var host = RunIsolatedCompilerLambdaRaise(
            nameof(VoidLambdaRaisingSamples.ByRefLambdaWithAnonymousSibling),
            out var candidate);

        Assert.Equal(TypeRefKind.ByRef, candidate.ParameterTypes[0].Kind);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            candidate.ParameterTypes[1],
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
        Assert.Single(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void ByRefLambdaWithSpellableSibling_RaisesAtExplicitParameterBoundary()
    {
        var host = RunIsolatedCompilerLambdaRaise(
            nameof(VoidLambdaRaisingSamples.ByRefLambdaWithSpellableSibling),
            out var candidate);

        Assert.Equal(TypeRefKind.ByRef, candidate.ParameterTypes[0].Kind);
        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            candidate.ParameterTypes[1],
            host,
            ArgumentRefKind.Value));
        var lambda = Assert.Single(host.Descendants.OfType<Lambda>());
        Assert.Equal([ArgumentRefKind.Ref, ArgumentRefKind.Value], lambda.ParameterRefKinds);
        Assert.Empty(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void ByRefLambdaWithInScopeGenericSibling_RaisesAtExplicitParameterBoundary()
    {
        var fixtureHost = RunIsolatedCompilerLambdaRaise(
            typeof(GenericLambdaRaisingSamples<>),
            nameof(GenericLambdaRaisingSamples<int>.ByRefLambdaWithGenericSibling),
            out var candidate);
        var host = RunSyntheticSiblingLambdaRaise(
            candidate.ParameterTypes[1],
            fixtureHost.DeclaringTypeGenericParameterNames);

        Assert.Equal(TypeRefKind.GenericParameter, candidate.ParameterTypes[1].Kind);
        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            candidate.ParameterTypes[1],
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
        Assert.Empty(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void ByRefLambdaWithRefReadonlyFunctionPointerSibling_StaysLowered()
    {
        var host = RunIsolatedCompilerLambdaRaise(
            typeof(VoidLambdaRaisingSamples),
            nameof(VoidLambdaRaisingSamples.ByRefLambdaWithRefReadonlyFunctionPointerSibling),
            out var candidate);

        Assert.Equal(TypeRefKind.FunctionPointer, candidate.ParameterTypes[1].Kind);
        Assert.False(candidate.ParameterTypes[1].FunctionPointerSignatureIsExact);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            candidate.ParameterTypes[1],
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
        Assert.Single(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void ByRefLambdaWithMdArraySibling_Raises()
    {
        var host = RunIsolatedCompilerLambdaRaise(
            typeof(VoidLambdaRaisingSamples),
            nameof(VoidLambdaRaisingSamples.ByRefLambdaWithMdArraySibling),
            out var candidate);

        Assert.Equal(TypeRefKind.Array, candidate.ParameterTypes[1].Kind);
        Assert.True(candidate.ParameterTypes[1].ArrayShapeIsExact);
        Assert.Equal("int[,]", candidate.ParameterTypes[1].ToDisplayString());
        Assert.Single(host.Descendants.OfType<Lambda>());
        Assert.Empty(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void ByRefLambdaWithNestedArraySibling_RaisesWithExactSuffixOrder()
    {
        var host = RunIsolatedCompilerLambdaRaise(
            typeof(VoidLambdaRaisingSamples),
            nameof(VoidLambdaRaisingSamples.ByRefLambdaWithNestedArraySibling),
            out var candidate);

        Assert.Equal("int[][,]", candidate.ParameterTypes[1].ToDisplayString());
        Assert.Single(host.Descendants.OfType<Lambda>());
        Assert.Empty(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void MdArrayDecoder_AcceptsOnlyDefaultLowerBounds()
    {
        var exact = TypeRefDecoder.Instance.GetArrayType(
            s_int,
            new ArrayShape(2, [], [0, 0]));
        var lossy = TypeRefDecoder.Instance.GetArrayType(
            s_int,
            new ArrayShape(2, [], [1, 0]));
        var excess = TypeRefDecoder.Instance.GetArrayType(
            s_int,
            new ArrayShape(2, [], [0, 0, 0]));
        var host = RunSyntheticSiblingLambdaRaise(s_int);

        Assert.True(exact.ArrayShapeIsExact);
        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            exact,
            host,
            ArgumentRefKind.Value));
        Assert.False(lossy.ArrayShapeIsExact);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            lossy,
            host,
            ArgumentRefKind.Value));
        Assert.False(excess.ArrayShapeIsExact);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            excess,
            host,
            ArgumentRefKind.Value));
    }

    [Fact]
    public void ByRefLambdaWithExactFunctionPointerSibling_Raises()
    {
        var functionPointer = TypeRef.FunctionPointer(
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.ByRef(s_int)],
            "");
        var host = RunSyntheticSiblingLambdaRaise(functionPointer);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            functionPointer,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
        Assert.Empty(host.Descendants.OfType<DelegateCreation>());
    }

    public static TheoryData<ArgumentRefKind, TypeRef> ExactFunctionPointerSiblingTypes() => new()
    {
        {
            ArgumentRefKind.In,
            FunctionPointerWithParameterModifier(
                "System.Runtime.InteropServices",
                "InAttribute")
        },
        {
            ArgumentRefKind.Out,
            FunctionPointerWithParameterModifier(
                "System.Runtime.InteropServices",
                "OutAttribute")
        },
    };

    [Theory]
    [MemberData(nameof(ExactFunctionPointerSiblingTypes))]
    public void ByRefLambdaWithExactFunctionPointerRefKindSibling_Raises(
        ArgumentRefKind expectedRefKind,
        TypeRef functionPointer)
    {
        var host = RunSyntheticSiblingLambdaRaise(functionPointer);

        Assert.True(functionPointer.FunctionPointerSignatureIsExact);
        Assert.Equal([expectedRefKind], functionPointer.FunctionPointerParameterRefKinds);
        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            functionPointer,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    public static TheoryData<string, TypeRef> SpellableExplicitSiblingTypes() => new()
    {
        {
            "pointer array generic argument",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"),
                [TypeRef.SzArray(TypeRef.Pointer(s_int))])
        },
        {
            "function-pointer array generic argument",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"),
                [TypeRef.SzArray(
                    TypeRef.FunctionPointer(TypeRef.CoreLib("System", "Void"), [], ""))])
        },
        {
            "nested exact generic arity",
            TypeRef.GenericInstance(
                TypeRef.Definition("Synthetic", "Samples", "Outer`1+Inner`1"),
                [s_int, s_int])
        },
        {
            "member-function convention",
            FunctionPointerWithConventionModifier("MemberFunction")
        },
        { "rank-two MD array", TypeRef.MdArray(s_int, 2) },
        { "rank-thirty-two MD array", TypeRef.MdArray(s_int, 32) },
        { "value TypedReference", TypeRef.CoreLib("System", "TypedReference") },
        { "value ArgIterator", TypeRef.CoreLib("System", "ArgIterator") },
        { "value RuntimeArgumentHandle", TypeRef.CoreLib("System", "RuntimeArgumentHandle") },
        { "function-pointer TypedReference parameter",
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [TypeRef.CoreLib("System", "TypedReference")],
                "") },
        { "value Span",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System", "Span`1"),
                [s_int]) },
    };

    [Theory]
    [MemberData(nameof(SpellableExplicitSiblingTypes))]
    public void ByRefLambdaWithSpellableTypeShapeSibling_Raises(string _, TypeRef siblingType)
    {
        var host = RunSyntheticSiblingLambdaRaise(siblingType);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
        Assert.Empty(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void ByRefLambdaWithInScopeMethodGenericSibling_Raises()
    {
        var siblingType = TypeRef.MethodGenericParameter(0, "T");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringTypeGenericParameterNames: ["T"],
            methodGenericParameterNames: ["T"]);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ByRefLambdaWithShadowedTypeGenericSibling_StaysLowered()
    {
        var siblingType = TypeRef.GenericParameter(0, "T");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringTypeGenericParameterNames: ["T"],
            methodGenericParameterNames: ["T"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
        Assert.Single(host.Descendants.OfType<DelegateCreation>());
    }

    public static TheoryData<ArgumentRefKind, string, string> ExactParameterModifiers() => new()
    {
        {
            ArgumentRefKind.In,
            "System.Runtime.InteropServices",
            "InAttribute"
        },
        {
            ArgumentRefKind.Out,
            "System.Runtime.InteropServices",
            "OutAttribute"
        },
    };

    [Theory]
    [MemberData(nameof(ExactParameterModifiers))]
    public void ExplicitParameterModifier_MustMatchRefKind(
        ArgumentRefKind refKind,
        string ns,
        string name)
    {
        var type = TypeRef.ByRef(s_int).WithCustomModifier(
            TypeRef.Definition(TypeRef.CoreLibrary, ns, name),
            isRequired: true);
        var host = RunSyntheticSiblingLambdaRaise(s_int);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(type, host, refKind));
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            type,
            host,
            ArgumentRefKind.Ref));
    }

    public static TheoryData<string> RestrictedByRefTypeNames() => new()
    {
        "TypedReference",
        "ArgIterator",
        "RuntimeArgumentHandle",
    };

    [Fact]
    public void NamedTypeCollidingWithHostGenericParameter_StaysLowered()
    {
        var siblingType = TypeRef.Definition("Synthetic", "Samples", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringTypeGenericParameterNames: ["Widget"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NestedTypeLeadingSegmentCollidingWithHostGenericParameter_StaysLowered()
    {
        var nested = TypeRef.Definition("Synthetic", "Other", "Outer2+Inner");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringTypeGenericParameterNames: ["Outer2"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NestedGenericInstanceLeadingSegmentCollidingWithHostGenericParameter_StaysLowered()
    {
        var nested = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Other", "Outer6+Inner6`1"),
            [s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringTypeGenericParameterNames: ["Outer6"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NestedGenericInstanceLeadingGenericSegmentCollidingWithHostGenericParameter_StaysLowered()
    {
        var nested = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Other", "Outer`1+Inner`1"),
            [s_int, s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringTypeGenericParameterNames: ["Outer"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NestedGenericInstanceTrailingNameMatchingGenericParameter_StillRaises()
    {
        var nested = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Samples", "Outer`1+Inner`1"),
            [s_int, s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringTypeGenericParameterNames: ["Inner"]);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void GenericInstanceMatchingHostGenericParameterName_StillRaises()
    {
        var list = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            [s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            list,
            declaringTypeGenericParameterNames: ["List"]);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            list,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void GenericInstanceMatchingHostSimpleNameDifferentArity_StillRaises()
    {
        var list = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            [s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            list,
            declaringType: TypeRef.CoreLib("System.Collections.Generic", "List`2"),
            declaringTypeGenericParameterNames: ["T", "U"]);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            list,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void InScopeNestedTypeCollidingWithMethodGenericParameter_StaysLowered()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Inner");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            methodGenericParameterNames: ["Inner"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void SameSimpleNameFromDifferentDeclaringType_StaysLowered()
    {
        var siblingType = TypeRef.Definition("OtherAssembly", "A", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Widget"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void SameDeclaringTypeSimpleName_StillRaises()
    {
        var widget = TypeRef.Definition("Synthetic", "Samples", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(widget, declaringType: widget);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            widget,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedNestedLeadingSegmentCollidingWithDeclaringType_StaysLowered()
    {
        var nested = TypeRef.Definition("OtherAssembly", "A", "Outer+Inner");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedNestedGenericLeadingSegmentCollidingWithDeclaringType_StaysLowered()
    {
        var nested = TypeRef.GenericInstance(
            TypeRef.Definition("OtherAssembly", "A", "Outer`1+Inner"),
            [s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer`1"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedNestedLeadingSegmentArityMismatch_StillRaises()
    {
        var nested = TypeRef.GenericInstance(
            TypeRef.Definition("OtherAssembly", "A", "Outer`1+Inner"),
            [s_int]);
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void SameIdentityQualifiedNestedDeclaringChain_StillRaises()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Inner");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void BareNameCollidingWithNestedHostDeclaringChain_StaysLowered()
    {
        var siblingType = TypeRef.Definition("OtherAssembly", "A", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Widget+Holder"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void BareNameCollidingWithMiddleHostDeclaringSegment_StaysLowered()
    {
        var siblingType = TypeRef.Definition("OtherAssembly", "A", "Mid");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Widget+Mid+Holder"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void SameIdentityEnclosingTypeOnNestedHost_StillRaises()
    {
        var widget = TypeRef.Definition("Synthetic", "Samples", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(
            widget,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Widget+Holder"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            widget,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedNestedLeadingSegmentWithRepeatedHostName_StaysLowered()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Zed+Deep");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Outer"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedNestedLeadingSegmentWithoutRepeatedHostName_StillRaises()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Zed+Deep");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void BareNameWithRepeatedHostDeclaringSegment_StaysLowered()
    {
        var siblingType = TypeRef.Definition("Synthetic", "Samples", "Outer");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Outer"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void BareNameCollidingWithRepeatedMiddleHostSegment_StaysLowered()
    {
        var siblingType = TypeRef.Definition("Synthetic", "Samples", "X+A");
        var host = RunSyntheticSiblingLambdaRaise(
            siblingType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "X+A+Y+A"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedSiblingChainShadowingLeadingSegment_StaysLowered()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Outer+Deep");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedSiblingChainShadowingOnHostAncestor_StaysLowered()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Outer+Deep");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Q"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void QualifiedSiblingChainWithoutHostPrefixShadow_StillRaises()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Outer+Deep");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ForgedDynamicOnNonObjectSibling_StaysLowered()
    {
        var host = RunSyntheticSiblingLambdaRaise(s_int);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            s_int,
            host,
            ArgumentRefKind.Value,
            isDynamic: true));
    }

    [Fact]
    public void DynamicObjectSibling_IsSpellable()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var host = RunSyntheticSiblingLambdaRaise(objectType);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            objectType,
            host,
            ArgumentRefKind.Value,
            isDynamic: true));
    }

    [Fact]
    public void NintKeywordCollidingWithDeclaringTypeName_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "nint"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nint,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NuintKeywordCollidingWithDeclaringTypeName_StaysLowered()
    {
        var nuint = TypeRef.CoreLib("System", "UIntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nuint,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "nuint"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            nuint,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordWithoutDeclaringTypeCollision_StillRaises()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(nint);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nint,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void DynamicKeywordCollidingWithDeclaringTypeName_StaysLowered()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var host = RunSyntheticSiblingLambdaRaise(
            objectType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "dynamic"),
            siblingIsDynamic: true);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            objectType,
            host,
            ArgumentRefKind.Value,
            isDynamic: true));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ConstituentGenericArgumentShadowingLeadingSegment_StaysLowered()
    {
        var sibling = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Samples", "Foo+Bar`1"),
            [TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Foo")]);
        var host = RunSyntheticSiblingLambdaRaise(
            sibling,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void CrossParameterSiblingChainShadowingLeadingSegment_StaysLowered()
    {
        var first = TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Foo");
        var second = TypeRef.Definition("Synthetic", "Samples", "Foo+Bar");
        var host = RunSyntheticSiblingLambdaRaise(
            first,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"),
            additionalSiblings: [second]);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            first,
            host,
            ArgumentRefKind.Value));
        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            second,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void CrossParameterSiblingChainWithoutHostPrefix_StillRaises()
    {
        var first = TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Foo");
        var second = TypeRef.Definition("Synthetic", "Samples", "Foo+Bar");
        var host = RunSyntheticSiblingLambdaRaise(
            first,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"),
            additionalSiblings: [second]);

        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ConstituentCandidateShadowedBySiblingTypeArgument_StaysLowered()
    {
        var sibling = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Samples", "Pair`2"),
            [
                TypeRef.Definition("Synthetic", "Samples", "Foo+Bar"),
                TypeRef.Definition("Synthetic", "Samples", "Outer+Mid+Foo"),
            ]);
        var host = RunSyntheticSiblingLambdaRaise(
            sibling,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ConstituentCandidateWithoutHostPrefixProof_StillRaises()
    {
        var sibling = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Samples", "Pair`2"),
            [
                TypeRef.Definition("Synthetic", "Samples", "Foo+Bar"),
                TypeRef.Definition("Synthetic", "Samples", "Zed+Foo"),
            ]);
        var host = RunSyntheticSiblingLambdaRaise(
            sibling,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordShadowedBySiblingNestedType_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"),
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "Outer+nint")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordShadowedByConstituentNestedType_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var proof = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            [TypeRef.Definition("Synthetic", "Samples", "Outer+nint")]);
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"),
            additionalSiblings: [proof]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void DynamicKeywordShadowedBySiblingNestedType_StaysLowered()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var host = RunSyntheticSiblingLambdaRaise(
            objectType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"),
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "Outer+dynamic")],
            siblingIsDynamic: true);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void PairOfNintAndNestedNint_StaysLowered()
    {
        var sibling = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Samples", "Pair`2"),
            [
                TypeRef.CoreLib("System", "IntPtr"),
                TypeRef.Definition("Synthetic", "Samples", "Outer+nint"),
            ]);
        var host = RunSyntheticSiblingLambdaRaise(
            sibling,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer"));

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ReservedIntKeywordWithDeclaringTypeNamedInt_StillRaises()
    {
        var host = RunSyntheticSiblingLambdaRaise(
            s_int,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "int"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            s_int,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ReservedStringKeywordWithDeclaringTypeNamedString_StillRaises()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var host = RunSyntheticSiblingLambdaRaise(
            stringType,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "string"));

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            stringType,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordShadowedByTopLevelSibling_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "nint")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NuintKeywordShadowedByTopLevelSibling_StaysLowered()
    {
        var nuint = TypeRef.CoreLib("System", "UIntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nuint,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "nuint")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void DynamicKeywordShadowedByTopLevelSibling_StaysLowered()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var host = RunSyntheticSiblingLambdaRaise(
            objectType,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "dynamic")],
            siblingIsDynamic: true);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordShadowedByTopLevelConstituent_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var proof = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            [TypeRef.Definition("Synthetic", "Samples", "nint")]);
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            additionalSiblings: [proof]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ForeignNestedTypeShadowedByTopLevelSibling_StaysLowered()
    {
        var nested = TypeRef.Definition("Other", "Other", "Foo+Bar");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "Foo")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NestedTypeWithOwnTopLevelLeadingSibling_StillRaises()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Foo+Bar");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "Foo")]);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordShadowedByNestedTypesOutermostSegment_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "nint+Child")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void TopLevelTypeShadowedByHostNestedSibling_StaysLowered()
    {
        var topLevel = TypeRef.Definition("Synthetic", "Samples", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(
            topLevel,
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "Outer+Widget")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void TopLevelTypeShadowedByHostAncestorNestedSibling_StaysLowered()
    {
        var topLevel = TypeRef.Definition("Synthetic", "Samples", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(
            topLevel,
            declaringType: TypeRef.Definition("Synthetic", "Samples", "Outer+Mid"),
            additionalSiblings: [TypeRef.Definition("Synthetic", "Samples", "Outer+Widget")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void HostNestedTypePrintingBareNameAlone_StillRaises()
    {
        var nested = TypeRef.Definition("Synthetic", "Samples", "Outer+Widget");
        var host = RunSyntheticSiblingLambdaRaise(nested);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            nested,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ForeignNestedTypeShadowedByOtherAssemblyTopLevel_StaysLowered()
    {
        var nested = TypeRef.Definition("Other", "Other", "Foo+Bar");
        var host = RunSyntheticSiblingLambdaRaise(
            nested,
            additionalSiblings: [TypeRef.Definition("OtherAsm", "Samples", "Foo")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordShadowedByOtherAssemblyTopLevel_StaysLowered()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            additionalSiblings: [TypeRef.Definition("OtherAsm", "Samples", "nint")]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void NintKeywordWithArityOneNestedChild_StillRaises()
    {
        var nint = TypeRef.CoreLib("System", "IntPtr");
        var host = RunSyntheticSiblingLambdaRaise(
            nint,
            additionalSiblings:
            [
                TypeRef.GenericInstance(
                    TypeRef.Definition("Synthetic", "Samples", "nint`1+Child"),
                    [s_int]),
            ]);

        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ForeignNestedTypesSharingPrintedLeadingName_StaysLowered()
    {
        var first = TypeRef.Definition("A", "A", "Foo+Bar");
        var second = TypeRef.Definition("B", "B", "Foo+Baz");
        var host = RunSyntheticSiblingLambdaRaise(first, additionalSiblings: [second]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ForeignBareTypesSharingPrintedName_StaysLowered()
    {
        var first = TypeRef.Definition("A", "A", "Widget");
        var second = TypeRef.Definition("B", "B", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(first, additionalSiblings: [second]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void SameLeadingIdentityQualifiedNestedSiblings_StillRaises()
    {
        var first = TypeRef.Definition("A", "A", "Foo+Bar");
        var second = TypeRef.Definition("A", "A", "Foo+Baz");
        var host = RunSyntheticSiblingLambdaRaise(first, additionalSiblings: [second]);

        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void SameIdentityForeignBareSiblings_StillRaises()
    {
        var widget = TypeRef.Definition("A", "A", "Widget");
        var host = RunSyntheticSiblingLambdaRaise(widget, additionalSiblings: [widget]);

        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ForeignNestedLeadingNameAndBareType_StaysLowered()
    {
        var first = TypeRef.Definition("A", "A", "Foo+Bar");
        var second = TypeRef.Definition("B", "B", "Foo");
        var host = RunSyntheticSiblingLambdaRaise(first, additionalSiblings: [second]);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void DynamicAliasBesideForeignTypeNamedDynamic_StaysLowered()
    {
        var host = RunSyntheticSiblingLambdaRaise(
            TypeRef.CoreLib("System", "Object"),
            additionalSiblings: [TypeRef.Definition("Zed", "Zed", "dynamic")],
            siblingIsDynamic: true);

        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("file")]
    [InlineData("init")]
    [InlineData("record")]
    [InlineData("required")]
    public void DeclarationContextualTypeNamePrintedBare_StaysLowered(string name)
    {
        var sibling = TypeRef.Definition("Synthetic", "Samples", name);
        var host = RunSyntheticSiblingLambdaRaise(sibling);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ReservedKeywordTypeNameClass_StillRaises()
    {
        var sibling = TypeRef.Definition("Synthetic", "Samples", "class");
        var host = RunSyntheticSiblingLambdaRaise(sibling);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void AwaitContextualTypeName_StillRaises()
    {
        var sibling = TypeRef.Definition("Synthetic", "Samples", "await");
        var host = RunSyntheticSiblingLambdaRaise(sibling);

        Assert.True(CSharpSpellability.CanSpellExplicitParameterType(
            sibling,
            host,
            ArgumentRefKind.Value));
        Assert.Single(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void CompilerProducedScopedTypeSibling_StaysLowered()
    {
        var host = RunIsolatedCompilerLambdaRaise(
            typeof(ScopedTypeLambdaSamples),
            nameof(ScopedTypeLambdaSamples.ByRefLambdaWithScopedSibling),
            out var candidate);

        Assert.Equal("scoped", candidate.ParameterTypes[1].Name);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            candidate.ParameterTypes[1],
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
        Assert.Single(host.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void DynamicObjectSiblingCollidingWithHostGenericParameter_StaysLowered()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var host = RunSyntheticSiblingLambdaRaise(
            objectType,
            declaringTypeGenericParameterNames: ["dynamic"]);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            objectType,
            host,
            ArgumentRefKind.Value,
            isDynamic: true));
    }

    [Fact]
    public void ExplicitPointerSibling_MarksHostUnsafe()
    {
        var pointer = TypeRef.Pointer(s_int);
        var host = RunSyntheticSiblingLambdaRaise(pointer);

        Assert.Single(host.Descendants.OfType<Lambda>());
        var result = CSharpPrinter.PrintRaised(host);
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.True(result.RequiresUnsafeBodyModifier);
        Assert.Contains("int*", result.Output);
    }

    [Fact]
    public void SameAssemblyRefStructArray_StaysLowered()
    {
        var refStruct = TypeRef.Definition("Synthetic", "Samples", "RefBox");
        var host = RunSyntheticSiblingLambdaRaise(s_int);
        host.ByRefLikeTypes = ImmutableHashSet.Create(refStruct);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            TypeRef.SzArray(refStruct),
            host,
            ArgumentRefKind.Value));
    }

    [Theory]
    [MemberData(nameof(RestrictedByRefTypeNames))]
    public void RestrictedByRefParameterType_StaysLowered(string name)
    {
        var type = TypeRef.ByRef(TypeRef.CoreLib("System", name));
        var host = RunSyntheticSiblingLambdaRaise(s_int);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            type,
            host,
            ArgumentRefKind.Ref));
    }

    [Fact]
    public void DuplicateExplicitParameterModifier_StaysLowered()
    {
        var modifier = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System.Runtime.InteropServices",
            "InAttribute");
        var type = TypeRef.ByRef(s_int)
            .WithCustomModifier(modifier, isRequired: true)
            .WithCustomModifier(modifier, isRequired: true);
        var host = RunSyntheticSiblingLambdaRaise(s_int);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            type,
            host,
            ArgumentRefKind.In));
    }

    public static TheoryData<SignatureAttributes, int> LossyFunctionPointerHeaders() => new()
    {
        { SignatureAttributes.Instance, 0 },
        { SignatureAttributes.ExplicitThis, 0 },
        { SignatureAttributes.Instance | SignatureAttributes.ExplicitThis, 0 },
        { SignatureAttributes.Generic, 1 },
        { SignatureAttributes.None, 1 },
    };

    [Theory]
    [MemberData(nameof(LossyFunctionPointerHeaders))]
    public void FunctionPointerHeaderWithoutCSharpSpelling_StaysLowered(
        SignatureAttributes attributes,
        int genericParameterCount)
    {
        var signature = new System.Reflection.Metadata.MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.Default,
                attributes),
            TypeRef.CoreLib("System", "Void"),
            requiredParameterCount: 0,
            genericParameterCount,
            []);
        var functionPointer = TypeRefDecoder.Instance.GetFunctionPointerType(signature);
        var host = RunSyntheticSiblingLambdaRaise(functionPointer);

        Assert.False(functionPointer.FunctionPointerSignatureIsExact);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            functionPointer,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
    }

    [Fact]
    public void ReservedFunctionPointerHeader_StaysLowered()
    {
        var signature = new System.Reflection.Metadata.MethodSignature<TypeRef>(
            new SignatureHeader(0x80),
            TypeRef.CoreLib("System", "Void"),
            requiredParameterCount: 0,
            genericParameterCount: 0,
            []);
        var functionPointer = TypeRefDecoder.Instance.GetFunctionPointerType(signature);
        var host = RunSyntheticSiblingLambdaRaise(functionPointer);

        Assert.False(functionPointer.FunctionPointerSignatureIsExact);
        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            functionPointer,
            host,
            ArgumentRefKind.Value));
    }

    [Fact]
    public void ConstituentTypeMapping_PreservesSignatureFidelityFacts()
    {
        var modifier = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System.Runtime.InteropServices",
            "InAttribute");
        var parameter = TypeRef.ByRef(
                TypeRef.Definition("Original", "Samples", "Value"))
            .WithCustomModifier(modifier, isRequired: true);
        var functionPointer = TypeRef.FunctionPointer(
            TypeRef.CoreLib("System", "Void"),
            [parameter],
            "");
        var array = TypeRef.MdArray(
            TypeRef.Definition("Original", "Samples", "Value"),
            2,
            arrayShapeIsExact: false);

        var mappedFunctionPointer = MapDefinitionsToInt(functionPointer);
        var mappedArray = MapDefinitionsToInt(array);

        Assert.True(mappedFunctionPointer.FunctionPointerSignatureIsExact);
        Assert.Equal(
            [ArgumentRefKind.In],
            mappedFunctionPointer.FunctionPointerParameterRefKinds);
        Assert.False(mappedArray.ArrayShapeIsExact);
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

    public static TheoryData<string, TypeRef> UnspellableExplicitSiblingTypes() => new()
    {
        { "unsupported", TypeRef.Unsupported("unsupported sibling") },
        { "pinned", TypeRef.Pinned(s_int) },
        { "unnamed type generic parameter", TypeRef.GenericParameter(0) },
        { "unnamed method generic parameter", TypeRef.MethodGenericParameter(0) },
        { "nested by-ref", TypeRef.ByRef(TypeRef.ByRef(s_int)) },
        { "by-ref array element", TypeRef.SzArray(TypeRef.ByRef(s_int)) },
        { "void", TypeRef.CoreLib("System", "Void") },
        { "open generic definition", TypeRef.Definition("Synthetic", "Samples", "Pair`2") },
        { "malformed generic arity", TypeRef.Definition("Synthetic", "Samples", "Pair`many") },
        { "space-prefixed generic arity",
            TypeRef.GenericInstance(TypeRef.Definition("Synthetic", "Samples", "Pair` 1"), [s_int]) },
        { "sign-prefixed generic arity",
            TypeRef.GenericInstance(TypeRef.Definition("Synthetic", "Samples", "Pair`+1"), [s_int]) },
        { "zero-prefixed generic arity",
            TypeRef.GenericInstance(TypeRef.Definition("Synthetic", "Samples", "Pair`01"), [s_int]) },
        { "too few generic arguments",
            TypeRef.GenericInstance(TypeRef.Definition("Synthetic", "Samples", "Pair`2"), [s_int]) },
        { "too many generic arguments",
            TypeRef.GenericInstance(TypeRef.Definition("Synthetic", "Samples", "Pair`2"), [s_int, s_int, s_int]) },
        { "arguments on a non-generic definition",
            TypeRef.GenericInstance(TypeRef.Definition("Synthetic", "Samples", "Plain"), [s_int]) },
        { "pointer generic argument",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"),
                [TypeRef.Pointer(s_int)]) },
        { "function-pointer generic argument",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"),
                [TypeRef.FunctionPointer(TypeRef.CoreLib("System", "Void"), [], "")]) },
        { "rank-zero MD array", TypeRef.MdArray(s_int, 0) },
        { "rank-one MD array", TypeRef.MdArray(s_int, 1) },
        { "excessive MD array rank", TypeRef.MdArray(s_int, 33) },
        { "non-default MD array bounds", TypeRef.MdArray(s_int, 2, arrayShapeIsExact: false) },
        { "instantiated non-default MD array bounds",
            TypeRef.MdArray(
                    TypeRef.GenericParameter(0, "T"),
                    2,
                    arrayShapeIsExact: false)
                .Instantiate([s_int], []) },
        { "out-of-scope type generic parameter", TypeRef.GenericParameter(0, "T") },
        { "out-of-scope method generic parameter", TypeRef.MethodGenericParameter(0, "T") },
        { "unknown required modifier",
            ModifiedType(s_int, "Adversarial", "Synthetic", "UnknownModifier") },
        { "nested unknown required modifier",
            TypeRef.Pointer(
                ModifiedType(s_int, "Adversarial", "Synthetic", "UnknownModifier")) },
        { "instantiated unknown required modifier",
            ModifiedType(
                    TypeRef.GenericParameter(0, "T"),
                    "Adversarial",
                    "Synthetic",
                    "UnknownModifier")
                .Instantiate([s_int], []) },
        { "instantiated outer generic-instance modifier",
            ModifiedType(
                    TypeRef.GenericInstance(
                        TypeRef.Definition("Synthetic", "Samples", "Box`1"),
                        [TypeRef.GenericParameter(0, "T")]),
                    "Adversarial",
                    "Synthetic",
                    "UnknownModifier")
                .Instantiate([s_int], []) },
        { "instantiated outer function-pointer modifier",
            ModifiedType(
                    TypeRef.FunctionPointer(
                        TypeRef.CoreLib("System", "Void"),
                        [TypeRef.GenericParameter(0, "T")],
                        ""),
                    "Adversarial",
                    "Synthetic",
                    "UnknownModifier")
                .Instantiate([s_int], []) },
        { "value-hinted unknown required modifier",
            ModifiedType(s_int, "Adversarial", "Synthetic", "UnknownModifier")
                .WithValueTypeHint(ValueTypeHint.ValueType) },
        { "lossy function-pointer convention",
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [],
                "unmanaged",
                callingConventionIsExact: false) },
        { "instantiated lossy function-pointer convention",
            TypeRef.FunctionPointer(
                    TypeRef.CoreLib("System", "Void"),
                    [TypeRef.GenericParameter(0, "T")],
                    "unmanaged",
                    callingConventionIsExact: false)
                .Instantiate([s_int], []) },
        { "unknown function-pointer convention modifier",
            FunctionPointerWithConventionModifier("Unknown") },
        { "required function-pointer convention modifier",
            FunctionPointerWithConventionModifier("Cdecl", isRequired: true) },
        { "unknown function-pointer parameter modifier",
            FunctionPointerWithParameterModifier("Synthetic", "UnknownModifier") },
        { "duplicate function-pointer parameter modifier",
            FunctionPointerWithDuplicateParameterModifier() },
        { "nested unknown function-pointer parameter modifier",
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [TypeRef.Pointer(
                    ModifiedType(s_int, "Adversarial", "Synthetic", "UnknownModifier"))],
                "") },
        { "spoofed function-pointer in modifier",
            FunctionPointerWithParameterModifier(
                "System.Runtime.InteropServices",
                "InAttribute",
                assembly: "Adversarial") },
        { "invalid function-pointer convention text",
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "Void"),
                [],
                "unmanaged[Unknown]") },
        { "unsupported modifier identity",
            s_int.WithCustomModifier(
                TypeRef.Unsupported("unsupported modifier"),
                isRequired: true) },
        { "TypedReference array",
            TypeRef.SzArray(TypeRef.CoreLib("System", "TypedReference")) },
        { "ArgIterator generic argument",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"),
                [TypeRef.CoreLib("System", "ArgIterator")]) },
        { "TypedReference function-pointer return",
            TypeRef.FunctionPointer(
                TypeRef.CoreLib("System", "TypedReference"),
                [],
                "") },
        { "ref RuntimeArgumentHandle",
            TypeRef.ByRef(TypeRef.CoreLib("System", "RuntimeArgumentHandle")) },
        { "Span array",
            TypeRef.SzArray(
                TypeRef.GenericInstance(
                    TypeRef.CoreLib("System", "Span`1"),
                    [s_int])) },
        { "Span generic argument",
            TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"),
                [TypeRef.GenericInstance(
                    TypeRef.CoreLib("System", "Span`1"),
                    [s_int])]) },
    };

    [Theory]
    [MemberData(nameof(UnspellableExplicitSiblingTypes))]
    public void ByRefLambdaWithUnspellableSibling_StaysLowered(string _, TypeRef siblingType)
    {
        var host = RunSyntheticSiblingLambdaRaise(siblingType);

        Assert.False(CSharpSpellability.CanSpellExplicitParameterType(
            siblingType,
            host,
            ArgumentRefKind.Value));
        Assert.Empty(host.Descendants.OfType<Lambda>());
        Assert.Single(host.Descendants.OfType<DelegateCreation>());
        host.CheckInvariant();
    }

    static IrFunction RunSyntheticSiblingLambdaRaise(
        TypeRef siblingType,
        ImmutableArray<string> declaringTypeGenericParameterNames = default,
        ImmutableArray<string> methodGenericParameterNames = default,
        TypeRef? declaringType = null,
        TypeRef[]? additionalSiblings = null,
        bool siblingIsDynamic = false)
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Outer+<>c");
        var delegateType = TypeRef.Definition("Synthetic", "Samples", "RefCallback");
        var byRefInt = TypeRef.ByRef(s_int);
        var extra = additionalSiblings ?? [];
        var parameterTypesBuilder = ImmutableArray.CreateBuilder<TypeRef>(2 + extra.Length);
        parameterTypesBuilder.Add(byRefInt);
        parameterTypesBuilder.Add(siblingType);
        parameterTypesBuilder.AddRange(extra);
        var parameterTypes = parameterTypesBuilder.MoveToImmutable();
        var parameterRefKindsBuilder = ImmutableArray.CreateBuilder<ArgumentRefKind>(parameterTypes.Length);
        parameterRefKindsBuilder.Add(ArgumentRefKind.Ref);
        for (int i = 1; i < parameterTypes.Length; i++)
            parameterRefKindsBuilder.Add(ArgumentRefKind.Value);
        var lambdaParametersBuilder = ImmutableArray.CreateBuilder<Parameter>(parameterTypes.Length);
        lambdaParametersBuilder.Add(new Parameter("value", byRefInt));
        lambdaParametersBuilder.Add(new Parameter("sibling", siblingType, IsDynamic: siblingIsDynamic));
        for (int i = 0; i < extra.Length; i++)
            lambdaParametersBuilder.Add(new Parameter("sibling" + (i + 2), extra[i]));
        var lambdaParameters = lambdaParametersBuilder.MoveToImmutable();
        var lambdaMethod = new MethodRef(
            holder,
            "<M>b__0",
            TypeRef.CoreLib("System", "Void"),
            parameterTypes,
            HasThis: true)
        {
            CompilerGenerated = MetadataFactState.Yes,
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
            ParameterRefKinds = parameterRefKindsBuilder.MoveToImmutable(),
        };
        var singleton = new LoadField(new FieldRef(holder, "<>9", holder), instance: null);
        var hostBlock = new Block();
        hostBlock.Add(new Return(new DelegateCreation(delegateType, lambdaMethod, isVirtual: false, singleton)));
        var hostBody = new BlockContainer();
        hostBody.Add(hostBlock);
        var host = new IrFunction(
            "M",
            declaringType ?? TypeRef.Definition("Synthetic", "Samples", "Outer"),
            new MethodSignature(
                delegateType,
                [],
                HasThis: false,
                GenericParameterCount: methodGenericParameterNames.IsDefault
                    ? 0
                    : methodGenericParameterNames.Length)
            {
                GenericParameterNames = methodGenericParameterNames.IsDefault
                    ? []
                    : methodGenericParameterNames,
            },
            [],
            hostBody)
        {
            DeclaringTypeGenericParameterNames =
                declaringTypeGenericParameterNames.IsDefault
                    ? []
                    : declaringTypeGenericParameterNames,
        };

        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(null));
        var lambdaContainer = new BlockContainer();
        lambdaContainer.Add(lambdaBlock);
        var lambdaBody = new IrFunction(
            lambdaMethod.Name,
            holder,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                lambdaParameters,
                HasThis: true,
                GenericParameterCount: 0),
            [],
            lambdaContainer);

        new LambdaRaisingPass().Run(
            host,
            new PassContext(
                new Stepper(enabled: false),
                importMethodBody: _ => lambdaBody));
        return host;
    }

    static TypeRef FunctionPointerWithParameterModifier(
        string ns,
        string name,
        string assembly = TypeRef.CoreLibrary)
    {
        var parameter = TypeRef.ByRef(s_int).WithCustomModifier(
            TypeRef.Definition(assembly, ns, name),
            isRequired: true);
        return TypeRef.FunctionPointer(
            TypeRef.CoreLib("System", "Void"),
            [parameter],
            "");
    }

    static TypeRef ModifiedType(
        TypeRef type,
        string assembly,
        string ns,
        string name)
        => type.WithCustomModifier(
            TypeRef.Definition(assembly, ns, name),
            isRequired: true);

    static TypeRef FunctionPointerWithDuplicateParameterModifier()
    {
        var modifier = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System.Runtime.InteropServices",
            "InAttribute");
        var parameter = TypeRef.ByRef(s_int)
            .WithCustomModifier(modifier, isRequired: true)
            .WithCustomModifier(modifier, isRequired: true);
        return TypeRef.FunctionPointer(
            TypeRef.CoreLib("System", "Void"),
            [parameter],
            "");
    }

    static TypeRef MapDefinitionsToInt(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => s_int,
            TypeRefKind.GenericInstance or TypeRefKind.FunctionPointer =>
                type.WithComponents(
                    MapDefinitionsToInt(type.ElementType!),
                    [.. type.TypeArguments.Select(MapDefinitionsToInt)]),
            TypeRefKind.SzArray
                or TypeRefKind.Array
                or TypeRefKind.ByRef
                or TypeRefKind.Pointer
                or TypeRefKind.Pinned =>
                type.WithComponents(MapDefinitionsToInt(type.ElementType!)),
            _ => type,
        };

    static TypeRef FunctionPointerWithConventionModifier(
        string name,
        bool isRequired = false)
    {
        var returnType = TypeRef.CoreLib("System", "Void").WithCustomModifier(
            TypeRef.Definition(
                TypeRef.CoreLibrary,
                "System.Runtime.CompilerServices",
                $"CallConv{name}"),
            isRequired);
        return TypeRef.FunctionPointer(returnType, [], "unmanaged");
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

    static IrFunction RunIsolatedCompilerLambdaRaise(
        Type fixtureType,
        string methodName,
        out MethodRef candidate)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var fixture = IrImporter.Import(
            source,
            fixtureType.FullName!,
            methodName);
        Assert.NotNull(fixture);
        candidate = Assert.Single(fixture!.Descendants.OfType<LoadFunctionPointer>()).Method;
        var result = CSharpPrinter.PrintRaised(
            fixture!,
            method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var delegateType = TypeRef.Definition("Synthetic", "Samples", "RefCallback");

        var singleton = new LoadField(
            new FieldRef(candidate.DeclaringType, "<>9", candidate.DeclaringType),
            instance: null);
        var block = new Block();
        block.Add(new Return(new DelegateCreation(
            delegateType,
            candidate,
            isVirtual: false,
            singleton)));
        var container = new BlockContainer();
        container.Add(block);
        var host = new IrFunction(
            "M",
            fixture.DeclaringType,
            fixture.Signature with { ReturnType = delegateType, Parameters = [] },
            [],
            container)
        {
            DeclaringTypeGenericParameterNames = fixture.DeclaringTypeGenericParameterNames,
        };

        new LambdaRaisingPass().Run(
            host,
            new PassContext(
                new Stepper(enabled: false),
                importMethodBody: method => IrImporter.Import(source, method)));
        host.CheckInvariant();
        return host;
    }

    static IrFunction RunIsolatedCompilerLambdaRaise(
        string methodName,
        out MethodRef candidate)
        => RunIsolatedCompilerLambdaRaise(
            typeof(VoidLambdaRaisingSamples),
            methodName,
            out candidate);

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
    public delegate void RefSiblingCallback<T>(ref int value, T sibling);
    public delegate void RefMdArrayCallback(ref int value, int[,] sibling);
    public delegate void RefNestedArrayCallback(ref int value, int[][,] sibling);
    public unsafe delegate void RefReadonlyFunctionPointerSiblingCallback(
        ref int value,
        delegate*<ref readonly int, void> sibling);

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

    public static object ByRefLambdaWithAnonymousSibling()
        => CreateRefSiblingCallback(
            new { Value = 1 },
            (ref value, sibling) => System.Console.WriteLine(value + sibling.Value));

    public static RefSiblingCallback<int> ByRefLambdaWithSpellableSibling()
        => (ref int value, int sibling) => System.Console.WriteLine(value + sibling);

    public static RefMdArrayCallback ByRefLambdaWithMdArraySibling()
        => (ref int value, int[,] sibling) => System.Console.WriteLine(value);

    public static RefNestedArrayCallback ByRefLambdaWithNestedArraySibling()
        => (ref int value, int[][,] sibling) => System.Console.WriteLine(value);

    public static unsafe RefReadonlyFunctionPointerSiblingCallback
        ByRefLambdaWithRefReadonlyFunctionPointerSibling()
        => (ref int value, delegate*<ref readonly int, void> sibling)
            => System.Console.WriteLine(value);

    public static RefReadonlyCallback RefReadonlyVoidLambda()
        => (ref readonly int value) => System.Console.WriteLine(value);

    static RefSiblingCallback<T> CreateRefSiblingCallback<T>(
        T sibling,
        RefSiblingCallback<T> callback)
        => callback;

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

public sealed class @scoped { }

public static class ScopedTypeLambdaSamples
{
    public delegate void RefScopedCallback(ref int value, @scoped sibling);

    public static RefScopedCallback ByRefLambdaWithScopedSibling()
        => (ref int value, @scoped sibling) => System.Console.WriteLine(value);
}

public static class GenericLambdaRaisingSamples<T>
{
    public delegate void RefGenericCallback(ref int value, T sibling);

    public static RefGenericCallback ByRefLambdaWithGenericSibling()
        => (ref int value, T sibling) => System.Console.WriteLine(value);
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
