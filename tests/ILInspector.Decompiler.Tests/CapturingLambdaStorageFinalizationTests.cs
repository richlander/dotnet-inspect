using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class CapturingLambdaStorageFinalizationTests
{
    [Fact]
    public void CallbackInlinesArrayBeforeStorageFinalization()
    {
        var (function, text) = Raise(nameof(CapturingLambdaStorageFinalizationSamples.Callback));
        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        var call = Assert.Single(lambda.Body.Descendants.OfType<Call>());
        Assert.IsType<ArrayLiteral>(call.Arguments[^1]);
        Assert.Empty(lambda.Body.Descendants.OfType<StoreLocal>());
        Assert.Empty(lambda.Body.Descendants.OfType<StoreStackSlot>());
        Assert.Contains("method.Invoke(target, new object[] { value });", text);
    }

    [Fact]
    public void PureCapturedReceiverDoesNotNeedAnAliasAcrossTheCall()
    {
        var (function, text) = Raise(nameof(CapturingLambdaStorageFinalizationSamples.CapturedReader));
        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        Assert.Empty(lambda.Body.Descendants.OfType<StoreStackSlot>());
        var store = Assert.Single(lambda.Body.Descendants.OfType<StoreLocal>());
        Assert.Equal("GenericParameter", store.Type.Name);
        Assert.Contains("reader.GetGenericParameter(handle)", text);
        Assert.Contains("reader.GetString(", text);
    }

    [Fact]
    public void EffectfulEarlierArgumentKeepsArrayBeforeCall()
    {
        var (function, _) = Raise(nameof(CapturingLambdaStorageFinalizationSamples.EffectfulEarlierArgument));
        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        var store = Assert.Single(lambda.Body.Descendants.OfType<StoreLocal>());
        Assert.IsType<ArrayLiteral>(store.Value);
        var invocation = Assert.Single(lambda.Body.Descendants.OfType<Call>(),
            call => call.Callee.Name == "Invoke" && call.Arguments.Count == 3);
        Assert.IsType<LoadLocal>(invocation.Arguments[^1]);
        Assert.True(store.ChildIndex < invocation.Parent!.ChildIndex);
    }

    [Fact]
    public void MultipleUsesKeepOneArrayIdentity()
    {
        var (function, _) = Raise(nameof(CapturingLambdaStorageFinalizationSamples.MultipleUses));
        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        var array = Assert.Single(lambda.Body.Descendants.OfType<ArrayLiteral>());
        var store = Assert.IsType<StoreLocal>(array.Parent);
        Assert.Equal(2, lambda.Body.Descendants.OfType<LoadLocal>()
            .Count(load => load.Index == store.Index));
    }

    [Theory]
    [InlineData(nameof(CapturingLambdaStorageFinalizationSamples.EffectfulEarlierArgument))]
    [InlineData(nameof(CapturingLambdaStorageFinalizationSamples.CovariantArrayStorage))]
    [InlineData(nameof(CapturingLambdaStorageFinalizationSamples.MultipleUses))]
    public void SyntheticSpillRetainsOrderingTypeWitnessAndMultipleUseBoundaries(string method)
    {
        var (function, _) = Raise(method, useSyntheticArrayStorage: true);
        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        var array = Assert.Single(lambda.Body.Descendants.OfType<ArrayLiteral>());
        Assert.True(array.Parent is StoreLocal or StoreStackSlot);
        var invocations = lambda.Body.Descendants.OfType<Call>()
            .Where(call => call.Callee.Name == "Invoke" && call.Arguments.Count == 3).ToArray();
        Assert.NotEmpty(invocations);
        foreach (var invocation in invocations)
        {
            Assert.True(invocation.Arguments[^1] is LoadLocal or LoadStackSlot);
            Assert.True(array.Parent.ChildIndex < invocation.Parent!.ChildIndex);
        }
    }

    [Theory]
    [InlineData(nameof(CapturingLambdaStorageFinalizationSamples.CapturedLocal))]
    [InlineData(nameof(CapturingLambdaStorageFinalizationSamples.FurtherNestedBody))]
    [InlineData(nameof(CapturingLambdaStorageFinalizationSamples.NestedNonCapturingBody))]
    public void UnsupportedCaptureScopesKeepTheirDelegateCreation(string method)
    {
        var (function, _) = Raise(method);
        Assert.Empty(function.Descendants.OfType<Lambda>());
        Assert.NotEmpty(function.Descendants.OfType<DelegateCreation>());
    }

    [Fact]
    public void UserLocalUpdateKeepsItsStorage()
    {
        var (function, _) = Raise(nameof(CapturingLambdaStorageFinalizationSamples.ExistingUserLocal));
        var stores = function.Descendants.OfType<StoreLocal>().Where(store => store.Index == 0).ToArray();
        Assert.Equal(2, stores.Length);
        Assert.IsType<LoadArgument>(stores[0].Value);
        var addition = Assert.IsType<Binary>(stores[1].Value);
        Assert.Equal(BinaryKind.Add, addition.Kind);
        var savedValue = Assert.IsType<LoadLocal>(addition.Left);
        Assert.NotEqual(stores[0].Index, savedValue.Index);
        var array = Assert.Single(function.Descendants.OfType<ArrayLiteral>());
        Assert.Contains(array.Descendants, node => node is LoadLocal load && load.Index == savedValue.Index);
    }

    [Fact]
    public void OrdinaryArrayReturnIsUnchanged()
    {
        var (function, _) = Raise(nameof(CapturingLambdaStorageFinalizationSamples.NonCapturing));
        Assert.IsType<ArrayLiteral>(Assert.Single(function.Descendants.OfType<Return>()).Value);
    }

    static (IrFunction Function, string Text) Raise(string method, bool useSyntheticArrayStorage = false)
    {
        using var source = MetadataSource.Open(typeof(CapturingLambdaStorageFinalizationSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CapturingLambdaStorageFinalizationSamples).FullName!, method);
        Assert.NotNull(function);
        var lambdaMethod = useSyntheticArrayStorage
            ? Assert.Single(function.Descendants.OfType<LoadFunctionPointer>()).Method
            : null;
        int substitutedBodies = 0;
        var result = CSharpPrinter.PrintRaised(function, reference =>
        {
            var body = IrImporter.Import(source, reference);
            if (body is not null && reference == lambdaMethod)
            {
                ReplaceArrayLocalWithSyntheticSpill(body);
                substitutedBodies++;
            }
            return body;
        });
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(useSyntheticArrayStorage ? 1 : 0, substitutedBodies);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        Assert.NotNull(result.Output);
        return (function, result.Output);
    }

    static void ReplaceArrayLocalWithSyntheticSpill(IrFunction body)
    {
        const int slot = 1024;
        var invocations = body.Descendants.OfType<Call>()
            .Where(call => call.Callee.Name == "Invoke" && call.Arguments.Count == 3).ToArray();
        Assert.NotEmpty(invocations);
        int index = Assert.IsType<LoadLocal>(invocations[0].Arguments[^1]).Index;
        Assert.All(invocations, call => Assert.Equal(index, Assert.IsType<LoadLocal>(call.Arguments[^1]).Index));
        Assert.Equal(TypeRef.SzArray(TypeRef.CoreLib("System", "Object")), body.Locals[index]);
        Assert.DoesNotContain(body.Descendants, node => node is StoreStackSlot store && store.Slot == slot
            || node is LoadStackSlot load && load.Slot == slot);
        foreach (var load in body.Descendants.OfType<LoadLocal>().Where(load => load.Index == index).ToArray())
            load.ReplaceWith(new LoadStackSlot(slot, body.Locals[index]));
        foreach (var store in body.Descendants.OfType<StoreLocal>().Where(store => store.Index == index).ToArray())
        {
            var value = Assert.IsAssignableFrom<IrExpression>(Assert.Single(store.DetachChildren()));
            store.ReplaceWith(new StoreStackSlot(slot, value));
        }
        body.CheckInvariant(includeSemantics: true);
    }
}
