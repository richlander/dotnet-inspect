using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExtensionMethodCallTests
{
    // Extension-ness is a cross-assembly fact (System.Linq lives in the shared
    // framework, not beside the test assembly), so the default sibling resolver
    // cannot resolve it. Reach the running runtime directory, the same pattern
    // AllocationOccurrenceFactTests uses for its corelib base-chain checks.
    static readonly ILInspector.Metadata.IAssemblyReferenceResolver RuntimeResolver = TestAssemblyReferenceResolvers.RuntimeAssemblies();

    static string PrintRaised(string methodName)
        => PrintRaised(typeof(CfgSampleClass), methodName);

    static IrFunction Import(string methodName)
    {
        using var context = new MetadataContext(RuntimeResolver);
        using var source = MetadataSource.Open(
            typeof(CfgSampleClass).Assembly.Location,
            null,
            RuntimeResolver,
            context);
        return Assert.IsType<IrFunction>(
            IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName));
    }

    static string PrintRaised(Type type, string methodName)
    {
        using var context = new MetadataContext(RuntimeResolver);
        using var source = MetadataSource.Open(type.Assembly.Location, null, RuntimeResolver, context);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void LinqCall_RendersAsInstanceExtensionSyntax()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        // The static spelling Enumerable.Where(items, pred) is rendered as the
        // instance form items.Where(pred) the source used.
        Assert.Contains(".Where", output);
        Assert.DoesNotContain("Enumerable.Where", output);
    }

    [Fact]
    public void LinqChain_RendersAsFluentInstanceChain()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        Assert.Contains(".Where", output);
        Assert.Contains(".Select", output);
        Assert.DoesNotContain("Enumerable.Where", output);
        Assert.DoesNotContain("Enumerable.Select", output);
        // The first call is the receiver of the second: ...Where(...).Select(...).
        int where = output.IndexOf(".Where", StringComparison.Ordinal);
        int select = output.IndexOf(".Select", StringComparison.Ordinal);
        Assert.True(where >= 0 && select > where, $"expected .Where(...).Select(...), got: {output}");
    }

    [Fact]
    public void SameAssemblyUserExtension_RendersAsInstanceSyntax()
    {
        // The [Extension] mark is read from the same-assembly MethodDef, so a user
        // extension sugars the same way the cross-assembly LINQ ones do.
        string output = PrintRaised(nameof(CfgSampleClass.CallsUserExtension));

        Assert.Contains("n.Doubled()", output);
        Assert.DoesNotContain("ExtensionMethodSamples.Doubled", output);
    }

    [Fact]
    public void NonExtensionStatic_KeepsStaticSpelling()
    {
        // A plain static with a first parameter is byte-identical in shape to an
        // extension call but carries no [Extension] mark, so it must NOT sugar to
        // receiver syntax — the precision guard on the IsExtension gate.
        string output = PrintRaised(nameof(CfgSampleClass.CallsNonExtensionStatic));

        Assert.Contains("ExtensionMethodSamples.Combine(a, b)", output);
        Assert.DoesNotContain("a.Combine", output);
    }

    [Fact]
    public void ShadowingInstanceMethod_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionMethodCollisionSamples),
            nameof(ExtensionMethodCollisionSamples.CallsShadowedExtension));

        Assert.Equal(
            "return Values(receiver, typeof(Attribute), true).FirstOrDefault();",
            output);
    }

    [Fact]
    public void PlatformBaseInstanceMethod_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionMethodCollisionSamples),
            nameof(
                ExtensionMethodCollisionSamples
                    .CallsPlatformShadowedExtension));

        Assert.Contains(
            "global::System.Reflection.CustomAttributeExtensions.GetCustomAttributes(typeInfo, typeof(Attribute), true)",
            output);
        Assert.DoesNotContain(
            "typeInfo.GetCustomAttributes",
            output);
    }

    [Fact]
    public void GenericExtensionShadowedByInstanceMethod_KeepsStaticSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionMethodCollisionSamples),
            nameof(
                ExtensionMethodCollisionSamples
                    .CallsShadowedGenericExtension));

        Assert.Equal(
            "return global::System.Linq.Enumerable.Contains(values, value);",
            output);
    }

    [Fact]
    public void SameNamedProperty_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ExtensionPropertyCollisionSamples),
            nameof(
                ExtensionPropertyCollisionSamples
                    .CallsPropertyShadowedExtension));

        Assert.Equal(
            "return Values(receiver, typeof(Attribute), true).FirstOrDefault();",
            output);
    }

    [Fact]
    public void ByRefReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(RefExtensionCollisionSamples),
            nameof(
                RefExtensionCollisionSamples
                    .CallsShadowedRefExtension));

        Assert.Equal("return Value(ref receiver);", output);
    }

    [Fact]
    public void PointerBackedRefReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(RefExtensionCollisionSamples),
            nameof(
                RefExtensionCollisionSamples
                    .CallsPointerShadowedRefExtension));

        Assert.Equal("return Value(ref *receiver);", output);
    }

    [Fact]
    public void ArrayReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(ArrayExtensionCollisionSamples),
            nameof(
                ArrayExtensionCollisionSamples
                    .CallsShadowedArrayExtension));

        Assert.Equal(
            "return Clone(values);",
            output);
    }

    [Fact]
    public void InterfaceReceiver_IncludesObjectMembers()
    {
        string output = PrintRaised(
            typeof(InterfaceExtensionCollisionSamples),
            nameof(
                InterfaceExtensionCollisionSamples
                    .CallsObjectShadowedExtension));

        Assert.Equal(
            "return Equals(receiver, other);",
            output);
    }

    [Fact]
    public void GenericParameterReceiver_KeepsStaticExtensionSpelling()
    {
        string output = PrintRaised(
            typeof(GenericParameterExtensionCollisionSamples),
            nameof(
                GenericParameterExtensionCollisionSamples
                    .CallsConstraintUnknownExtension));

        Assert.Equal(
            "return Equals<T>(value, other);",
            output);
    }

    [Fact]
    public void ReceiverInferredGenericArguments_AreOmitted()
    {
        var function = Import(nameof(CfgSampleClass.ReceiverInferredExtensionArguments));
        var concat = Assert.Single(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Concat");
        Assert.Equal(MetadataFactState.Yes, concat.Callee.TypeArgumentElisionOverloadSafety);
        Assert.Equal(TypeRefKind.GenericInstance, concat.Callee.DefinitionParameterTypes[0].Kind);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            concat.Callee.DefinitionParameterTypes[0].TypeArguments[0].Kind);
        Assert.True(concat.Callee.CanOmitTypeArguments);

        string output = PrintRaised(nameof(CfgSampleClass.ReceiverInferredExtensionArguments));

        Assert.Contains("values.Concat([extra]).Distinct()", output);
        Assert.DoesNotContain("Concat<string>", output);
        Assert.DoesNotContain("Distinct<string>", output);
    }

    [Fact]
    public void SameReceiverOverloads_KeepTheInferenceCandidateSet()
    {
        string output = PrintRaised(nameof(CfgSampleClass.SameReceiverOverloadsRemainInferable));

        Assert.Contains("values.Where(value =>", output);
        Assert.DoesNotContain("Where<string>", output);
    }

    [Fact]
    public void LambdaResultInference_OmitsGenericArguments()
    {
        var function = Import(nameof(CfgSampleClass.ResultInferredExtensionArgumentsAreOmitted));
        var select = Assert.Single(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Select");
        var output = Assert.Single(select.Callee.TypeArgumentElisionLambdaOutputs);
        Assert.Equal(1, output.TypeArgumentIndex);
        Assert.Equal(1, output.ArgumentIndex);
        Assert.True(select.Callee.CanOmitTypeArguments);

        string text = PrintRaised(nameof(CfgSampleClass.ResultInferredExtensionArgumentsAreOmitted));

        Assert.Contains("values.Select(value => value.Length)", text);
        Assert.DoesNotContain("Select<string, int>", text);
    }

    [Fact]
    public void FluentLambdaResultInference_OmitsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.FluentResultInferredExtensionArgumentsAreOmitted));

        Assert.Contains(
            "values.OrderBy(value => value.Length).ThenBy(value => value)",
            output);
        Assert.DoesNotContain("OrderBy<string, int>", output);
        Assert.DoesNotContain("ThenBy<string, string>", output);
    }

    [Fact]
    public void AmbiguousReceiverInstantiation_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.AmbiguousReceiverArgumentsRemainExplicit));

        Assert.Contains("receiver.Value<int>()", output);
    }

    [Fact]
    public void CovariantReceiverConversion_KeepsDifferentGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CovariantReceiverArgumentsRemainExplicit));

        Assert.Contains("receiver.CovariantValue<object>()", output);
    }

    [Fact]
    public void CompetingGenericArity_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CompetingGenericArityRemainsExplicit));

        Assert.Contains("receiver.Overloaded<int>(\"value\")", output);
    }

    [Fact]
    public void ExactSameAssemblyReceiver_OmitsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.ExactSameAssemblyReceiverArgumentsAreOmitted));

        Assert.Contains("receiver.Value()", output);
    }

    [Fact]
    public void ConflictingArgumentInference_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.ConflictingArgumentInferenceRemainsExplicit));

        Assert.Contains("values.Append<byte>(1)", output);
    }

    [Fact]
    public void CompetingSiblingInference_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CompetingSiblingInferenceRemainsExplicit));

        Assert.Contains("values.SiblingInference<string>(factory)", output);
    }

    [Fact]
    public void BareReceiverConversion_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.BareReceiverInferenceRemainsExplicit));

        Assert.Contains("value.BareReceiverType<object>()", output);
    }

    [Fact]
    public void RaisedLambdaOutputInference_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.LambdaOutputInferenceRemainsExplicit));

        Assert.Contains("values.TakeFactory<byte>(() => 1)", output);
    }

    [Fact]
    public void LambdaOutputNaturalTypeMismatch_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.OutputInferenceNaturalTypeMismatchRemainsExplicit));

        Assert.Contains("values.Select<string, byte>(_ => 1)", output);
    }

    [Fact]
    public void LambdaOutputWithoutNaturalType_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.OutputInferenceTypelessResultRemainsExplicit));

        Assert.Contains("values.Select<string, string>(_ => null)", output);
    }

    [Fact]
    public void ExpressionTreeOutputInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.ExpressionTreeOutputInferenceRemainsExplicit));

        Assert.Contains("values.Select<string, int>", output);
    }

    [Fact]
    public void OutputInferredMethodGroup_KeepsGenericArguments()
    {
        string output = PrintRaised(
            typeof(OutputInferenceMethodGroupSamples),
            nameof(OutputInferenceMethodGroupSamples.Call));

        Assert.Contains("values.Select<string, int>", output);
        Assert.Contains("int.Parse", output);
    }

    [Fact]
    public void CompetingOutputInferenceSibling_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.CompetingOutputInferenceSiblingRemainsExplicit));

        Assert.Contains(
            "values.OutputCandidate<string, int>(value => value.Length)",
            output);
    }

    [Fact]
    public void ParamsCollectionOverload_KeepsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.ParamsCollectionInferenceRemainsExplicit));

        Assert.Contains("values.ParamsCollection<string>(\"a\", \"b\")", output);
    }

    [Fact]
    public void ArrayReceiverInference_OmitsGenericArguments()
    {
        string output = PrintRaised(nameof(CfgSampleClass.ArrayReceiverArgumentsAreOmitted));

        Assert.Contains("values.CopyArray()", output);
    }

    [Fact]
    public void FunctionPointerParameterInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.FunctionPointerParameterInferenceRemainsExplicit));

        Assert.Contains("values.FunctionPointerArgument<string>(callback)", output);
    }

    [Fact]
    public void NullSiblingInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.NullSiblingInferenceRemainsExplicit));

        Assert.Contains(
            "values.NullSiblingInference<string>(null, factory)",
            output);
    }

    [Fact]
    public void ConversionSiblingInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.ConversionSiblingInferenceRemainsExplicit));

        Assert.Contains(
            "values.ConversionSiblingInference<string>(\"value\", factory)",
            output);
    }

    [Fact]
    public void SpanSiblingInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.SpanSiblingInferenceRemainsExplicit));

        Assert.Contains(
            "values.SpanSiblingInference<string>(span, \"value\")",
            output);
    }

    [Fact]
    public void CollectionSiblingInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.CollectionSiblingInferenceRemainsExplicit));

        Assert.Contains(
            "values.CollectionSiblingInference<string>([new object()])",
            output);
    }

    [Fact]
    public void CollectionElementInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.CollectionElementInferenceRemainsExplicit));

        Assert.Contains("values.CollectionArgument<byte>([1])", output);
    }

    [Fact]
    public void TupleElementInference_KeepsGenericArguments()
    {
        string output = PrintRaised(
            nameof(CfgSampleClass.TupleElementInferenceRemainsExplicit));

        Assert.Contains("values.TupleArgument<byte>((1, 2))", output);
    }

    [Fact]
    public void UnresolvedMethodMetadata_KeepsGenericArguments()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.ReceiverInferredExtensionArguments)));
        var concat = Assert.Single(
            function.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Concat");

        Assert.Equal(MetadataFactState.Unknown, concat.Callee.IsExtension);
        Assert.False(concat.Callee.CanOmitTypeArguments);
    }
}
