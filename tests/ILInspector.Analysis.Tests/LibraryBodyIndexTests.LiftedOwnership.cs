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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall directCall = Assert.Single(
            index.DirectCalls,
            call => call.Caller.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync)
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
        DirectCall groupedCall = Assert.Single(
            index.GetDirectCallsByCaller()[
                directCall.Caller.MetadataToken],
            call => call.ILOffset == directCall.ILOffset);
        DirectCall foundCall = Assert.Single(
            index.FindCalls(
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
            index.DirectCalls,
            call => call.Caller.Name == "MoveNext"
                && call.Caller.DeclaringType.Name.Contains(
                    nameof(ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync),
                    StringComparison.Ordinal));
        Assert.NotEmpty(
            index.DirectCalls.Where(
                call => call.Caller.Name
                    == nameof(
                        ClassicAsyncSiblingFixture.ReadValueAsync)));
        Assert.All(
            index.DirectCalls.Where(
                call => call.Caller.Name
                    == nameof(
                        ClassicAsyncSiblingFixture.ReadValueAsync)),
            call => Assert.Equal(
                call.Caller,
                call.EvidenceMethod));

        MethodIdentity sourceMethod = Assert.Single(
            index.Methods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture
                    .CallsSyncSiblingFromAsync));
        var scoped = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                sourceMethod.MetadataToken,
            });
        Assert.Contains(
            scoped.DirectCalls,
            call => call.Caller == sourceMethod
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
    }

    [Fact]
    public void TopLeverage_UsesCallGraphDeclaredCallerCurrency()
    {
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity target = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
        MethodLeverage leverage = Assert.Single(
            index.TopLeverage(
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
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity sourceMethod = Assert.Single(
            full.Methods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture
                    .AwaitTaskInAsyncLambda));
        DirectCall expected = Assert.Single(
            full.DirectCalls,
            call => call.Caller == sourceMethod
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name
                    == "GetAwaiter");
        var memberScoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                sourceMethod.MetadataToken,
            });
        var typeScoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyTypeScope:
                type => type.ToQualifiedDisplayString()
                    == sourceMethod.DeclaringType
                        .ToQualifiedDisplayString());

        Assert.Contains(
            memberScoped.DirectCalls,
            call => call.Caller == expected.Caller
                && call.EvidenceMethod
                    == expected.EvidenceMethod
                && call.ILOffset == expected.ILOffset
                && call.Callee == expected.Callee);
        Assert.Contains(
            typeScoped.DirectCalls,
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
        var index = LibraryBodyIndex.Open(path);

        var source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture.CallsSyncSiblingFromAsync));
        var mapped = Assert.Single(
            index.FindCalls(
                MemberPattern.Method(
                    source.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => index.ResolveDeclaredMethod(
                    call.EvidenceMethod)?.MetadataToken
                == source.MetadataToken);

        Assert.Equal(source, mapped.Caller);
        Assert.Equal("MoveNext", mapped.EvidenceMethod.Name);
        Assert.Null(index.ResolveDeclaredMethod(source));
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsClassicAsyncMoveNextWithoutOpportunities()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            includeAllocations: false,
            includeOpportunities: false);

        var source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture.CallsSyncSiblingFromAsync));
        Assert.Contains(
            index.FindCalls(
                MemberPattern.Method(
                    source.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == source
                && call.EvidenceMethod.Name == "MoveNext"
                && index.ResolveDeclaredMethod(
                    call.EvidenceMethod)?.MetadataToken
                    == source.MetadataToken);
    }

    [Fact]
    public void ResultSinks_PublishRuntimeAsyncBodyAttribution()
    {
        string path =
            typeof(OptimizationOpportunityAsyncSiblingFixtures)
                .Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures)
                && method.Name == nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                        .CallsSyncSiblingFromAsync));
        MethodResultSink sink = Assert.Single(
            index.ResultSinks,
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures)
                && method.Name == nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                        .ReadValuesAsync));
        MethodResultSink[] iteratorSinks =
        [
            .. index.ResultSinks.Where(
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
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync));
        MethodResultSink sink = Assert.Single(
            index.ResultSinks,
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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink sink = Assert.Single(
            index.ResultSinks,
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
            index.DirectCalls,
            call => call.EvidenceMethod == sink.EvidenceMethod
                && fieldSource.SourceCallOffsets.Contains(
                    call.ILOffset));
        Assert.Equal("ProducePayload", producer.Callee.Name);

        MethodIdentity multipleAwaits = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeMultipleAwaits));
        MethodResultSink multipleAwaitSink = Assert.Single(
            index.ResultSinks,
            candidate => candidate.Caller == multipleAwaits
                && candidate.StateMachineFieldSource is not null);
        Assert.Equal(
            2,
            index.DirectCalls.Count(call =>
                call.Caller == multipleAwaits
                && call.Callee.Name
                    is "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted"));
        Assert.Equal(
            "ProducePayload",
            Assert.Single(
                index.DirectCalls,
                call => call.EvidenceMethod
                        == multipleAwaitSink.EvidenceMethod
                    && multipleAwaitSink.StateMachineFieldSource!
                        .SourceCallOffsets.Contains(
                            call.ILOffset))
                .Callee.Name);

        var unoptimized = LibraryBodyIndex.Open(
            typeof(UnoptimizedAsyncFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures
                    .JsonWireContractFlow);
        MethodIdentity referenceStateMachine = Assert.Single(
            unoptimized.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(UnoptimizedAsyncFixture)
                && method.Name == nameof(
                    UnoptimizedAsyncFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink referenceSink = Assert.Single(
            unoptimized.ResultSinks,
            candidate => candidate.Caller
                    == referenceStateMachine
                && candidate.StateMachineFieldSource
                    is not null);
        DirectCall referenceSuspension = Assert.Single(
            unoptimized.DirectCalls,
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
            unoptimized.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(UnoptimizedAsyncFixture)
                && method.Name == nameof(
                    UnoptimizedAsyncFixture
                        .ReturnsCallStoredBeforeMultipleAwaits));
        Assert.Single(
            unoptimized.ResultSinks,
            candidate => candidate.Caller
                    == multipleReferenceAwaits
                && candidate.StateMachineFieldSource
                    is not null);
        DirectCall[] multipleReferenceSuspensions =
        [
            .. unoptimized.DirectCalls.Where(call =>
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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .AddressMutatedReferenceStateMachineSource));
        DirectCall replacement = Assert.Single(
            index.DirectCalls,
            call => call.Caller == source
                && call.Callee.Name == "ReplaceStateMachine");
        DirectCall suspension = Assert.Single(
            index.DirectCalls,
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
            index.ResultSinks,
            sink => sink.Caller == source
                && sink.StateMachineFieldSource is not null);
    }

    [Fact]
    public async Task
        ResultSinks_RejectWholeStateMachineInstanceWrite()
    {
        string? actual = await ClassicAsyncSiblingFixture
            .WholeInstanceWriteStateMachineSource();
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .WholeInstanceWriteStateMachineSource));
        MethodResultSink completion = Assert.Single(
            index.ResultSinks,
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
            index.FieldStores,
            store => store.EvidenceMethod
                    == completion.EvidenceMethod
                && store.Identity?.Name == "Payload");
        Assert.DoesNotContain(
            index.FieldLoads,
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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .NonGenericSuspensionBuilderSource));
        DirectCall[] suspensions =
        [
            .. index.DirectCalls.Where(call =>
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
            index.ResultSinks,
            sink => sink.Caller == source
                && sink.StateMachineFieldSource is not null);
    }

    [Fact]
    public void
        ResultSinks_RejectAmbiguousAsyncStateMachineFieldSources()
    {
        var index = LibraryBodyIndex.Open(
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
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == methodName);
            Assert.DoesNotContain(
                index.ResultSinks,
                sink => sink.Caller == source
                    && sink.StateMachineFieldSource is not null);
        }

        MethodIdentity multipleStores = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .HasMultipleStoresBeforeAwait));
        MethodResultSink multipleStoreSink = Assert.Single(
            index.ResultSinks,
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
            index.FieldStores.Count(store =>
                store.EvidenceMethod
                    == multipleStoreSink.EvidenceMethod
                && store.ILOffset < multipleStoreLoad.ILOffset
                && multipleStoreLoad.FieldIdentity!.Equals(
                    store.Identity)
                && store.Value.Single?.Kind
                    == ResolvedValueSourceKind.CallResult));

        MethodIdentity byReference = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .MutatesFieldByReferenceAfterAwait));
        DirectCall byReferenceCompletion = Assert.Single(
            index.DirectCalls,
            call => call.Caller == byReference
                && call.Callee.Name == "SetResult");
        MethodResultSink byReferenceSink = Assert.Single(
            index.ResultSinks,
            sink =>
                sink.EvidenceMethod
                    == byReferenceCompletion.EvidenceMethod
                && sink.ILOffset
                    == byReferenceCompletion.ILOffset);
        FieldIdentity byReferenceField =
            byReferenceSink.ResolvedValue!.Single!.FieldIdentity!;
        Assert.Contains(
            index.FieldLoads,
            load => load.EvidenceMethod
                    == byReferenceSink.EvidenceMethod
                && load.IsAddress
                && byReferenceField.Equals(load.Identity));

        MethodIdentity conditionalSuspension = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ConditionallySuspendsAfterParameterOverwrite));
        FieldIdentity conditionalField = Assert.IsType<FieldIdentity>(
            Assert.Single(
                index.ResultSinks,
                sink => sink.Caller == conditionalSuspension
                    && sink.ResolvedValue?.Single is
                    {
                        Kind:
                            ResolvedValueSourceKind.InstanceFieldLoad,
                    })
                .ResolvedValue!.Single!.FieldIdentity);
        Assert.Contains(
            index.FieldStores,
            store => store.EvidenceMethod
                    == conditionalSuspension
                && conditionalField.Equals(store.Identity));
        Assert.Contains(
            index.FieldStores,
            store => store.Caller == conditionalSuspension
                && store.EvidenceMethod
                    != conditionalSuspension
                && conditionalField.Equals(store.Identity));

        MethodIdentity conditionalLocal = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ConditionallyInitializesLocalBeforeSuspension));
        FieldIdentity conditionalLocalField =
            Assert.IsType<FieldIdentity>(
                Assert.Single(
                    index.ResultSinks,
                    sink => sink.Caller == conditionalLocal
                        && sink.ResolvedValue?.Single is
                        {
                            Kind:
                                ResolvedValueSourceKind.InstanceFieldLoad,
                        })
                    .ResolvedValue!.Single!.FieldIdentity);
        FieldStoreFact[] conditionalLocalStores =
        [
            .. index.FieldStores.Where(store =>
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .StoresInLoopBeforeAwait));
        Assert.Contains(
            index.DirectCalls,
            call => call.Caller == looped
                && call.Callee.Name == "ProducePayload"
                && call.InLoop);

        MethodIdentity customBuilder = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .UsesCustomAsyncBuilder));
        DirectCall customCompletion = Assert.Single(
            index.DirectCalls,
            call => call.Caller == customBuilder
                && call.Callee.Name == "SetResult");
        MethodResultSink customSink = Assert.Single(
            index.ResultSinks,
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .CustomBuilderSecondarySource));
        DirectCall customSecondaryCompletion = Assert.Single(
            index.DirectCalls,
            call => call.Caller == customSecondary
                && call.Callee.Name == "SetResult");
        MethodResultSink customSecondarySink = Assert.Single(
            index.ResultSinks,
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ExternalAddressSource));
        MethodResultSink externalAddressSink = Assert.Single(
            index.ResultSinks,
            sink => sink.Caller == externalAddress
                && sink.ResolvedValue?.Single is
                {
                    Kind:
                        ResolvedValueSourceKind.InstanceFieldLoad,
                    FieldIdentity: not null,
                });
        Assert.Contains(
            index.FieldLoads,
            load => load.EvidenceMethod.Name == "Corrupt"
                && load.IsAddress
                && externalAddressSink.ResolvedValue!.Single!
                    .FieldIdentity!.Equals(load.Identity));

        MethodIdentity mismatchedBuilder = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .MismatchedBuilderSource));
        DirectCall mismatchedCompletion = Assert.Single(
            index.DirectCalls,
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .MixedSuspensionBuilderSource));
        DirectCall[] mixedSuspensionCalls =
        [
            .. index.DirectCalls.Where(call =>
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ImmediateCompletionSource));
        DirectCall immediateSuspension = Assert.Single(
            index.DirectCalls,
            call => call.Caller == immediateCompletion
                && call.Callee.Name is
                    "AwaitOnCompleted"
                        or "AwaitUnsafeOnCompleted");
        MethodResultSink immediateSink = Assert.Single(
            index.ResultSinks,
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .WrongStateMachineArgumentSource));
        DirectCall wrongArgumentSuspension = Assert.Single(
            index.DirectCalls,
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
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReenteringCleanupSource));
        DirectCall reenteringCompletion = Assert.Single(
            index.DirectCalls,
            call => call.Caller == reenteringCleanup
                && call.Callee.Name == "SetResult");
        MethodResultSink reenteringSink = Assert.Single(
            index.ResultSinks,
            sink => sink.EvidenceMethod
                    == reenteringCompletion.EvidenceMethod
                && sink.ILOffset == reenteringCompletion.ILOffset);
        FieldIdentity reenteringField = Assert.IsType<FieldIdentity>(
            reenteringSink.ResolvedValue?.Single?.FieldIdentity);
        Assert.True(reenteringCompletion.InLoop);
        Assert.Contains(
            index.FieldStores,
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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredAcrossFinally));
        DirectCall completion = Assert.Single(
            index.DirectCalls,
            call => call.Caller == source
                && call.Callee.Name == "SetResult");
        MethodResultSink sink = Assert.Single(
            index.ResultSinks,
            candidate => candidate.EvidenceMethod
                    == completion.EvidenceMethod
                && candidate.ILOffset == completion.ILOffset);

        Assert.Contains(
            index.DirectCalls,
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
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .JsonWireContractFlow);
            MethodIdentity source = Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreSource));

            Assert.Contains(
                index.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == corruptMethodToken);
            MethodResultSink sink = Assert.Single(
                index.ResultSinks,
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
                index.FieldStores,
                store => store.EvidenceMethod
                        != sink.EvidenceMethod
                    && store.IsReachable != false
                    && field.MightBeSameFieldAs(
                        store.Identity));
            Assert.DoesNotContain(
                index.ResultSinks,
                candidate => candidate.Caller == source
                    && candidate.StateMachineFieldSource
                        is not null);
            AssertCompilerPositiveSuppressedByCensus(index);
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
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .JsonWireContractFlow);
            MethodIdentity source = Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreSource));

            Assert.Contains(
                index.FieldStores,
                store => store.FieldToken == fieldOperandToken
                    && store.Identity is null);
            Assert.DoesNotContain(
                index.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == corruptMethodToken);
            Assert.DoesNotContain(
                index.ResultSinks,
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
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .JsonWireContractFlow);
            MethodIdentity source = Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(ClassicAsyncSiblingFixture)
                    && method.Name == nameof(
                        ClassicAsyncSiblingFixture
                            .FailedExternalStoreSource));

            Assert.Contains(
                index.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == corruptMethodToken);
            Assert.DoesNotContain(
                index.ResultSinks,
                sink => sink.Caller == source
                    && sink.StateMachineFieldSource is not null);
            AssertCompilerPositiveSuppressedByCensus(index);
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
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            full.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink fullSink = Assert.Single(
            full.ResultSinks,
            sink => sink.Caller == source
                && sink.StateMachineFieldSource is not null);

        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow,
            bodyScope:
                new HashSet<int>
                {
                    fullSink.EvidenceMethod.MetadataToken,
                });

        Assert.DoesNotContain(
            scoped.ResultSinks,
            sink => sink.StateMachineFieldSource is not null);
        Assert.Contains(
            scoped.ResultSinks,
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
        LibraryBodyIndex left = LibraryBodyIndex.Open(
            path,
            features);
        LibraryBodyIndex right = LibraryBodyIndex.Open(
            path,
            features);

        static MethodResultSink Select(
            LibraryBodyIndex index) =>
            Assert.Single(
                index.ResultSinks,
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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .UsesSecondaryBuilderAfterAwait));
        DirectCall[] completions =
        [
            .. index.DirectCalls.Where(call =>
                call.Caller == source
                && call.EvidenceMethod != source
                && call.Callee.Name == "SetResult"),
        ];
        Assert.Equal(2, completions.Length);

        MethodResultSink[] completionSinks =
        [
            .. completions.Select(completion => Assert.Single(
                index.ResultSinks,
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
        var index = LibraryBodyIndex.Open(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(ClassicAsyncSiblingFixture)
                && method.Name == nameof(
                    ClassicAsyncSiblingFixture
                        .ReturnsCallStoredBeforeAwait));
        MethodResultSink sink = Assert.Single(
            index.ResultSinks,
            candidate => candidate.Caller == source
                && candidate.StateMachineFieldSource is not null);
        AsyncStateMachineFieldResultSource fieldSource =
            sink.StateMachineFieldSource!;
        FieldStoreFact store = Assert.Single(
            index.FieldStores,
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
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.DeclaringType.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures)
                && method.Name == nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                        .ReadValues));
        MethodIdentity moveNext = Assert.Single(
            index.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    $"<{source.Name}>",
                    StringComparison.Ordinal));
        MethodResultSink[] sinks =
        [
            .. index.ResultSinks.Where(
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
        var index = LibraryBodyIndex.Open(path);

        var owner = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture.CallsThroughLocalFunction));
        var liftedCall = Assert.Single(
            index.FindCalls(
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
                index.ResolveDeclaredMethod(
                    liftedCall.EvidenceMethod)).MetadataToken);
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsSiblingReferencedLocalFunctionToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CallsThroughSiblingLocalFunctions));
        DirectCall call = Assert.Single(
            index.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.StartsWith(
                    "<CallsThroughSiblingLocalFunctions>g__Second|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.ResolveDeclaredMethod(call.EvidenceMethod));
    }

    [Fact]
    public void
        DirectCalls_AsyncLiftedMoveNextComposesToDeclaredOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncLiftedFunctionCallsSibling));
        DirectCall call = Assert.Single(
            index.DirectCalls,
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
            index.ResolveDeclaredMethod(call.EvidenceMethod));

        DirectCall[] expected = index.DirectCalls
            .Where(expectedCall =>
                expectedCall.Caller == owner)
            .ToArray();
        var methodScoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                owner.MetadataToken,
            });
        var typeScoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyTypeScope: type =>
                type.Equals(owner.DeclaringType));
        foreach (LibraryBodyIndex scoped
            in new[] { methodScoped, typeScoped })
        {
            Assert.Equal(
                expected,
                scoped.DirectCalls
                    .Where(scopedCall =>
                        scopedCall.Caller == owner)
                    .ToArray());
            Assert.Equal(
                owner,
                scoped.ResolveDeclaredMethod(
                    call.EvidenceMethod));
        }
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsAsyncOwnerLocalFunctionToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncOwnerCallsThroughLocalFunction));
        DirectCall expected = Assert.Single(
            index.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.StartsWith(
                    "<AsyncOwnerCallsThroughLocalFunction>g__Core|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.ResolveDeclaredMethod(expected.EvidenceMethod));

        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                expected.EvidenceMethod.MetadataToken,
            });
        Assert.Contains(
            scoped.DirectCalls,
            call => call.Caller == owner
                && call.EvidenceMethod == expected.EvidenceMethod
                && call.Callee == expected.Callee);
    }

    [Fact]
    public void ResolveDeclaredMethod_MapsAsyncOwnerLambdaToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncOwnerCallsThroughAsyncLambda));
        DirectCall expected = Assert.Single(
            index.FindCalls(
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
            index.ResolveDeclaredMethod(expected.EvidenceMethod));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_MapsAsyncLiftedFunctionSiblingToOwner()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .AsyncLiftedFunctionCallsSibling));
        DirectCall call = Assert.Single(
            index.FindCalls(
                MemberPattern.Method(
                    owner.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == owner
                && call.EvidenceMethod.Name.StartsWith(
                    "<AsyncLiftedFunctionCallsSibling>g__Inner|",
                    StringComparison.Ordinal));

        Assert.Equal(
            owner,
            index.ResolveDeclaredMethod(call.EvidenceMethod));
    }

    [Fact]
    public void AsyncMoveNextResolution_UsesExplicitInterfaceImplementation()
    {
        string path = typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .ExplicitMoveNextSource));
        DirectCall explicitCall = Assert.Single(
            index.FindCalls(
                MemberPattern.Method(
                    source.DeclaringType,
                    nameof(ClassicAsyncSiblingFixture.ReadValue))),
            call => call.Caller == source
                && call.EvidenceMethod.Name.EndsWith(
                    ".MoveNext",
                    StringComparison.Ordinal));

        Assert.NotEqual(source, explicitCall.EvidenceMethod);
        Assert.Contains(
            index.DirectCalls,
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

            var index = LibraryBodyIndex.Open(path);
            var opportunity = Assert.Single(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name == "AnalyzeAsync");

            Assert.Contains(
                index.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "BrokenAsync",
                    StringComparison.Ordinal));
            Assert.Contains(
                index.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "MalformedValueAsync",
                    StringComparison.Ordinal));
            Assert.Contains(
                index.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "DuplicateAsync",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                index.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "ForeignAssemblyAsync",
                    StringComparison.Ordinal));
            Assert.Contains(
                index.DirectCalls,
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
                    index.Methods,
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
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisLookalike
                .AssemblyPath(),
            LibraryBodyAnalysisFeatures.All);
        const string MethodName =
            "MalformedAsyncAttributeEvidence";
        MethodIdentity method = Assert.Single(
            index.Methods,
            method => method.Name == MethodName);
        MethodSignals signals =
            index.GetMethodSignals()
                .GetValueOrDefault(
                    method.MetadataToken,
                    MethodSignals.None);

        Assert.Contains(
            index.Diagnostics,
            diagnostic => diagnostic.MethodToken
                == method.MetadataToken);
        Assert.Contains(
            index.OptimizationOpportunities,
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
        var spoof = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisSpoofSystemRuntime
                .AssemblyPath());
        Assert.DoesNotContain(
            spoof.OptimizationOpportunities,
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

            var index = LibraryBodyIndex.Open(path);

            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(index.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    methodImplBodyAsMemberReference:
                        true));
            var memberReferenceBody =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                memberReferenceBody
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                memberReferenceBody.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true));
            var inheritedMethodImpl =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                inheritedMethodImpl
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                inheritedMethodImpl.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    methodImplBodyAsMemberReference:
                        true,
                    inheritedMethodImpl: true));
            var inheritedMemberReference =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                inheritedMemberReference
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                inheritedMemberReference.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync"));
            var unrelatedOverride =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                unrelatedOverride
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                unrelatedOverride.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync",
                    unrelatedSourceMethodImpl: true));
            var unrelatedSourceMethodImpl =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                unrelatedSourceMethodImpl
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                unrelatedSourceMethodImpl.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    inheritedMethodImplBodyName:
                        "CoreAsync",
                    malformedSourceMethodImpl: true));
            var malformedSourceMethodImpl =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                malformedSourceMethodImpl
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                malformedSourceMethodImpl.Diagnostics);

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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                invalidDeclarationOwner
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                invalidDeclarationOwner.Diagnostics);

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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                incompatibleSourceMethodImpl
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(
                incompatibleSourceMethodImpl.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: true,
                    inheritedMethodImpl: true,
                    sourceStartsNewSlot: true));
            var inheritedNewSlot =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                inheritedNewSlot
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(inheritedNewSlot.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false));
            var control = LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                control.OptimizationOpportunities,
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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                tasksContract.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Empty(tasksContract.Diagnostics);

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    finalInterfaceSibling: true,
                    sourceMethodName: "ReadAsync"));
            var finalInterfaceSibling =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                finalInterfaceSibling
                    .OptimizationOpportunities,
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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                malformedAttributeConstructor
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                malformedAttributeConstructor.Diagnostics,
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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                nativeClassicAsync
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                nativeClassicAsync.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    validStateMachine: false));
            var invalidStateMachine =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                invalidStateMachine
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                invalidStateMachine.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    sourceHasBody: false));
            var bodilessSource =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                bodilessSource
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                bodilessSource.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            var scopedBodilessSource =
                LibraryBodyIndex.Open(
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
                scopedBodilessSource.Diagnostics,
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
                LibraryBodyIndex.Open(path);
            Assert.Contains(
                bodilessRuntimeAsync.Diagnostics,
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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                nativeRuntimeAsync
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                nativeRuntimeAsync.Diagnostics,
                diagnostic => diagnostic.Method.Contains(
                    "AnalyzeAsync",
                    StringComparison.Ordinal));

            File.WriteAllBytes(
                path,
                BuildMethodImplAsyncSourceAssembly(
                    includeMethodImpl: false,
                    moveNextHasBody: false));
            var bodilessMoveNext =
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                bodilessMoveNext
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                bodilessMoveNext.Diagnostics,
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
                LibraryBodyIndex.Open(path);
            Assert.DoesNotContain(
                nonIlMoveNext
                    .OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                nonIlMoveNext.Diagnostics,
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
                    LibraryBodyIndex.Open(path);
                Assert.DoesNotContain(
                    unsupported.OptimizationOpportunities,
                    opportunity => opportunity.Shape
                            == "sync-call-in-async"
                        && opportunity.Method.Name
                            == "AnalyzeAsync");
                Assert.Empty(unsupported.Diagnostics);
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
        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "AmbiguousAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity generatedSource = Assert.Single(
            full.DeclaredMethods,
            method => method.Name
                == "<AnalyzeAsync>g__Generated|0_0");
        Assert.Equal(
            "<>c",
            generatedSource.DeclaringType.Name);
        MethodIdentity moveNext = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "MoveNext");
        DirectCall call = Assert.Single(
            full.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext);

        Assert.Equal(moveNext, call.Caller);
        Assert.Null(full.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            full.Diagnostics,
            diagnostic => diagnostic.MethodToken
                    == moveNext.MetadataToken
                && diagnostic.Message.Contains(
                    "Multiple async source methods",
                    StringComparison.Ordinal));

        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "AmbiguousAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope:
                    new HashSet<int> { source.MetadataToken });
        Assert.DoesNotContain(
            scoped.DirectCalls,
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

            var index = LibraryBodyIndex.Open(path);

            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync");
            Assert.Contains(
                index.Diagnostics,
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
            var full = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.Contains(
                full.OptimizationOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.DoesNotContain(
                full.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            var scoped = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));

            Assert.Contains(
                full.OptimizationOpportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.Contains(
                scoped.Diagnostics,
                diagnostic => diagnostic.MethodToken
                        == moveNext.MetadataToken
                    && diagnostic.Message.Contains(
                        "Multiple async source methods",
                        StringComparison.Ordinal));
            Assert.DoesNotContain(
                scoped.OptimizationOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.AllocationFanoutOpportunities,
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
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "CrossKindAsyncAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity moveNext = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        DirectCall call = Assert.Single(
            index.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext
                && candidate.Callee.Name == "Read");

        Assert.Equal(moveNext, call.Caller);
        Assert.Null(index.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            index.Diagnostics,
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
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "CrossKindSynchronousIteratorAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity moveNext = Assert.Single(
            index.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        DirectCall call = Assert.Single(
            index.DirectCalls,
            candidate =>
                candidate.EvidenceMethod == moveNext
                && candidate.Callee.Name == "Read");

        Assert.Equal(moveNext, call.Caller);
        Assert.Null(index.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            index.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken == moveNext.MetadataToken
                && diagnostic.Message.Contains(
                    "source is invalid or ambiguous",
                    StringComparison.Ordinal));

        LibraryBodyIndex methodScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "CrossKindSynchronousIteratorAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                });
        Assert.DoesNotContain(
            methodScoped.DirectCalls,
            candidate =>
                candidate.EvidenceMethod.MetadataToken
                    == moveNext.MetadataToken);

        LibraryBodyIndex typeScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "CrossKindSynchronousIteratorAttributes.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        source.DeclaringType));
        Assert.DoesNotContain(
            typeScoped.DirectCalls,
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
        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncDecoy.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "AnalyzeAsync");
        MethodIdentity moveNext = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        Assert.Equal(
            source,
            full.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            full.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == source
                && call.Callee.Name == "Read");
        Assert.DoesNotContain(
            full.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken
                    == source.MetadataToken);

        LibraryBodyIndex sourceScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncDecoy.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                });
        Assert.Equal(
            source,
            sourceScoped.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            sourceScoped.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == source);

        MethodIdentity unrelated = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "Read");
        LibraryBodyIndex unrelatedScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncDecoy.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    unrelated.MetadataToken,
                });
        Assert.Equal(
            source,
            unrelatedScoped.ResolveDeclaredMethod(moveNext));
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
            var full = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            MethodIdentity kickoff = Assert.Single(
                full.Methods,
                method => method.Name
                    == "<Outer>b__0_0");
            MethodIdentity caller = Assert.Single(
                full.Methods,
                method => method.Name
                    == "CompetingAsync");
            var methodScoped = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    kickoff.MetadataToken,
                });
            var scoped = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        kickoff.DeclaringType));

            Assert.Contains(
                full.OptimizationOpportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                full.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method == kickoff
                    && opportunity.EvidenceMethodToken
                        == moveNext.MetadataToken);
            Assert.Equal(
                kickoff,
                full.ResolveDeclaredMethod(moveNext));
            Assert.Contains(
                full.DirectCalls,
                call => call.EvidenceMethod == moveNext
                    && call.Callee.Name == "Read"
                    && call.Caller == moveNext);
            Assert.Contains(
                full.DirectCalls,
                call => call.EvidenceMethod == caller
                    && call.Callee.Name == "MoveNext");
            Assert.DoesNotContain(
                full.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            OptimizationOpportunity callerFanout =
                Assert.Single(
                    full.AllocationFanoutOpportunities,
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
                methodScoped.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method == kickoff
                    && opportunity.EvidenceMethodToken
                        == moveNext.MetadataToken);
            Assert.Contains(
                methodScoped.OptimizationOpportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                methodScoped.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.Null(
                methodScoped.ResolveDeclaredMethod(moveNext));
            Assert.Contains(
                scoped.GetAllocationOccurrences(),
                pair => pair.Key == moveNext.MetadataToken);
            Assert.Contains(
                scoped.OptimizationOpportunities,
                opportunity => opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method == kickoff
                    && opportunity.EvidenceMethodToken
                        == moveNext.MetadataToken);
            Assert.Null(
                scoped.ResolveDeclaredMethod(moveNext));
            Assert.DoesNotContain(
                scoped.AllocationFanoutOpportunities,
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
            LibraryBodyIndex full = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.True(
                CompilerGeneratedNames.RequiresDeclaredOwner(
                    moveNext));
            Assert.Null(
                full.ResolveDeclaredMethod(moveNext));

            foreach (LibraryBodyIndex index in new[]
            {
                full,
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope: new HashSet<int>
                    {
                        moveNext.MetadataToken,
                    }),
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyTypeScope:
                        type => type.Equals(
                            moveNext.DeclaringType)),
            })
            {
                Assert.Contains(
                    index.OptimizationOpportunities,
                    opportunity =>
                        opportunity.Shape == "small-array"
                        && opportunity.Method.MetadataToken
                            == moveNext.MetadataToken);
                Assert.DoesNotContain(
                    index.AllocationFanoutOpportunities,
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
            LibraryBodyIndex full = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.True(
                CompilerGeneratedNames.RequiresDeclaredOwner(
                    moveNext));
            Assert.Null(
                full.ResolveDeclaredMethod(moveNext));
            Assert.Contains(
                full.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);

            foreach (LibraryBodyIndex scoped in new[]
            {
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope: new HashSet<int>
                    {
                        moveNext.MetadataToken,
                    }),
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyTypeScope:
                        type => type.Equals(
                            moveNext.DeclaringType)),
            })
            {
                Assert.DoesNotContain(
                    scoped.OptimizationOpportunities,
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
            LibraryBodyIndex full = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity execution = Assert.Single(
                full.Methods,
                method => method.Name == "Run"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            Assert.Null(
                full.ResolveDeclaredMethod(execution));
            Assert.Contains(
                full.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape == "small-array"
                    && opportunity.Method.MetadataToken
                        == execution.MetadataToken);

            foreach (LibraryBodyIndex scoped in new[]
            {
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope: new HashSet<int>
                    {
                        execution.MetadataToken,
                    }),
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyTypeScope:
                        type => type.Equals(
                            execution.DeclaringType)),
            })
            {
                bool containsIntrinsic =
                    scoped.OptimizationOpportunities.Any(
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
        LibraryBodyIndex full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
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
            full.OptimizationOpportunities,
            opportunity => opportunity.Method.MetadataToken
                == moveNext.MetadataToken);

        LibraryBodyIndex scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities,
            bodyTypeScope:
                type => type.Equals(
                    moveNext.DeclaringType));

        Assert.DoesNotContain(
            scoped.OptimizationOpportunities,
            opportunity => opportunity.Method.MetadataToken
                == moveNext.MetadataToken);
    }

    [Fact]
    public void
        OptimizationOpportunities_CompiledGenericAsyncLocalStateMachineIsScopeInvariant()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyIndex full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
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
            full.OptimizationOpportunities,
            opportunity => opportunity.Shape == "small-array"
                && opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);

        LibraryBodyIndex scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities,
            bodyTypeScope:
                type => type.Equals(
                    moveNext.DeclaringType));

        Assert.DoesNotContain(
            scoped.OptimizationOpportunities,
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
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity owner = Assert.Single(
            index.Methods,
            method => method.Name
                == "ScopedCapturedAsyncLocalAllocationOwner");
        MethodIdentity source = Assert.Single(
            index.Methods,
            method => method.Name.StartsWith(
                "<ScopedCapturedAsyncLocalAllocationOwner>g__BuildAsync|",
                StringComparison.Ordinal));
        MethodIdentity moveNext = Assert.Single(
            index.Methods,
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
            index.ResolveDeclaredMethod(source));
        Assert.Equal(
            owner,
            index.ResolveDeclaredMethod(moveNext));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_LegacyHexLambdaOrdinalOnContainingHostMapsToAuthoredOwner()
    {
        string fixturePath =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyIndex original =
            LibraryBodyIndex.Open(fixturePath);
        MethodIdentity originalLambda = Assert.Single(
            original.Methods,
            method => method.Name.StartsWith(
                "<SharedLambdaOrdinalOwner>b__",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "<>c__DisplayClass",
            originalLambda.DeclaringType.Name,
            StringComparison.Ordinal);
        Assert.Equal(
            "SharedLambdaOrdinalOwner",
            original.ResolveDeclaredMethod(
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
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "LegacyHexLambdaOrdinal.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            index.Methods,
            method => method.Name
                == "SharedLambdaOrdinalOwner");
        MethodIdentity lambda = Assert.Single(
            index.Methods,
            method => method.Name == legacyName);

        Assert.Equal(
            owner,
            index.ResolveDeclaredMethod(lambda));
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
        LibraryBodyIndex identities =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            identities.Methods,
            method => method.Name == unresolvedOwner);
        MethodIdentity moveNext = Assert.Single(
            identities.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    unresolvedOwner,
                    StringComparison.Ordinal));
        Assert.Equal(
            source,
            identities.ResolveDeclaredMethod(moveNext));

        foreach (LibraryBodyIndex index in new[]
        {
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "UnresolvedAsyncGenericBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                }),
            LibraryBodyIndex.OpenFromPrefetchedImage(
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
                index.OptimizationOpportunities,
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
        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "NestedUnresolvedLiftedSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        MethodIdentity inner = Assert.Single(
            full.Methods,
            method => method.Name
                == "<<Outer>b__0_0>b__0_1");
        DirectCall call = Assert.Single(
            full.DirectCalls,
            candidate => candidate.EvidenceMethod == moveNext
                && candidate.Callee.Name == "Read");

        Assert.Equal(moveNext, call.Caller);
        Assert.Equal(
            inner,
            full.ResolveDeclaredMethod(moveNext));

        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "NestedUnresolvedLiftedSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));
        Assert.Null(scoped.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            scoped.DirectCalls,
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
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedLiftedSourceName.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            index.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        Assert.Null(index.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            index.DirectCalls,
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
        LibraryBodyIndex original =
            LibraryBodyIndex.Open(fixturePath);
        MethodIdentity originalLifted = Assert.Single(
            original.Methods,
            method => method.Name.StartsWith(
                "<GenericObjectEqualsLocalFunction>g__EqualsCore|",
                StringComparison.Ordinal));
        Assert.Equal(
            nameof(OptimizationOpportunityFixtures
                .GenericObjectEqualsLocalFunction),
            original.ResolveDeclaredMethod(
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
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedLiftedOrdinal.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity malformedLifted = Assert.Single(
            index.Methods,
            method => method.Name == malformedName);

        Assert.Null(
            index.ResolveDeclaredMethod(malformedLifted));
    }

    [Fact]
    public void
        ResolveDeclaredMethod_TypeGeneratedMalformedAsyncSourceFailsClosedAcrossScopes()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                typeGeneratedMalformedLiftedSource: true);
        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.Methods,
            method => method.Name == "Noise>b__0_0");
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);

        foreach (LibraryBodyIndex index in new[]
        {
            full,
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                }),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        source.DeclaringType)),
        })
        {
            Assert.Null(
                index.ResolveDeclaredMethod(moveNext));
        }
        Assert.Contains(
            full.DirectCalls,
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
        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        MethodIdentity immediateSource = Assert.Single(
            full.Methods,
            method => method.Name
                == "<Noise>b__0_0>b__0_1");
        MethodIdentity malformedOwner = Assert.Single(
            full.Methods,
            method => method.Name == "Noise>b__0_0");

        Assert.Equal(
            immediateSource,
            full.ResolveDeclaredMethod(moveNext));
        Assert.Null(
            full.ResolveDeclaredMethod(immediateSource));
        Assert.Contains(
            full.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == moveNext
                && call.Callee.Name == "Read");
        Assert.Contains(
            full.DirectCalls,
            call => call.EvidenceMethod == immediateSource
                && call.Caller == immediateSource
                && call.Caller != malformedOwner
                && call.Callee.Name == "Read");

        LibraryBodyIndex stateMachineScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));
        Assert.Null(
            stateMachineScoped.ResolveDeclaredMethod(moveNext));

        foreach (LibraryBodyIndex ownerScoped in new[]
        {
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    immediateSource.MetadataToken,
                }),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        immediateSource.DeclaringType)),
        })
        {
            Assert.Null(
                ownerScoped.ResolveDeclaredMethod(
                    immediateSource));
            Assert.Contains(
                ownerScoped.DirectCalls,
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
        LibraryBodyIndex full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            full.DeclaredMethods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CompilerGeneratedAsyncOwner));
        DirectCall expected = Assert.Single(
            full.DirectCalls,
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
            full.ResolveDeclaredMethod(
                expected.EvidenceMethod));
        Assert.DoesNotContain(
            full.Diagnostics,
            diagnostic => diagnostic.MethodToken
                == expected.EvidenceMethod.MetadataToken);

        foreach (LibraryBodyIndex scoped in new[]
        {
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                }),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType)),
        })
        {
            Assert.Contains(
                scoped.DirectCalls,
                call => call.Caller == owner
                    && call.EvidenceMethod
                        == expected.EvidenceMethod
                    && call.Callee == expected.Callee);
            Assert.Equal(
                owner,
                scoped.ResolveDeclaredMethod(
                    expected.EvidenceMethod));
            Assert.DoesNotContain(
                scoped.Diagnostics,
                diagnostic => diagnostic.MethodToken
                    == expected.EvidenceMethod.MetadataToken);
        }
    }

    [Fact]
    public void
        ResolveDeclaredMethod_CompilerGeneratedOwnersRetainAttribution()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
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
                index.Methods,
                method => method.Name == ownerName);
            MethodIdentity lifted = Assert.Single(
                index.Methods,
                method => method.Name.StartsWith(
                    $"<{ownerName}>g__EqualsCore|",
                    StringComparison.Ordinal));

            Assert.Equal(
                owner,
                index.ResolveDeclaredMethod(lifted));
            Assert.Contains(
                index.DirectCalls,
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

        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TypeGeneratedMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            full.Methods,
            method => method.Name == malformedOwner);
        MethodIdentity lifted = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                $"<{malformedOwner}>g__EqualsCore|",
                StringComparison.Ordinal));

        AssertRejected(full, evidenceExpected: true);

        LibraryBodyIndex methodScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
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

        LibraryBodyIndex typeScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
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
            LibraryBodyIndex index,
            bool evidenceExpected)
        {
            Assert.Null(
                index.ResolveDeclaredMethod(lifted));
            Assert.DoesNotContain(
                index.DirectCalls,
                call => call.EvidenceMethod == lifted
                    && call.Caller == owner);
            Assert.Equal(
                evidenceExpected,
                index.DirectCalls.Any(
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
        LibraryBodyIndex immediateFull =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedImmediateScope.dll",
                [.. immediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity malformedSource = Assert.Single(
            immediateFull.Methods,
            method => method.Name == "Noise>b__0_0");
        LibraryBodyIndex immediateScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedImmediateScope.dll",
                [.. immediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    malformedSource.MetadataToken,
                });
        Assert.DoesNotContain(
            immediateScoped.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "Read");

        byte[] intermediateImage =
            BuildMalformedAsyncSourceAssembly(
                malformedNestedLiftedIntermediate: true);
        LibraryBodyIndex intermediateFull =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedIntermediateScope.dll",
                [.. intermediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity malformedOwner = Assert.Single(
            intermediateFull.Methods,
            method => method.Name == "Noise>b__0_0");
        LibraryBodyIndex intermediateScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedIntermediateScope.dll",
                [.. intermediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    malformedOwner.MetadataToken,
                });
        Assert.DoesNotContain(
            intermediateScoped.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "Read");

        MethodIdentity immediateSource = Assert.Single(
            intermediateFull.Methods,
            method => method.Name
                == "<Noise>b__0_0>b__0_1");
        LibraryBodyIndex intermediateTypeScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedIntermediateTypeScope.dll",
                [.. intermediateImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyTypeScope:
                    type => type.Equals(
                        immediateSource.DeclaringType));
        Assert.DoesNotContain(
            intermediateTypeScoped.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "Read");
    }

    [Fact]
    public void
        OptimizationOpportunities_CompilerGeneratedAsyncOwnerIsScopeInvariant()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyIndex identities = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            identities.Methods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .CompilerGeneratedAsyncOwner));
        MethodIdentity moveNext = Assert.Single(
            identities.Methods,
            method => method.Name == "MoveNext"
                && identities.ResolveDeclaredMethod(method)
                    == owner);

        foreach (LibraryBodyIndex index in new[]
        {
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                }),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType)),
        })
        {
            Assert.Contains(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "capturing-delegate"
                    && opportunity.Method.MetadataToken
                        == moveNext.MetadataToken);
            Assert.DoesNotContain(
                index.OptimizationOpportunities,
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
        LibraryBodyIndex identities = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            identities.Methods,
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

        foreach (LibraryBodyIndex index in new[]
        {
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    owner.MetadataToken,
                }),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        owner.DeclaringType)),
        })
        {
            Assert.DoesNotContain(
                index.OptimizationOpportunities,
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
        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TerminalMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity lifted = Assert.Single(
            full.Methods,
            method => method.Name
                == "<Noise>b__0_0>b__0_1");
        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TerminalMalformedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        lifted.DeclaringType));

        Assert.Null(
            scoped.ResolveDeclaredMethod(lifted));
    }

    [Fact]
    public void
        OptimizationOpportunities_TerminalMalformedOwnerFailsClosedWhenEvidenceIsSelected()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedNestedLiftedIntermediate: true,
                moveNextSmallArray: true);
        LibraryBodyIndex identities =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TerminalMalformedOwnerOpportunity.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            identities.Methods,
            method => method.Name == "MoveNext"
                && method.ParameterTypes.IsEmpty);
        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "TerminalMalformedOwnerOpportunity.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    moveNext.MetadataToken,
                });

        Assert.DoesNotContain(
            scoped.OptimizationOpportunities,
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
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedParallelStateMap.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.Default);

        Assert.Contains(
            index.DirectCalls,
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
            var full = LibraryBodyIndex.Open(path);
            MethodIdentity lifted = Assert.Single(
                full.Methods,
                method => method.Name
                    == "<AnalyzeAsync>g__Local|0_0");
            MethodIdentity invalidMoveNext = Assert.Single(
                full.Methods,
                method => method.Name == "MoveNext"
                    && method.IsStatic);
            Assert.Null(full.ResolveDeclaredMethod(lifted));
            Assert.Contains(
                full.OptimizationOpportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);

            var methodScoped = LibraryBodyIndex.Open(
                path,
                bodyScope: new HashSet<int>
                {
                    invalidMoveNext.MetadataToken,
                });
            Assert.DoesNotContain(
                methodScoped.OptimizationOpportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);

            var scoped = LibraryBodyIndex.Open(
                path,
                bodyTypeScope:
                    type => type.Equals(
                        invalidMoveNext.DeclaringType));
            Assert.Contains(
                scoped.GetAllocationOccurrences(),
                pair => pair.Key
                    == invalidMoveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.OptimizationOpportunities,
                opportunity => opportunity.Method
                    == invalidMoveNext);
            Assert.DoesNotContain(
                scoped.AllocationFanoutOpportunities,
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
        var index = LibraryBodyIndex.OpenFromPrefetchedImage(
            "ForgedTopLevelOwner.exe",
            [.. image],
            LibraryBodyAnalysisFeatures.Default);
        MethodIdentity lifted = Assert.Single(
            index.Methods,
            method => method.Name
                == "<<Main>$>g__Local|0_0");

        Assert.Null(index.ResolveDeclaredMethod(lifted));
    }

    [Fact]
    public void
        LiftedOwners_TopLevelRejectsMalformedManagedEntryPoint()
    {
        byte[] image =
            BuildMalformedAsyncSourceAssembly(
                malformedTopLevelEntryPoint: true);
        var index = LibraryBodyIndex.OpenFromPrefetchedImage(
            "MalformedTopLevelEntryPoint.exe",
            [.. image],
            LibraryBodyAnalysisFeatures.Default);
        MethodIdentity lifted = Assert.Single(
            index.Methods,
            method => method.Name
                == "<<Main>$>g__Local|0_0");

        Assert.Null(index.ResolveDeclaredMethod(lifted));
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
        var index = LibraryBodyIndex.OpenFromPrefetchedImage(
            "IteratorOwnership.dll",
            [.. image],
            LibraryBodyAnalysisFeatures.Default);
        MethodIdentity lifted = Assert.Single(
            index.Methods,
            method => method.Name
                == "<OwnerA>g__Local|0_0");

        Assert.Null(index.ResolveDeclaredMethod(lifted));
        if (probe
            == IteratorOwnershipProbe
                .AsyncIteratorWrongKind)
        {
            MethodIdentity moveNext = Assert.Single(
                index.Methods,
                method => method.Name == "MoveNext");
            Assert.Null(
                index.ResolveDeclaredMethod(moveNext));
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
            var full = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
            MethodIdentity moveNext = Assert.Single(
                full.Methods,
                method => method.Name == "MoveNext"
                    && method.ParameterTypes.IsDefaultOrEmpty);
            var scoped = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        moveNext.DeclaringType));

            Assert.Contains(
                scoped.GetAllocationOccurrences(),
                pair => pair.Key == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.OptimizationOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
            Assert.DoesNotContain(
                scoped.AllocationFanoutOpportunities,
                opportunity => opportunity.Method.MetadataToken
                    == moveNext.MetadataToken);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
