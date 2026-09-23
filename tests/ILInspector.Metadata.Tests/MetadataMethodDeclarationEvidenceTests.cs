using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.ConsumerCanary;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataMethodDeclarationEvidenceTests
{
    [Fact]
    public void RealInt32MethodPostsExactFlagsAndSignature()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = reader.TypeDefinitions.First(handle =>
        {
            TypeDefinition candidate = reader.GetTypeDefinition(handle);
            return reader.GetString(candidate.Name) == "Int32"
                && reader.GetString(candidate.Namespace) == "System";
        });
        MethodDefinitionHandle method =
            reader.GetTypeDefinition(type).GetMethods().First(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name)
                    == "ToString"
                && reader.GetMethodDefinition(handle)
                    .GetParameters().Count == 0);
        var result = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(path, MetadataTokens.GetToken(type), method));
        Assert.Equal(MetadataTypeDefinitionAddress.FromHandle(reader, type),
            result.Evidence.Type);
        Assert.Equal(MetadataMethodAddress.Create(reader, method),
            result.Evidence.Method);
        Assert.Equal("ToString", result.Evidence.Name.ToString());
        Assert.Equal(reader.GetMethodDefinition(method).Attributes,
            result.Evidence.Attributes);
        Assert.Equal(reader.GetMethodDefinition(method).ImplAttributes,
            result.Evidence.ImplementationAttributes);
        Assert.Empty(result.Evidence.Signature.ParameterTypes);
        Assert.True(MetadataMethodImplementationConsumerCanary.Consume(
            MetadataMethodImplementationConsumerCanary.PostMethod(
                path, result.Evidence.Type, result.Evidence.Method)));
    }

    [Fact]
    public void RealInt32ExplicitInterfaceOperatorBodyPreservesFalseCandidate()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = reader.TypeDefinitions.First(handle =>
        {
            TypeDefinition candidate = reader.GetTypeDefinition(handle);
            return reader.GetString(candidate.Name) == "Int32"
                && reader.GetString(candidate.Namespace) == "System";
        });
        MethodDefinitionHandle method =
            reader.GetTypeDefinition(type).GetMethods().First(handle =>
            {
                MethodDefinition candidate =
                    reader.GetMethodDefinition(handle);
                string name = reader.GetString(candidate.Name);
                return name.Contains(
                        "IAdditionOperators",
                        StringComparison.Ordinal)
                    && name.EndsWith(
                        ".op_Addition",
                        StringComparison.Ordinal);
            });

        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(path, MetadataTokens.GetToken(type), method));

        Assert.EndsWith(
            ".op_Addition",
            posted.Evidence.Name.ToString(),
            StringComparison.Ordinal);
        Assert.False(posted.Evidence.Attributes.HasFlag(
            MethodAttributes.SpecialName));
        Assert.False(posted.Evidence.OperatorCandidate);
    }

    [Theory]
    [InlineData("op_Addition", MethodAttributes.Public | MethodAttributes.Static,
        MetadataConstructorCandidate.None, false, false)]
    [InlineData("op_Addition", MethodAttributes.Public | MethodAttributes.Static
        | MethodAttributes.SpecialName, MetadataConstructorCandidate.None, true, false)]
    [InlineData(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
        MetadataConstructorCandidate.InstanceConstructorCandidate, false, false)]
    [InlineData(".cctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
        MetadataConstructorCandidate.StaticConstructorCandidate, false, false)]
    [InlineData(".ctor", MethodAttributes.SpecialName,
        MetadataConstructorCandidate.None, false, false)]
    [InlineData("Finalize", MethodAttributes.Virtual,
        MetadataConstructorCandidate.None, false, true)]
    public void CandidateFactsDoNotApplyCSharpPolicy(
        string name, MethodAttributes flags,
        MetadataConstructorCandidate constructor, bool op, bool finalizer)
    {
        using var fixture = Fixture.Create(name: name, flags: flags,
            signature: [0x20, 0x00, 0x01]);
        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        Assert.Equal(flags, posted.Evidence.Attributes);
        Assert.False(posted.Evidence.Attributes.HasFlag(MethodAttributes.HideBySig));
        Assert.Equal(constructor, posted.Evidence.ConstructorCandidate);
        Assert.Equal(op, posted.Evidence.OperatorCandidate);
        Assert.Equal(finalizer, posted.Evidence.FinalizerShapeCandidate);
        using var stream = File.OpenRead(fixture.Path);
        using var pe = new PEReader(stream);
        Assert.Equal(pe.GetMetadataReader().GetMethodDefinition(
            fixture.Method.Handle).RelativeVirtualAddress != 0,
            posted.Evidence.HasBodyRva);
    }

    [Fact]
    public void GenericContextsConstraintsAndMarkerCountsAreDetached()
    {
        using var fixture = Fixture.Create(generic: true,
            sequences: [1, 0], markerCount: 2);
        using (var source = File.OpenRead(fixture.Path))
        using (var pe = new PEReader(source))
        {
            MetadataReader reader = pe.GetMetadataReader();
            Assert.Single(reader.GetMethodDefinition(
                fixture.Method.Handle).GetGenericParameters());
            Assert.Single(reader.GetTypeDefinition(
                MetadataTokens.TypeDefinitionHandle(2)).GetGenericParameters());
        }
        MetadataMethodDeclarationResult.Posted posted;
        using (var assembly = AssemblyInspectionSession.Open(fixture.Path))
        using (var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded))
        using (var declarations = assembly.CreateDeclarationSession(operation))
        {
            MetadataMethodDeclarationResult result =
                declarations.PostMethodDeclaration(
                    fixture.Type, fixture.Method,
                    TestContext.Current.CancellationToken);
            Assert.True(result is MetadataMethodDeclarationResult.Posted,
                (result as MetadataMethodDeclarationResult.Rejected)?.Failure.ToString());
            posted = (MetadataMethodDeclarationResult.Posted)result;
        }
        var evidence = posted.Evidence;
        var typeParameter = Assert.Single(evidence.TypeParameters);
        var methodParameter = Assert.Single(evidence.MethodParameters);
        Assert.Equal("T", typeParameter.Name.ToString());
        Assert.Equal("U", methodParameter.Name.ToString());
        Assert.True(methodParameter.IsMethodParameter);
        Assert.Equal(0, methodParameter.Index);
        Assert.Equal(GenericParameterAttributes.ReferenceTypeConstraint,
            methodParameter.Attributes);
        Assert.Equal(2, methodParameter.Constraints.Length);
        var first = Assert.IsType<MetadataTypeIdentity.Named>(
            methodParameter.Constraints[0]);
        var second = Assert.IsType<MetadataTypeIdentity.Named>(
            methodParameter.Constraints[1]);
        Assert.NotEqual(first.Definition.Scope, second.Definition.Scope);
        Assert.Equal("IDisposable", first.Definition.Segments[0].ToString());
        Assert.Equal(2, methodParameter.IsUnmanaged.Count);
        Assert.True(methodParameter.IsUnmanaged.IsComplete);
        Assert.Equal(2, evidence.Parameters[0].Markers.IsReadOnlyCount);
        Assert.Equal(2, evidence.ReturnParameter.Markers.ScopedRefCount);
        Assert.Equal(1, evidence.Parameters[0].Markers.RequiresLocationCount);
        Assert.Equal(1, evidence.Parameters[0].Markers.ParamArrayCount);
        Assert.Equal(1, evidence.Parameters[0].Markers.ParamCollectionCount);
        Assert.Equal(1, evidence.Parameters[0].Markers.UnscopedRefCount);
        Assert.True(evidence.ReturnParameter.Markers.IsComplete);
        Assert.Equal("arg", evidence.Parameters[0].Name?.ToString());
        Assert.Equal("ret", evidence.ReturnParameter.Name?.ToString());
        Assert.IsType<MetadataTypeIdentity.GenericParameter>(
            evidence.Signature.ReturnType);
        Assert.IsType<MetadataTypeIdentity.GenericParameter>(
            Assert.Single(evidence.Signature.ParameterTypes));
    }

    [Fact]
    public void UnidentifiedAttributeProducesUnknownRatherThanAbsentMarkers()
    {
        using var fixture = Fixture.Create(generic: true, sequences: [1],
            markerCount: 1, unknownMarkers: true);
        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        Assert.False(posted.Evidence.Parameters[0].Markers.IsComplete);
        Assert.Equal(1, posted.Evidence.Parameters[0].Markers.IsReadOnlyCount);
        Assert.False(posted.Evidence.MethodParameters[0].IsUnmanaged.IsComplete);
        Assert.Equal(1, posted.Evidence.MethodParameters[0].IsUnmanaged.Count);
        Assert.True(posted.Evidence.ReturnParameter.Markers.IsComplete);
    }

    [Fact]
    public void NestedMarkerNameCannotAliasTopLevelMarker()
    {
        using var fixture = Fixture.Create(
            sequences: [1],
            nestedMarkerAlias: true);

        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));

        Assert.True(
            posted.Evidence.Parameters[0].Markers.IsComplete);
        Assert.Equal(
            0,
            posted.Evidence.Parameters[0].Markers.IsReadOnlyCount);
    }

    [Fact]
    public void MalformedMarkerNameReturnsTypedRejection()
    {
        using var fixture = Fixture.Create(
            sequences: [1, 0],
            markerCount: 1,
            corruptMarkerName: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationStage.MarkerRead,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodDeclarationMechanism
                .CustomAttributeIdentification,
            rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(new byte[] { 0x20 },
        MetadataMethodDeclarationFailureReason.MalformedMetadata)]
    [InlineData(new byte[] { 0x30, 0x01, 0x00, 0x01 },
        MetadataMethodDeclarationFailureReason.MalformedMetadata)]
    [InlineData(new byte[] { 0x28, 0x00, 0x01 },
        MetadataMethodDeclarationFailureReason.MalformedMetadata)]
    public void IncompleteSignatureOrGenericArityRejects(
        byte[] signature, MetadataMethodDeclarationFailureReason reason)
    {
        using var fixture = Fixture.Create(signature: signature);
        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));
        Assert.Equal(reason, rejected.Failure.Reason);
        Assert.Equal(MetadataMethodDeclarationStage.SignatureDecode,
            rejected.Failure.Stage);
    }

    [Fact]
    public void MalformedConstraintRejectsTheWholePost()
    {
        using var fixture = Fixture.Create(generic: true,
            malformedConstraint: true);
        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));
        Assert.Equal(MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(HandleKind.TypeSpecification,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void ConstraintTypeSpecificationConsumesSignatureBudget()
    {
        using var fixture = Fixture.Create(
            generic: true,
            typeSpecConstraint: true);
        var baseline = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        Assert.True(baseline.Counters.SignatureBytes > 7);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(
                fixture,
                new MetadataOperationPolicy(
                    long.MaxValue,
                    maxSignatureBytes: 7)));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.SignatureBytes,
            rejected.Failure.BudgetDimension);
        Assert.Equal(7, rejected.Counters.SignatureBytes);
    }

    [Fact]
    public void TypeConstraintCannotReferenceMethodGenericParameter()
    {
        using var fixture = Fixture.Create(
            generic: true,
            typeConstraintUsesMethodParameter: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationStage.GenericContextRead,
            rejected.Failure.Stage);
    }

    [Fact]
    public void GenericParameterEdgeBudgetFailsBeforeInvalidIndexRead()
    {
        using var fixture = Fixture.Create(
            generic: true,
            invalidGenericIndex: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(
                fixture,
                new MetadataOperationPolicy(
                    long.MaxValue,
                    maxRelationshipEdges: 1)));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            rejected.Failure.BudgetDimension);
        Assert.Equal(1, rejected.Counters.RelationshipEdges);
    }

    [Fact]
    public void InvalidConstraintCodedIndexReturnsTypedRejection()
    {
        using var fixture = Fixture.Create(
            generic: true,
            invalidConstraintCodedIndex: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationStage.GenericContextRead,
            rejected.Failure.Stage);
    }

    [Fact]
    public void UnorderedCustomAttributeOwnersRejectBeforeMarkerLookup()
    {
        using var fixture = Fixture.Create(
            generic: true,
            sequences: [1, 0],
            markerCount: 1,
            unsortedCustomAttributes: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationMechanism.HandleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(
            HandleKind.CustomAttribute,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void UnorderedConstraintOwnersRejectBeforeConstraintLookup()
    {
        using var fixture = Fixture.Create(
            generic: true,
            unsortedConstraints: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationMechanism.HandleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(
            HandleKind.GenericParameterConstraint,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void TypeNameSafetyBudgetIsTypedAsBudgetExceeded()
    {
        using var fixture = Fixture.Create(
            longReturnTypeName: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationStage.SignatureDecode,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodDeclarationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void VarargHeaderIsReportedWithoutLanguagePolicy()
    {
        using var fixture = Fixture.Create(signature: [0x25, 0x01, 0x01, 0x08]);
        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        Assert.Equal(0x25, posted.Evidence.Signature.Header);
        Assert.Single(posted.Evidence.Signature.ParameterTypes);
    }

    [Fact]
    public void SignaturePreservesRequiredModifierAndByReferenceShape()
    {
        using var fixture = Fixture.Create(modifiedSignature: true);
        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        var modifier = Assert.IsType<MetadataTypeIdentity.Modified>(
            posted.Evidence.Signature.ReturnType);
        Assert.True(modifier.IsRequired);
        Assert.IsType<MetadataTypeIdentity.ByReference>(modifier.Type);
        var name = Assert.IsType<MetadataTypeIdentity.Named>(modifier.Modifier);
        Assert.Equal("IsReadOnlyAttribute",
            Assert.Single(name.Definition.Segments).ToString());
    }

    [Fact]
    public void InvalidGenericIndexIsNotPublished()
    {
        using var fixture = Fixture.Create(generic: true,
            invalidGenericIndex: true);
        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));
        Assert.Equal(MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(MetadataMethodDeclarationStage.GenericContextRead,
            rejected.Failure.Stage);
    }

    [Fact]
    public void CancellationAtPublicationBoundaryThrowsInsteadOfRejecting()
    {
        using var fixture = Fixture.Create();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var cancellation = new CancellationTokenSource();
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            kind =>
            {
                if (kind == MetadataOperationWorkKind.MethodDeclarationPublication)
                    cancellation.Cancel();
            });
        using var session = assembly.CreateDeclarationSession(operation);
        Assert.Throws<OperationCanceledException>(() =>
            session.PostMethodDeclaration(fixture.Type, fixture.Method,
                cancellation.Token));
        Assert.Equal(1, operation.Counters.DeclarationCandidates);
    }

    [Fact]
    public void OperationLimitsAcceptExactCostsAndRejectFirstExcess()
    {
        using var fixture = Fixture.Create(sequences: [1, 0]);
        var baseline = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        MetadataOperationCounters counters = baseline.Counters;
        var atLimit = new MetadataOperationPolicy(long.MaxValue,
            maxDeclarationCandidates: 1,
            maxRelationshipEdges: counters.RelationshipEdges,
            maxSignatureBytes: counters.SignatureBytes,
            maxStructuredNodes: counters.StructuredNodes,
            maxRetainedText: counters.RetainedText);
        Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture, atLimit));
        var belowLimit = new MetadataOperationPolicy(long.MaxValue,
            maxSignatureBytes: counters.SignatureBytes - 1);
        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture, belowLimit));
        Assert.Equal(MetadataOperationDimension.SignatureBytes,
            rejected.Failure.BudgetDimension);
        Assert.Equal(counters.SignatureBytes - 1, rejected.Failure.BudgetLimit);
        Assert.Equal(0, rejected.Counters.SignatureBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new ushort[] { 1, 0 })]
    public void MissingAndReorderedParameterRowsAlignWithSignature(
        ushort[]? sequences)
    {
        using var fixture = Fixture.Create(sequences: sequences);
        var posted = Assert.IsType<MetadataMethodDeclarationResult.Posted>(
            Run(fixture));
        Assert.Equal(sequences is not null, posted.Evidence.ReturnParameter.HasRow);
        Assert.Equal(sequences is not null, posted.Evidence.Parameters[0].HasRow);
        Assert.True(posted.Evidence.Parameters[0].Markers.IsComplete);
    }

    [Theory]
    [InlineData(new ushort[] { 1, 1 })]
    [InlineData(new ushort[] { 2 })]
    public void DuplicateOrOutOfRangeParameterRowsRejectAtomically(
        ushort[] sequences)
    {
        using var fixture = Fixture.Create(sequences: sequences);
        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));
        Assert.Equal(MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(MetadataMethodDeclarationStage.ParameterCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(HandleKind.Parameter, rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void MalformedParameterRangeReturnsTypedRejection()
    {
        using var fixture = Fixture.Create(
            malformedParameterRange: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationStage.ParameterCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            HandleKind.Parameter,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void DecreasingParameterRangeReturnsTypedRejection()
    {
        using var fixture = Fixture.Create(
            decreasingParameterRange: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(fixture));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodDeclarationStage.ParameterCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            HandleKind.MethodDefinition,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void ParameterRangeBudgetIsChargedBeforeMaterialization()
    {
        using var fixture = Fixture.Create(
            sequences: [1, 0]);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(
                fixture,
                new MetadataOperationPolicy(
                    long.MaxValue,
                    maxRelationshipEdges: 1)));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            rejected.Failure.BudgetDimension);
        Assert.Equal(1, rejected.Counters.RelationshipEdges);
    }

    [Fact]
    public void AncestorGenericParameterEdgeBudgetPrecedesInvalidIndex()
    {
        using var fixture = Fixture.Create(
            generic: true,
            nestedOwner: true,
            invalidAncestorGenericIndex: true);

        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            Run(
                fixture,
                new MetadataOperationPolicy(
                    long.MaxValue,
                    maxRelationshipEdges: 2)));

        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            rejected.Failure.BudgetDimension);
        Assert.Equal(2, rejected.Counters.RelationshipEdges);
    }

    [Fact]
    public void InvalidAddressOwnerBudgetCancellationAndDisposalRemainDistinct()
    {
        using var fixture = Fixture.Create();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var context = new MetadataOperationContext(
            new MetadataOperationPolicy(long.MaxValue,
                maxDeclarationCandidates: 0));
        using var declarations = assembly.CreateDeclarationSession(context);
        var invalid = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            declarations.PostMethodDeclaration(default, fixture.Method,
                TestContext.Current.CancellationToken));
        Assert.Equal(MetadataMethodDeclarationFailureReason.InvalidRequest,
            invalid.Failure.Reason);
        Assert.Equal(0, invalid.Counters.DeclarationCandidates);
        var wrongOwner = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            declarations.PostMethodDeclaration(fixture.OtherType, fixture.Method,
                TestContext.Current.CancellationToken));
        Assert.Equal(MetadataMethodDeclarationFailureReason.BudgetExceeded,
            wrongOwner.Failure.Reason);
        Assert.Equal(MetadataOperationDimension.DeclarationCandidates,
            wrongOwner.Failure.BudgetDimension);
        Assert.Equal(0, wrongOwner.Failure.BudgetLimit);
        Assert.Equal(1, wrongOwner.Failure.AttemptedCharge);
        using var unbounded = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using var second = assembly.CreateDeclarationSession(unbounded);
        var mismatch = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            second.PostMethodDeclaration(fixture.OtherType, fixture.Method,
                TestContext.Current.CancellationToken));
        Assert.Equal(MetadataMethodDeclarationFailureReason.InvalidRequest,
            mismatch.Failure.Reason);
        Assert.Equal(MetadataMethodDeclarationMechanism.DirectOwnership,
            mismatch.Failure.Mechanism);
        var foreign = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            second.PostMethodDeclaration(
                new MetadataTypeDefinitionAddress(Guid.NewGuid(),
                    fixture.Type.Definition), fixture.Method,
                TestContext.Current.CancellationToken));
        Assert.Equal(MetadataMethodDeclarationFailureReason.InvalidRequest,
            foreign.Failure.Reason);
        var outOfRange = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            second.PostMethodDeclaration(fixture.Type,
                new MetadataMethodAddress(fixture.Method.ModuleVersionId,
                    MetadataTokens.MethodDefinitionHandle(1000)),
                TestContext.Current.CancellationToken));
        Assert.Equal(MetadataMethodDeclarationFailureReason.InvalidRequest,
            outOfRange.Failure.Reason);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            second.PostMethodDeclaration(fixture.Type, fixture.Method,
                cancelled.Token));
        second.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            second.PostMethodDeclaration(fixture.Type, fixture.Method,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ImageAdmissionAndOwnerLifetimesRemainVisible()
    {
        using var fixture = Fixture.Create();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var denied = new MetadataOperationContext(
            new MetadataOperationPolicy(maxMetadataRows: 0));
        using var session = assembly.CreateDeclarationSession(denied);
        var rejected = Assert.IsType<MetadataMethodDeclarationResult.Rejected>(
            session.PostMethodDeclaration(fixture.Type, fixture.Method,
                TestContext.Current.CancellationToken));
        Assert.Equal(MetadataMethodDeclarationMechanism.ImageAdmission,
            rejected.Failure.Mechanism);
        Assert.Equal(MetadataOperationDimension.MetadataRows,
            rejected.Failure.BudgetDimension);
        Assert.Equal(0, rejected.Counters.DeclarationCandidates);

        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using var live = assembly.CreateDeclarationSession(operation);
        operation.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            live.PostMethodDeclaration(fixture.Type, fixture.Method,
                TestContext.Current.CancellationToken));

        using var next = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using var dependent = assembly.CreateDeclarationSession(next);
        assembly.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            dependent.PostMethodDeclaration(fixture.Type, fixture.Method,
                TestContext.Current.CancellationToken));
    }

    static MetadataMethodDeclarationResult Run(Fixture fixture,
        MetadataOperationPolicy? policy = null) =>
        Run(fixture.Path, fixture.Type.Definition.Value,
            fixture.Method.Handle, policy);

    static MetadataMethodDeclarationResult Run(
        string path, int typeToken, MethodDefinitionHandle method,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using var session = assembly.CreateDeclarationSession(operation);
        MetadataReader reader = assembly.GetMetadataReaderForDeclarationSession();
        return session.PostMethodDeclaration(
            MetadataTypeDefinitionAddress.FromToken(
                MetadataModuleIdentity.ReadVersionId(reader), typeToken),
            MetadataMethodAddress.Create(reader, method),
            TestContext.Current.CancellationToken);
    }

    sealed class Fixture : IDisposable
    {
        Fixture(string path, MetadataTypeDefinitionAddress type,
            MetadataTypeDefinitionAddress otherType,
            MetadataMethodAddress method)
        {
            Path = path;
            Type = type;
            OtherType = otherType;
            Method = method;
        }

        internal string Path { get; }
        internal MetadataTypeDefinitionAddress Type { get; }
        internal MetadataTypeDefinitionAddress OtherType { get; }
        internal MetadataMethodAddress Method { get; }

        internal static Fixture Create(string name = "M",
            MethodAttributes flags = MethodAttributes.Public,
            byte[]? signature = null, bool generic = false,
            ushort[]? sequences = null, int markerCount = 0,
            bool unknownMarkers = false, bool malformedConstraint = false,
            bool invalidGenericIndex = false, bool modifiedSignature = false,
            bool typeSpecConstraint = false,
            bool typeConstraintUsesMethodParameter = false,
            bool invalidConstraintCodedIndex = false,
            bool malformedParameterRange = false,
            bool decreasingParameterRange = false,
            bool nestedOwner = false,
            bool invalidAncestorGenericIndex = false,
            bool unsortedCustomAttributes = false,
            bool unsortedConstraints = false,
            bool longReturnTypeName = false,
            bool nestedMarkerAlias = false,
            bool corruptMarkerName = false)
        {
            var metadata = new MetadataBuilder();
            Guid mvid = Guid.NewGuid();
            metadata.AddModule(0, metadata.GetOrAddString("method-post.dll"),
                metadata.GetOrAddGuid(mvid), default, default);
            metadata.AddAssembly(metadata.GetOrAddString("MethodPost"),
                new Version(1, 0), default, default, 0,
                AssemblyHashAlgorithm.None);
            if (longReturnTypeName)
            {
                AssemblyReferenceHandle scope =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("LongNames"),
                        new Version(1, 0),
                        default,
                        default,
                        0,
                        default);
                metadata.AddTypeReference(
                    scope,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString(new string('N', 5000)));
            }
            byte[] bytes = signature ?? (longReturnTypeName
                ? [0x20, 0x00, 0x12, 0x05]
                : generic
                ? [0x30, 0x01, 0x01, 0x13, 0x00, 0x1e, 0x00]
                : modifiedSignature
                    ? [0x20, 0x01, 0x1f, 0x05, 0x10, 0x08, 0x08]
                    : [0x20, 0x01, 0x01, 0x08]);
            var blob = new BlobBuilder();
            blob.WriteBytes(bytes);
            BlobHandle signatureHandle = metadata.GetOrAddBlob(blob);
            if (modifiedSignature)
            {
                AssemblyReferenceHandle scope = metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(1, 0), default, default, 0, default);
                metadata.AddTypeReference(scope,
                    metadata.GetOrAddString("System.Runtime.CompilerServices"),
                    metadata.GetOrAddString("IsReadOnlyAttribute"));
            }
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default,
                metadata.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            int firstParameterRow = decreasingParameterRange
                ? 3
                : malformedParameterRange
                    ? 2
                    : 1;
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                flags, MethodImplAttributes.IL, metadata.GetOrAddString(name),
                signatureHandle, 0,
                MetadataTokens.ParameterHandle(firstParameterRow));
            metadata.AddMethodDefinition(MethodAttributes.Public,
                MethodImplAttributes.IL, metadata.GetOrAddString("Other"),
                signatureHandle, 0,
                MetadataTokens.ParameterHandle(
                    decreasingParameterRange
                        ? 1
                        : malformedParameterRange
                        ? 3
                        : (sequences?.Length ?? 0) + 1));
            TypeDefinitionHandle outer = nestedOwner
                ? metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Outer"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    method)
                : default;
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                nestedOwner
                    ? TypeAttributes.NestedPublic
                    : TypeAttributes.Public,
                nestedOwner
                    ? default
                    : metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Owner"), default,
                MetadataTokens.FieldDefinitionHandle(1), method);
            TypeDefinitionHandle other = metadata.AddTypeDefinition(
                TypeAttributes.Public, metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Other"), default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
            if (nestedOwner)
                metadata.AddNestedType(type, outer);
            if (sequences is not null)
            {
                foreach (ushort sequence in sequences)
                    metadata.AddParameter(0,
                        metadata.GetOrAddString(sequence == 0 ? "ret" : "arg"),
                        sequence);
            }
            else if (malformedParameterRange || decreasingParameterRange)
            {
                metadata.AddParameter(
                    0,
                    metadata.GetOrAddString("arg"),
                    1);
            }
            if (generic)
            {
                GenericParameterHandle methodGeneric = metadata.AddGenericParameter(
                    method, GenericParameterAttributes.ReferenceTypeConstraint,
                    metadata.GetOrAddString("U"),
                    invalidGenericIndex ? 1 : 0);
                GenericParameterHandle typeGeneric =
                    metadata.AddGenericParameter(type, 0,
                    metadata.GetOrAddString("T"), 0);
                if (nestedOwner)
                {
                    metadata.AddGenericParameter(
                        outer,
                        0,
                        metadata.GetOrAddString("TOuter"),
                        invalidAncestorGenericIndex ? 1 : 0);
                }
                foreach (string assembly in new[] { "Alpha", "Beta" })
                {
                    AssemblyReferenceHandle scope = metadata.AddAssemblyReference(
                        metadata.GetOrAddString(assembly), new Version(1, 0),
                        default, default, 0, default);
                    TypeReferenceHandle constraint = metadata.AddTypeReference(
                        scope, metadata.GetOrAddString("System"),
                        metadata.GetOrAddString("IDisposable"));
                    metadata.AddGenericParameterConstraint(methodGeneric, constraint);
                }
                if (typeSpecConstraint)
                {
                    var typeParameter = new BlobBuilder();
                    typeParameter.WriteByte(0x13);
                    typeParameter.WriteCompressedInteger(0);
                    metadata.AddGenericParameterConstraint(
                        methodGeneric,
                        metadata.AddTypeSpecification(
                            metadata.GetOrAddBlob(typeParameter)));
                }
                if (typeConstraintUsesMethodParameter)
                {
                    var methodParameter = new BlobBuilder();
                    methodParameter.WriteByte(0x1e);
                    methodParameter.WriteCompressedInteger(0);
                    metadata.AddGenericParameterConstraint(
                        typeGeneric,
                        metadata.AddTypeSpecification(
                            metadata.GetOrAddBlob(methodParameter)));
                }
                if (malformedConstraint)
                {
                    var invalid = new BlobBuilder();
                    invalid.WriteByte(0x14);
                    metadata.AddGenericParameterConstraint(methodGeneric,
                        metadata.AddTypeSpecification(metadata.GetOrAddBlob(invalid)));
                }
            }
            if (markerCount != 0 && sequences is not null)
            {
                for (int i = 0; i < markerCount; i++)
                {
                    AddMarker(metadata, MetadataTokens.ParameterHandle(1),
                        KnownAttributeNames.IsReadOnlyAttribute);
                    AddMarker(metadata, MetadataTokens.ParameterHandle(2),
                        KnownAttributeNames.ScopedRefAttribute);
                }
                AddMarker(metadata, MetadataTokens.ParameterHandle(1),
                    KnownAttributeNames.RequiresLocationAttribute);
                AddMarker(metadata, MetadataTokens.ParameterHandle(1),
                    "System.ParamArrayAttribute");
                AddMarker(metadata, MetadataTokens.ParameterHandle(1),
                    KnownAttributeNames.ParamCollectionAttribute);
                AddMarker(metadata, MetadataTokens.ParameterHandle(1),
                    KnownAttributeNames.UnscopedRefAttribute);
                if (unknownMarkers)
                    AddUnknownMarker(metadata, MetadataTokens.ParameterHandle(1));
            }
            if (generic)
            {
                for (int i = 0; i < markerCount; i++)
                    AddMarker(metadata, MetadataTokens.GenericParameterHandle(1),
                        KnownAttributeNames.IsUnmanagedAttribute);
                if (unknownMarkers)
                    AddUnknownMarker(metadata,
                        MetadataTokens.GenericParameterHandle(1));
            }
            if (nestedMarkerAlias)
            {
                AddNestedMarkerAlias(
                    metadata,
                    MetadataTokens.ParameterHandle(1));
            }
            var image = new BlobBuilder();
            new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(metadata, suppressValidation: true),
                new BlobBuilder(), flags: CorFlags.ILOnly).Serialize(image);
            byte[] imageBytes = image.ToArray();
            if (invalidConstraintCodedIndex)
            {
                using var probe = new PEReader(
                    new MemoryStream(imageBytes, writable: false));
                MetadataReader probeReader = probe.GetMetadataReader();
                Assert.Equal(
                    4,
                    probeReader.GetTableRowSize(
                        TableIndex.GenericParamConstraint));
                int constraintOffset =
                    probe.PEHeaders.MetadataStartOffset
                    + probeReader.GetTableMetadataOffset(
                        TableIndex.GenericParamConstraint)
                    + sizeof(ushort);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    imageBytes.AsSpan(
                        constraintOffset,
                        sizeof(ushort)),
                    3);
            }
            if (unsortedCustomAttributes)
            {
                using var probe = new PEReader(
                    new MemoryStream(imageBytes, writable: false));
                MetadataReader probeReader = probe.GetMetadataReader();
                int rowCount = probeReader.GetTableRowCount(
                    TableIndex.CustomAttribute);
                Assert.True(rowCount >= 2);
                int rowSize = probeReader.GetTableRowSize(
                    TableIndex.CustomAttribute);
                int tableOffset =
                    probe.PEHeaders.MetadataStartOffset
                    + probeReader.GetTableMetadataOffset(
                        TableIndex.CustomAttribute);
                ushort firstParent =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        imageBytes.AsSpan(
                            tableOffset,
                            sizeof(ushort)));
                int lastOffset =
                    tableOffset + ((rowCount - 1) * rowSize);
                ushort lastParent =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        imageBytes.AsSpan(
                            lastOffset,
                            sizeof(ushort)));
                Assert.True(lastParent > firstParent);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    imageBytes.AsSpan(
                        lastOffset,
                        sizeof(ushort)),
                    firstParent);
            }
            if (unsortedConstraints)
            {
                using var probe = new PEReader(
                    new MemoryStream(imageBytes, writable: false));
                MetadataReader probeReader = probe.GetMetadataReader();
                int rowCount = probeReader.GetTableRowCount(
                    TableIndex.GenericParamConstraint);
                Assert.True(rowCount >= 2);
                int rowSize = probeReader.GetTableRowSize(
                    TableIndex.GenericParamConstraint);
                Assert.Equal(4, rowSize);
                int tableOffset =
                    probe.PEHeaders.MetadataStartOffset
                    + probeReader.GetTableMetadataOffset(
                        TableIndex.GenericParamConstraint);
                ushort owner =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        imageBytes.AsSpan(
                            tableOffset,
                            sizeof(ushort)));
                if (owner == 1)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        imageBytes.AsSpan(
                            tableOffset,
                            sizeof(ushort)),
                        2);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        imageBytes.AsSpan(
                            tableOffset + rowSize,
                            sizeof(ushort)),
                        1);
                }
            }
            if (corruptMarkerName)
            {
                using var probe = new PEReader(
                    new MemoryStream(imageBytes, writable: false));
                MetadataReader probeReader = probe.GetMetadataReader();
                Assert.True(
                    probeReader.GetTableRowCount(TableIndex.TypeRef) > 0);
                int rowOffset =
                    probe.PEHeaders.MetadataStartOffset
                    + probeReader.GetTableMetadataOffset(
                        TableIndex.TypeRef);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    imageBytes.AsSpan(
                        rowOffset + sizeof(ushort),
                        sizeof(ushort)),
                    ushort.MaxValue);
            }
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"method-post-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, imageBytes);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new(path, MetadataTypeDefinitionAddress.FromHandle(reader, type),
                MetadataTypeDefinitionAddress.FromHandle(reader, other),
                MetadataMethodAddress.Create(reader, method));
        }

        static void AddMarker(MetadataBuilder metadata, EntityHandle parent,
            string fullName)
        {
            int split = fullName.LastIndexOf('.');
            AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"), new Version(1, 0),
                default, default, 0, default);
            TypeReferenceHandle type = metadata.AddTypeReference(assembly,
                metadata.GetOrAddString(fullName[..split]),
                metadata.GetOrAddString(fullName[(split + 1)..]));
            var ctor = new BlobBuilder();
            ctor.WriteByte(0x20);
            ctor.WriteByte(0x00);
            ctor.WriteByte(0x01);
            MemberReferenceHandle member = metadata.AddMemberReference(type,
                metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(ctor));
            var value = new BlobBuilder();
            value.WriteByte(0x01);
            value.WriteByte(0x00);
            value.WriteByte(0x00);
            value.WriteByte(0x00);
            metadata.AddCustomAttribute(parent, member,
                metadata.GetOrAddBlob(value));
        }

        static void AddUnknownMarker(MetadataBuilder metadata,
            EntityHandle parent)
        {
            ModuleReferenceHandle module = metadata.AddModuleReference(
                metadata.GetOrAddString("unresolved.netmodule"));
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            signature.WriteByte(0);
            signature.WriteByte(1);
            MemberReferenceHandle member = metadata.AddMemberReference(module,
                metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(signature));
            var value = new BlobBuilder();
            value.WriteByte(1);
            value.WriteByte(0);
            value.WriteByte(0);
            value.WriteByte(0);
            metadata.AddCustomAttribute(parent, member, metadata.GetOrAddBlob(value));
        }

        static void AddNestedMarkerAlias(
            MetadataBuilder metadata,
            EntityHandle parent)
        {
            AssemblyReferenceHandle assembly =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(1, 0),
                    default,
                    default,
                    0,
                    default);
            TypeReferenceHandle outer = metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("System.Runtime"),
                metadata.GetOrAddString("CompilerServices"));
            TypeReferenceHandle nested = metadata.AddTypeReference(
                outer,
                default,
                metadata.GetOrAddString("IsReadOnlyAttribute"));
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            signature.WriteByte(0);
            signature.WriteByte(1);
            MemberReferenceHandle constructor =
                metadata.AddMemberReference(
                    nested,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(signature));
            var value = new BlobBuilder();
            value.WriteByte(1);
            value.WriteByte(0);
            value.WriteByte(0);
            value.WriteByte(0);
            metadata.AddCustomAttribute(
                parent,
                constructor,
                metadata.GetOrAddBlob(value));
        }

        public void Dispose() => File.Delete(Path);
    }
}
