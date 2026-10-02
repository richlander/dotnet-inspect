using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void DirectCalls_AttributeAsyncCallSitesToSourceMethod()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall directCall = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync)
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
        DirectCall groupedCall = Assert.Single(
            index.CallGraph.DirectCallsByCaller[
                directCall.Caller.MetadataToken],
            call => call.ILOffset == directCall.ILOffset);
        DirectCall foundCall = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    directCall.Callee.DeclaringType,
                    directCall.Callee.Name)),
            call => call.Caller == directCall.Caller);

        foreach (DirectCall call in
            new[] { directCall, groupedCall, foundCall })
        {
            Assert.Equal(
                nameof(ClassicAsyncSiblingFixture
                    .CallsSyncSiblingFromAsync),
                call.Caller.Name);
            Assert.NotEqual(
                call.Caller.MetadataToken,
                call.EvidenceMethod.MetadataToken);
            Assert.Equal("MoveNext", call.EvidenceMethod.Name);
        }

        Assert.DoesNotContain(
            index.CallGraph.DirectCalls,
            call => call.Caller.Name == "MoveNext"
                && call.Caller.DeclaringType.Name.Contains(
                    nameof(ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync),
                    StringComparison.Ordinal));
        Assert.NotEmpty(
            index.CallGraph.DirectCalls.Where(
                call => call.Caller.Name
                    == nameof(
                        ClassicAsyncSiblingFixture.ReadValueAsync)));
        Assert.All(
            index.CallGraph.DirectCalls.Where(
                call => call.Caller.Name
                    == nameof(
                        ClassicAsyncSiblingFixture.ReadValueAsync)),
            call => Assert.Equal(
                call.Caller,
                call.EvidenceMethod));

        MethodIdentity sourceMethod = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture
                    .CallsSyncSiblingFromAsync));
        var scoped = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                sourceMethod.MetadataToken,
            });
        Assert.Contains(
            scoped.CallGraph.DirectCalls,
            call => call.Caller == sourceMethod
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
    }

    [Fact]
    public void TopLeverage_UsesCallGraphDeclaredCallerCurrency()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity target = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
        MethodLeverage leverage = Assert.Single(
            index.Leverage.Top(
                    int.MaxValue,
                    method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture))
                .Where(entry => entry.Method == target));
        CallTreeNode callers = index.BuildCallerTree(
            target.MetadataToken,
            maxDepth: 1,
            maxNodes: 100);

        Assert.Equal(
            callers.Perf!.Fanin,
            leverage.DirectCallerCount);
    }

    [Fact]
    public void
        DirectCalls_TypeScopeIncludesAsyncLiftedBodies()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity sourceMethod = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture
                    .AwaitTaskInAsyncLambda));
        DirectCall expected = Assert.Single(
            full.CallGraph.DirectCalls,
            call => call.Caller == sourceMethod
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name
                    == "GetAwaiter");
        var memberScoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                sourceMethod.MetadataToken,
            });
        var typeScoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyTypeScope:
                type => type.ToQualifiedDisplayString()
                    == sourceMethod.DeclaringType
                        .ToQualifiedDisplayString());

        Assert.Contains(
            memberScoped.CallGraph.DirectCalls,
            call => call.Caller == expected.Caller
                && call.EvidenceMethod
                    == expected.EvidenceMethod
                && call.ILOffset == expected.ILOffset
                && call.Callee == expected.Callee);
        Assert.Contains(
            typeScoped.CallGraph.DirectCalls,
            call => call.Caller == expected.Caller
                && call.EvidenceMethod
                    == expected.EvidenceMethod
                && call.ILOffset == expected.ILOffset
                && call.Callee == expected.Callee);
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsClassicAsyncMoveNextToSource()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(path);

        var source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture.CallsSyncSiblingFromAsync));
        var mapped = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    source.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => index.CallGraph.ResolveDeclaredMethod(
                    call.EvidenceMethod)?.MetadataToken
                == source.MetadataToken);

        Assert.Equal(source, mapped.Caller);
        Assert.Equal("MoveNext", mapped.EvidenceMethod.Name);
        Assert.Null(index.CallGraph.ResolveDeclaredMethod(source));
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsClassicAsyncMoveNextWithoutOpportunities()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            includeAllocations: false,
            includeOpportunities: false);

        var source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture.CallsSyncSiblingFromAsync));
        Assert.Contains(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    source.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == source
                && call.EvidenceMethod.Name == "MoveNext"
                && index.CallGraph.ResolveDeclaredMethod(
                    call.EvidenceMethod)?.MetadataToken
                    == source.MetadataToken);
    }

    [Fact]
    public void ResultSinks_PublishRuntimeAsyncBodyAttribution()
    {
        string path =
            typeof(OptimizationOpportunityAsyncSiblingFixtures)
                .Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures)
                && method.Name == nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                        .CallsSyncSiblingFromAsync));
        MethodResultSink sink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller == source
                && candidate.EvidenceMethod == source
                && candidate.Kind
                    == MethodResultSinkKind.MethodReturn);
        AsyncBodyAttribution attribution =
            Assert.IsType<AsyncBodyAttribution>(sink.AsyncBody);

        Assert.True(sink.IsComplete);
        Assert.Equal(source, attribution.SourceMethod);
        Assert.Equal(
            AsyncLoweringKind.Runtime,
            attribution.Lowering);

        MethodIdentity iteratorSource = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures)
                && method.Name == nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                        .ReadValuesAsync));
        MethodResultSink[] iteratorSinks =
        [
            .. index.JsonWireContracts.ResultSinks.Where(
                candidate => candidate.AsyncBody?.SourceMethod
                    == iteratorSource),
        ];

        Assert.NotEmpty(iteratorSinks);
        Assert.All(
            iteratorSinks,
            candidate =>
            {
                Assert.NotEqual(
                    iteratorSource,
                    candidate.EvidenceMethod);
                Assert.Equal(
                    AsyncLoweringKind.StateMachine,
                    candidate.AsyncBody!.Lowering);
            });
    }

    [Fact]
    public void ResultSinks_PublishStateMachineAsyncBodyAttribution()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync));
        MethodResultSink sink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller == source
                && candidate.EvidenceMethod != source
                && candidate.Kind
                    == MethodResultSinkKind.SingleArgumentCall
                && candidate.IsComplete);
        AsyncBodyAttribution attribution =
            Assert.IsType<AsyncBodyAttribution>(sink.AsyncBody);

        Assert.Equal("MoveNext", sink.EvidenceMethod.Name);
        Assert.Equal(source, attribution.SourceMethod);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            attribution.Lowering);
    }

    [Fact]
    public void
        ResultSinks_PreserveCallSourceAcrossAsyncStateMachineField()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink sink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller == source
                && candidate.StateMachineFieldSource is not null);
        AsyncStateMachineFieldResultSource fieldSource =
            sink.StateMachineFieldSource!;

        Assert.False(sink.IsComplete);
        Assert.Empty(sink.SourceCallOffsets);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            sink.AsyncBody?.Lowering);
        Assert.Equal("MoveNext", sink.EvidenceMethod.Name);
        Assert.Equal(
            sink.EvidenceMethod.DeclaringType,
            fieldSource.Field.DeclaringType);
        Assert.NotEqual(0, fieldSource.Field.LocalDefinitionToken);
        Assert.True(fieldSource.StoreOffset < fieldSource.LoadOffset);
        DirectCall producer = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.EvidenceMethod == sink.EvidenceMethod
                && fieldSource.SourceCallOffsets.Contains(
                    call.ILOffset));
        Assert.Equal("ProducePayload", producer.Callee.Name);

        MethodIdentity multipleAwaits = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeMultipleAwaits));
        MethodResultSink multipleAwaitSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller == multipleAwaits
                && candidate.StateMachineFieldSource is not null);
        Assert.Equal(
            2,
            index.CallGraph.DirectCalls.Count(call =>
                call.Caller == multipleAwaits
                && call.Callee.Name
                    is "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted"));
        Assert.Equal(
            "ProducePayload",
            Assert.Single(
                index.CallGraph.DirectCalls,
                call => call.EvidenceMethod
                        == multipleAwaitSink.EvidenceMethod
                    && multipleAwaitSink.StateMachineFieldSource!
                        .SourceCallOffsets.Contains(
                            call.ILOffset))
                .Callee.Name);

        var unoptimized = BodyAnalysisTestExecution.Open(
            typeof(UnoptimizedAsyncFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures
                    .JsonWireContractFlow);
        MethodIdentity referenceStateMachine = Assert.Single(
            unoptimized.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(UnoptimizedAsyncFixture)
                && method.Name == nameof(
                    UnoptimizedAsyncFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink referenceSink = Assert.Single(
            unoptimized.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller
                    == referenceStateMachine
                && candidate.StateMachineFieldSource
                    is not null);
        DirectCall referenceSuspension = Assert.Single(
            unoptimized.CallGraph.DirectCalls,
            call => call.Caller == referenceStateMachine
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted");
        Assert.Contains(
            typeof(UnoptimizedAsyncFixture).Assembly.GetTypes(),
            type => type.IsClass
                && typeof(IAsyncStateMachine)
                    .IsAssignableFrom(type));
        Assert.True(
            referenceSuspension
                .SecondByRefArgumentIsCurrentInstance);

        MethodIdentity multipleReferenceAwaits = Assert.Single(
            unoptimized.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(UnoptimizedAsyncFixture)
                && method.Name == nameof(
                    UnoptimizedAsyncFixture
                        .ReturnsCallStoredBeforeMultipleAwaits));
        Assert.Single(
            unoptimized.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller
                    == multipleReferenceAwaits
                && candidate.StateMachineFieldSource
                    is not null);
        DirectCall[] multipleReferenceSuspensions =
        [
            .. unoptimized.CallGraph.DirectCalls.Where(call =>
                call.Caller == multipleReferenceAwaits
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted"),
        ];
        Assert.Equal(2, multipleReferenceSuspensions.Length);
        Assert.All(
            multipleReferenceSuspensions,
            suspension => Assert.True(
                suspension
                    .SecondByRefArgumentIsCurrentInstance));
    }

    [Fact]
    public void
        ResultSinks_RejectAddressMutatedReferenceStateMachineArgument()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .AddressMutatedReferenceStateMachineSource));
        DirectCall replacement = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == source
                && call.Callee.Name == "ReplaceStateMachine");
        DirectCall suspension = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == source
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted");

        Assert.True(replacement.ILOffset < suspension.ILOffset);
        Assert.Equal(
            suspension.EvidenceMethod.DeclaringType,
            suspension.Callee.ParameterTypes[1].ElementType);
        Assert.False(
            suspension.SecondByRefArgumentIsCurrentInstance);
        Assert.DoesNotContain(
            index.JsonWireContracts.ResultSinks,
            sink => sink.Caller == source
                && sink.StateMachineFieldSource is not null);
    }

    [Fact]
    public async Task
        ResultSinks_RejectWholeStateMachineInstanceWrite()
    {
        string? actual = await ClassicAsyncSiblingFixture
            .WholeInstanceWriteStateMachineSource();
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .WholeInstanceWriteStateMachineSource));
        MethodResultSink completion = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink => sink.Caller == source
                && sink.ResolvedValue?.Single is
                {
                    Kind:
                        ResolvedValueSourceKind.InstanceFieldLoad,
                    FieldIdentity: { } field,
                }
                && field.Name == "Payload");

        Assert.Null(actual);
        Assert.Single(
            index.JsonWireContracts.FieldStores,
            store => store.EvidenceMethod
                    == completion.EvidenceMethod
                && store.Identity?.Name == "Payload");
        Assert.DoesNotContain(
            index.JsonWireContracts.FieldLoads,
            load => load.EvidenceMethod
                    == completion.EvidenceMethod
                && load.Identity?.Name == "Payload"
                && load.IsAddress);
        Assert.Null(completion.StateMachineFieldSource);
    }

    [Fact]
    public void
        ResultSinks_InventoryNonGenericFrameworkBuilderSuspensions()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .NonGenericSuspensionBuilderSource));
        DirectCall[] suspensions =
        [
            .. index.CallGraph.DirectCalls.Where(call =>
                call.Caller == source
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted"),
        ];

        Assert.Equal(2, suspensions.Length);
        Assert.Contains(
            suspensions,
            call => FrameworkIdentity.IsCoreLibraryType(
                call.Callee.DeclaringType,
                "System.Runtime.CompilerServices",
                "AsyncTaskMethodBuilder`1"));
        Assert.Contains(
            suspensions,
            call => FrameworkIdentity.IsCoreLibraryType(
                call.Callee.DeclaringType,
                "System.Runtime.CompilerServices",
                "AsyncTaskMethodBuilder"));
        Assert.DoesNotContain(
            index.JsonWireContracts.ResultSinks,
            sink => sink.Caller == source
                && sink.StateMachineFieldSource is not null);
    }

    [Fact]
    public void
        ResultSinks_RejectAmbiguousAsyncStateMachineFieldSources()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        string[] rejected =
        [
            nameof(
                ClassicAsyncSiblingFixture
                    .DoesNotBorrowUnrelatedFieldStore),
            nameof(
                ClassicAsyncSiblingFixture
                    .HasMultipleStoresBeforeAwait),
            nameof(
                ClassicAsyncSiblingFixture
                    .ConditionallyOverwritesParameterBeforeAwait),
            nameof(
                ClassicAsyncSiblingFixture
                    .ConditionallySuspendsAfterParameterOverwrite),
            nameof(
                ClassicAsyncSiblingFixture
                    .ConditionallyInitializesLocalBeforeSuspension),
            nameof(
                ClassicAsyncSiblingFixture
                    .MutatesFieldByReferenceAfterAwait),
            nameof(
                ClassicAsyncSiblingFixture
                    .StoresInLoopBeforeAwait),
            nameof(
                ClassicAsyncSiblingFixture
                    .UsesCustomAsyncBuilder),
            nameof(
                ClassicAsyncSiblingFixture
                    .CustomBuilderSecondarySource),
            nameof(
                ClassicAsyncSiblingFixture
                    .ExternalAddressSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .MismatchedBuilderSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .ReenteringCleanupSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .MixedSuspensionBuilderSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .ImmediateCompletionSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .WrongStateMachineArgumentSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .FailedExternalStoreSource),
            nameof(
                ClassicAsyncSiblingFixture
                    .StoresAfterDifferentSuspension),
        ];

        foreach (string methodName in rejected)
        {
            MethodIdentity source = Assert.Single(
                index.CallGraph.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == methodName);
            Assert.DoesNotContain(
                index.JsonWireContracts.ResultSinks,
                sink => sink.Caller == source
                    && sink.StateMachineFieldSource is not null);
        }

        MethodIdentity multipleStores = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .HasMultipleStoresBeforeAwait));
        MethodResultSink multipleStoreSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink => sink.Caller == multipleStores
                && sink.ResolvedValue?.Single is
                {
                    Kind:
                        ResolvedValueSourceKind.InstanceFieldLoad,
                    FieldIdentity: not null,
                });
        ResolvedValueSource multipleStoreLoad =
            multipleStoreSink.ResolvedValue!.Single!;
        Assert.Equal(
            2,
            index.JsonWireContracts.FieldStores.Count(store =>
                store.EvidenceMethod
                    == multipleStoreSink.EvidenceMethod
                && store.ILOffset < multipleStoreLoad.ILOffset
                && multipleStoreLoad.FieldIdentity!.Equals(
                    store.Identity)
                && store.Value.Single?.Kind
                    == ResolvedValueSourceKind.CallResult));

        MethodIdentity byReference = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .MutatesFieldByReferenceAfterAwait));
        DirectCall byReferenceCompletion = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == byReference
                && call.Callee.Name == "SetResult");
        MethodResultSink byReferenceSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink =>
                sink.EvidenceMethod
                    == byReferenceCompletion.EvidenceMethod
                && sink.ILOffset
                    == byReferenceCompletion.ILOffset);
        FieldIdentity byReferenceField =
            byReferenceSink.ResolvedValue!.Single!.FieldIdentity!;
        Assert.Contains(
            index.JsonWireContracts.FieldLoads,
            load => load.EvidenceMethod
                    == byReferenceSink.EvidenceMethod
                && load.IsAddress
                && byReferenceField.Equals(load.Identity));

        MethodIdentity conditionalSuspension = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ConditionallySuspendsAfterParameterOverwrite));
        FieldIdentity conditionalField = Assert.IsType<FieldIdentity>(
            Assert.Single(
                index.JsonWireContracts.ResultSinks,
                sink => sink.Caller == conditionalSuspension
                    && sink.ResolvedValue?.Single is
                    {
                        Kind:
                            ResolvedValueSourceKind.InstanceFieldLoad,
                    })
                .ResolvedValue!.Single!.FieldIdentity);
        Assert.Contains(
            index.JsonWireContracts.FieldStores,
            store => store.EvidenceMethod
                    == conditionalSuspension
                && conditionalField.Equals(store.Identity));
        Assert.Contains(
            index.JsonWireContracts.FieldStores,
            store => store.Caller == conditionalSuspension
                && store.EvidenceMethod
                    != conditionalSuspension
                && conditionalField.Equals(store.Identity));

        MethodIdentity conditionalLocal = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ConditionallyInitializesLocalBeforeSuspension));
        FieldIdentity conditionalLocalField =
            Assert.IsType<FieldIdentity>(
                Assert.Single(
                    index.JsonWireContracts.ResultSinks,
                    sink => sink.Caller == conditionalLocal
                        && sink.ResolvedValue?.Single is
                        {
                            Kind:
                                ResolvedValueSourceKind.InstanceFieldLoad,
                        })
                    .ResolvedValue!.Single!.FieldIdentity);
        FieldStoreFact[] conditionalLocalStores =
        [
            .. index.JsonWireContracts.FieldStores.Where(store =>
                store.EvidenceMethod.DeclaringType.Equals(
                    conditionalLocalField.DeclaringType)
                && conditionalLocalField.Equals(
                    store.Identity)),
        ];
        Assert.Contains(
            conditionalLocalStores,
            store => store.Value.Single?.Kind
                == ResolvedValueSourceKind.CallResult);
        Assert.Contains(
            conditionalLocalStores,
            store => store.Value.Single?.Kind
                == ResolvedValueSourceKind.NullReference);

        MethodIdentity looped = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .StoresInLoopBeforeAwait));
        Assert.Contains(
            index.CallGraph.DirectCalls,
            call => call.Caller == looped
                && call.Callee.Name == "ProducePayload"
                && call.InLoop);

        MethodIdentity customBuilder = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .UsesCustomAsyncBuilder));
        DirectCall customCompletion = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == customBuilder
                && call.Callee.Name == "SetResult");
        MethodResultSink customSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink =>
                sink.EvidenceMethod
                    == customCompletion.EvidenceMethod
                && sink.ILOffset == customCompletion.ILOffset);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            customSink.AsyncBody?.Lowering);
        Assert.Equal(
            TypeRefKind.GenericInstance,
            customCompletion.Callee.DeclaringType.Kind);
        Assert.Equal(
            typeof(AnalysisCustomTaskMethodBuilder<>).Name,
            customCompletion.Callee.DeclaringType
                .ElementType?.Name);
        Assert.Null(customSink.StateMachineFieldSource);

        MethodIdentity customSecondary = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .CustomBuilderSecondarySource));
        DirectCall customSecondaryCompletion = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == customSecondary
                && call.Callee.Name == "SetResult");
        MethodResultSink customSecondarySink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink =>
                sink.EvidenceMethod
                    == customSecondaryCompletion.EvidenceMethod
                && sink.ILOffset
                    == customSecondaryCompletion.ILOffset);
        Assert.Equal(
            AsyncLoweringKind.StateMachine,
            customSecondarySink.AsyncBody?.Lowering);
        Assert.Equal(
            TypeRef.CoreLibrary,
            customSecondaryCompletion.Callee
                .DeclaringType.ElementType?.Assembly);
        Assert.Null(
            customSecondarySink.StateMachineFieldSource);

        MethodIdentity externalAddress = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ExternalAddressSource));
        MethodResultSink externalAddressSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink => sink.Caller == externalAddress
                && sink.ResolvedValue?.Single is
                {
                    Kind:
                        ResolvedValueSourceKind.InstanceFieldLoad,
                    FieldIdentity: not null,
                });
        Assert.Contains(
            index.JsonWireContracts.FieldLoads,
            load => load.EvidenceMethod.Name == "Corrupt"
                && load.IsAddress
                && externalAddressSink.ResolvedValue!.Single!
                    .FieldIdentity!.Equals(load.Identity));

        MethodIdentity mismatchedBuilder = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .MismatchedBuilderSource));
        DirectCall mismatchedCompletion = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == mismatchedBuilder
                && call.Callee.Name == "SetResult");
        Assert.True(
            FrameworkIdentity.IsCoreLibraryType(
                mismatchedBuilder.ReturnType,
                "System.Threading.Tasks",
                "Task`1"));
        Assert.True(
            FrameworkIdentity.IsCoreLibraryType(
                mismatchedCompletion.Callee.DeclaringType,
                "System.Runtime.CompilerServices",
                "AsyncValueTaskMethodBuilder`1"));

        MethodIdentity mixedSuspension = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .MixedSuspensionBuilderSource));
        DirectCall[] mixedSuspensionCalls =
        [
            .. index.CallGraph.DirectCalls.Where(call =>
                call.Caller == mixedSuspension
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted"),
        ];
        Assert.Equal(2, mixedSuspensionCalls.Length);
        Assert.Contains(
            mixedSuspensionCalls,
            call => FrameworkIdentity.IsCoreLibraryType(
                call.Callee.DeclaringType,
                "System.Runtime.CompilerServices",
                "AsyncTaskMethodBuilder`1"));
        Assert.Contains(
            mixedSuspensionCalls,
            call => FrameworkIdentity.IsCoreLibraryType(
                call.Callee.DeclaringType,
                "System.Runtime.CompilerServices",
                "AsyncValueTaskMethodBuilder`1"));

        MethodIdentity immediateCompletion = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ImmediateCompletionSource));
        DirectCall immediateSuspension = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == immediateCompletion
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted");
        MethodResultSink immediateSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink => sink.Caller == immediateCompletion
                && sink.ResolvedValue?.Single is
                {
                    Kind:
                        ResolvedValueSourceKind.InstanceFieldLoad,
                });
        Assert.True(
            immediateSuspension.ILOffset
                < immediateSink.ResolvedValue!.Single!.ILOffset);

        MethodIdentity wrongStateMachineArgument = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .WrongStateMachineArgumentSource));
        DirectCall wrongArgumentSuspension = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == wrongStateMachineArgument
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted");
        Assert.Equal(
            wrongArgumentSuspension.EvidenceMethod.DeclaringType,
            wrongArgumentSuspension.Callee.ParameterTypes[1]
                .ElementType);
        Assert.False(
            wrongArgumentSuspension
                .SecondByRefArgumentIsCurrentInstance);

        MethodIdentity reenteringCleanup = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReenteringCleanupSource));
        DirectCall reenteringCompletion = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == reenteringCleanup
                && call.Callee.Name == "SetResult");
        MethodResultSink reenteringSink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            sink => sink.EvidenceMethod
                    == reenteringCompletion.EvidenceMethod
                && sink.ILOffset == reenteringCompletion.ILOffset);
        FieldIdentity reenteringField = Assert.IsType<FieldIdentity>(
            reenteringSink.ResolvedValue?.Single?.FieldIdentity);
        Assert.True(reenteringCompletion.InLoop);
        Assert.Contains(
            index.JsonWireContracts.FieldStores,
            store => store.EvidenceMethod
                    == reenteringSink.EvidenceMethod
                && store.ILOffset
                    > reenteringSink.ResolvedValue!.Single!.ILOffset
                && reenteringField.Equals(store.Identity)
                && store.Value.Single?.Kind
                    == ResolvedValueSourceKind.NullReference);
    }

    [Fact]
    public void
        ResultSinks_WithholdFieldSourceForConservativeFinallyFlow()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredAcrossFinally));
        DirectCall completion = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == source
                && call.Callee.Name == "SetResult");
        MethodResultSink sink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            candidate => candidate.EvidenceMethod
                    == completion.EvidenceMethod
                && candidate.ILOffset == completion.ILOffset);

        Assert.Contains(
            index.CallGraph.DirectCalls,
            call => call.Caller == source
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted");
        Assert.Equal(
            ResolvedValueSourceKind.InstanceFieldLoad,
            sink.ResolvedValue?.Single?.Kind);
        Assert.Null(sink.StateMachineFieldSource);
    }

    [Fact]
    public void
        ResultSinks_SuppressFieldSourceWhenAssemblyCensusIsIncomplete()
    {
        string sourcePath =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        int corruptMethodToken;
        using (var stream = new MemoryStream(
            image,
            writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition stateMachine = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreStateMachine));
            MethodDefinitionHandle corruptHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "Corrupt");
            MethodDefinition corrupt =
                reader.GetMethodDefinition(corruptHandle);
            corruptMethodToken =
                MetadataTokens.GetToken(corruptHandle);
            DecodedInstruction call = MethodInstructions
                .Decode(peReader.GetMethodBody(
                    corrupt.RelativeVirtualAddress))
                .Instructions
                .First(instruction =>
                    instruction.OpCode == ILOpCode.Call);
            int bodyOffset = RvaToFileOffset(
                peReader.PEHeaders,
                corrupt.RelativeVirtualAddress);
            int headerSize = MethodHeaderSize(
                image,
                bodyOffset);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    bodyOffset
                        + headerSize
                        + call.OperandOffset,
                    sizeof(int)),
                0x06FFFFFF);
        }

        string scratchDirectory = Path.Combine(
            "artifacts",
            $"analysis-census-{Guid.NewGuid():N}");
        string path = Path.Combine(
            scratchDirectory,
            "fixture.dll");
        try
        {
            Directory.CreateDirectory(scratchDirectory);
            File.WriteAllBytes(path, image);
            LibraryBodyAnalysisExecution index = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .JsonWireContractFlow);
            MethodIdentity source = Assert.Single(
                index.CallGraph.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreSource));

            Assert.Contains(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == corruptMethodToken);
            MethodResultSink sink = Assert.Single(
                index.JsonWireContracts.ResultSinks,
                candidate => candidate.Caller == source
                    && candidate.ResolvedValue?.Single is
                    {
                        Kind:
                            ResolvedValueSourceKind.InstanceFieldLoad,
                        FieldIdentity: not null,
                    });
            FieldIdentity field =
                sink.ResolvedValue!.Single!.FieldIdentity!;
            Assert.DoesNotContain(
                index.JsonWireContracts.FieldStores,
                store => store.EvidenceMethod
                        != sink.EvidenceMethod
                    && store.IsReachable != false
                    && field.MightBeSameFieldAs(
                        store.Identity));
            Assert.DoesNotContain(
                index.JsonWireContracts.ResultSinks,
                candidate => candidate.Caller == source
                    && candidate.StateMachineFieldSource
                        is not null);
            AssertCompilerPositiveSuppressedByCensus(index.CompatibilityIndex());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(scratchDirectory))
                Directory.Delete(scratchDirectory);
        }
    }

    [Fact]
    public void
        ResultSinks_RejectUnresolvedExternalFieldStoreAlias()
    {
        string sourcePath =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        int fieldOperandToken;
        int corruptMethodToken;
        using (var stream = new MemoryStream(
            image,
            writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition stateMachine = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreStateMachine));
            MethodDefinitionHandle corruptHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "Corrupt");
            MethodDefinitionHandle probeHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "Probe");
            fieldOperandToken =
                MetadataTokens.GetToken(probeHandle);
            corruptMethodToken =
                MetadataTokens.GetToken(corruptHandle);
            MethodDefinition corrupt =
                reader.GetMethodDefinition(corruptHandle);
            DecodedInstruction store = MethodInstructions
                .Decode(peReader.GetMethodBody(
                    corrupt.RelativeVirtualAddress))
                .Instructions
                .Single(instruction =>
                    instruction.OpCode == ILOpCode.Stfld);
            int bodyOffset = RvaToFileOffset(
                peReader.PEHeaders,
                corrupt.RelativeVirtualAddress);
            int headerSize = MethodHeaderSize(
                image,
                bodyOffset);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    bodyOffset
                        + headerSize
                        + store.OperandOffset,
                    sizeof(int)),
                fieldOperandToken);
        }

        string scratchDirectory = Path.Combine(
            "artifacts",
            $"analysis-alias-{Guid.NewGuid():N}");
        string path = Path.Combine(
            scratchDirectory,
            "fixture.dll");
        try
        {
            Directory.CreateDirectory(scratchDirectory);
            File.WriteAllBytes(path, image);
            LibraryBodyAnalysisExecution index = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .JsonWireContractFlow);
            MethodIdentity source = Assert.Single(
                index.CallGraph.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreSource));

            Assert.Contains(
                index.JsonWireContracts.FieldStores,
                store => store.FieldToken == fieldOperandToken
                    && store.Identity is null);
            Assert.DoesNotContain(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == corruptMethodToken);
            Assert.DoesNotContain(
                index.JsonWireContracts.ResultSinks,
                sink => sink.Caller == source
                    && sink.StateMachineFieldSource is not null);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(scratchDirectory))
                Directory.Delete(scratchDirectory);
        }
    }

    [Fact]
    public void
        ResultSinks_SuppressFieldSourceWhenBodyClassificationFails()
    {
        string sourcePath =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        int corruptMethodToken;
        using (var stream = new MemoryStream(
            image,
            writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition stateMachine = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreStateMachine));
            MethodDefinitionHandle corruptHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "Corrupt");
            corruptMethodToken =
                MetadataTokens.GetToken(corruptHandle);
            Assert.True(
                reader.GetHeapSize(HeapIndex.Blob)
                    <= ushort.MaxValue
                && reader.GetHeapSize(HeapIndex.String)
                    <= ushort.MaxValue);
            int signatureHandleOffset =
                peReader.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.MethodDef)
                + (MetadataTokens.GetRowNumber(
                        corruptHandle)
                    - 1)
                    * reader.GetTableRowSize(
                        TableIndex.MethodDef)
                + sizeof(int)
                + sizeof(ushort)
                + sizeof(ushort)
                + sizeof(ushort);
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(
                    signatureHandleOffset,
                    sizeof(ushort)),
                ushort.MaxValue);
        }

        string scratchDirectory = Path.Combine(
            "artifacts",
            $"analysis-signature-{Guid.NewGuid():N}");
        string path = Path.Combine(
            scratchDirectory,
            "fixture.dll");
        try
        {
            Directory.CreateDirectory(scratchDirectory);
            File.WriteAllBytes(path, image);
            LibraryBodyAnalysisExecution index = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .JsonWireContractFlow);
            MethodIdentity source = Assert.Single(
                index.CallGraph.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreSource));

            Assert.Contains(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == corruptMethodToken);
            Assert.DoesNotContain(
                index.JsonWireContracts.ResultSinks,
                sink => sink.Caller == source
                    && sink.StateMachineFieldSource is not null);
            AssertCompilerPositiveSuppressedByCensus(index.CompatibilityIndex());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(scratchDirectory))
                Directory.Delete(scratchDirectory);
        }
    }

    [Fact]
    public void
        ResultSinks_SuppressStateMachineFieldSourceForScopedCensus()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink fullSink = Assert.Single(
            full.JsonWireContracts.ResultSinks,
            sink => sink.Caller == source
                && sink.StateMachineFieldSource is not null);

        var scoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow,
            bodyScope:
                new HashSet<int>
                {
                    fullSink.EvidenceMethod.MetadataToken,
                });

        Assert.DoesNotContain(
            scoped.JsonWireContracts.ResultSinks,
            sink => sink.StateMachineFieldSource is not null);
        Assert.Contains(
            scoped.JsonWireContracts.ResultSinks,
            sink => sink.Caller == source
                && sink.EvidenceMethod.MetadataToken
                    == fullSink.EvidenceMethod.MetadataToken);
    }

    [Fact]
    public void
        ResultSinks_WithStateMachineFieldSourceRemainEqualityStable()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence
            | LibraryBodyAnalysisFeatures.JsonWireContractFlow;
        LibraryBodyAnalysisExecution left = BodyAnalysisTestExecution.Open(
            path,
            features);
        LibraryBodyAnalysisExecution right = BodyAnalysisTestExecution.Open(
            path,
            features);

        static MethodResultSink Select(
            LibraryBodyAnalysisExecution index) =>
            Assert.Single(
                index.JsonWireContracts.ResultSinks,
                sink => sink.Caller.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .ReturnsCallStoredBeforeAwait)
                    && sink.StateMachineFieldSource is not null);

        MethodResultSink leftSink = Select(left);
        MethodResultSink rightSink = Select(right);

        Assert.Equal(leftSink, rightSink);
        Assert.Equal(
            leftSink.GetHashCode(),
            rightSink.GetHashCode());
        Assert.Single(
            new[] { leftSink, rightSink }.Distinct());
    }

    [Fact]
    public void
        ResultSinks_AuthenticateStateMachineCompletionBuilderField()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .UsesSecondaryBuilderAfterAwait));
        DirectCall[] completions =
        [
            .. index.CallGraph.DirectCalls.Where(call =>
                call.Caller == source
                && call.EvidenceMethod != source
                && call.Callee.Name == "SetResult"),
        ];
        Assert.Equal(2, completions.Length);

        MethodResultSink[] completionSinks =
        [
            .. completions.Select(completion => Assert.Single(
                index.JsonWireContracts.ResultSinks,
                sink =>
                    sink.EvidenceMethod
                        == completion.EvidenceMethod
                    && sink.ILOffset == completion.ILOffset)),
        ];
        Assert.All(
            completionSinks,
            sink => Assert.Null(
                sink.StateMachineFieldSource));
    }

    [Fact]
    public void
        ResultSinks_RejectUnresolvedStateMachineFieldStoreAlias()
    {
        var index = BodyAnalysisTestExecution.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink sink = Assert.Single(
            index.JsonWireContracts.ResultSinks,
            candidate => candidate.Caller == source
                && candidate.StateMachineFieldSource is not null);
        AsyncStateMachineFieldResultSource fieldSource =
            sink.StateMachineFieldSource!;
        FieldStoreFact store = Assert.Single(
            index.JsonWireContracts.FieldStores,
            candidate =>
                candidate.EvidenceMethod == sink.EvidenceMethod
                && candidate.ILOffset == fieldSource.StoreOffset);
        FieldStoreFact physicalStore = store with
        {
            Caller = sink.EvidenceMethod,
        };

        Assert.True(
            MethodCallAnalysis
                .TryFindAsyncStateMachineFieldSourceStore(
                    sink.EvidenceMethod,
                    fieldSource.Field,
                    fieldSource.LoadOffset,
                    [physicalStore],
                    out FieldStoreFact? exact));
        Assert.Equal(physicalStore, exact);

        Assert.False(
            MethodCallAnalysis
                .TryFindAsyncStateMachineFieldSourceStore(
                    sink.EvidenceMethod,
                    fieldSource.Field,
                    fieldSource.LoadOffset,
                    [physicalStore with { Identity = null }],
                    out _));
        FieldIdentity possibleAlias = Assert.IsType<FieldIdentity>(
            FieldIdentity.TryCreate(
                fieldSource.Field.DeclaringType,
                fieldSource.Field.Name));
        Assert.True(
            fieldSource.Field.MightBeSameFieldAs(possibleAlias));
        Assert.NotEqual(fieldSource.Field, possibleAlias);
        Assert.False(
            MethodCallAnalysis
                .TryFindAsyncStateMachineFieldSourceStore(
                    sink.EvidenceMethod,
                    fieldSource.Field,
                    fieldSource.LoadOffset,
                    [physicalStore with { Identity = possibleAlias }],
                    out _));
        Assert.False(
            MethodCallAnalysis
                .TryFindAsyncStateMachineFieldSourceStore(
                    sink.EvidenceMethod,
                    fieldSource.Field,
                    fieldSource.LoadOffset,
                    [physicalStore with { IsReachable = null }],
                    out _));
    }

    [Fact]
    public void
        ResultSinks_DoNotAttributeSynchronousIteratorBodiesAsAsync()
    {
        string path =
            typeof(OptimizationOpportunityAsyncSiblingFixtures)
                .Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures)
                && method.Name == nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                        .ReadValues));
        MethodIdentity moveNext = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    $"<{source.Name}>",
                    StringComparison.Ordinal));
        MethodResultSink[] sinks =
        [
            .. index.JsonWireContracts.ResultSinks.Where(
                candidate => candidate.Caller == moveNext),
        ];

        Assert.NotEmpty(sinks);
        Assert.All(
            sinks,
            candidate => Assert.Null(candidate.AsyncBody));
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsLiftedLocalFunctionToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(path);

        var owner = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture.CallsThroughLocalFunction));
        var liftedCall = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.Contains(
                    ">g__",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner.MetadataToken,
            Assert.IsType<MethodIdentity>(
                index.CallGraph.ResolveDeclaredMethod(
                    liftedCall.EvidenceMethod)).MetadataToken);
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsSiblingReferencedLocalFunctionToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CallsThroughSiblingLocalFunctions));
        DirectCall call = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.StartsWith(
                    "<CallsThroughSiblingLocalFunctions>g__Second|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(call.EvidenceMethod));
    }

    [Fact]
    public void
        DirectCalls_AsyncLiftedMoveNextComposesToDeclaredOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncLiftedFunctionCallsSibling));
        DirectCall call = Assert.Single(
            index.CallGraph.DirectCalls,
            call => call.Caller == owner
                && call.EvidenceMethod.Name == "MoveNext"
                && call.EvidenceMethod.DeclaringType.Name.Contains(
                    "AsyncLiftedFunctionCallsSibling",
                    StringComparison.Ordinal)
                && call.Callee.Name.StartsWith(
                    "<AsyncLiftedFunctionCallsSibling>g__Inner|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(call.EvidenceMethod));

        DirectCall[] expected = index.CallGraph.DirectCalls
            .Where(expectedCall =>
                expectedCall.Caller == owner)
            .ToArray();
        var methodScoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                owner.MetadataToken,
            });
        var typeScoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyTypeScope: type =>
                type.Equals(owner.DeclaringType));
        foreach (LibraryBodyAnalysisExecution scoped
            in new[] { methodScoped, typeScoped })
        {
            Assert.Equal(
                expected,
                scoped.CallGraph.DirectCalls
                    .Where(scopedCall =>
                        scopedCall.Caller == owner)
                    .ToArray());
            Assert.Equal(
                owner,
                scoped.CallGraph.ResolveDeclaredMethod(
                    call.EvidenceMethod));
        }
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsAsyncOwnerLocalFunctionToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncOwnerCallsThroughLocalFunction));
        DirectCall expected = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.StartsWith(
                    "<AsyncOwnerCallsThroughLocalFunction>g__Core|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(expected.EvidenceMethod));

        var scoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                expected.EvidenceMethod.MetadataToken,
            });
        Assert.Contains(
            scoped.CallGraph.DirectCalls,
            call => call.Caller == owner
                && call.EvidenceMethod == expected.EvidenceMethod
                && call.Callee == expected.Callee);
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsAsyncOwnerLambdaToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncOwnerCallsThroughAsyncLambda));
        DirectCall expected = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name == "MoveNext"
                && call.EvidenceMethod.DeclaringType.Name.Contains(
                    "AsyncOwnerCallsThroughAsyncLambda",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(expected.EvidenceMethod));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_MapsAsyncLiftedFunctionSiblingToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncLiftedFunctionCallsSibling));
        DirectCall call = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.StartsWith(
                    "<AsyncLiftedFunctionCallsSibling>g__Inner|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(call.EvidenceMethod));
    }

    [Fact]
    public void AsyncMoveNextResolution_UsesExplicitInterfaceImplementation()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .ExplicitMoveNextSource));
        DirectCall explicitCall = Assert.Single(
            index.CallGraph.FindCalls(
                MemberPattern.Method(
                    source.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == source
                && call.EvidenceMethod.Name.EndsWith(
                    ".MoveNext",
                    StringComparison.Ordinal));

        Assert.NotEqual(source, explicitCall.EvidenceMethod);
        Assert.Contains(
            index.CallGraph.DirectCalls,
            call => call.Caller == call.EvidenceMethod
                && call.Caller.Name == "MoveNext"
                && call.Caller.ParameterTypes.Length == 1
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
    }

    [Fact]
    public void OptimizationOpportunities_MalformedAsyncSourceDoesNotAbortStateMachineMap()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MalformedAsyncSource-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                BuildMalformedAsyncSourceAssembly());

            var index = BodyAnalysisTestExecution.Open(path);
            var opportunity = Assert.Single(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name == "AnalyzeAsync");

            Assert.Contains(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "BrokenAsync",
                    StringComparison.Ordinal));
            Assert.Contains(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "MalformedValueAsync",
                    StringComparison.Ordinal));
            Assert.Contains(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "DuplicateAsync",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "ForeignAssemblyAsync",
                    StringComparison.Ordinal));
            Assert.Contains(
                index.CallGraph.DirectCalls,
                call => call.Caller.Name
                        == "MalformedValueAsync"
                    && call.Callee.Name == "Read");
            Assert.Contains(
                "ReadAsync",
                opportunity.Evidence,
                StringComparison.Ordinal);
            Assert.Equal(
                "MoveNext",
                Assert.Single(
                    index.CallGraph.Methods,
                    method => method.MetadataToken
                        == opportunity.EvidenceMethodToken).Name);
        }

        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OptimizationOpportunities_MalformedAsyncAttributePreservesIndependentEvidence()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisLookalike
                .AssemblyPath(),
            LibraryBodyAnalysisFeatures.All);
        const string MethodName =
            "MalformedAsyncAttributeEvidence";
        MethodIdentity method = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name == MethodName);
        MethodSignals signals =
            index.CallGraph.MethodSignals
                .GetValueOrDefault(
                    method.MetadataToken,
                    MethodSignals.None);

        Assert.Contains(
            index.Receipt.Diagnostics,
            diagnostic => diagnostic.MethodToken
                == method.MetadataToken);
        Assert.Contains(
            index.Optimization.Opportunities,
            opportunity => opportunity.Method.MetadataToken
                    == method.MetadataToken
                && opportunity.Shape
                    == "capturing-delegate");
        Assert.True(signals.Throws >= 1);
        Assert.True(signals.Catches >= 1);
        Assert.True(signals.Finallys >= 1);
    }

    [Fact]
    public void AsyncStateMachineAttribute_RequiresFrameworkOrigin()
    {
        Assert.True(IsTrustedAttributeConstructor(
            typeof(ClassicAsyncSiblingFixture)
                .Assembly.Location,
            nameof(
                ClassicAsyncSiblingFixture
                    .CallsSyncSiblingFromAsync)));
        Assert.False(IsTrustedAttributeConstructor(
            FixtureCatalog.AnalysisSpoofSystemRuntime
                .AssemblyPath(),
            ".ctor"));
        var spoof = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisSpoofSystemRuntime
                .AssemblyPath());
        Assert.DoesNotContain(
            spoof.Optimization.Opportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "Analyze");

        static bool IsTrustedAttributeConstructor(
            string path,
            string methodName)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader =
                pe.GetMetadataReader();
            foreach (MethodDefinitionHandle handle
                in reader.MethodDefinitions)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                if (!reader.StringComparer.Equals(
                        method.Name,
                        methodName))
                {
                    continue;
                }
                if (methodName == ".ctor"
                    && reader.GetString(
                        reader.GetTypeDefinition(
                                method.GetDeclaringType())
                            .Namespace)
                        != "System.Runtime.CompilerServices")
                {
                    continue;
                }

                EntityHandle constructor = handle;
                if (methodName != ".ctor")
                {
                    constructor = method.GetCustomAttributes()
                        .Select(reader.GetCustomAttribute)
                        .Single(attribute =>
                            AttributeDecoder
                                .GetAttributeTypeName(
                                    reader,
                                    attribute.Constructor)
                            == KnownAttributeNames
                                .AsyncStateMachineAttribute)
                        .Constructor;
                }
                return LibraryBodyAsyncSourceResolver
                    .IsTrustedAsyncStateMachineAttribute(
                        reader,
                        constructor,
                        KnownAttributeNames
                            .AsyncStateMachineAttribute);
            }
            throw new InvalidOperationException(
                "Attribute constructor was not found.");
        }
    }

    [Fact]
    public void ScopedStateMachineExpansion_RequiresTrustedClassicSource()
    {
        AssertScopeExpansion(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            nameof(
                ClassicAsyncSiblingFixture
                    .CallsSyncSiblingFromAsync),
            expected: true);
        AssertScopeExpansion(
            FixtureCatalog.AnalysisSpoofSystemRuntime
                .AssemblyPath(),
            "Analyze",
            expected: false);

        byte[] runtimeAsync =
            BuildDirectionProbeCaller(
                byRef: false,
                addStateMachineAttribute: true);
        using var peReader = new PEReader(
            new MemoryStream(
                runtimeAsync,
                writable: false));
        MetadataReader reader =
            peReader.GetMetadataReader();
        MethodDefinitionHandle method =
            reader.MethodDefinitions.Single(
                handle => reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    "AnalyzeAsync"));
        using var builder =
            new LibraryBodyAnalysisBuilder(
                "DirectionProbeCaller.dll",
                reader,
                peReader);
        Assert.False(
            builder.ScopeMayRequireStateMachineBody(
                new HashSet<int>
                {
                    MetadataTokens.GetToken(method),
                }));

        static void AssertScopeExpansion(
            string path,
            string methodName,
            bool expected)
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            MetadataReader reader =
                peReader.GetMetadataReader();
            MethodDefinitionHandle method =
                reader.MethodDefinitions.Single(
                    handle => reader.StringComparer.Equals(
                        reader.GetMethodDefinition(handle).Name,
                        methodName));
            using var builder =
                new LibraryBodyAnalysisBuilder(
                    path,
                    reader,
                    peReader);
            Assert.Equal(
                expected,
                builder.ScopeMayRequireStateMachineBody(
                    new HashSet<int>
                    {
                        MetadataTokens.GetToken(method),
                    }));
        }
    }

    [Fact]
    public void OptimizationOpportunities_MethodImplSelfDispatchIsSuppressed()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MethodImplAsyncSource-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true));

            var index = BodyAnalysisTestExecution.Open(path);

            Assert.DoesNotContain(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(index.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    methodImplBodyAsMemberReference:
                        true));
            var memberReferenceBody =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                memberReferenceBody
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                memberReferenceBody.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true));
            var inheritedMethodImpl =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                inheritedMethodImpl
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                inheritedMethodImpl.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    methodImplBodyAsMemberReference:
                        true,
                    inheritedMethodImpl: true));
            var inheritedMemberReference =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                inheritedMemberReference
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                inheritedMemberReference.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync"));
            var unrelatedOverride =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                unrelatedOverride
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                unrelatedOverride.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync",
                    unrelatedSourceMethodImpl: true));
            var unrelatedSourceMethodImpl =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                unrelatedSourceMethodImpl
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                unrelatedSourceMethodImpl.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync",
                    malformedSourceMethodImpl: true));
            var malformedSourceMethodImpl =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                malformedSourceMethodImpl
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                malformedSourceMethodImpl.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync",
                    sourceImplementsOtherInterface:
                        false,
                    unrelatedSourceMethodImpl: true));
            var invalidDeclarationOwner =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                invalidDeclarationOwner
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                invalidDeclarationOwner.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync",
                    unrelatedSourceMethodImpl: true,
                    incompatibleSourceMethodImpl:
                        true));
            var incompatibleSourceMethodImpl =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                incompatibleSourceMethodImpl
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                incompatibleSourceMethodImpl.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    sourceStartsNewSlot: true));
            var inheritedNewSlot =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                inheritedNewSlot
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(inheritedNewSlot.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false));
            var control = BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                control.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    stateMachineUsesTasksContract:
                        true));
            var tasksContract =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                tasksContract.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(tasksContract.Receipt.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    finalInterfaceSibling: true,
                    sourceMethodName: "ReadAsync"));
            var finalInterfaceSibling =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                finalInterfaceSibling
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "ReadAsync");

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    attributeConstructorHeader: 0x25));
            var malformedAttributeConstructor =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                malformedAttributeConstructor
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                malformedAttributeConstructor.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    sourceImplementation:
                        MethodImplAttributes.Native));
            var nativeClassicAsync =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                nativeClassicAsync
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                nativeClassicAsync.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    validStateMachine: false));
            var invalidStateMachine =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                invalidStateMachine
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                invalidStateMachine.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    sourceHasBody: false));
            var bodilessSource =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                bodilessSource
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                bodilessSource.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            var scopedBodilessSource =
                BodyAnalysisTestExecution.Open(
                    path,
                    bodyScope:
                        new HashSet<int>
                        {
                            MetadataTokens.GetToken(
                                MetadataTokens
                                    .MethodDefinitionHandle(
                                        4)),
                        });
            Assert.DoesNotContain(
                scopedBodilessSource.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    sourceHasBody: false,
                    runtimeAsyncSource: true));
            var bodilessRuntimeAsync =
                BodyAnalysisTestExecution.Open(path);
            Assert.Contains(
                bodilessRuntimeAsync.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    runtimeAsyncSource: true,
                    sourceImplementation:
                        MethodImplAttributes.Native));
            var nativeRuntimeAsync =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                nativeRuntimeAsync
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                nativeRuntimeAsync.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    moveNextHasBody: false));
            var bodilessMoveNext =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                bodilessMoveNext
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                bodilessMoveNext.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    moveNextImplementation:
                        MethodImplAttributes.InternalCall));
            var nonIlMoveNext =
                BodyAnalysisTestExecution.Open(path);
            Assert.DoesNotContain(
                nonIlMoveNext
                    .Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                nonIlMoveNext.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            foreach (byte unsupportedHeader
                in new byte[] { 0x25, 0x60, 0xA0 })
            {
                File.WriteAllBytes(
                    path,
                    BuildMethodImplAsyncSourceAssembly(
                        includeMethodImpl: false,
                        siblingSignatureHeader:
                            unsupportedHeader));
                var unsupported =
                    BodyAnalysisTestExecution.Open(path);
                Assert.DoesNotContain(
                    unsupported.Optimization.Opportunities,
                    opportunity => opportunity.Shape
                            == "sync-call-in-async"
                        && opportunity.Method.Name
                            == "AnalyzeAsync");
                Assert.Empty(unsupported.Receipt.Diagnostics);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        DirectCalls_SourceGeneratedAsyncCollisionCannotEscapeRejection()
    {
        byte[] image = BuildMethodImplAsyncSourceAssembly(
            includeMethodImpl: false,
            duplicateSourceGeneratedSource: true);
        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "AmbiguousAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity generatedSource = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name
                == "<AnalyzeAsync>g__Generated|0_0");
        Assert.Equal(
            "<>c",
            generatedSource.DeclaringType.Name);
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name == "MoveNext");
        DirectCall call = Assert.Single(
            full.CallGraph.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext);

        Assert.Equal(moveNext, call.Caller);
        Assert.Null(full.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            full.Receipt.Diagnostics,
            diagnostic => diagnostic.MethodToken
                    == moveNext.MetadataToken
                && diagnostic.Message.Contains(
                    "Multiple async source methods",
                    StringComparison.Ordinal));

        LibraryBodyAnalysisExecution scoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "AmbiguousAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope:
                    new HashSet<int> { source.MetadataToken });
        Assert.DoesNotContain(
            scoped.CallGraph.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext);
    }

    [Fact]
    public void OptimizationOpportunities_AmbiguousStateMachineSourceIsDiagnostic()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"AmbiguousAsyncSource-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                BuildMalformedAsyncSourceAssembly(
                    ambiguousSource: true));

            var index = BodyAnalysisTestExecution.Open(path);

            Assert.DoesNotContain(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                index.Receipt.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "MoveNext",
                    StringComparison.Ordinal)
                    && diagnostic.Message.Contains(
                        "Multiple async source methods",
                        StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        OptimizationOpportunities_ScopedAmbiguousSourceFailsClosed()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                ambiguousSource: true,
                moveNextSmallArray: true);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"AmbiguousAsyncArray-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            var full = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.Contains(
                full.Optimization.Opportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.DoesNotContain(
                full.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            var scoped = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));

            Assert.Contains(
                full.Optimization.Opportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.Contains(
                scoped.Receipt.Diagnostics,
                diagnostic => diagnostic.MethodToken
                        == moveNext.MetadataToken
                    && diagnostic.Message.Contains(
                        "Multiple async source methods",
                        StringComparison.Ordinal));
            Assert.DoesNotContain(
                scoped.Optimization.Opportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
        DirectCalls_CrossKindStateMachineAttributesFailClosed(
            bool iteratorAttributeFirst)
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                crossKindDuplicate: true,
                crossKindIteratorFirst:
                    iteratorAttributeFirst);
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "CrossKindAsyncAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity moveNext = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        DirectCall call = Assert.Single(
            index.CallGraph.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext
                && candidate.Callee.Name == "Read");

        Assert.Equal(moveNext, call.Caller);
        Assert.Null(index.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            index.Receipt.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken == source.MetadataToken
                && diagnostic.Message.Contains(
                    "attribute is malformed or ambiguous",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void
        DirectCalls_ClassicAndSynchronousIteratorAttributesFailClosed()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                crossKindDuplicate: true,
                crossKindSynchronousIterator: true);
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "CrossKindSynchronousIteratorAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity moveNext = Assert.Single(
            index.CallGraph.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        DirectCall call = Assert.Single(
            index.CallGraph.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext
                && candidate.Callee.Name == "Read");

        Assert.Equal(moveNext, call.Caller);
        Assert.Null(index.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            index.Receipt.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken == moveNext.MetadataToken
                && diagnostic.Message.Contains(
                    "source is invalid or ambiguous",
                    StringComparison.Ordinal));

        LibraryBodyAnalysisExecution methodScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "CrossKindSynchronousIteratorAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                });
        Assert.DoesNotContain(
            methodScoped.CallGraph.DirectCalls,
            candidate =>
                candidate.EvidenceMethod.MetadataToken
                    == moveNext.MetadataToken);

        LibraryBodyAnalysisExecution typeScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "CrossKindSynchronousIteratorAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        source.DeclaringType));
        Assert.DoesNotContain(
            typeScoped.CallGraph.DirectCalls,
            candidate =>
                candidate.EvidenceMethod.MetadataToken
                    == moveNext.MetadataToken);
    }

    [Fact]
    public void
        DirectCalls_RuntimeAsyncDecoyDoesNotPoisonValidSource()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                ambiguousSource: true,
                runtimeAsyncCompetingSource: true);
        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "RuntimeAsyncDecoy.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        Assert.Equal(
            source,
            full.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            full.CallGraph.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == source
                && call.Callee.Name == "Read");
        Assert.DoesNotContain(
            full.Receipt.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken
                    == source.MetadataToken);

        LibraryBodyAnalysisExecution sourceScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "RuntimeAsyncDecoy.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                });
        Assert.Equal(
            source,
            sourceScoped.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            sourceScoped.CallGraph.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == source);

        MethodIdentity unrelated = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name == "Read");
        LibraryBodyAnalysisExecution unrelatedScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "RuntimeAsyncDecoy.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    unrelated.MetadataToken,
                });
        Assert.Equal(
            source,
            unrelatedScoped.CallGraph.ResolveDeclaredMethod(moveNext));
    }

    [Fact]
    public void
        OptimizationOpportunities_UnresolvedLiftedSourceFailsClosedAcrossScopes()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                moveNextSmallArray: true,
                unresolvedLiftedSource: true,
                authoredCallerIntoMoveNext: true);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"UnresolvedLiftedAsyncArray-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            var full = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            MethodIdentity kickoff = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name
                    == "<Outer>b__0_0");
            MethodIdentity caller = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name
                    == "CompetingAsync");
            var methodScoped = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    kickoff.MetadataToken,
                });
            var scoped = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        kickoff.DeclaringType));

            Assert.Contains(
                full.Optimization.Opportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                full.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method == kickoff
                    && opportunity.EvidenceMethodToken
                        == moveNext.MetadataToken);
            Assert.Equal(
                kickoff,
                full.CallGraph.ResolveDeclaredMethod(moveNext));
            Assert.Contains(
                full.CallGraph.DirectCalls,
                call => call.EvidenceMethod == moveNext
                    && call.Callee.Name == "Read"
                    && call.Caller == moveNext);
            Assert.Contains(
                full.CallGraph.DirectCalls,
                call => call.EvidenceMethod == caller
                    && call.Callee.Name == "MoveNext");
            Assert.DoesNotContain(
                full.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            OptimizationOpportunity callerFanout =
                Assert.Single(
                    full.Optimization.AllocationFanoutOpportunities,
                    opportunity =>
                        opportunity.Method.MetadataToken
                            == caller.MetadataToken);
            Assert.Equal(
                1,
                callerFanout.DirectAllocationSites);
            Assert.Equal(
                1,
                callerFanout.OnceAllocationPaths);
            Assert.Equal(
                1,
                callerFanout.OpaqueCallPaths);
            Assert.Equal(
                "medium",
                callerFanout.Confidence);
            Assert.DoesNotContain(
                methodScoped.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method == kickoff
                    && opportunity.EvidenceMethodToken
                        == moveNext.MetadataToken);
            Assert.Contains(
                methodScoped.Optimization.Opportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                methodScoped.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.Null(
                methodScoped.CallGraph.ResolveDeclaredMethod(moveNext));
            Assert.Contains(
                scoped.Allocations.Occurrences,
                pair => pair.Key == moveNext.MetadataToken);
            Assert.Contains(
                scoped.Optimization.Opportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method == kickoff
                    && opportunity.EvidenceMethodToken
                        == moveNext.MetadataToken);
            Assert.Null(
                scoped.CallGraph.ResolveDeclaredMethod(moveNext));
            Assert.DoesNotContain(
                scoped.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
        OptimizationOpportunities_OrphanGeneratedBodyFailsClosedAcrossScopes(
            bool liftedStateMachineName)
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                moveNextSmallArray: true,
                orphanStateMachine: true);
        if (liftedStateMachineName)
        {
            ReplaceAscii(
                image,
                "<AnalyzeAsync>d__1",
                "<<AnalyzeAs>b__0>d",
                expectedReplacements: 1);
        }
        string path = Path.Combine(
            Path.GetTempPath(),
            $"OrphanAsyncBody-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            LibraryBodyAnalysisExecution full = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.True(
                CompilerGeneratedNames.RequiresDeclaredOwner(
                    moveNext));
            Assert.Null(
                full.CallGraph.ResolveDeclaredMethod(moveNext));

            foreach (LibraryBodyAnalysisExecution index in new[]
            {
                full,
                BodyAnalysisTestExecution.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope: new HashSet<int>
                    {
                        moveNext.MetadataToken,
                    }),
                BodyAnalysisTestExecution.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyTypeScope:
                        type => type.Equals(
                            moveNext.DeclaringType)),
            })
            {
                Assert.Contains(
                    index.Optimization.Opportunities,
                    opportunity =>
                        opportunity.Shape == "small-array"
                        && opportunity.Method.MetadataToken
                            == moveNext.MetadataToken);
                Assert.DoesNotContain(
                    index.Optimization.AllocationFanoutOpportunities,
                    opportunity =>
                        opportunity.Method.MetadataToken
                            == moveNext.MetadataToken);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("<<AnalyzeAs>b__x>d")]
    [InlineData("<<Analyze>b__0>d`0")]
    [InlineData("<<>b__2147483647>d")]
    public void
        OptimizationOpportunities_MalformedLiftedStateMachineFailsClosedInScopedViews(
            string malformedName)
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                moveNextSmallArray: true,
                orphanStateMachine: true);
        ReplaceAscii(
            image,
            "<AnalyzeAsync>d__1",
            malformedName,
            expectedReplacements: 1);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MalformedLiftedStateMachine-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            LibraryBodyAnalysisExecution full = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.True(
                CompilerGeneratedNames.RequiresDeclaredOwner(
                    moveNext));
            Assert.Null(
                full.CallGraph.ResolveDeclaredMethod(moveNext));
            Assert.Contains(
                full.Optimization.Opportunities,
                opportunity =>
                    opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);

            foreach (LibraryBodyAnalysisExecution scoped in new[]
            {
                BodyAnalysisTestExecution.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope: new HashSet<int>
                    {
                        moveNext.MetadataToken,
                    }),
                BodyAnalysisTestExecution.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyTypeScope:
                        type => type.Equals(
                            moveNext.DeclaringType)),
            })
            {
                Assert.DoesNotContain(
                    scoped.Optimization.Opportunities,
                    opportunity =>
                        opportunity.Method.MetadataToken
                            == moveNext.MetadataToken);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void
        OptimizationOpportunities_MethodImplMoveNextMappingControlsMalformedStateMachineScopeAdmission(
            bool mapsMoveNext,
            bool expectScopedIntrinsic)
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                moveNextSmallArray: true,
                orphanStateMachine: true,
                explicitMoveNextMethodImpl: mapsMoveNext,
                moveNextMethodName: "Run");
        ReplaceAscii(
            image,
            "<AnalyzeAsync>d__1",
            "<<AnalyzeAs>b__x>d",
            expectedReplacements: 1);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MalformedExplicitMoveNext-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            LibraryBodyAnalysisExecution full = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity execution = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "Run"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.Null(
                full.CallGraph.ResolveDeclaredMethod(execution));
            Assert.Contains(
                full.Optimization.Opportunities,
                opportunity =>
                    opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == execution.MetadataToken);

            foreach (LibraryBodyAnalysisExecution scoped in new[]
            {
                BodyAnalysisTestExecution.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope: new HashSet<int>
                    {
                        execution.MetadataToken,
                    }),
                BodyAnalysisTestExecution.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyTypeScope:
                        type => type.Equals(
                            execution.DeclaringType)),
            })
            {
                bool containsIntrinsic =
                    scoped.Optimization.Opportunities.Any(
                        opportunity =>
                            opportunity.Shape == "small-array"
                            && opportunity.Method.MetadataToken
                                == execution.MetadataToken);
                Assert.Equal(
                    expectScopedIntrinsic,
                    containsIntrinsic);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        OptimizationOpportunities_CompiledAsyncLambdaStateMachineIsScopeInvariant()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisExecution full = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    "<ScopedAsyncAllocationHotspotLambdaOwner>",
                    StringComparison.Ordinal));
        Assert.EndsWith(
            ">d",
            moveNext.DeclaringType.Name,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ">d__",
            moveNext.DeclaringType.Name,
            StringComparison.Ordinal);
        Assert.Contains(
            full.Optimization.Opportunities,
            opportunity => opportunity.Method.MetadataToken
                == moveNext.MetadataToken);

        LibraryBodyAnalysisExecution scoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities,
            bodyTypeScope:
                type => type.Equals(
                    moveNext.DeclaringType));

        Assert.DoesNotContain(
            scoped.Optimization.Opportunities,
            opportunity => opportunity.Method.MetadataToken
                == moveNext.MetadataToken);
    }

    [Fact]
    public void
        OptimizationOpportunities_CompiledGenericAsyncLocalStateMachineIsScopeInvariant()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisExecution full = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    "<ScopedGenericIteratorFinallyAsyncLocalAllocationOwner>",
                    StringComparison.Ordinal)
                && method.DeclaringType.Name.Contains(
                    ">g__BuildAsync",
                    StringComparison.Ordinal));
        Assert.EndsWith(
            ">d`1",
            moveNext.DeclaringType.Name,
            StringComparison.Ordinal);
        Assert.Contains(
            full.Optimization.Opportunities,
            opportunity => opportunity.Shape == "small-array"
                && opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);

        LibraryBodyAnalysisExecution scoped = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities,
            bodyTypeScope:
                type => type.Equals(
                    moveNext.DeclaringType));

        Assert.DoesNotContain(
            scoped.Optimization.Opportunities,
            opportunity => opportunity.Method.MetadataToken
                == moveNext.MetadataToken);
        Assert.True(
            CompilerGeneratedNames.RequiresDeclaredOwner(
                moveNext));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_CompiledCapturedAsyncLocalMapsToAuthoredOwner()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisExecution index = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name
                == "ScopedCapturedAsyncLocalAllocationOwner");
        MethodIdentity source = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name.StartsWith(
                "<ScopedCapturedAsyncLocalAllocationOwner>g__BuildAsync|",
                StringComparison.Ordinal));
        MethodIdentity moveNext = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    source.Name,
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            '_',
            source.Name[
                source.Name.LastIndexOf('|')..]);
        Assert.True(
            CompilerGeneratedNames
                .IsLocalFunctionOrLambda(source.Name));
        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(source));
        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(moveNext));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_LegacyHexLambdaOrdinalOnContainingHostMapsToAuthoredOwner()
    {
        string fixturePath =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisExecution original =
            BodyAnalysisTestExecution.Open(fixturePath);
        MethodIdentity originalLambda = Assert.Single(
            original.CallGraph.Methods,
            method => method.Name.StartsWith(
                "<SharedLambdaOrdinalOwner>b__",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "<>c__DisplayClass",
            originalLambda.DeclaringType.Name,
            StringComparison.Ordinal);
        Assert.Equal(
            "SharedLambdaOrdinalOwner",
            original.CallGraph.ResolveDeclaredMethod(
                originalLambda)?.Name);
        int marker = originalLambda.Name.LastIndexOf(
            ">b__",
            StringComparison.Ordinal);
        Assert.Contains(
            '_',
            originalLambda.Name[(marker + 4)..]);
        int suffixLength =
            originalLambda.Name.Length - marker - 4;
        string legacyName =
            originalLambda.Name[..(marker + 4)]
            + new string('a', suffixLength);
        byte[] image = File.ReadAllBytes(fixturePath);
        ReplaceAscii(
            image,
            originalLambda.Name,
            legacyName,
            expectedReplacements: 1);
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "LegacyHexLambdaOrdinal.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name
                == "SharedLambdaOrdinalOwner");
        MethodIdentity lambda = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name == legacyName);

        Assert.Equal(
            owner,
            index.CallGraph.ResolveDeclaredMethod(lambda));
    }

    [Fact]
    public void
        OptimizationOpportunities_UnresolvedAsyncOwnerDoesNotProjectGenericBoxingAcrossScopes()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location);
        const string unresolvedOwner = "<Outer>b__0_0";
        ReplaceAscii(
            image,
            nameof(ClassicAsyncSiblingFixture.AsyncGenBoxed),
            unresolvedOwner,
            expectedReplacements: 3);
        LibraryBodyAnalysisExecution identities =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name == unresolvedOwner);
        MethodIdentity moveNext = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    unresolvedOwner,
                    StringComparison.Ordinal));
        Assert.Equal(
            source,
            identities.CallGraph.ResolveDeclaredMethod(moveNext));

        foreach (LibraryBodyAnalysisExecution index in new[]
        {
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                }),
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        source.DeclaringType)),
        })
        {
            Assert.DoesNotContain(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
        }
    }

    [Fact]
    public void
        DirectCalls_UnresolvedNestedLiftedSourceRetainsPhysicalCaller()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                nestedUnresolvedLiftedSource: true);
        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "NestedUnresolvedLiftedSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        MethodIdentity inner = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name
                == "<<Outer>b__0_0>b__0_1");
        DirectCall call = Assert.Single(
            full.CallGraph.DirectCalls,
            candidate => candidate.EvidenceMethod == moveNext
                && candidate.Callee.Name == "Read");

        Assert.Equal(moveNext, call.Caller);
        Assert.Equal(
            inner,
            full.CallGraph.ResolveDeclaredMethod(moveNext));

        LibraryBodyAnalysisExecution scoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "NestedUnresolvedLiftedSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));
        Assert.Null(scoped.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            scoped.CallGraph.DirectCalls,
            candidate => candidate.EvidenceMethod == moveNext
                && candidate.Caller == moveNext
                && candidate.Callee.Name == "Read");
    }

    [Fact]
    public void
        ResolveDeclaredMethod_MalformedLiftedSourceNameFailsClosed()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedGeneratedLiftedSource: true);
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedLiftedSourceName.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        Assert.Null(index.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            index.CallGraph.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == moveNext
                && call.Callee.Name == "Read");
    }

    [Fact]
    public void
        ResolveDeclaredMethod_RejectsMalformedLiftedOrdinalSuffix()
    {
        string fixturePath =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        LibraryBodyAnalysisExecution original =
            BodyAnalysisTestExecution.Open(fixturePath);
        MethodIdentity originalLifted = Assert.Single(
            original.CallGraph.Methods,
            method => method.Name.StartsWith(
                "<GenericObjectEqualsLocalFunction>g__EqualsCore|",
                StringComparison.Ordinal));
        Assert.Equal(
            nameof(OptimizationOpportunityFixtures
                .GenericObjectEqualsLocalFunction),
            original.CallGraph.ResolveDeclaredMethod(
                originalLifted)?.Name);
        int separator = originalLifted.Name.LastIndexOf('|');
        Assert.True(separator >= 0);
        string malformedName =
            originalLifted.Name[..(separator + 1)]
            + new string(
                'x',
                originalLifted.Name.Length - separator - 1);
        byte[] image = File.ReadAllBytes(fixturePath);
        ReplaceAscii(
            image,
            originalLifted.Name,
            malformedName,
            expectedReplacements: 1);
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedLiftedOrdinal.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity malformedLifted = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name == malformedName);

        Assert.Null(
            index.CallGraph.ResolveDeclaredMethod(malformedLifted));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_TypeGeneratedMalformedAsyncSourceFailsClosedAcrossScopes()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                typeGeneratedMalformedLiftedSource: true);
        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "Noise>b__0_0");
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        foreach (LibraryBodyAnalysisExecution index in new[]
        {
            full,
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                }),
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        source.DeclaringType)),
        })
        {
            Assert.Null(
                index.CallGraph.ResolveDeclaredMethod(moveNext));
        }
        Assert.Contains(
            full.CallGraph.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == moveNext
                && call.Callee.Name == "Read");
    }

    [Fact]
    public void
        ResolveDeclaredMethod_MalformedNestedLiftedOwnerDoesNotBecomeUltimateOwner()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedNestedLiftedIntermediate: true);
        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        MethodIdentity immediateSource = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name
                == "<Noise>b__0_0>b__0_1");
        MethodIdentity malformedOwner = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == "Noise>b__0_0");

        Assert.Equal(
            immediateSource,
            full.CallGraph.ResolveDeclaredMethod(moveNext));
        Assert.Null(
            full.CallGraph.ResolveDeclaredMethod(immediateSource));
        Assert.Contains(
            full.CallGraph.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == moveNext
                && call.Callee.Name == "Read");
        Assert.Contains(
            full.CallGraph.DirectCalls,
            call => call.EvidenceMethod == immediateSource
                && call.Caller == immediateSource
                && call.Caller != malformedOwner
                && call.Callee.Name == "Read");

        LibraryBodyAnalysisExecution stateMachineScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));
        Assert.Null(
            stateMachineScoped.CallGraph.ResolveDeclaredMethod(moveNext));

        foreach (LibraryBodyAnalysisExecution ownerScoped in new[]
        {
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    immediateSource.MetadataToken,
                }),
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        immediateSource.DeclaringType)),
        })
        {
            Assert.Null(
                ownerScoped.CallGraph.ResolveDeclaredMethod(
                    immediateSource));
            Assert.Contains(
                ownerScoped.CallGraph.DirectCalls,
                call => call.EvidenceMethod == immediateSource
                    && call.Caller == immediateSource
                    && call.Callee.Name == "Read");
        }
    }

    [Fact]
    public void
        DirectCalls_CompilerGeneratedAsyncOwnerRetainsAttributionAcrossScopes()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisExecution full = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            full.CallGraph.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CompilerGeneratedAsyncOwner));
        DirectCall expected = Assert.Single(
            full.CallGraph.DirectCalls,
            call => call.Caller == owner
                && call.EvidenceMethod.Name == "MoveNext"
                && call.EvidenceMethod.DeclaringType.Name.Contains(
                    nameof(
                        ClassicAsyncSiblingFixture
                            .CompilerGeneratedAsyncOwner),
                    StringComparison.Ordinal)
                && call.Callee.Name
                    == nameof(
                        ClassicAsyncSiblingFixture.ReadValue));

        Assert.Equal(
            owner,
            full.CallGraph.ResolveDeclaredMethod(
                expected.EvidenceMethod));
        Assert.DoesNotContain(
            full.Receipt.Diagnostics,
            diagnostic => diagnostic.MethodToken
                == expected.EvidenceMethod.MetadataToken);

        foreach (LibraryBodyAnalysisExecution scoped in new[]
        {
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                }),
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType)),
        })
        {
            Assert.Contains(
                scoped.CallGraph.DirectCalls,
                call => call.Caller == owner
                    && call.EvidenceMethod
                        == expected.EvidenceMethod
                    && call.Callee == expected.Callee);
            Assert.Equal(
                owner,
                scoped.CallGraph.ResolveDeclaredMethod(
                    expected.EvidenceMethod));
            Assert.DoesNotContain(
                scoped.Receipt.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == expected.EvidenceMethod.MetadataToken);
        }
    }

    [Fact]
    public void
        ResolveDeclaredMethod_CompilerGeneratedOwnersRetainAttribution()
    {
        LibraryBodyAnalysisExecution index = BodyAnalysisTestExecution.Open(
            typeof(LibraryBodyIndexTests).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);

        AssertOwner(
            nameof(
                OptimizationOpportunityFixtures
                    .CompilerGeneratedOwner));
        AssertOwner(
            nameof(
                CompilerGeneratedOwnerContainer
                    .CompilerGeneratedTypeOwner));

        void AssertOwner(string ownerName)
        {
            MethodIdentity owner = Assert.Single(
                index.CallGraph.Methods,
                method => method.Name == ownerName);
            MethodIdentity lifted = Assert.Single(
                index.CallGraph.Methods,
                method => method.Name.StartsWith(
                    $"<{ownerName}>g__EqualsCore|",
                    StringComparison.Ordinal));

            Assert.Equal(
                owner,
                index.CallGraph.ResolveDeclaredMethod(lifted));
            Assert.Contains(
                index.CallGraph.DirectCalls,
                call => call.EvidenceMethod == lifted
                    && call.Caller == owner);
        }
    }

    [Fact]
    public void
        ResolveDeclaredMethod_TypeGeneratedMalformedOwnerFailsClosedAcrossScopes()
    {
        byte[] image = File.ReadAllBytes(
            typeof(LibraryBodyIndexTests).Assembly.Location);
        const string originalOwner =
            nameof(CompilerGeneratedOwnerContainer
                .CompilerGeneratedTypeOwner);
        const string malformedOwner =
            "Noise>g__ABCDEFGHIJKLMNOPQ";
        ReplaceAscii(
            image,
            originalOwner,
            malformedOwner,
            expectedReplacements: 2);

        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name == malformedOwner);
        MethodIdentity lifted = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name.StartsWith(
                $"<{malformedOwner}>g__EqualsCore|",
                StringComparison.Ordinal));

        AssertRejected(full, evidenceExpected: true);

        LibraryBodyAnalysisExecution methodScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                });
        AssertRejected(
            methodScoped,
            evidenceExpected: false);

        LibraryBodyAnalysisExecution typeScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType));
        AssertRejected(
            typeScoped,
            evidenceExpected: true);

        void AssertRejected(
            LibraryBodyAnalysisExecution index,
            bool evidenceExpected)
        {
            Assert.Null(
                index.CallGraph.ResolveDeclaredMethod(lifted));
            Assert.DoesNotContain(
                index.CallGraph.DirectCalls,
                call => call.EvidenceMethod == lifted
                    && call.Caller == owner);
            Assert.Equal(
                evidenceExpected,
                index.CallGraph.DirectCalls.Any(
                    call => call.EvidenceMethod == lifted
                        && call.Caller == lifted));
        }
    }

    [Fact]
    public void
        Scopes_MalformedGeneratedOwnersDoNotAdmitStateMachineBodies()
    {
        byte[] immediateImage =
            BuildMalformedAsyncSourceAssembly(
                malformedGeneratedLiftedSource: true);
        LibraryBodyAnalysisExecution immediateFull =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedImmediateScope.dll",
                [.. immediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity malformedSource = Assert.Single(
            immediateFull.CallGraph.Methods,
            method => method.Name == "Noise>b__0_0");
        LibraryBodyAnalysisExecution immediateScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedImmediateScope.dll",
                [.. immediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    malformedSource.MetadataToken,
                });
        Assert.DoesNotContain(
            immediateScoped.CallGraph.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "Read");

        byte[] intermediateImage =
            BuildMalformedAsyncSourceAssembly(
                malformedNestedLiftedIntermediate: true);
        LibraryBodyAnalysisExecution intermediateFull =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedIntermediateScope.dll",
                [.. intermediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity malformedOwner = Assert.Single(
            intermediateFull.CallGraph.Methods,
            method => method.Name == "Noise>b__0_0");
        LibraryBodyAnalysisExecution intermediateScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedIntermediateScope.dll",
                [.. intermediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    malformedOwner.MetadataToken,
                });
        Assert.DoesNotContain(
            intermediateScoped.CallGraph.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "Read");

        MethodIdentity immediateSource = Assert.Single(
            intermediateFull.CallGraph.Methods,
            method => method.Name
                == "<Noise>b__0_0>b__0_1");
        LibraryBodyAnalysisExecution intermediateTypeScoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedIntermediateTypeScope.dll",
                [.. intermediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        immediateSource.DeclaringType));
        Assert.DoesNotContain(
            intermediateTypeScoped.CallGraph.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "Read");
    }

    [Fact]
    public void
        OptimizationOpportunities_CompilerGeneratedAsyncOwnerIsScopeInvariant()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyAnalysisExecution identities = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CompilerGeneratedAsyncOwner));
        MethodIdentity moveNext = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && identities.CallGraph.ResolveDeclaredMethod(method)
                    == owner);

        foreach (LibraryBodyAnalysisExecution index in new[]
        {
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                }),
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType)),
        })
        {
            Assert.Contains(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "capturing-delegate"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.MetadataToken
                        == owner.MetadataToken);
        }
    }

    [Fact]
    public void
        OptimizationOpportunities_TypeGeneratedAsyncOwnerSuppressesSiblingAcrossScopes()
    {
        string path =
            typeof(
                ClassicAsyncSiblingFixture
                    .CompilerGeneratedAsyncOwnerContainer)
                .Assembly.Location;
        LibraryBodyAnalysisExecution identities = BodyAnalysisTestExecution.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CompilerGeneratedAsyncOwnerContainer
                        .AnalyzeAsync)
                && method.DeclaringType.Name.Contains(
                    nameof(
                        ClassicAsyncSiblingFixture
                            .CompilerGeneratedAsyncOwnerContainer),
                    StringComparison.Ordinal));

        foreach (LibraryBodyAnalysisExecution index in new[]
        {
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                }),
            BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType)),
        })
        {
            Assert.DoesNotContain(
                index.Optimization.Opportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.MetadataToken
                        == owner.MetadataToken);
        }
    }

    [Fact]
    public void
        ResolveUltimateDeclaredMethod_AuthenticatesTopLevelEntryPoint()
    {
        string path =
            FixtureCatalog.AnalysisTopLevelClassicAsync
                .AssemblyPath();
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle moveNextHandle =
            reader.MethodDefinitions.Single(handle =>
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                return reader.StringComparer.Equals(
                        method.Name,
                        "MoveNext")
                    && reader.GetString(
                        reader.GetTypeDefinition(
                            method.GetDeclaringType()).Name)
                        .Contains(
                            "<Main>$",
                            StringComparison.Ordinal);
            });
        MethodDefinition moveNext =
            reader.GetMethodDefinition(moveNextHandle);
        TypeDefinitionHandle typeHandle =
            moveNext.GetDeclaringType();
        TypeDefinition type =
            reader.GetTypeDefinition(typeHandle);
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader);
        ILibraryMethodAnalysisInfrastructure infrastructure =
            builder;
        GenericScope scope = infrastructure.CreateScope(
            type,
            moveNext);
        MethodIdentity identity =
            infrastructure.CreateMethodIdentity(
                typeHandle,
                moveNextHandle,
                moveNext,
                scope);

        DeclaredOwnerResolution resolution =
            infrastructure.ResolveUltimateDeclaredMethod(
                moveNextHandle,
                moveNext,
                identity,
                typeSourceGenerated: true,
                out _,
                out AuthenticatedSourceOwner? owner);

        Assert.Equal(
            DeclaredOwnerResolution.Resolved,
            resolution);
        Assert.True(
            owner?.IsAuthenticatedTopLevelEntryPoint);
    }

    [Fact]
    public void
        ResolveUltimateDeclaredMethod_PreservesRejectedFirstLiftedHop()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"RejectedFirstLiftedHop-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(
                path,
                BuildMalformedAsyncSourceAssembly(
                    malformedNestedLiftedIntermediate: true));
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinitionHandle methodHandle =
                reader.MethodDefinitions.Single(handle =>
                    reader.StringComparer.Equals(
                        reader.GetMethodDefinition(handle).Name,
                        "Noise>b__0_0"));
            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            TypeDefinitionHandle typeHandle =
                method.GetDeclaringType();
            TypeDefinition type =
                reader.GetTypeDefinition(typeHandle);
            using var builder = new LibraryBodyAnalysisBuilder(
                path,
                reader,
                peReader);
            ILibraryMethodAnalysisInfrastructure infrastructure =
                builder;
            GenericScope scope = infrastructure.CreateScope(
                type,
                method);
            MethodIdentity identity =
                infrastructure.CreateMethodIdentity(
                    typeHandle,
                    methodHandle,
                    method,
                    scope);

            DeclaredOwnerResolution resolution =
                infrastructure.ResolveUltimateDeclaredMethod(
                    methodHandle,
                    method,
                    identity,
                    typeSourceGenerated: false,
                    out _,
                    out _);

            Assert.Equal(
                DeclaredOwnerResolution.Rejected,
                resolution);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        ScopedAsyncAdmission_DoesNotIndexUnselectedTopLevelEntryPoint()
    {
        string path =
            FixtureCatalog.AnalysisTopLevelClassicAsync
                .AssemblyPath();
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        CorHeader corHeader = Assert.IsType<CorHeader>(
            peReader.PEHeaders.CorHeader);
        MethodDefinitionHandle entryPoint =
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                corHeader.EntryPointTokenOrRelativeVirtualAddress);
        int indexed = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodBodyReferenceIndexed: handle =>
            {
                if (handle == entryPoint)
                    indexed++;
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.MethodEvidence,
            methodScope: null,
            typeScope: static _ => false));

        Assert.Equal(0, indexed);
    }

    [Fact]
    public void
        ResolveDeclaredMethod_TerminalMalformedOwnerFailsClosed()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedNestedLiftedIntermediate: true);
        LibraryBodyAnalysisExecution full =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TerminalMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity lifted = Assert.Single(
            full.CallGraph.Methods,
            method => method.Name
                == "<Noise>b__0_0>b__0_1");
        LibraryBodyAnalysisExecution scoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TerminalMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        lifted.DeclaringType));

        Assert.Null(
            scoped.CallGraph.ResolveDeclaredMethod(lifted));
    }

    [Fact]
    public void
        OptimizationOpportunities_TerminalMalformedOwnerFailsClosedWhenEvidenceIsSelected()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedNestedLiftedIntermediate: true,
                moveNextSmallArray: true);
        LibraryBodyAnalysisExecution identities =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TerminalMalformedOwnerOpportunity.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            identities.CallGraph.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        LibraryBodyAnalysisExecution scoped =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "TerminalMalformedOwnerOpportunity.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    moveNext.MetadataToken,
                });

        Assert.DoesNotContain(
            scoped.Optimization.Opportunities,
            opportunity => opportunity.Shape == "small-array"
                && opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
    }

    [Fact]
    public void
        DirectCalls_RecoverableUltimateOwnerFailureRetainsPhysicalCaller()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                deepNestedLiftedSource: true);
        using var stream = new MemoryStream(image);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        int injectedFailures = 0;
        using var builder =
            new LibraryBodyAnalysisBuilder(
                "RecoverableUltimateOwnerFailure.dll",
                reader,
                peReader,
                methodBodyReferenceIndexed: handle =>
                {
                    if (reader.StringComparer.Equals(
                            reader.GetMethodDefinition(handle).Name,
                            "<Outer>b__0_0")
                        && Interlocked.Increment(
                            ref injectedFailures) == 1)
                    {
                        throw new BadImageFormatException(
                            "Injected ultimate-owner failure.");
                    }
                });

        LibraryBodyAnalysisResult analysis = builder.Build(
            LibraryBodyAnalysisPlan.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence,
                methodScope: null,
                typeScope: null));
        MethodIdentity moveNext = Assert.Single(
            analysis.Methods.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        Assert.Equal(1, injectedFailures);
        Assert.Contains(
            analysis.Methods.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == moveNext
                && call.Callee.Name == "Read");
        Assert.Contains(
            analysis.Diagnostics,
            diagnostic => diagnostic.MethodToken
                    == moveNext.MetadataToken
                && diagnostic.Message.Contains(
                    "Injected ultimate-owner failure",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void OptimizationOpportunities_MalformedParallelStateMapPreservesCalls()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedMoveNextMethodImpl: true,
                extraMethodCount: 200);

        var index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "MalformedParallelStateMap.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.Default);

        Assert.Contains(
            index.CallGraph.DirectCalls,
            call => call.Callee.Name == "Read");
    }

    [Fact]
    public void
        LiftedOwners_RejectForgedStateMachineExecutionMethod()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                moveNextSmallArray: true,
                forgedStateMachineOwnerEvidence: true);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ForgedStateMachineOwner-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            var full = BodyAnalysisTestExecution.Open(path);
            MethodIdentity lifted = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name
                    == "<AnalyzeAsync>g__Local|0_0");
            MethodIdentity invalidMoveNext = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "MoveNext"
                    && method.IsStatic);
            Assert.Null(full.CallGraph.ResolveDeclaredMethod(lifted));
            Assert.Contains(
                full.Optimization.Opportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);

            var methodScoped = BodyAnalysisTestExecution.Open(
                path,
                bodyScope: new HashSet<int>
                {
                    invalidMoveNext.MetadataToken,
                });
            Assert.DoesNotContain(
                methodScoped.Optimization.Opportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);

            var scoped = BodyAnalysisTestExecution.Open(
                path,
                bodyTypeScope:
                    type => type.Equals(
                        invalidMoveNext.DeclaringType));
            Assert.Contains(
                scoped.Allocations.Occurrences,
                pair => pair.Key
                    == invalidMoveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.Optimization.Opportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);
            Assert.DoesNotContain(
                scoped.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        LiftedOwners_TopLevelRejectsForgedStateMachineExecutionMethod()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                forgedTopLevelOwnerEvidence: true);
        var index = BodyAnalysisTestExecution.OpenFromPrefetchedImage(
            "ForgedTopLevelOwner.exe",
            [.. image],
            LibraryBodyAnalysisFeatures.Default);
        MethodIdentity lifted = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name
                == "<<Main>$>g__Local|0_0");

        Assert.Null(index.CallGraph.ResolveDeclaredMethod(lifted));
    }

    [Fact]
    public void
        LiftedOwners_TopLevelRejectsMalformedManagedEntryPoint()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedTopLevelEntryPoint: true);
        var index = BodyAnalysisTestExecution.OpenFromPrefetchedImage(
            "MalformedTopLevelEntryPoint.exe",
            [.. image],
            LibraryBodyAnalysisFeatures.Default);
        MethodIdentity lifted = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name
                == "<<Main>$>g__Local|0_0");

        Assert.Null(index.CallGraph.ResolveDeclaredMethod(lifted));
    }

    [Theory]
    [InlineData(IteratorOwnershipProbe.ExplicitMoveNextDecoy)]
    [InlineData(IteratorOwnershipProbe.DuplicateOwners)]
    [InlineData(IteratorOwnershipProbe.AsyncIteratorWrongKind)]
    public void
        LiftedOwners_RejectUnauthenticatedIteratorExecution(
            IteratorOwnershipProbe probe)
    {
        byte[] image =
            BuildIteratorOwnershipAssembly(probe);
        var index = BodyAnalysisTestExecution.OpenFromPrefetchedImage(
            "IteratorOwnership.dll",
            [.. image],
            LibraryBodyAnalysisFeatures.Default);
        MethodIdentity lifted = Assert.Single(
            index.CallGraph.Methods,
            method => method.Name
                == "<OwnerA>g__Local|0_0");

        Assert.Null(index.CallGraph.ResolveDeclaredMethod(lifted));
        if (probe
            == IteratorOwnershipProbe
                .AsyncIteratorWrongKind)
        {
            MethodIdentity moveNext = Assert.Single(
                index.CallGraph.Methods,
                method => method.Name == "MoveNext");
            Assert.Null(
                index.CallGraph.ResolveDeclaredMethod(moveNext));
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void
        OptimizationOpportunities_ScopedUnauthenticatedAsyncSourceFailsClosed(
            bool malformedAnalyzeSignature,
            bool duplicateAnalyzeAttribute,
            bool malformedAnalyzeConstructor)
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                moveNextSmallArray: true,
                malformedAnalyzeSignature:
                    malformedAnalyzeSignature,
                duplicateAnalyzeAttribute:
                    duplicateAnalyzeAttribute,
                malformedAnalyzeConstructor:
                    malformedAnalyzeConstructor);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"SkippedAsyncOwner-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            var full = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.CallGraph.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            var scoped = BodyAnalysisTestExecution.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));

            Assert.Contains(
                scoped.Allocations.Occurrences,
                pair => pair.Key == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.Optimization.Opportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.Optimization.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
