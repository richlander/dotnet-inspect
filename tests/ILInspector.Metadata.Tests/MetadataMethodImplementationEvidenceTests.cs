using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using DotnetInspector.Fixtures;
using ILInspector.Metadata.ConsumerCanary;
using ILInspector.MetadataPrimitives;
using InertText;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataMethodImplementationEvidenceTests
{
    const string CycleWorkerVariable =
        "DOTNET_INSPECT_METHODIMPL_CYCLE_WORKER";
    const int LongDeclarationNameLength = 128;
    const int OverlongDeclarationNameLength =
        MetadataSafetyPolicy.MaxStructuralSignatureChars + 1;

    [Fact]
    public void Related_PreservesPhysicalOrderMultiplicityAndSpecialNameEvidence()
    {
        using Fixture fixture = Fixture.Create(Scenario.Standard);
        MetadataMethodImplementationResult result =
            Run(fixture, fixture.Body);
        var related =
            Assert.IsType<MetadataMethodImplementationResult.Related>(
                result);

        Assert.Equal(
            [2, 4, 5, 6, 7],
            related.Relationships
                .Select(certificate =>
                    MetadataTokens.GetRowNumber(
                        certificate.Relationship.Handle)));
        Assert.Equal(
            [
                MetadataSpecialNameEvidence.KnownTrue,
                MetadataSpecialNameEvidence.KnownFalse,
                MetadataSpecialNameEvidence.KnownTrue,
                MetadataSpecialNameEvidence.Unknown,
                MetadataSpecialNameEvidence.KnownFalse,
            ],
            related.Relationships.Select(
                certificate => certificate.SpecialName));
        Assert.IsType<
            MetadataDeclarationDefinitionDisposition.LocalResolved>(
                related.Relationships[0].Definition);
        Assert.IsType<
            MetadataDeclarationDefinitionDisposition.ExternalUnresolved>(
                related.Relationships[3].Definition);
        Assert.Equal(
            "op_Addition",
            related.Relationships[1].DeclarationName.ToString());
        Assert.Equal(
            related.Relationships[1].Declaration,
            related.Relationships[4].Declaration);
        Assert.NotEqual(
            related.Relationships[1].Relationship,
            related.Relationships[4].Relationship);
        Assert.Equal(
            8,
            related.Counters.MethodImplementationRows);
    }

    [Fact]
    public void ConstructedLocalTypeSpec_SubstitutesAndResolvesDefinition()
    {
        using Fixture fixture = Fixture.Create(Scenario.Standard);
        var related =
            AssertRelated(
                Run(fixture, fixture.BodyInt));
        MetadataMethodImplementationCertificate certificate =
            Assert.Single(related.Relationships);

        var owner = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                certificate.DeclarationOwner);
        Assert.Single(owner.Arguments);
        Assert.IsType<MetadataTypeIdentity.Primitive>(
            owner.Arguments[0]);
        var local = Assert.IsType<
            MetadataDeclarationDefinitionDisposition.LocalResolved>(
                certificate.Definition);
        Assert.Equal(
            fixture.GenericDeclaration,
            local.Definition.Handle);
        Assert.NotEqual(
            fixture.GenericIntDeclaration,
            local.Definition.Handle);
        Assert.Equal(
            MetadataSpecialNameEvidence.KnownTrue,
            certificate.SpecialName);
        Assert.True(
            related.Counters.GenericSubstitutionNodes > 0);
    }

    [Fact]
    public void MethodGenericParametersCorrespondByPosition()
    {
        using Fixture fixture = Fixture.Create(Scenario.MethodGeneric);
        var related =
            Assert.IsType<MetadataMethodImplementationResult.Related>(
                Run(fixture, fixture.BodyMethodGeneric));
        MetadataMethodImplementationCertificate certificate =
            Assert.Single(related.Relationships);

        Assert.Equal(
            1,
            certificate.DeclarationSignature.GenericParameterCount);
        var returnParameter = Assert.IsType<
            MetadataTypeIdentity.GenericParameter>(
                certificate.DeclarationSignature.ReturnType);
        Assert.True(returnParameter.IsMethodParameter);
        Assert.Equal(0, returnParameter.Index);
        var local = Assert.IsType<
            MetadataDeclarationDefinitionDisposition.LocalResolved>(
                certificate.Definition);
        Assert.Equal(
            fixture.MethodGenericDeclaration,
            local.Definition.Handle);
    }

    [Fact]
    public void CompilerProducedFunctionPointerUsesEnclosingMethodContext()
    {
        string path =
            FixtureCatalog.MetadataMethodImplFixtures.AssemblyPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();

        MetadataMethodImplementationCertificate certificate =
            Assert.Single(
                AssertRelated(
                    Run(
                        path,
                        FindType(
                            reader,
                            "FunctionPointerImplementation"),
                        FindExplicitMethod(
                            reader,
                            "FunctionPointerImplementation",
                            ".M")))
                    .Relationships);

        var pointer = Assert.IsType<
            MetadataTypeIdentity.FunctionPointer>(
                Assert.Single(
                    certificate.DeclarationSignature.ParameterTypes));
        var parameter = Assert.IsType<
            MetadataTypeIdentity.GenericParameter>(
                Assert.Single(pointer.Signature.ParameterTypes));
        Assert.True(parameter.IsMethodParameter);
        Assert.Equal(0, parameter.Index);
    }

    [Fact]
    public void Absent_RequiresCompletePhysicalScan()
    {
        using Fixture fixture = Fixture.Create(Scenario.Absent);
        var absent =
            Assert.IsType<MetadataMethodImplementationResult.Absent>(
                Run(fixture, fixture.Body));

        Assert.Equal(
            fixture.MethodImplementationCount,
            absent.Counters.MethodImplementationRows);
    }

    [Fact]
    public void InvalidRequestsRejectBeforeMethodImplementationCharge()
    {
        using Fixture fixture = Fixture.Create(Scenario.Standard);

        MetadataMethodImplementationResult foreign = Run(
            fixture,
            fixture.Body,
            typeOverride: new MetadataTypeDefinitionAddress(
                Guid.NewGuid(),
                fixture.TypeAddress.Definition));
        AssertInvalidBeforeScan(foreign);

        MetadataMethodImplementationResult invalidType = Run(
            fixture,
            fixture.Body,
            typeOverride: default(MetadataTypeDefinitionAddress));
        AssertInvalidBeforeScan(invalidType);

        MetadataMethodImplementationResult invalidBody = Run(
            fixture,
            fixture.Body,
            bodyOverride: new MetadataMethodAddress(
                    fixture.ModuleVersionId,
                    MetadataTokens.MethodDefinitionHandle(0xFFFF)));
        AssertInvalidBeforeScan(invalidBody);

        MetadataMethodImplementationResult foreignBody = Run(
            fixture,
            fixture.Body,
            bodyOverride: new MetadataMethodAddress(
                Guid.NewGuid(),
                fixture.Body));
        AssertInvalidBeforeScan(foreignBody);

        MetadataMethodImplementationResult wrongOwner =
            Run(fixture, fixture.LocalDeclaration);
        AssertInvalidBeforeScan(wrongOwner);
    }

    [Fact]
    public void RelevanceFirst_SeparatesUnreadableNeighbors()
    {
        using Fixture unreadableBody =
            Fixture.Create(Scenario.UnreadableBody);
        AssertRejected(
            Run(unreadableBody, unreadableBody.Body),
            MetadataMethodImplementationFailureReason.MalformedMetadata,
            expectedRow: 1);

        using Fixture unreadableUnrelatedDeclaration =
            Fixture.Create(Scenario.UnreadableUnrelatedDeclaration);
        Assert.IsType<MetadataMethodImplementationResult.Absent>(
            Run(
                unreadableUnrelatedDeclaration,
                unreadableUnrelatedDeclaration.Body));

        using Fixture unreadableRelevantDeclaration =
            Fixture.Create(Scenario.UnreadableRelevantDeclaration);
        AssertRejected(
            Run(
                unreadableRelevantDeclaration,
                unreadableRelevantDeclaration.Body),
            MetadataMethodImplementationFailureReason.MalformedMetadata,
            expectedRow: 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideMethodImplOperandsRejectInsideTypedReadBoundary(
        bool declarationOperand)
    {
        using Fixture fixture =
            Fixture.CreateWideMalformedMethodImpl(
                declarationOperand);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);

        Assert.Equal(
            declarationOperand
                ? MetadataMethodImplementationStage.DeclarationRead
                : MetadataMethodImplementationStage
                    .MethodImplementationScan,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.HandleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(
            declarationOperand ? 3 : 2,
            rejected.Counters.RelationshipEdges);
    }

    [Fact]
    public void WideMethodImplClassRejectsInsideTypedReadBoundary()
    {
        using Fixture fixture =
            Fixture.CreateWideMalformedMethodImplClass();

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.MethodImplementationScan,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.HandleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(1, rejected.Counters.RelationshipEdges);
    }

    [Theory]
    [InlineData(
        false,
        MetadataMethodImplementationStage.RequestValidation,
        MetadataMethodImplementationMechanism.DirectOwnership)]
    [InlineData(
        true,
        MetadataMethodImplementationStage.DeclarationRead,
        MetadataMethodImplementationMechanism.RowRead)]
    public void WideTypeDefinitionMethodListOwnershipIsTypedRejected(
        bool declarationOwnership,
        MetadataMethodImplementationStage expectedStage,
        MetadataMethodImplementationMechanism expectedMechanism)
    {
        using Fixture fixture =
            Fixture.CreateWideMalformedTypeDefinitionMethodList(
                declarationOwnership);
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using var declaration =
            assembly.CreateDeclarationSession(operation);
        Assert.IsType<MetadataImageAdmissionResult.Admitted>(
            declaration.ImageAdmission);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();

        var rejected =
            Assert.IsType<MetadataMethodImplementationResult.Rejected>(
                declaration.Relate(
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        fixture.TargetType),
                    MetadataMethodAddress.Create(
                        reader,
                        fixture.Body),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataMethodImplementationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(expectedStage, rejected.Failure.Stage);
        Assert.Equal(expectedMechanism, rejected.Failure.Mechanism);
        Assert.Equal(
            fixture.ExpectedFailureSubject,
            rejected.Failure.RelevantHandle);
        Assert.Equal(
            declarationOwnership
                ? MetadataTokens.MethodImplementationHandle(1)
                : null,
            rejected.Failure.RelevantRow);
    }

    [Fact]
    public void MalformedWideMethodDefSignatureHandleIsTypedRejected()
    {
        using Fixture fixture =
            Fixture.CreateMalformedWideMethodDefSignatureHandle();

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.SignatureDecode,
            rejected.Failure.Mechanism);
        Assert.Equal(
            (EntityHandle)fixture.Body,
            rejected.Failure.RelevantHandle);
    }

    [Theory]
    [InlineData(
        WideStringNameTarget.MethodDefinitionDeclaration,
        MetadataMethodImplementationStage.DeclarationRead)]
    [InlineData(
        WideStringNameTarget.MemberReferenceDeclaration,
        MetadataMethodImplementationStage.DeclarationRead)]
    [InlineData(
        WideStringNameTarget.LocalCandidate,
        MetadataMethodImplementationStage.LocalDeclarationResolution)]
    public void WideStringNameHandlesRejectInsideTypedReadBoundary(
        WideStringNameTarget target,
        MetadataMethodImplementationStage expectedStage)
    {
        using Fixture fixture =
            Fixture.CreateMalformedWideStringNameHandle(target);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);

        Assert.Equal(expectedStage, rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.RowRead,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                rejected.Failure.RelevantHandle));
    }

    [Fact]
    public void LocalResolutionAndSignatureFailuresAreTyped()
    {
        AssertScenarioRejected(
            Scenario.UnsupportedParent,
            MetadataMethodImplementationFailureReason.UnsupportedShape);
        AssertScenarioRejected(
            Scenario.LocalMissingDeclaration,
            MetadataMethodImplementationFailureReason.UnsupportedShape);
        AssertScenarioRejected(
            Scenario.LocalOwnerAmbiguity,
            MetadataMethodImplementationFailureReason.LocalOwnerAmbiguous);
        AssertScenarioRejected(
            Scenario.LocalDeclarationAmbiguity,
            MetadataMethodImplementationFailureReason
                .LocalDeclarationAmbiguous);
        AssertScenarioRejected(
            Scenario.SignatureMismatch,
            MetadataMethodImplementationFailureReason.SignatureMismatch);
        AssertScenarioRejected(
            Scenario.SignatureArrayShapeMismatch,
            MetadataMethodImplementationFailureReason.SignatureMismatch);
        AssertScenarioRejected(
            Scenario.SignaturePassingShapeMismatch,
            MetadataMethodImplementationFailureReason.SignatureMismatch);
        AssertScenarioRejected(
            Scenario.InheritedLocalDeclaration,
            MetadataMethodImplementationFailureReason.UnsupportedShape);
    }

    [Theory]
    [InlineData(Scenario.BodyTypeParameterOutOfRange)]
    [InlineData(Scenario.BodyMethodParameterOutOfRange)]
    [InlineData(Scenario.BodyMethodArityMismatch)]
    [InlineData(Scenario.DeclarationTypeParameterOutOfRange)]
    [InlineData(Scenario.DeclarationMethodArityMismatch)]
    [InlineData(Scenario.ConstructedOwnerArityMismatch)]
    [InlineData(Scenario.FunctionPointerMvarOutOfRange)]
    [InlineData(Scenario.NestedConstructedArityMismatch)]
    public void GenericContextsRejectUnauthenticatedIndicesAndArities(
        Scenario scenario)
    {
        using Fixture fixture = Fixture.Create(scenario);
        MethodDefinitionHandle body =
            scenario == Scenario.FunctionPointerMvarOutOfRange
                ? fixture.BodyMethodGeneric
                : fixture.Body;
        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);

        Assert.Equal(
            scenario is Scenario.ConstructedOwnerArityMismatch
                or Scenario.NestedConstructedArityMismatch
                ? MetadataMethodImplementationStage.OwnerAuthentication
                : MetadataMethodImplementationStage
                    .SignatureCorrespondence,
            rejected.Failure.Stage);
    }

    [Fact]
    public void NestedConstructedGenericArityAcceptsValidNeighbor()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.NestedConstructedArityValid);

        AssertRelated(Run(fixture, fixture.Body));
    }

    [Theory]
    [InlineData(
        Scenario.SignatureTypeSpecTrailingData,
        (int)MetadataMethodImplementationFailureReason.MalformedMetadata)]
    [InlineData(
        Scenario.SignatureTypeSpecNestedCycle,
        (int)MetadataMethodImplementationFailureReason.Cycle)]
    [InlineData(
        Scenario.SignatureTypeSpecDepthBudget,
        (int)MetadataMethodImplementationFailureReason.BudgetExceeded)]
    [InlineData(
        Scenario.SignatureTypeSpecMalformedDependency,
        (int)MetadataMethodImplementationFailureReason.MalformedMetadata)]
    public void SignatureReachedTypeSpecsPreserveTypedGraphFailures(
        Scenario scenario,
        int reasonValue)
    {
        using Fixture fixture = Fixture.Create(scenario);
        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                (MetadataMethodImplementationFailureReason)reasonValue,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TypeSpecificationRoot,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                rejected.Failure.RelevantHandle));
        if (scenario == Scenario.SignatureTypeSpecDepthBudget)
        {
            Assert.Contains(
                "structural depth budget",
                rejected.Failure.Detail);
        }
    }

    [Theory]
    [InlineData(
        Scenario.SignatureTypeSpecDependency,
        MetadataOperationDimension.SignatureBytes,
        12)]
    [InlineData(
        Scenario.SignatureTypeSpecDependency,
        MetadataOperationDimension.RelationshipEdges,
        5)]
    [InlineData(
        Scenario.OwnerTypeSpecDependency,
        MetadataOperationDimension.SignatureBytes,
        10)]
    [InlineData(
        Scenario.OwnerTypeSpecDependency,
        MetadataOperationDimension.RelationshipEdges,
        3)]
    public void TypeSpecDependencyBudgetNamesActiveDependency(
        Scenario scenario,
        MetadataOperationDimension dimension,
        long limit)
    {
        using Fixture fixture = Fixture.Create(scenario);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(dimension, limit)),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                rejected.Failure.RelevantHandle));
        Assert.Equal(dimension, rejected.Failure.BudgetDimension);
    }

    [Theory]
    [InlineData(Scenario.SignatureTypeSpecDependency)]
    [InlineData(Scenario.OwnerTypeSpecDependency)]
    public void TypeSpecStructuredBudgetNamesActiveDependency(
        Scenario scenario)
    {
        using Fixture fixture = Fixture.Create(scenario);
        MetadataOperationCounters baseline =
            Run(fixture, fixture.Body).Counters;
        MetadataMethodImplementationResult.Rejected? matching = null;

        for (long limit = 0;
            limit < baseline.StructuredNodes;
            limit++)
        {
            if (Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.StructuredNodes,
                        limit))
                is MetadataMethodImplementationResult.Rejected rejected
                && rejected.Failure.BudgetDimension
                    == MetadataOperationDimension.StructuredNodes
                && rejected.Failure.RelevantHandle
                    == fixture.ExpectedFailureSubject)
            {
                matching = rejected;
                break;
            }
        }

        Assert.NotNull(matching);
        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                matching.Failure.RelevantHandle));
    }

    [Theory]
    [InlineData(Scenario.MethodDefSignatureDepthBudget)]
    [InlineData(Scenario.MemberRefSignatureDepthBudget)]
    public void MethodSignaturesPreserveTypedStructuralDepthFailure(
        Scenario scenario)
    {
        using Fixture fixture = Fixture.Create(scenario);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.SignatureDecode,
            rejected.Failure.Mechanism);
        Assert.Equal(
            scenario == Scenario.MethodDefSignatureDepthBudget
                ? (EntityHandle)fixture.Body
                : fixture.ExpectedFailureSubject,
            rejected.Failure.RelevantHandle);
        Assert.Contains(
            "structural-depth budget",
            rejected.Failure.Detail);
    }

    [Fact]
    public void ReservedMemberRefParentIsTypedMalformedMetadata()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.ReservedMemberRefParent);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.DeclarationRead,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.RowRead,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                rejected.Failure.RelevantHandle));
    }

    [Theory]
    [InlineData(
        Scenario.LocalIndexCycle,
        (int)MetadataMethodImplementationFailureReason.Cycle)]
    [InlineData(
        Scenario.LocalIndexDepthBudget,
        (int)MetadataMethodImplementationFailureReason.BudgetExceeded)]
    [InlineData(
        Scenario.LocalIndexMalformed,
        (int)MetadataMethodImplementationFailureReason.MalformedMetadata)]
    [InlineData(
        Scenario.LocalIndexMalformedString,
        (int)MetadataMethodImplementationFailureReason.MalformedMetadata)]
    [InlineData(
        Scenario.LocalIndexMalformedWideString,
        (int)MetadataMethodImplementationFailureReason.MalformedMetadata)]
    [InlineData(
        Scenario.LocalIndexEmptyName,
        (int)MetadataMethodImplementationFailureReason.MalformedMetadata)]
    public void ColdTypeDefinitionIndexFailuresPreserveTypedSubject(
        Scenario scenario,
        int reasonValue)
    {
        using Fixture fixture = Fixture.Create(scenario);
        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                (MetadataMethodImplementationFailureReason)reasonValue,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.OwnerAuthentication,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TypeDefinitionIndex,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                rejected.Failure.RelevantHandle));
    }

    [Fact]
    public void ColdTypeDefinitionIndexOverlongReadableNameIsTypedBudgetFailure()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.LocalIndexOverlongName);
        using (var stream = File.OpenRead(fixture.Path))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            var subject =
                (TypeDefinitionHandle)fixture.ExpectedFailureSubject;
            Assert.Equal(
                new string(
                    'N',
                    MetadataSafetyPolicy.MaxTypeNameCharacters + 1),
                reader.GetString(
                    reader.GetTypeDefinition(subject).Name));
        }

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.OwnerAuthentication,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TypeDefinitionIndex,
            rejected.Failure.Mechanism);
        Assert.Equal(
            fixture.ExpectedFailureSubject,
            rejected.Failure.RelevantHandle);
    }

    [Fact]
    public void ColdTypeDefinitionIndexChargesForwardParentEdges()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.LocalIndexForwardParentChain);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RelationshipEdges,
                        11)),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.OwnerAuthentication,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TypeDefinitionIndex,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataTokens.GetToken(
                fixture.ExpectedFailureSubject),
            MetadataTokens.GetToken(
                rejected.Failure.RelevantHandle));
        Assert.Equal(11, rejected.Counters.RelationshipEdges);
    }

    [Fact]
    public void CyclicTypeSpec_IsReportedAsCycle()
    {
        if (Environment.GetEnvironmentVariable(CycleWorkerVariable)
            != nameof(CyclicTypeSpec_IsReportedAsCycle))
        {
            RunCycleWorker();
            return;
        }

        using Fixture fixture = Fixture.Create(Scenario.CyclicTypeSpec);
        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason.Cycle,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationMechanism.TypeSpecificationRoot,
            rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(
        Scenario.CyclicTypeReference,
        (int)MetadataMethodImplementationFailureReason.Cycle)]
    [InlineData(
        Scenario.TypeReferenceTraversalBudget,
        (int)MetadataMethodImplementationFailureReason.BudgetExceeded)]
    public void OwnerTraversalPreservesInheritedTypedFailures(
        Scenario scenario,
        int reasonValue)
    {
        var reason =
            (MetadataMethodImplementationFailureReason)reasonValue;
        using Fixture fixture = Fixture.Create(scenario);
        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                reason,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.OwnerAuthentication,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(
        Scenario.BodyContextCycle,
        (int)MetadataMethodImplementationFailureReason.Cycle)]
    [InlineData(
        Scenario.BodyContextDepthBudget,
        (int)MetadataMethodImplementationFailureReason.BudgetExceeded)]
    public void BodyGenericContextPreservesDeclaringChainFailure(
        Scenario scenario,
        int reasonValue)
    {
        using Fixture fixture = Fixture.Create(scenario);

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                (MetadataMethodImplementationFailureReason)reasonValue,
                expectedRow: 1);

        Assert.Equal(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.SignatureDecode,
            rejected.Failure.Mechanism);
        Assert.Equal(
            fixture.ExpectedFailureSubject,
            rejected.Failure.RelevantHandle);
        Assert.Null(rejected.Failure.BudgetDimension);

        MetadataMethodImplementationResult exact = Run(
            fixture,
            fixture.Body,
            Policy(
                MetadataOperationDimension.RelationshipEdges,
                rejected.Counters.RelationshipEdges));
        MetadataMethodImplementationResult.Rejected exactRejected =
            Assert.IsType<
                MetadataMethodImplementationResult.Rejected>(exact);
        Assert.Equal(
            (MetadataMethodImplementationFailureReason)reasonValue,
            exactRejected.Failure.Reason);
        Assert.Null(exactRejected.Failure.BudgetDimension);

        MetadataMethodImplementationResult.Rejected below =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RelationshipEdges,
                        rejected.Counters.RelationshipEdges - 1)),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            below.Failure.BudgetDimension);
        Assert.Equal(
            MetadataTokens.GetToken(
                scenario == Scenario.BodyContextCycle
                    ? fixture.TargetType
                    : MetadataTokens.TypeDefinitionHandle(
                        MetadataTokens.GetRowNumber(
                            (TypeDefinitionHandle)
                                fixture.ExpectedFailureSubject) - 1)),
            MetadataTokens.GetToken(
                below.Failure.RelevantHandle));
    }

    [Theory]
    [InlineData(
        Scenario.MethodDefDeclarationName,
        Scenario.LongMethodDefDeclarationName)]
    [InlineData(
        Scenario.ExternalMemberRefDeclarationName,
        Scenario.LongExternalMemberRefDeclarationName)]
    public void DeclarationNamesChargeHeapWorkAndRetainOnce(
        Scenario shortScenario,
        Scenario longScenario)
    {
        using Fixture shortFixture = Fixture.Create(shortScenario);
        using Fixture longFixture = Fixture.Create(longScenario);
        MetadataMethodImplementationResult.Related shortResult =
            AssertRelated(Run(shortFixture, shortFixture.Body));
        MetadataMethodImplementationResult.Related longResult =
            AssertRelated(Run(longFixture, longFixture.Body));
        int shortLength =
            Assert.Single(shortResult.Relationships)
                .DeclarationName.ToString().Length;
        int longLength =
            Assert.Single(longResult.Relationships)
                .DeclarationName.ToString().Length;
        int difference = longLength - shortLength;

        Assert.Equal(LongDeclarationNameLength, longLength);
        Assert.Equal(
            difference,
            longResult.Counters.RetainedText
                - shortResult.Counters.RetainedText);
        Assert.Equal(
            difference,
            longResult.Counters.StructuredNodes
                - shortResult.Counters.StructuredNodes);

        AssertRelated(
            Run(
                longFixture,
                longFixture.Body,
                Policy(
                    MetadataOperationDimension.RetainedText,
                    longResult.Counters.RetainedText)));
        AssertRejected(
            Run(
                longFixture,
                longFixture.Body,
                Policy(
                    MetadataOperationDimension.RetainedText,
                    longResult.Counters.RetainedText - 1)),
            MetadataMethodImplementationFailureReason.BudgetExceeded,
            expectedRow: 1);
    }

    [Theory]
    [InlineData(
        Scenario.OverlongMethodDefDeclarationName,
        HandleKind.MethodDefinition)]
    [InlineData(
        Scenario.OverlongExternalMemberRefDeclarationName,
        HandleKind.MemberReference)]
    public void PublicRelate_OverlongReadableDeclarationNameIsTypedBudgetFailure(
        Scenario scenario,
        HandleKind expectedKind)
    {
        using Fixture fixture = Fixture.Create(scenario);
        var work = new List<MetadataOperationWorkKind>();

        MetadataMethodImplementationResult Relate(
            MetadataOperationPolicy policy,
            Action<MetadataOperationWorkKind>? observer = null)
        {
            using var assembly =
                AssemblyInspectionSession.Open(fixture.Path);
            using var operation =
                new MetadataOperationContext(policy, observer);
            using var declaration =
                assembly.CreateDeclarationSession(operation);
            MetadataReader reader =
                assembly.GetMetadataReaderForDeclarationSession();
            MethodImplementation implementation =
                reader.GetMethodImplementation(
                    MetadataTokens.MethodImplementationHandle(1));
            EntityHandle subject = implementation.MethodDeclaration;
            Assert.Equal(expectedKind, subject.Kind);
            StringHandle nameHandle = subject.Kind switch
            {
                HandleKind.MethodDefinition =>
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)subject).Name,
                HandleKind.MemberReference =>
                    reader.GetMemberReference(
                        (MemberReferenceHandle)subject).Name,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(
                OverlongDeclarationNameLength,
                reader.GetString(nameHandle).Length);

            MetadataMethodImplementationResult result =
                declaration.Relate(
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        fixture.TargetType),
                    MetadataMethodAddress.Create(
                        reader,
                        fixture.Body),
                    TestContext.Current.CancellationToken);
            MetadataMethodImplementationResult.Rejected rejected =
                AssertRejected(
                    result,
                    MetadataMethodImplementationFailureReason
                        .BudgetExceeded,
                    expectedRow: 1);
            Assert.Equal(
                MetadataMethodImplementationStage.DeclarationRead,
                rejected.Failure.Stage);
            Assert.Equal(subject, rejected.Failure.RelevantHandle);
            return rejected;
        }

        MetadataMethodImplementationResult.Rejected structural =
            Assert.IsType<MetadataMethodImplementationResult.Rejected>(
                Relate(
                    MetadataOperationPolicy.Unbounded,
                    work.Add));
        Assert.Equal(
            MetadataMethodImplementationMechanism.TextRetention,
            structural.Failure.Mechanism);
        Assert.Null(structural.Failure.BudgetDimension);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.DeclarationNameMaterialization,
            work);

        long retainedBeforeName = structural.Counters.RetainedText;
        MetadataMethodImplementationResult.Rejected retained =
            Assert.IsType<MetadataMethodImplementationResult.Rejected>(
                Relate(
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        retainedBeforeName
                            + OverlongDeclarationNameLength
                            - 1)));
        Assert.Equal(
            MetadataMethodImplementationMechanism.RowRead,
            retained.Failure.Mechanism);
        Assert.Equal(
            MetadataOperationDimension.RetainedText,
            retained.Failure.BudgetDimension);
        Assert.Equal(
            OverlongDeclarationNameLength,
            retained.Failure.AttemptedCharge);
        Assert.Equal(
            retainedBeforeName,
            retained.Counters.RetainedText);
    }

    [Fact]
    public void PublicRelate_OverlongReadableGenericParameterNameIsTypedBudgetFailure()
    {
        using Fixture shortFixture =
            Fixture.CreateExternalGenericParameterName("T");
        using Fixture overlongFixture =
            Fixture.CreateExternalGenericParameterName(
                new string(
                    'T',
                    MetadataSafetyPolicy.MaxStructuralSignatureChars
                        + 1));

        MetadataMethodImplementationResult Relate(
            Fixture fixture,
            int expectedNameLength)
        {
            using var assembly =
                AssemblyInspectionSession.Open(fixture.Path);
            using var operation =
                new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded);
            using var declaration =
                assembly.CreateDeclarationSession(operation);
            Assert.IsType<MetadataImageAdmissionResult.Admitted>(
                declaration.ImageAdmission);
            MetadataReader reader =
                assembly.GetMetadataReaderForDeclarationSession();
            MethodImplementation implementation =
                reader.GetMethodImplementation(
                    MetadataTokens.MethodImplementationHandle(1));
            GenericParameterHandle parameter =
                reader.GetMethodDefinition(fixture.Body)
                    .GetGenericParameters()
                    .Single();
            Assert.Equal(
                expectedNameLength,
                reader.GetString(
                    reader.GetGenericParameter(parameter).Name)
                    .Length);
            Assert.Equal(
                HandleKind.MemberReference,
                implementation.MethodDeclaration.Kind);
            var memberReference =
                (MemberReferenceHandle)
                    implementation.MethodDeclaration;
            Assert.Equal(
                reader.GetBlobBytes(
                    reader.GetMethodDefinition(
                        fixture.Body).Signature),
                reader.GetBlobBytes(
                    reader.GetMemberReference(
                        memberReference).Signature));

            return declaration.Relate(
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    fixture.TargetType),
                MetadataMethodAddress.Create(
                    reader,
                    fixture.Body),
                TestContext.Current.CancellationToken);
        }

        AssertRelated(Relate(shortFixture, expectedNameLength: 1));

        MetadataMethodImplementationResult.Rejected rejected =
            Assert.IsType<MetadataMethodImplementationResult.Rejected>(
                Relate(
                    overlongFixture,
                    MetadataSafetyPolicy.MaxStructuralSignatureChars
                        + 1));
        Assert.Equal(
            MetadataMethodImplementationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.SignatureDecode,
            rejected.Failure.Mechanism);
        Assert.Equal(
            (EntityHandle)overlongFixture.Body,
            rejected.Failure.RelevantHandle);
        Assert.Equal(
            MetadataTokens.MethodImplementationHandle(1),
            rejected.Failure.RelevantRow);
        Assert.Null(rejected.Failure.BudgetDimension);
    }

    [Fact]
    public void UnresolvedLocalDeclarationNamePreflightsBeforeAllocation()
    {
        using Fixture fixture =
            Fixture.Create(
                Scenario.LongUnresolvedLocalDeclarationName);
        const long ExactNamePreflightLimit = 169;

        var exactWork = new List<MetadataOperationWorkKind>();
        MetadataMethodImplementationResult.Rejected exact =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        ExactNamePreflightLimit),
                    workObserver: exactWork.Add),
                MetadataMethodImplementationFailureReason.UnsupportedShape,
                expectedRow: 1);
        Assert.Contains(
            MetadataOperationWorkKind.DeclarationNameMaterialization,
            exactWork);
        Assert.Equal(41, exact.Counters.RetainedText);

        var overWork = new List<MetadataOperationWorkKind>();
        MetadataMethodImplementationResult.Rejected over =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        ExactNamePreflightLimit - 1),
                    workObserver: overWork.Add),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.DeclarationNameMaterialization,
            overWork);
        Assert.Equal(
            LongDeclarationNameLength,
            over.Failure.AttemptedCharge);
    }

    [Fact]
    public void ExpandingRetainedTextUsesEncodedLengthAtExactBoundary()
    {
        using Fixture fixture =
            Fixture.Create(
                Scenario.ExpandingControlReturnTypeNamespace);
        const string EncodedName = @"A\u202EB";
        const long ExpectedRetainedText = 183;

        MetadataMethodImplementationResult.Related baseline =
            AssertRelated(
                Run(fixture, fixture.Body));
        MetadataMethodImplementationCertificate baselineCertificate =
            Assert.Single(baseline.Relationships);
        var returnType = Assert.IsType<MetadataTypeIdentity.Named>(
            baselineCertificate.DeclarationSignature.ReturnType);
        InertString retainedName =
            returnType.Definition.Namespace;

        Assert.Equal(
            EncodedName,
            retainedName.ToString());
        Assert.Equal(
            EncodedName.Length,
            retainedName.Length);
        Assert.Equal(
            ExpectedRetainedText,
            baseline.Counters.RetainedText);

        MetadataMethodImplementationResult.Rejected below =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        ExpectedRetainedText - 1)),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(
            MetadataMethodImplementationStage.ResultRetention,
            below.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TextRetention,
            below.Failure.Mechanism);
        Assert.Equal(
            (EntityHandle)fixture.LocalDeclaration,
            below.Failure.RelevantHandle);
        Assert.Equal(
            MetadataOperationDimension.RetainedText,
            below.Failure.BudgetDimension);
        Assert.Equal(
            EncodedName.Length,
            below.Failure.AttemptedCharge);
        Assert.Equal(
            ExpectedRetainedText - EncodedName.Length,
            below.Counters.RetainedText);

        MetadataMethodImplementationResult.Related exact =
            AssertRelated(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        ExpectedRetainedText)));
        MetadataMethodImplementationResult.Related above =
            AssertRelated(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        ExpectedRetainedText + 1)));

        Assert.Equal(
            ExpectedRetainedText,
            exact.Counters.RetainedText);
        Assert.Equal(
            ExpectedRetainedText,
            above.Counters.RetainedText);
        Assert.Equal(
            EncodedName.Length,
            Assert.IsType<MetadataTypeIdentity.Named>(
                    Assert.Single(exact.Relationships)
                        .DeclarationSignature.ReturnType)
                .Definition.Namespace.Length);
        Assert.Equal(
            EncodedName.Length,
            Assert.IsType<MetadataTypeIdentity.Named>(
                    Assert.Single(above.Relationships)
                        .DeclarationSignature.ReturnType)
                .Definition.Namespace.Length);
    }

    [Fact]
    public void RejectionAfterPendingRelationshipPublishesNoPartialResult()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.PendingThenRejected);
        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(fixture, fixture.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 2);

        Assert.Equal(2, rejected.Counters.MethodImplementationRows);
        Assert.DoesNotContain(
            typeof(MetadataMethodImplementationResult.Rejected)
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
            property =>
                property.PropertyType
                    == typeof(
                        ImmutableArray<
                            MetadataMethodImplementationCertificate>));
    }

    [Fact]
    public void FailuresRetainExactStageMechanismAndSubject()
    {
        using Fixture declaration =
            Fixture.Create(Scenario.UnreadableRelevantDeclaration);
        MetadataMethodImplementationResult.Rejected declarationFailure =
            AssertRejected(
                Run(declaration, declaration.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);
        Assert.Equal(
            MetadataMethodImplementationStage.DeclarationRead,
            declarationFailure.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.HandleValidation,
            declarationFailure.Failure.Mechanism);
        Assert.Equal(
            HandleKind.MemberReference,
            declarationFailure.Failure.RelevantHandle.Kind);

        using Fixture owner =
            Fixture.Create(Scenario.ConstructedOwnerArityMismatch);
        MetadataMethodImplementationResult.Rejected ownerFailure =
            AssertRejected(
                Run(owner, owner.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);
        Assert.Equal(
            MetadataMethodImplementationStage.OwnerAuthentication,
            ownerFailure.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TypeSpecificationRoot,
            ownerFailure.Failure.Mechanism);
        Assert.Equal(
            HandleKind.TypeSpecification,
            ownerFailure.Failure.RelevantHandle.Kind);

        using Fixture signature =
            Fixture.Create(Scenario.BodyTypeParameterOutOfRange);
        MetadataMethodImplementationResult.Rejected signatureFailure =
            AssertRejected(
                Run(signature, signature.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);
        Assert.Equal(
            MetadataMethodImplementationStage.SignatureCorrespondence,
            signatureFailure.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.SignatureDecode,
            signatureFailure.Failure.Mechanism);
        Assert.Equal(
            (EntityHandle)signature.Body,
            signatureFailure.Failure.RelevantHandle);

        using Fixture retention = Fixture.Create(Scenario.Standard);
        MetadataMethodImplementationResult.Rejected retentionFailure =
            AssertRejected(
                Run(
                    retention,
                    retention.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        70)),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 2);
        Assert.Equal(
            MetadataMethodImplementationStage.ResultRetention,
            retentionFailure.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.TextRetention,
            retentionFailure.Failure.Mechanism);

        using Fixture candidate =
            Fixture.Create(Scenario.LocalCandidateMalformedSignature);
        MetadataMethodImplementationResult.Rejected candidateFailure =
            AssertRejected(
                Run(candidate, candidate.Body),
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                expectedRow: 1);
        Assert.Equal(
            MetadataMethodImplementationStage.LocalDeclarationResolution,
            candidateFailure.Failure.Stage);
        Assert.Equal(
            MetadataMethodImplementationMechanism.SignatureDecode,
            candidateFailure.Failure.Mechanism);
        Assert.Equal(
            HandleKind.MethodDefinition,
            candidateFailure.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void SameSpelledGenericArgumentsFromDifferentAssembliesStayDistinct()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.DistinctExternalArguments);
        var related =
            Assert.IsType<MetadataMethodImplementationResult.Related>(
                Run(fixture, fixture.Body));
        Assert.Equal(2, related.Relationships.Length);

        var first = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                related.Relationships[0].DeclarationOwner);
        var second = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                related.Relationships[1].DeclarationOwner);
        var firstArgument = Assert.IsType<
            MetadataTypeIdentity.Named>(Assert.Single(first.Arguments));
        var secondArgument = Assert.IsType<
            MetadataTypeIdentity.Named>(Assert.Single(second.Arguments));

        Assert.Equal(
            firstArgument.Definition.Namespace,
            secondArgument.Definition.Namespace);
        Assert.Equal(
            firstArgument.Definition.Segments,
            secondArgument.Definition.Segments);
        Assert.NotEqual(
            firstArgument.Definition.Scope.Assembly!.Name,
            secondArgument.Definition.Scope.Assembly!.Name);
    }

    [Fact]
    public void CompilerProducedOwnersPreserveOpenConstructedAndNestedContexts()
    {
        string path =
            FixtureCatalog.MetadataMethodImplFixtures.AssemblyPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();

        MetadataMethodImplementationCertificate open =
            Assert.Single(
                AssertRelated(
                    Run(
                        path,
                        FindType(reader, "OpenImplementation`1"),
                        FindExplicitMethod(
                            reader,
                            "OpenImplementation`1",
                            ".Echo")))
                    .Relationships);
        var openOwner = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                open.DeclarationOwner);
        Assert.Equal([1], openOwner.Definition.IntroducedGenericParameterCounts);
        Assert.Equal(
            new MetadataTypeIdentity.GenericParameter(
                IsMethodParameter: false,
                Index: 0),
            Assert.Single(openOwner.Arguments));

        MetadataMethodImplementationCertificate constructed =
            Assert.Single(
                AssertRelated(
                    Run(
                        path,
                        FindType(reader, "ConstructedImplementation`1"),
                        FindExplicitMethod(
                            reader,
                            "ConstructedImplementation`1",
                            ".Echo")))
                    .Relationships);
        var constructedOwner = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                constructed.DeclarationOwner);
        var list = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                Assert.Single(constructedOwner.Arguments));
        Assert.Equal(
            new MetadataTypeIdentity.GenericParameter(
                IsMethodParameter: false,
                Index: 0),
            Assert.Single(list.Arguments));

        MetadataMethodImplementationCertificate nested =
            Assert.Single(
                AssertRelated(
                    Run(
                        path,
                        FindType(reader, "NestedImplementation`2"),
                        FindExplicitMethod(
                            reader,
                            "NestedImplementation`2",
                            ".Convert")))
                    .Relationships);
        var nestedOwner = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                nested.DeclarationOwner);
        Assert.Equal(
            ["IOuter`1", "IInner`1"],
            nestedOwner.Definition.Segments.Select(
                segment => segment.ToString()));
        Assert.Equal(
            [1, 1],
            nestedOwner.Definition.IntroducedGenericParameterCounts);
        Assert.Equal(2, nestedOwner.Arguments.Length);
    }

    [Fact]
    public void IndependentlyCompiledExternalMemberRefRetainsUnknownSpecialName()
    {
        const string ImplementationType = "ExternalImplementation";
        string path =
            FixtureCatalog.MetadataMethodImplFixtures.AssemblyPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();

        MetadataMethodImplementationCertificate certificate =
            Assert.Single(
                AssertRelated(
                    Run(
                        path,
                        FindType(reader, ImplementationType),
                        FindExplicitMethod(
                            reader,
                            ImplementationType,
                            ".External")))
                    .Relationships);

        Assert.Equal(
            MetadataSpecialNameEvidence.Unknown,
            certificate.SpecialName);
        Assert.IsType<
            MetadataDeclarationDefinitionDisposition.ExternalUnresolved>(
                certificate.Definition);
    }

    [Fact]
    public void OverlongExternalOwnerNameIsTypedBudgetExceeded()
    {
        using Fixture fixture =
                Fixture.Create(Scenario.OverlongExternalTypeName);

        MetadataMethodImplementationResult.Rejected rejected =
                AssertRejected(
                    Run(fixture, fixture.Body),
                    MetadataMethodImplementationFailureReason.BudgetExceeded,
                    expectedRow: 1);

        Assert.Equal(
                MetadataMethodImplementationStage.OwnerAuthentication,
                rejected.Failure.Stage);
        Assert.Equal(
                MetadataMethodImplementationMechanism.RelationshipTraversal,
                rejected.Failure.Mechanism);
        Assert.Equal(
                fixture.ExpectedFailureSubject,
                rejected.Failure.RelevantHandle);
    }

    [Fact]
    public void PinnedSystemInt32GenericMathCanaryResolvesKnownSpecialName()
    {
        const string RuntimeCommit =
            "81be0823c7162a79bcc8bde49763293c92567e9e";
        const string RuntimeBuild =
            "11.0.100-rc.1.26425.128";
        const string Sha256 =
            "9573ebabb9af0671f76f4aa958223b8a0b50c299affcb1a2f75c9ee717305fc8";
        Assert.Equal(40, RuntimeCommit.Length);
        Assert.StartsWith("11.0.100-rc.1", RuntimeBuild);

        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        Assert.True(
            File.Exists(path),
            $"Pinned runtime artifact missing: {path}");
        string actualSha256 = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(Sha256, actualSha256);
        string productVersion =
            FileVersionInfo.GetVersionInfo(path).ProductVersion
            ?? "";
        Assert.Contains(
            "11.0.0-rc.1.26425.128",
            productVersion);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = FindType(reader, "Int32", "System");
        MethodDefinitionHandle body =
            reader.GetTypeDefinition(type).GetMethods()
                .Single(handle =>
                {
                    string name = reader.GetString(
                        reader.GetMethodDefinition(handle).Name);
                    return name.Contains(
                            "IAdditionOperators",
                            StringComparison.Ordinal)
                        && name.EndsWith(
                            ".op_Addition",
                            StringComparison.Ordinal);
                });

        MetadataMethodImplementationCertificate certificate =
            Assert.Single(
                AssertRelated(Run(path, type, body)).Relationships);

        Assert.Equal(
            MetadataSpecialNameEvidence.KnownTrue,
            certificate.SpecialName);
        Assert.IsType<
            MetadataDeclarationDefinitionDisposition.LocalResolved>(
                certificate.Definition);
        Assert.Equal("op_Addition", certificate.DeclarationName.ToString());
    }

    [Fact]
    public void DetachedIdentityEqualityIsElementwise()
    {
        using Fixture firstFixture = Fixture.Create(Scenario.GenericOnly);

        MetadataMethodImplementationCertificate first =
            Assert.Single(
                AssertRelated(
                    Run(firstFixture, firstFixture.BodyInt))
                    .Relationships);
        MetadataMethodImplementationCertificate second =
            Assert.Single(
                AssertRelated(
                    Run(firstFixture, firstFixture.BodyInt))
                    .Relationships);

        Assert.NotSame(first.DeclarationOwner, second.DeclarationOwner);
        Assert.Equal(first.DeclarationOwner, second.DeclarationOwner);
        Assert.Equal(
            first.DeclarationOwner.GetHashCode(),
            second.DeclarationOwner.GetHashCode());
        Assert.NotSame(
            first.DeclarationSignature,
            second.DeclarationSignature);
        Assert.Equal(
            first.DeclarationSignature,
            second.DeclarationSignature);
        Assert.Equal(
            first.DeclarationSignature.GetHashCode(),
            second.DeclarationSignature.GetHashCode());

        MetadataTypeIdentity element =
            new MetadataTypeIdentity.Primitive(
                new InertString(TextPolicy.Field, "int"));
        MetadataTypeIdentity.Array firstArray =
            new(
                element,
                Rank: 2,
                Sizes: [3, 5],
                LowerBounds: [0, 1]);
        MetadataTypeIdentity.Array secondArray =
            new(
                new MetadataTypeIdentity.Primitive(
                    new InertString(TextPolicy.Field, "int")),
                Rank: 2,
                Sizes: ImmutableArray.Create(3, 5),
                LowerBounds: ImmutableArray.Create(0, 1));
        Assert.Equal(firstArray, secondArray);
        Assert.Equal(
            firstArray.GetHashCode(),
            secondArray.GetHashCode());

        MetadataMethodSignatureIdentity firstSignature =
            new(
                Header: 0x20,
                GenericParameterCount: 0,
                RequiredParameterCount: 1,
                ReturnType: element,
                ParameterTypes: [firstArray]);
        MetadataMethodSignatureIdentity secondSignature =
            new(
                Header: 0x20,
                GenericParameterCount: 0,
                RequiredParameterCount: 1,
                ReturnType:
                    new MetadataTypeIdentity.Primitive(
                        new InertString(
                            TextPolicy.Field,
                            "int")),
                ParameterTypes:
                    ImmutableArray.Create<MetadataTypeIdentity>(
                        secondArray));
        Assert.Equal(firstSignature, secondSignature);
        Assert.Equal(
            firstSignature.GetHashCode(),
            secondSignature.GetHashCode());
    }

    [Fact]
    public void ResultIsDetachedAndCancellationPreservesCallerToken()
    {
        using Fixture fixture = Fixture.Create(Scenario.Standard);
        MetadataMethodImplementationResult.Related related;
        using (var assembly =
            AssemblyInspectionSession.Open(fixture.Path))
        using (var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded))
        {
            MetadataReader reader =
                assembly.GetMetadataReaderForDeclarationSession();
            var declaration =
                assembly.CreateDeclarationSession(operation);
            related =
                Assert.IsType<
                    MetadataMethodImplementationResult.Related>(
                        declaration.Relate(
                            MetadataTypeDefinitionAddress.FromHandle(
                                reader,
                                fixture.TargetType),
                            MetadataMethodAddress.Create(
                                reader,
                                fixture.Body),
                            TestContext.Current.CancellationToken));
            declaration.Dispose();
        }

        Assert.Equal(
            "Special",
            related.Relationships[0].DeclarationName.ToString());
        Assert.NotNull(
            related.Relationships[0].DeclarationSignature.ReturnType);

        using var source = new CancellationTokenSource();
        source.Cancel();
        OperationCanceledException cancelled =
            Assert.Throws<OperationCanceledException>(
                () => RunCancelled(
                    fixture,
                    fixture.Body,
                    source.Token));
        Assert.Equal(source.Token, cancelled.CancellationToken);
    }

    [Fact]
    public void CancellationPrecedesCachedImageAdmissionRejection()
    {
        using Fixture fixture = Fixture.Create(Scenario.Standard);
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation =
            new MetadataOperationContext(
                new MetadataOperationPolicy(maxMetadataRows: 0));
        using var declaration =
            assembly.CreateDeclarationSession(operation);
        Assert.IsType<MetadataImageAdmissionResult.Rejected>(
            declaration.ImageAdmission);
        using var source = new CancellationTokenSource();
        source.Cancel();
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();

        OperationCanceledException cancelled =
            Assert.Throws<OperationCanceledException>(
                () => declaration.Relate(
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        fixture.TargetType),
                    MetadataMethodAddress.Create(
                        reader,
                        fixture.Body),
                    source.Token));

        Assert.Equal(source.Token, cancelled.CancellationToken);
    }

    static MetadataMethodImplementationResult Run(
        Fixture fixture,
        MethodDefinitionHandle body,
        MetadataOperationPolicy? policy = null,
        MetadataTypeDefinitionAddress? typeOverride = null,
        MetadataMethodAddress? bodyOverride = null,
        Action<MetadataOperationWorkKind>? workObserver = null) =>
        RunCore(
            fixture,
            body,
            policy,
            typeOverride,
            bodyOverride,
            TestContext.Current.CancellationToken,
            workObserver);

    static MetadataMethodImplementationResult Run(
        string path,
        TypeDefinitionHandle type,
        MethodDefinitionHandle body,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation =
            new MetadataOperationContext(
                policy ?? MetadataOperationPolicy.Unbounded);
        using var declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        return declaration.Relate(
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataMethodAddress.Create(reader, body),
            TestContext.Current.CancellationToken);
    }

    static MetadataMethodImplementationResult RunCore(
        Fixture fixture,
        MethodDefinitionHandle body,
        MetadataOperationPolicy? policy,
        MetadataTypeDefinitionAddress? typeOverride,
        MetadataMethodAddress? bodyOverride,
        CancellationToken token,
        Action<MetadataOperationWorkKind>? workObserver = null)
    {
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation =
            new MetadataOperationContext(
                policy ?? MetadataOperationPolicy.Unbounded,
                workObserver);
        using var declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        return declaration.Relate(
            typeOverride
                ?? MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    fixture.TargetType),
            bodyOverride
                ?? MetadataMethodAddress.Create(reader, body),
            token);
    }

    [Fact]
    public void RelationshipBudgetsEnforceEveryDimensionExactly()
    {
        using Fixture fixture = Fixture.Create(Scenario.GenericOnly);
        MetadataMethodImplementationResult.Related baseline =
            Assert.IsType<MetadataMethodImplementationResult.Related>(
                Run(fixture, fixture.BodyInt));
        MetadataOperationCounters expected =
            new(
                MetadataRows: 37,
                MethodImplementationRows: 1,
                DeclarationCandidates: 2,
                RelationshipEdges: 4,
                SignatureBytes: 30,
                GenericSubstitutionNodes: 2,
                StructuredNodes: 292,
                RetainedText: 114);
        Assert.Equal(expected, baseline.Counters);

        foreach (MetadataOperationDimension dimension in
            RelationshipDimensions)
        {
            long consumed = ReadCounter(expected, dimension);
            Assert.True(
                consumed > 0,
                $"{dimension} must be exercised by the budget fixture.");

            MetadataMethodImplementationResult exact = Run(
                fixture,
                fixture.BodyInt,
                Policy(dimension, consumed));
            Assert.IsType<
                MetadataMethodImplementationResult.Related>(
                    exact);
            Assert.Equal(
                expected,
                exact.Counters);

            MetadataMethodImplementationResult plusOne = Run(
                fixture,
                fixture.BodyInt,
                Policy(dimension, consumed + 1));
            Assert.IsType<
                MetadataMethodImplementationResult.Related>(
                    plusOne);
            Assert.Equal(
                expected,
                plusOne.Counters);

            var below = Assert.IsType<
                MetadataMethodImplementationResult.Rejected>(
                    Run(
                        fixture,
                        fixture.BodyInt,
                        Policy(dimension, consumed - 1)));
            Assert.Equal(
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                below.Failure.Reason);
            Assert.Equal(
                dimension,
                below.Failure.BudgetDimension);
            Assert.Equal(consumed - 1, below.Failure.BudgetLimit);
            (
                MetadataOperationCounters rejectedCounters,
                long attemptedCharge) =
                ExpectedBudgetRejection(dimension);
            Assert.Equal(rejectedCounters, below.Counters);
            Assert.Equal(
                attemptedCharge,
                below.Failure.AttemptedCharge);
            Assert.Null(
                typeof(MetadataMethodImplementationResult.Rejected)
                    .GetProperty(
                        "Relationships",
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic));
        }
    }

    [Fact]
    public void BudgetChargesPrecedeTraversalMaterializationAndRetention()
    {
        using Fixture fixture = Fixture.Create(Scenario.GenericOnly);

        MetadataMethodImplementationResult.Rejected rows =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension
                            .MethodImplementationRows,
                        0)),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(0, rows.Counters.MethodImplementationRows);
        Assert.Equal(0, rows.Counters.RelationshipEdges);
        Assert.Equal(0, rows.Counters.StructuredNodes);

        var genericContextWork =
            new List<MetadataOperationWorkKind>();
        MetadataMethodImplementationResult.Rejected context =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension.StructuredNodes,
                        0),
                    workObserver: genericContextWork.Add),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(0, context.Counters.StructuredNodes);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.GenericContextConstruction,
            genericContextWork);

        var typeNodeWork =
            new List<MetadataOperationWorkKind>();
        MetadataMethodImplementationResult.Rejected node =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension.StructuredNodes,
                        1),
                    workObserver: typeNodeWork.Add),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(1, node.Counters.StructuredNodes);
        Assert.Contains(
            MetadataOperationWorkKind.GenericContextConstruction,
            typeNodeWork);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.TypeNodeCreation,
            typeNodeWork);

        MetadataMethodImplementationResult.Rejected edges =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension.RelationshipEdges,
                        0)),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(0, edges.Counters.RelationshipEdges);
        Assert.Equal(0, edges.Counters.SignatureBytes);
        Assert.Equal(0, edges.Counters.StructuredNodes);

        MetadataMethodImplementationResult.Rejected structured =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension.StructuredNodes,
                        0)),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(0, structured.Counters.StructuredNodes);
        Assert.Equal(0, structured.Counters.RetainedText);

        var candidateNameWork =
            new List<MetadataOperationWorkKind>();
        MetadataMethodImplementationResult.Rejected candidate =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension
                            .DeclarationCandidates,
                        0),
                    workObserver: candidateNameWork.Add),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(0, candidate.Counters.DeclarationCandidates);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.CandidateNameMaterialization,
            candidateNameWork);

        using Fixture localIndex =
            Fixture.Create(Scenario.LocalReferenceOnly);
        var indexWork = new List<MetadataOperationWorkKind>();
        using var indexStream = File.OpenRead(localIndex.Path);
        using var indexPe = new PEReader(indexStream);
        using var indexContext =
            new MetadataOperationContext(
                Policy(
                    MetadataOperationDimension.StructuredNodes,
                    0),
                indexWork.Add);
        Assert.Throws<MetadataOperationBudgetExceededException>(
            () => MetadataTypeDefinitionIndex.Create(
                indexPe.GetMetadataReader(),
                definitionVisited: null,
                beforeCreateNode: () =>
                {
                    indexContext.Charge(
                        MetadataOperationDimension.StructuredNodes);
                    indexContext.ObserveWork(
                        MetadataOperationWorkKind
                            .TypeDefinitionIndexMaterialization);
                }));
        Assert.DoesNotContain(
            MetadataOperationWorkKind.TypeDefinitionIndexMaterialization,
            indexWork);

        var retainedTextWork =
            new List<MetadataOperationWorkKind>();
        MetadataMethodImplementationResult.Rejected retained =
            AssertRejected(
                Run(
                    fixture,
                    fixture.BodyInt,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        0),
                    workObserver: retainedTextWork.Add),
                MetadataMethodImplementationFailureReason
                    .BudgetExceeded,
                expectedRow: 1);
        Assert.Equal(0, retained.Counters.RetainedText);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.TypeNodeTextRetention,
            retainedTextWork);
    }

    [Fact]
    public void LongTypeNameExhaustsRetainedTextBeforeMaterialization()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.LongExternalTypeName);
        var work = new List<MetadataOperationWorkKind>();

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.RetainedText,
                        100),
                    workObserver: work.Add),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataOperationDimension.RetainedText,
            rejected.Failure.BudgetDimension);
        Assert.Equal(4_000, rejected.Failure.AttemptedCharge);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.TypeNameMaterialization,
            work);
    }

    [Fact]
    public void LargePublicKeyExhaustsMaterializationBeforeHash()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.LargeExternalPublicKey);
        var work = new List<MetadataOperationWorkKind>();

        MetadataMethodImplementationResult.Rejected rejected =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.StructuredNodes,
                        10_000),
                    workObserver: work.Add),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataOperationDimension.StructuredNodes,
            rejected.Failure.BudgetDimension);
        Assert.Equal(
            (1024 * 1024) + 16,
            rejected.Failure.AttemptedCharge);
        Assert.Contains(
            MetadataOperationWorkKind.TypeNameMaterialization,
            work);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.PublicKeyTokenMaterialization,
            work);
    }

    [Fact]
    public void RepeatedTypeSpecDecodesChargeEveryBlobAtExactBoundary()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.RepeatedTypeSpecReferences);
        MetadataMethodImplementationResult.Related baseline =
            AssertRelated(Run(fixture, fixture.Body));
        long exact = baseline.Counters.SignatureBytes;

        AssertRelated(
            Run(
                fixture,
                fixture.Body,
                Policy(
                    MetadataOperationDimension.SignatureBytes,
                    exact)));
        MetadataMethodImplementationResult.Rejected below =
            AssertRejected(
                Run(
                    fixture,
                    fixture.Body,
                    Policy(
                        MetadataOperationDimension.SignatureBytes,
                        exact - 1)),
                MetadataMethodImplementationFailureReason.BudgetExceeded,
                expectedRow: 1);

        Assert.Equal(
            MetadataOperationDimension.SignatureBytes,
            below.Failure.BudgetDimension);
        Assert.Equal(1, below.Failure.AttemptedCharge);
        Assert.Equal(exact - 1, below.Counters.SignatureBytes);
    }

    [Fact]
    public void RelationshipCountersAccumulateAcrossCallsAndSessions()
    {
        using Fixture fixture = Fixture.Create(Scenario.GenericOnly);
        MetadataOperationCounters first;
        MetadataOperationCounters second;
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using (var assembly =
            AssemblyInspectionSession.Open(fixture.Path))
        using (var declaration =
            assembly.CreateDeclarationSession(operation))
        {
            MetadataReader reader =
                assembly.GetMetadataReaderForDeclarationSession();
            first = Assert.IsType<
                MetadataMethodImplementationResult.Related>(
                    declaration.Relate(
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            fixture.TargetType),
                        MetadataMethodAddress.Create(
                            reader,
                            fixture.BodyInt),
                        TestContext.Current.CancellationToken))
                .Counters;
        }

        using (var assembly =
            AssemblyInspectionSession.Open(fixture.Path))
        using (var declaration =
            assembly.CreateDeclarationSession(operation))
        {
            MetadataReader reader =
                assembly.GetMetadataReaderForDeclarationSession();
            second = Assert.IsType<
                MetadataMethodImplementationResult.Related>(
                    declaration.Relate(
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            fixture.TargetType),
                        MetadataMethodAddress.Create(
                            reader,
                            fixture.BodyInt),
                        TestContext.Current.CancellationToken))
                .Counters;
        }

        Assert.Equal(
            first.MethodImplementationRows * 2,
            second.MethodImplementationRows);
        Assert.Equal(
            first.DeclarationCandidates * 2,
            second.DeclarationCandidates);
        Assert.Equal(
            first.RelationshipEdges * 2,
            second.RelationshipEdges);
        Assert.Equal(
            first.SignatureBytes * 2,
            second.SignatureBytes);
        Assert.Equal(
            first.GenericSubstitutionNodes * 2,
            second.GenericSubstitutionNodes);
        Assert.Equal(
            first.StructuredNodes * 2,
            second.StructuredNodes);
        Assert.Equal(
            first.RetainedText * 2,
            second.RetainedText);
    }

    [Fact]
    public void LocalOwnerIndexChargesColdConstructionAndReusesWarmSessionIndex()
    {
        using Fixture fixture =
            Fixture.Create(Scenario.LocalReferenceOnly);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                fixture.TargetType);
        MetadataMethodAddress body =
            MetadataMethodAddress.Create(reader, fixture.Body);

        MetadataOperationCounters cold =
            AssertRelated(
                declaration.Relate(
                    type,
                    body,
                    TestContext.Current.CancellationToken))
                .Counters;
        MetadataOperationCounters warm =
            AssertRelated(
                declaration.Relate(
                    type,
                    body,
                    TestContext.Current.CancellationToken))
                .Counters;

        Assert.Equal(
            new MetadataOperationCounters(
                MetadataRows: 35,
                MethodImplementationRows: 1,
                DeclarationCandidates: 9,
                RelationshipEdges: 10,
                SignatureBytes: 9,
                StructuredNodes: 256,
                RetainedText: 185),
            cold);
        Assert.Equal(
            new MetadataOperationCounters(
                MetadataRows: 35,
                MethodImplementationRows: 2,
                DeclarationCandidates: 18,
                RelationshipEdges: 14,
                SignatureBytes: 18,
                StructuredNodes: 496,
                RetainedText: 284),
            warm);
        Assert.Equal(6, cold.RelationshipEdges - 4);
        Assert.Equal(16, cold.StructuredNodes - 240);
        Assert.Equal(86, cold.RetainedText - 99);
        Assert.Equal(4, warm.RelationshipEdges - cold.RelationshipEdges);
        Assert.Equal(240, warm.StructuredNodes - cold.StructuredNodes);
        Assert.Equal(99, warm.RetainedText - cold.RetainedText);
    }

    [Fact]
    public void SessionApi_IsCallableFromANonFriendConsumerAssembly()
    {
        using Fixture fixture = Fixture.Create(Scenario.GenericOnly);
        MetadataMethodImplementationResult result =
            MetadataMethodImplementationConsumerCanary.Relate(
                fixture.Path,
                fixture.TypeAddress,
                new MetadataMethodAddress(
                    fixture.ModuleVersionId,
                    fixture.BodyInt));

        Assert.True(
            MetadataMethodImplementationConsumerCanary.Consume(result));
        Assert.IsType<MetadataMethodImplementationResult.Related>(
            result);
    }

    static readonly MetadataOperationDimension[]
        RelationshipDimensions =
        [
            MetadataOperationDimension.MethodImplementationRows,
            MetadataOperationDimension.DeclarationCandidates,
            MetadataOperationDimension.RelationshipEdges,
            MetadataOperationDimension.SignatureBytes,
            MetadataOperationDimension.GenericSubstitutionNodes,
            MetadataOperationDimension.StructuredNodes,
            MetadataOperationDimension.RetainedText,
        ];

    static void AssertScenarioRejected(
        Scenario scenario,
        MetadataMethodImplementationFailureReason reason)
    {
        using Fixture fixture = Fixture.Create(scenario);
        AssertRejected(
            Run(fixture, fixture.Body),
            reason,
            expectedRow: 1);
    }

    static void AssertInvalidBeforeScan(
        MetadataMethodImplementationResult result)
    {
        var rejected =
            Assert.IsType<MetadataMethodImplementationResult.Rejected>(
                result);
        Assert.Equal(
            MetadataMethodImplementationFailureReason.InvalidRequest,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodImplementationStage.RequestValidation,
            rejected.Failure.Stage);
        Assert.Equal(
            0,
            rejected.Counters.MethodImplementationRows);
    }

    static MetadataMethodImplementationResult.Rejected AssertRejected(
        MetadataMethodImplementationResult result,
        MetadataMethodImplementationFailureReason reason,
        int expectedRow)
    {
        var rejected =
            Assert.IsType<MetadataMethodImplementationResult.Rejected>(
                result);
        Assert.True(
            reason == rejected.Failure.Reason,
            $"Expected {reason}, actual {rejected.Failure.Reason}: "
                + rejected.Failure.Detail);
        Assert.Equal(
            expectedRow,
            MetadataTokens.GetRowNumber(
                rejected.Failure.RelevantRow!.Value));
        Assert.DoesNotContain(
            typeof(MetadataMethodImplementationResult.Rejected)
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
            property =>
                property.PropertyType
                    == typeof(
                        ImmutableArray<
                            MetadataMethodImplementationCertificate>));
        return rejected;
    }

    static MetadataMethodImplementationResult.Related AssertRelated(
        MetadataMethodImplementationResult result)
    {
        if (result
            is MetadataMethodImplementationResult.Rejected rejected)
        {
            Assert.Fail(
                $"{rejected.Failure.Reason} at "
                + $"{rejected.Failure.Stage}/"
                + $"{rejected.Failure.Mechanism} "
                + $"({rejected.Failure.RelevantHandle.Kind} "
                + $"0x{MetadataTokens.GetToken(rejected.Failure.RelevantHandle):X8}): "
                + rejected.Failure.Detail);
        }
        return Assert.IsType<
            MetadataMethodImplementationResult.Related>(result);
    }

    static MetadataMethodImplementationResult RunCancelled(
        Fixture fixture,
        MethodDefinitionHandle body,
        CancellationToken token) =>
        RunCore(
            fixture,
            body,
            policy: null,
            typeOverride: null,
            bodyOverride: null,
            token,
            workObserver: null);

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string name,
        string? @namespace = null) =>
        reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            return reader.GetString(definition.Name) == name
                && (@namespace is null
                    || reader.GetString(definition.Namespace)
                        == @namespace);
        });

    static MethodDefinitionHandle FindExplicitMethod(
        MetadataReader reader,
        string typeName,
        string suffix)
    {
        TypeDefinitionHandle type = FindType(reader, typeName);
        return reader.GetTypeDefinition(type).GetMethods()
            .Single(handle =>
                reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                    .EndsWith(suffix, StringComparison.Ordinal));
    }

    static void RunCycleWorker()
    {
        string dotnetHost =
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(
            typeof(MetadataMethodImplementationEvidenceTests)
                .Assembly.Location);
        startInfo.ArgumentList.Add("--filter-method");
        startInfo.ArgumentList.Add(
            $"*{nameof(CyclicTypeSpec_IsReportedAsCycle)}*");
        startInfo.Environment[CycleWorkerVariable] =
            nameof(CyclicTypeSpec_IsReportedAsCycle);

        using Process process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The cycle worker did not start.");
        bool exited = process.WaitForExit(30_000);
        if (!exited)
            process.Kill(entireProcessTree: true);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(
            exited && process.ExitCode == 0,
            $"Cycle worker exited {process.ExitCode}.\n"
                + $"stdout:\n{output}\nstderr:\n{error}");
    }

    static MetadataOperationPolicy Policy(
        MetadataOperationDimension dimension,
        long limit) =>
        new(
            maxMetadataRows: long.MaxValue,
            maxMethodImplementationRows:
                dimension
                    == MetadataOperationDimension
                        .MethodImplementationRows
                    ? limit
                    : long.MaxValue,
            maxDeclarationCandidates:
                dimension
                    == MetadataOperationDimension
                        .DeclarationCandidates
                    ? limit
                    : long.MaxValue,
            maxRelationshipEdges:
                dimension
                    == MetadataOperationDimension.RelationshipEdges
                    ? limit
                    : long.MaxValue,
            maxSignatureBytes:
                dimension
                    == MetadataOperationDimension.SignatureBytes
                    ? limit
                    : long.MaxValue,
            maxGenericSubstitutionNodes:
                dimension
                    == MetadataOperationDimension
                        .GenericSubstitutionNodes
                    ? limit
                    : long.MaxValue,
            maxStructuredNodes:
                dimension
                    == MetadataOperationDimension.StructuredNodes
                    ? limit
                    : long.MaxValue,
            maxRetainedText:
                dimension
                    == MetadataOperationDimension.RetainedText
                    ? limit
                    : long.MaxValue);

    static long ReadCounter(
        MetadataOperationCounters counters,
        MetadataOperationDimension dimension) =>
        dimension switch
        {
            MetadataOperationDimension.MethodImplementationRows =>
                counters.MethodImplementationRows,
            MetadataOperationDimension.DeclarationCandidates =>
                counters.DeclarationCandidates,
            MetadataOperationDimension.RelationshipEdges =>
                counters.RelationshipEdges,
            MetadataOperationDimension.SignatureBytes =>
                counters.SignatureBytes,
            MetadataOperationDimension.GenericSubstitutionNodes =>
                counters.GenericSubstitutionNodes,
            MetadataOperationDimension.StructuredNodes =>
                counters.StructuredNodes,
            MetadataOperationDimension.RetainedText =>
                counters.RetainedText,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension)),
        };

    static (MetadataOperationCounters Counters, long AttemptedCharge)
        ExpectedBudgetRejection(
            MetadataOperationDimension dimension) =>
        dimension switch
        {
            MetadataOperationDimension.MethodImplementationRows =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 0),
                    1),
            MetadataOperationDimension.DeclarationCandidates =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 1,
                        DeclarationCandidates: 1,
                        RelationshipEdges: 4,
                        SignatureBytes: 26,
                        StructuredNodes: 245,
                        RetainedText: 53),
                    1),
            MetadataOperationDimension.RelationshipEdges =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 1,
                        RelationshipEdges: 3,
                        SignatureBytes: 14,
                        StructuredNodes: 35,
                        RetainedText: 6),
                    1),
            MetadataOperationDimension.SignatureBytes =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 1,
                        DeclarationCandidates: 2,
                        RelationshipEdges: 4,
                        SignatureBytes: 26,
                        StructuredNodes: 250,
                        RetainedText: 54),
                    4),
            MetadataOperationDimension.GenericSubstitutionNodes =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 1,
                        DeclarationCandidates: 2,
                        RelationshipEdges: 4,
                        SignatureBytes: 30,
                        GenericSubstitutionNodes: 1,
                        StructuredNodes: 284,
                        RetainedText: 60),
                    1),
            MetadataOperationDimension.StructuredNodes =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 1,
                        DeclarationCandidates: 2,
                        RelationshipEdges: 4,
                        SignatureBytes: 30,
                        GenericSubstitutionNodes: 2,
                        StructuredNodes: 291,
                        RetainedText: 114),
                    1),
            MetadataOperationDimension.RetainedText =>
                (
                    new(
                        MetadataRows: 37,
                        MethodImplementationRows: 1,
                        DeclarationCandidates: 2,
                        RelationshipEdges: 4,
                        SignatureBytes: 30,
                        GenericSubstitutionNodes: 2,
                        StructuredNodes: 289,
                        RetainedText: 111),
                    3),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension)),
        };

    public enum WideStringNameTarget
    {
        MethodDefinitionDeclaration,
        MemberReferenceDeclaration,
        LocalCandidate,
    }

    public enum Scenario
    {
        Standard,
        GenericOnly,
        Absent,
        UnreadableBody,
        UnreadableUnrelatedDeclaration,
        UnreadableRelevantDeclaration,
        UnsupportedParent,
        LocalMissingDeclaration,
        LocalReferenceOnly,
        LocalOwnerAmbiguity,
        LocalDeclarationAmbiguity,
        SignatureMismatch,
        SignatureArrayShapeMismatch,
        SignaturePassingShapeMismatch,
        BodyTypeParameterOutOfRange,
        BodyMethodParameterOutOfRange,
        BodyMethodArityMismatch,
        DeclarationTypeParameterOutOfRange,
        DeclarationMethodArityMismatch,
        ConstructedOwnerArityMismatch,
        FunctionPointerMvarOutOfRange,
        NestedConstructedArityMismatch,
        NestedConstructedArityValid,
        LocalCandidateMalformedSignature,
        CyclicTypeSpec,
        SignatureTypeSpecTrailingData,
        SignatureTypeSpecNestedCycle,
        SignatureTypeSpecDepthBudget,
        SignatureTypeSpecMalformedDependency,
        SignatureTypeSpecDependency,
        OwnerTypeSpecDependency,
        CyclicTypeReference,
        TypeReferenceTraversalBudget,
        BodyContextCycle,
        BodyContextDepthBudget,
        ReservedMemberRefParent,
        LocalIndexCycle,
        LocalIndexDepthBudget,
        LocalIndexMalformed,
        LocalIndexMalformedString,
        LocalIndexMalformedWideString,
        LocalIndexEmptyName,
        LocalIndexOverlongName,
        LocalIndexForwardParentChain,
        MethodDefSignatureDepthBudget,
        MemberRefSignatureDepthBudget,
        MethodDefDeclarationName,
        LongMethodDefDeclarationName,
        OverlongMethodDefDeclarationName,
        ExpandingControlReturnTypeNamespace,
        ExternalMemberRefDeclarationName,
        LongExternalMemberRefDeclarationName,
        OverlongExternalMemberRefDeclarationName,
        LongUnresolvedLocalDeclarationName,
        LongExternalTypeName,
        OverlongExternalTypeName,
        LargeExternalPublicKey,
        RepeatedTypeSpecReferences,
        DistinctExternalArguments,
        MethodGeneric,
        InheritedLocalDeclaration,
        PendingThenRejected,
    }

    sealed class Fixture : IDisposable
    {
        Fixture(
            string path,
            Guid moduleVersionId,
            TypeDefinitionHandle targetType,
            MethodDefinitionHandle body,
            MethodDefinitionHandle bodyInt,
            MethodDefinitionHandle bodyMethodGeneric,
            MethodDefinitionHandle localDeclaration,
            MethodDefinitionHandle genericDeclaration,
            MethodDefinitionHandle genericIntDeclaration,
            MethodDefinitionHandle methodGenericDeclaration,
            int methodImplementationCount,
            EntityHandle expectedFailureSubject)
        {
            Path = path;
            ModuleVersionId = moduleVersionId;
            TargetType = targetType;
            Body = body;
            BodyInt = bodyInt;
            BodyMethodGeneric = bodyMethodGeneric;
            LocalDeclaration = localDeclaration;
            GenericDeclaration = genericDeclaration;
            GenericIntDeclaration = genericIntDeclaration;
            MethodGenericDeclaration = methodGenericDeclaration;
            MethodImplementationCount = methodImplementationCount;
            ExpectedFailureSubject = expectedFailureSubject;
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            TypeAddress = new MetadataTypeDefinitionAddress(
                moduleVersionId,
                TypeDefinitionToken.FromHandle(
                    reader.GetMetadataReader(),
                    targetType));
        }

        internal string Path { get; }
        internal Guid ModuleVersionId { get; }
        internal TypeDefinitionHandle TargetType { get; }
        internal MethodDefinitionHandle Body { get; }
        internal MethodDefinitionHandle BodyInt { get; }
        internal MethodDefinitionHandle BodyMethodGeneric { get; }
        internal MethodDefinitionHandle LocalDeclaration { get; }
        internal MethodDefinitionHandle GenericDeclaration { get; }
        internal MethodDefinitionHandle GenericIntDeclaration { get; }
        internal MethodDefinitionHandle MethodGenericDeclaration
        { get; }
        internal int MethodImplementationCount { get; }
        internal EntityHandle ExpectedFailureSubject { get; }
        internal MetadataTypeDefinitionAddress TypeAddress { get; }

        internal static Fixture Create(Scenario scenario)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("MethodImplFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);

            BlobHandle voidSignature =
                AddBlob(metadata, 0x20, 0x00, 0x01);
            BlobHandle intSignature =
                AddBlob(metadata, 0x20, 0x01, 0x08, 0x08);
            BlobHandle genericSignature =
                AddBlob(
                    metadata,
                    0x20,
                    0x01,
                    0x13,
                    0x00,
                    0x13,
                    0x00);
            BlobHandle genericMethodSignature =
                AddBlob(
                    metadata,
                    0x30,
                    0x01,
                    0x01,
                    0x1e,
                    0x00,
                    0x1e,
                    0x00);
            BlobHandle genericHeaderWithoutRows =
                AddBlob(metadata, 0x30, 0x01, 0x00, 0x01);
            BlobHandle functionPointerMvarOutOfRange =
                AddBlob(
                    metadata,
                    0x30,
                    0x01,
                    0x01,
                    0x01,
                    0x1b,
                    0x00,
                    0x01,
                    0x01,
                    0x1e,
                    0x01);
            BlobHandle modifierTypeSpec =
                AddMethodSignatureWithModifierTypeSpec(
                    metadata,
                    typeSpecRow: 1);
            BlobHandle bodyTypeParameterOutOfRange =
                AddBlob(metadata, 0x20, 0x01, 0x01, 0x13, 0x00);
            BlobHandle bodyMethodParameterOutOfRange =
                AddBlob(metadata, 0x20, 0x01, 0x01, 0x1e, 0x00);
            BlobHandle szArraySignature =
                AddBlob(
                    metadata,
                    0x20,
                    0x01,
                    0x01,
                    0x1d,
                    0x08);
            BlobHandle mdArraySignature =
                AddBlob(
                    metadata,
                    0x20,
                    0x01,
                    0x01,
                    0x14,
                    0x08,
                    0x01,
                    0x00,
                    0x00);
            BlobHandle byReferenceSignature =
                AddBlob(
                    metadata,
                    0x20,
                    0x01,
                    0x01,
                    0x10,
                    0x08);
            BlobHandle pointerSignature =
                AddBlob(
                    metadata,
                    0x20,
                    0x01,
                    0x01,
                    0x0f,
                    0x08);
            BlobHandle malformedMethodSignature =
                AddBlob(metadata, 0x20);
            var deepMethod = new BlobBuilder();
            deepMethod.WriteByte(0x20);
            deepMethod.WriteByte(0x00);
            for (int index = 0;
                index <= SignatureBlobGuard.DefaultMaxDepth;
                index++)
            {
                deepMethod.WriteByte(0x0f);
            }
            deepMethod.WriteByte(0x08);
            BlobHandle deepMethodSignature =
                metadata.GetOrAddBlob(deepMethod);
            BlobHandle repeatedTypeSpecSignature = default;
            if (scenario == Scenario.RepeatedTypeSpecReferences)
            {
                TypeSpecificationHandle repeated =
                    metadata.AddTypeSpecification(
                        AddBlob(metadata, 0x08));
                repeatedTypeSpecSignature =
                    AddMethodSignatureWithRepeatedModifierTypeSpec(
                        metadata,
                        repeated);
            }
            BlobHandle expandingControlSignature = default;
            if (scenario
                == Scenario.ExpandingControlReturnTypeNamespace)
            {
                AssemblyReferenceHandle controlAssembly =
                    AddAssemblyReference(
                        metadata,
                        "Control.Contracts");
                TypeReferenceHandle controlType =
                    metadata.AddTypeReference(
                        controlAssembly,
                        metadata.GetOrAddString("A\u202EB"),
                        metadata.GetOrAddString("ControlType"));
                expandingControlSignature =
                    AddMethodSignatureWithReturnType(
                        metadata,
                        controlType);
            }

            MethodDefinitionHandle body = AddMethod(
                metadata,
                "Body",
                scenario switch
                {
                    Scenario.BodyTypeParameterOutOfRange =>
                        bodyTypeParameterOutOfRange,
                    Scenario.BodyMethodParameterOutOfRange =>
                        bodyMethodParameterOutOfRange,
                    Scenario.BodyMethodArityMismatch =>
                        genericHeaderWithoutRows,
                    Scenario.SignatureTypeSpecTrailingData =>
                        modifierTypeSpec,
                    Scenario.SignatureTypeSpecNestedCycle
                        or Scenario.SignatureTypeSpecDepthBudget
                        or Scenario.SignatureTypeSpecMalformedDependency
                        or Scenario.SignatureTypeSpecDependency =>
                        modifierTypeSpec,
                    Scenario.SignatureArrayShapeMismatch =>
                        mdArraySignature,
                    Scenario.SignaturePassingShapeMismatch =>
                        pointerSignature,
                    Scenario.MethodDefSignatureDepthBudget =>
                        deepMethodSignature,
                    Scenario.RepeatedTypeSpecReferences =>
                        repeatedTypeSpecSignature,
                    Scenario.ExpandingControlReturnTypeNamespace =>
                        expandingControlSignature,
                    _ => voidSignature,
                });
            MethodDefinitionHandle otherBody = AddMethod(
                metadata,
                "Other",
                voidSignature);
            MethodDefinitionHandle bodyInt = AddMethod(
                metadata,
                "BodyInt",
                intSignature);
            MethodDefinitionHandle bodyMethodGeneric =
                AddMethod(
                    metadata,
                    "BodyGeneric",
                    scenario
                        == Scenario.FunctionPointerMvarOutOfRange
                        ? functionPointerMvarOutOfRange
                        : genericMethodSignature);
            string methodDefinitionDeclarationName =
                scenario switch
                {
                    Scenario.LongMethodDefDeclarationName =>
                        new string(
                            'D',
                            LongDeclarationNameLength),
                    Scenario.OverlongMethodDefDeclarationName =>
                        new string(
                            'D',
                            OverlongDeclarationNameLength),
                    _ => "Special",
                };
            MethodDefinitionHandle special = AddMethod(
                metadata,
                methodDefinitionDeclarationName,
                scenario switch
                {
                    Scenario.RepeatedTypeSpecReferences =>
                        repeatedTypeSpecSignature,
                    Scenario.ExpandingControlReturnTypeNamespace =>
                        expandingControlSignature,
                    _ => voidSignature,
                },
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.SpecialName);
            MethodDefinitionHandle ordinary = AddMethod(
                metadata,
                "op_Addition",
                voidSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);
            MethodDefinitionHandle duplicate = default;
            if (scenario == Scenario.LocalDeclarationAmbiguity)
            {
                duplicate = AddMethod(
                    metadata,
                    "op_Addition",
                    voidSignature,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            }
            MethodDefinitionHandle methodGeneric =
                AddMethod(
                    metadata,
                    "Transform",
                    scenario
                        == Scenario.FunctionPointerMvarOutOfRange
                        ? functionPointerMvarOutOfRange
                        : genericMethodSignature,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            _ = AddMethod(
                metadata,
                "Mismatch",
                intSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);
            MethodDefinitionHandle shapeArray = AddMethod(
                metadata,
                "ShapeArray",
                szArraySignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);
            MethodDefinitionHandle shapePassing = AddMethod(
                metadata,
                "ShapePassing",
                byReferenceSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);
            MethodDefinitionHandle declarationTypeOutOfRange =
                AddMethod(
                    metadata,
                    "DeclarationTypeOutOfRange",
                    genericSignature,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            MethodDefinitionHandle declarationMethodArityMismatch =
                AddMethod(
                    metadata,
                    "DeclarationMethodArityMismatch",
                    genericHeaderWithoutRows,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            _ = AddMethod(
                metadata,
                "CandidateMalformed",
                malformedMethodSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);
            MethodDefinitionHandle generic = AddMethod(
                metadata,
                "Echo",
                genericSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.SpecialName);
            MethodDefinitionHandle genericInt = AddMethod(
                metadata,
                "Echo",
                intSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);
            MethodDefinitionHandle inherited = AddMethod(
                metadata,
                "Inherited",
                voidSignature,
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual);

            TypeDefinitionHandle module = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle target = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle local = metadata.AddTypeDefinition(
                TypeAttributes.Interface
                    | TypeAttributes.Abstract
                    | TypeAttributes.Public,
                metadata.GetOrAddString("Contracts"),
                metadata.GetOrAddString("ILocal"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                special);
            TypeDefinitionHandle genericType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IGeneric`1"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    generic);
            TypeDefinitionHandle baseInterface =
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IBase"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    inherited);
            TypeDefinitionHandle derivedInterface =
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IDerived"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        MetadataTokens.GetRowNumber(inherited) + 1));
            metadata.AddInterfaceImplementation(
                derivedInterface,
                baseInterface);
            metadata.AddGenericParameter(
                genericType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            metadata.AddGenericParameter(
                bodyMethodGeneric,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TBody"),
                index: 0);
            metadata.AddGenericParameter(
                methodGeneric,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TDeclaration"),
                index: 0);
            if (scenario == Scenario.LocalOwnerAmbiguity)
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("ILocal"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        MetadataTokens.GetRowNumber(generic) + 1));
            }
            EntityHandle expectedFailureSubject = default;
            if (scenario is Scenario.BodyContextCycle)
            {
                TypeDefinitionHandle enclosing =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("Enclosing"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1));
                metadata.AddNestedType(target, enclosing);
                metadata.AddNestedType(enclosing, target);
                expectedFailureSubject = enclosing;
            }
            else if (scenario is Scenario.BodyContextDepthBudget)
            {
                TypeDefinitionHandle child = target;
                TypeDefinitionHandle rejected = default;
                for (int index = 0;
                    index <= MetadataSafetyPolicy.MaxRelationshipNodes;
                    index++)
                {
                    TypeDefinitionHandle enclosing =
                        metadata.AddTypeDefinition(
                            index
                                == MetadataSafetyPolicy
                                    .MaxRelationshipNodes
                                ? TypeAttributes.Public
                                : TypeAttributes.NestedPublic,
                            index
                                == MetadataSafetyPolicy
                                    .MaxRelationshipNodes
                                ? metadata.GetOrAddString("Deep")
                                : default,
                            metadata.GetOrAddString(
                                $"Enclosing{index}"),
                            default,
                            MetadataTokens.FieldDefinitionHandle(1),
                            MetadataTokens.MethodDefinitionHandle(
                                MetadataTokens.GetRowNumber(inherited) + 1));
                    metadata.AddNestedType(child, enclosing);
                    child = enclosing;
                    if (index
                        == MetadataSafetyPolicy.MaxRelationshipNodes)
                    {
                        rejected = enclosing;
                    }
                }
                expectedFailureSubject = rejected;
            }
            else if (scenario is Scenario.LocalIndexCycle)
            {
                TypeDefinitionHandle cycleA =
                    metadata.AddTypeDefinition(
                        TypeAttributes.Public,
                        metadata.GetOrAddString("Broken"),
                        metadata.GetOrAddString("CycleA"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1));
                TypeDefinitionHandle cycleB =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("CycleB"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1));
                metadata.AddNestedType(cycleA, cycleB);
                metadata.AddNestedType(cycleB, cycleB);
                expectedFailureSubject = cycleB;
            }
            else if (scenario is Scenario.LocalIndexDepthBudget)
            {
                TypeDefinitionHandle parent = default;
                for (int index = 0;
                    index <= MetadataSafetyPolicy.MaxRelationshipNodes;
                    index++)
                {
                    TypeDefinitionHandle nested =
                        metadata.AddTypeDefinition(
                            index == 0
                                ? TypeAttributes.Public
                                : TypeAttributes.NestedPublic,
                            index == 0
                                ? metadata.GetOrAddString("Broken")
                                : default,
                            metadata.GetOrAddString($"Deep{index}"),
                            default,
                            MetadataTokens.FieldDefinitionHandle(1),
                            MetadataTokens.MethodDefinitionHandle(
                                MetadataTokens.GetRowNumber(inherited) + 1));
                    if (!parent.IsNil)
                        metadata.AddNestedType(nested, parent);
                    parent = nested;
                }
                expectedFailureSubject = parent;
            }
            else if (scenario is Scenario.LocalIndexMalformed)
            {
                TypeDefinitionHandle malformed =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("Malformed"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1));
                var invalid =
                    MetadataTokens.TypeDefinitionHandle(0xFFFF);
                metadata.AddNestedType(malformed, invalid);
                expectedFailureSubject = invalid;
            }
            else if (scenario is
                Scenario.LocalIndexMalformedString
                or Scenario.LocalIndexMalformedWideString
                or Scenario.LocalIndexEmptyName
                or Scenario.LocalIndexOverlongName)
            {
                TypeDefinitionHandle malformed =
                    metadata.AddTypeDefinition(
                        TypeAttributes.Public,
                        metadata.GetOrAddString("Broken"),
                        scenario switch
                        {
                            Scenario.LocalIndexEmptyName => default,
                            Scenario.LocalIndexOverlongName =>
                                metadata.GetOrAddString(
                                    new string(
                                        'N',
                                        MetadataSafetyPolicy
                                            .MaxTypeNameCharacters + 1)),
                            _ => metadata.GetOrAddString(
                                "MalformedString"),
                        },
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1));
                expectedFailureSubject = malformed;
            }
            else if (scenario
                is Scenario.LocalIndexForwardParentChain)
            {
                int childRow =
                    metadata.GetRowCount(TableIndex.TypeDef) + 1;
                TypeDefinitionHandle child =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("ForwardChild"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1));
                TypeDefinitionHandle middle =
                    MetadataTokens.TypeDefinitionHandle(
                        childRow + 1);
                TypeDefinitionHandle root =
                    MetadataTokens.TypeDefinitionHandle(
                        childRow + 2);
                metadata.AddNestedType(child, middle);
                Assert.Equal(
                    middle,
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("ForwardMiddle"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1)));
                metadata.AddNestedType(middle, root);
                Assert.Equal(
                    root,
                    metadata.AddTypeDefinition(
                        TypeAttributes.Public,
                        metadata.GetOrAddString("Broken"),
                        metadata.GetOrAddString("ForwardRoot"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            MetadataTokens.GetRowNumber(inherited) + 1)));
                expectedFailureSubject = middle;
            }

            EntityHandle moduleScope =
                MetadataTokens.EntityHandle(0x00000001);
            TypeReferenceHandle localReference =
                metadata.AddTypeReference(
                    moduleScope,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("ILocal"));
            AssemblyReferenceHandle externalAssembly =
                AddAssemblyReference(metadata, "External.Contracts");
            TypeReferenceHandle externalType =
                metadata.AddTypeReference(
                    externalAssembly,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IExternal"));
            MemberReferenceHandle localDirect =
                metadata.AddMemberReference(
                    local,
                    metadata.GetOrAddString("op_Addition"),
                    voidSignature);
            MemberReferenceHandle localThroughReference =
                metadata.AddMemberReference(
                    localReference,
                    metadata.GetOrAddString("Special"),
                    voidSignature);
            MemberReferenceHandle external =
                metadata.AddMemberReference(
                    externalType,
                    metadata.GetOrAddString("External"),
                    voidSignature);
            MemberReferenceHandle reservedParentToPatch = default;
            TypeDefinitionHandle malformedStringToPatch =
                scenario is
                    Scenario.LocalIndexMalformedString
                    or Scenario.LocalIndexMalformedWideString
                    ? (TypeDefinitionHandle)expectedFailureSubject
                    : default;

            switch (scenario)
            {
                case Scenario.Standard:
                    metadata.AddMethodImplementation(
                        target,
                        otherBody,
                        MetadataTokens.MemberReferenceHandle(0xFFFF));
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    metadata.AddMethodImplementation(
                        local,
                        MetadataTokens.MethodDefinitionHandle(0xFFFF),
                        MetadataTokens.MemberReferenceHandle(0xFFFF));
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localDirect);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localThroughReference);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        external);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localDirect);
                    AddGenericRelationship(
                        metadata,
                        target,
                        bodyInt,
                        genericType,
                        genericSignature);
                    break;

                case Scenario.GenericOnly:
                    AddGenericRelationship(
                        metadata,
                        target,
                        bodyInt,
                        genericType,
                        genericSignature);
                    break;

                case Scenario.Absent:
                case Scenario.UnreadableUnrelatedDeclaration:
                    metadata.AddMethodImplementation(
                        target,
                        otherBody,
                        MetadataTokens.MemberReferenceHandle(0xFFFF));
                    break;

                case Scenario.UnreadableBody:
                    metadata.AddMethodImplementation(
                        target,
                        MetadataTokens.MethodDefinitionHandle(0xFFFF),
                        localDirect);
                    break;

                case Scenario.UnreadableRelevantDeclaration:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        MetadataTokens.MemberReferenceHandle(0xFFFF));
                    break;

                case Scenario.UnsupportedParent:
                    ModuleReferenceHandle moduleReference =
                        metadata.AddModuleReference(
                            metadata.GetOrAddString("other.netmodule"));
                    MemberReferenceHandle unsupported =
                        metadata.AddMemberReference(
                            moduleReference,
                            metadata.GetOrAddString("Missing"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        unsupported);
                    break;

                case Scenario.LocalMissingDeclaration:
                    MemberReferenceHandle missing =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString("Missing"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        missing);
                    break;

                case Scenario.LocalReferenceOnly:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localThroughReference);
                    break;

                case Scenario.LocalOwnerAmbiguity:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localThroughReference);
                    break;

                case Scenario.LocalDeclarationAmbiguity:
                    Assert.False(duplicate.IsNil);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localDirect);
                    break;

                case Scenario.SignatureMismatch:
                    MemberReferenceHandle mismatch =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString("Mismatch"),
                            intSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        mismatch);
                    break;

                case Scenario.SignatureArrayShapeMismatch:
                    MemberReferenceHandle arrayMismatch =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString("ShapeArray"),
                            szArraySignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        arrayMismatch);
                    break;

                case Scenario.SignaturePassingShapeMismatch:
                    MemberReferenceHandle passingMismatch =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString("ShapePassing"),
                            byReferenceSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        passingMismatch);
                    break;

                case Scenario.BodyTypeParameterOutOfRange:
                case Scenario.BodyMethodParameterOutOfRange:
                case Scenario.BodyMethodArityMismatch:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.DeclarationTypeParameterOutOfRange:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        declarationTypeOutOfRange);
                    break;

                case Scenario.DeclarationMethodArityMismatch:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        declarationMethodArityMismatch);
                    break;

                case Scenario.ConstructedOwnerArityMismatch:
                    AddZeroArgumentConstructedRelationship(
                        metadata,
                        target,
                        body,
                        genericType,
                        genericSignature);
                    break;

                case Scenario.FunctionPointerMvarOutOfRange:
                    metadata.AddMethodImplementation(
                        target,
                        bodyMethodGeneric,
                        methodGeneric);
                    break;

                case Scenario.NestedConstructedArityMismatch:
                case Scenario.NestedConstructedArityValid:
                    AddNestedConstructedRelationship(
                        metadata,
                        target,
                        body,
                        voidSignature,
                        invalidArity:
                            scenario
                                == Scenario
                                    .NestedConstructedArityMismatch);
                    break;

                case Scenario.LocalCandidateMalformedSignature:
                    MemberReferenceHandle malformedCandidate =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString(
                                "CandidateMalformed"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        malformedCandidate);
                    break;

                case Scenario.CyclicTypeSpec:
                    TypeSpecificationHandle cyclic =
                        metadata.AddTypeSpecification(
                            AddBlob(
                                metadata,
                                0x20,
                                0x06,
                                0x08));
                    MemberReferenceHandle cyclicDeclaration =
                        metadata.AddMemberReference(
                            cyclic,
                            metadata.GetOrAddString("Cycle"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        cyclicDeclaration);
                    break;

                case Scenario.SignatureTypeSpecTrailingData:
                    TypeSpecificationHandle trailing =
                        metadata.AddTypeSpecification(
                            AddBlob(metadata, 0x08, 0x08));
                    expectedFailureSubject = trailing;
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.SignatureTypeSpecNestedCycle:
                    int firstTypeSpecRow = 1;
                    TypeSpecificationHandle firstNested =
                        metadata.AddTypeSpecification(
                            AddTypeSpecReference(
                                metadata,
                                typeSpecRow: firstTypeSpecRow + 1));
                    _ = metadata.AddTypeSpecification(
                        AddTypeSpecReference(
                            metadata,
                            typeSpecRow: firstTypeSpecRow + 1));
                    expectedFailureSubject =
                        MetadataTokens.TypeSpecificationHandle(
                            firstTypeSpecRow + 1);
                    Assert.Equal(
                        firstTypeSpecRow,
                        MetadataTokens.GetRowNumber(firstNested));
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.SignatureTypeSpecDepthBudget:
                    var deepType = new BlobBuilder();
                    for (int index = 0;
                        index
                            <= TypeSpecificationRoot
                                .MaxAuthenticationSignatureDepth;
                        index++)
                    {
                        deepType.WriteByte(0x0f);
                    }
                    deepType.WriteByte(0x08);
                    TypeSpecificationHandle deep =
                        metadata.AddTypeSpecification(
                            metadata.GetOrAddBlob(deepType));
                    expectedFailureSubject = deep;
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.SignatureTypeSpecMalformedDependency:
                    TypeSpecificationHandle malformedDependencyRoot =
                        metadata.AddTypeSpecification(
                            AddTypeSpecReference(
                                metadata,
                                typeSpecRow: 0xFFFF));
                    expectedFailureSubject =
                        MetadataTokens.TypeSpecificationHandle(
                            0xFFFF);
                    Assert.Equal(
                        1,
                        MetadataTokens.GetRowNumber(
                            malformedDependencyRoot));
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.SignatureTypeSpecDependency:
                    TypeSpecificationHandle signatureDependencyRoot =
                        metadata.AddTypeSpecification(
                            AddTypeSpecReference(
                                metadata,
                                typeSpecRow: 2));
                    TypeSpecificationHandle signatureDependency =
                        metadata.AddTypeSpecification(
                            AddBlob(metadata, 0x08));
                    Assert.Equal(
                        1,
                        MetadataTokens.GetRowNumber(
                            signatureDependencyRoot));
                    expectedFailureSubject = signatureDependency;
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.OwnerTypeSpecDependency:
                    TypeReferenceHandle dependencyContract =
                        metadata.AddTypeReference(
                            externalAssembly,
                            metadata.GetOrAddString("Contracts"),
                            metadata.GetOrAddString(
                                "IDependency`1"));
                    var dependencyOwner = new BlobBuilder();
                    dependencyOwner.WriteByte(0x15);
                    dependencyOwner.WriteByte(0x12);
                    WriteTypeDefOrRef(
                        dependencyOwner,
                        dependencyContract);
                    dependencyOwner.WriteCompressedInteger(1);
                    dependencyOwner.WriteByte(0x20);
                    WriteTypeDefOrRef(
                        dependencyOwner,
                        MetadataTokens.TypeSpecificationHandle(2));
                    dependencyOwner.WriteByte(0x08);
                    TypeSpecificationHandle ownerDependencyRoot =
                        metadata.AddTypeSpecification(
                            metadata.GetOrAddBlob(dependencyOwner));
                    TypeSpecificationHandle ownerDependency =
                        metadata.AddTypeSpecification(
                            AddBlob(metadata, 0x08));
                    Assert.Equal(
                        1,
                        MetadataTokens.GetRowNumber(
                            ownerDependencyRoot));
                    expectedFailureSubject = ownerDependency;
                    MemberReferenceHandle dependencyDeclaration =
                        metadata.AddMemberReference(
                            ownerDependencyRoot,
                            metadata.GetOrAddString("M"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        dependencyDeclaration);
                    break;

                case Scenario.CyclicTypeReference:
                    int firstCycleRow =
                        MetadataTokens.GetRowNumber(externalType) + 1;
                    TypeReferenceHandle firstCycle =
                        metadata.AddTypeReference(
                            MetadataTokens.TypeReferenceHandle(
                                firstCycleRow + 1),
                            metadata.GetOrAddString("Contracts"),
                            metadata.GetOrAddString("CycleA"));
                    metadata.AddTypeReference(
                        firstCycle,
                        default,
                        metadata.GetOrAddString("CycleB"));
                    MemberReferenceHandle cyclicTypeReference =
                        metadata.AddMemberReference(
                            firstCycle,
                            metadata.GetOrAddString("Cycle"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        cyclicTypeReference);
                    break;

                case Scenario.TypeReferenceTraversalBudget:
                    TypeReferenceHandle firstDeep = default;
                    int firstDeepRow =
                        MetadataTokens.GetRowNumber(externalType) + 1;
                    for (int index = 1;
                        index <= MetadataSafetyPolicy
                            .MaxRelationshipNodes + 1;
                        index++)
                    {
                        TypeReferenceHandle current =
                            metadata.AddTypeReference(
                                index
                                    <= MetadataSafetyPolicy
                                        .MaxRelationshipNodes
                                    ? MetadataTokens
                                        .TypeReferenceHandle(
                                            firstDeepRow + index)
                                    : moduleScope,
                                index == 1
                                    ? metadata.GetOrAddString(
                                        "Contracts")
                                    : default,
                                metadata.GetOrAddString($"Deep{index}"));
                        if (index == 1)
                            firstDeep = current;
                    }
                    MemberReferenceHandle deepDeclaration =
                        metadata.AddMemberReference(
                            firstDeep,
                            metadata.GetOrAddString("Deep"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        deepDeclaration);
                    break;

                case Scenario.BodyContextCycle:
                case Scenario.BodyContextDepthBudget:
                case Scenario.MethodDefDeclarationName:
                case Scenario.LongMethodDefDeclarationName:
                case Scenario.OverlongMethodDefDeclarationName:
                case Scenario.ExpandingControlReturnTypeNamespace:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.DistinctExternalArguments:
                    AddDistinctExternalRelationships(
                        metadata,
                        target,
                        body,
                        voidSignature);
                    break;

                case Scenario.MethodGeneric:
                    MemberReferenceHandle methodGenericReference =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString("Transform"),
                            genericMethodSignature);
                    metadata.AddMethodImplementation(
                        target,
                        bodyMethodGeneric,
                        methodGenericReference);
                    break;

                case Scenario.InheritedLocalDeclaration:
                    MemberReferenceHandle inheritedReference =
                        metadata.AddMemberReference(
                            derivedInterface,
                            metadata.GetOrAddString("Inherited"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        inheritedReference);
                    break;

                case Scenario.ReservedMemberRefParent:
                    reservedParentToPatch =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString("Reserved"),
                            voidSignature);
                    expectedFailureSubject = reservedParentToPatch;
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        reservedParentToPatch);
                    break;

                case Scenario.LocalIndexCycle:
                case Scenario.LocalIndexDepthBudget:
                case Scenario.LocalIndexMalformed:
                case Scenario.LocalIndexMalformedString:
                case Scenario.LocalIndexMalformedWideString:
                case Scenario.LocalIndexEmptyName:
                case Scenario.LocalIndexOverlongName:
                case Scenario.LocalIndexForwardParentChain:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        localThroughReference);
                    break;

                case Scenario.MethodDefSignatureDepthBudget:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.MemberRefSignatureDepthBudget:
                    MemberReferenceHandle deepReference =
                        metadata.AddMemberReference(
                            externalType,
                            metadata.GetOrAddString("Deep"),
                            deepMethodSignature);
                    expectedFailureSubject = deepReference;
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        deepReference);
                    break;

                case Scenario.ExternalMemberRefDeclarationName:
                case Scenario.LongExternalMemberRefDeclarationName:
                case Scenario.OverlongExternalMemberRefDeclarationName:
                    MemberReferenceHandle namedExternal =
                        metadata.AddMemberReference(
                            externalType,
                            metadata.GetOrAddString(
                                scenario
                                    == Scenario
                                        .LongExternalMemberRefDeclarationName
                                    ? new string(
                                        'D',
                                        LongDeclarationNameLength)
                                    : scenario
                                        == Scenario
                                            .OverlongExternalMemberRefDeclarationName
                                        ? new string(
                                            'D',
                                            OverlongDeclarationNameLength)
                                    : "External"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        namedExternal);
                    break;

                case Scenario.LongUnresolvedLocalDeclarationName:
                    MemberReferenceHandle unresolvedLocal =
                        metadata.AddMemberReference(
                            local,
                            metadata.GetOrAddString(
                                new string(
                                    'D',
                                    LongDeclarationNameLength)),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        unresolvedLocal);
                    break;

                case Scenario.LongExternalTypeName:
                    TypeReferenceHandle longExternalType =
                        metadata.AddTypeReference(
                            externalAssembly,
                            default,
                            metadata.GetOrAddString(
                                new string('N', 4_000)));
                    MemberReferenceHandle longNameDeclaration =
                        metadata.AddMemberReference(
                            longExternalType,
                            metadata.GetOrAddString("M"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        longNameDeclaration);
                    break;

                case Scenario.OverlongExternalTypeName:
                    TypeReferenceHandle overlongExternalType =
                        metadata.AddTypeReference(
                            externalAssembly,
                            default,
                            metadata.GetOrAddString(
                                new string(
                                    'N',
                                    MetadataSafetyPolicy
                                        .MaxTypeNameCharacters + 1)));
                    expectedFailureSubject = overlongExternalType;
                    MemberReferenceHandle overlongNameDeclaration =
                        metadata.AddMemberReference(
                            overlongExternalType,
                            metadata.GetOrAddString("M"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        overlongNameDeclaration);
                    break;

                case Scenario.LargeExternalPublicKey:
                    AssemblyReferenceHandle largeKeyAssembly =
                        AddAssemblyReference(
                            metadata,
                            "Large.Key.Assembly",
                            new byte[1024 * 1024]);
                    TypeReferenceHandle largeKeyType =
                        metadata.AddTypeReference(
                            largeKeyAssembly,
                            metadata.GetOrAddString("Contracts"),
                            metadata.GetOrAddString("ILargeKey"));
                    MemberReferenceHandle largeKeyDeclaration =
                        metadata.AddMemberReference(
                            largeKeyType,
                            metadata.GetOrAddString("M"),
                            voidSignature);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        largeKeyDeclaration);
                    break;

                case Scenario.RepeatedTypeSpecReferences:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    break;

                case Scenario.PendingThenRejected:
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        special);
                    metadata.AddMethodImplementation(
                        target,
                        body,
                        MetadataTokens.MemberReferenceHandle(
                            0xFFFF));
                    break;
            }

            if (scenario == Scenario.LocalIndexMalformedWideString)
            {
                _ = metadata.GetOrAddString(
                    new string('W', ushort.MaxValue + 1));
            }

            byte[] image = Serialize(metadata);
            if (!reservedParentToPatch.IsNil)
            {
                PatchMemberReferenceParentToReservedTag(
                    image,
                    reservedParentToPatch);
            }
            if (!malformedStringToPatch.IsNil)
            {
                PatchTypeDefinitionNameToInvalidString(
                    image,
                    malformedStringToPatch);
            }
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                target,
                body,
                bodyInt,
                bodyMethodGeneric,
                special,
                generic,
                genericInt,
                methodGeneric,
                CountMethodImplementations(image),
                expectedFailureSubject);
        }

        internal static Fixture CreateWideMalformedMethodImpl(
            bool declarationOperand)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("WideMethodImpl"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            BlobHandle signature =
                AddBlob(metadata, 0x20, 0x00, 0x01);
            MethodDefinitionHandle body =
                AddMethod(metadata, "Body", signature);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            TypeReferenceHandle declarationType =
                metadata.AddTypeReference(
                    MetadataTokens.EntityHandle(0x00000001),
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract"));
            MemberReferenceHandle declaration = default;
            for (int index = 0; index < 32_768; index++)
            {
                MemberReferenceHandle current =
                    metadata.AddMemberReference(
                        declarationType,
                        metadata.GetOrAddString("M"),
                        signature);
                if (index == 0)
                    declaration = current;
            }
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);

            byte[] image = Serialize(metadata);
            PatchMethodImplementationOperandToInvalidWideIndex(
                image,
                declarationOperand);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                target,
                body,
                body,
                body,
                body,
                body,
                body,
                body,
                methodImplementationCount: 1,
                expectedFailureSubject: default);
        }

        internal static Fixture CreateWideMalformedMethodImplClass()
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("WideMethodImplClass"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            BlobHandle signature =
                AddBlob(metadata, 0x20, 0x00, 0x01);
            MethodDefinitionHandle body =
                AddMethod(metadata, "Body", signature);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            for (int index = 2;
                index < ushort.MaxValue + 1;
                index++)
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Wide"),
                    metadata.GetOrAddString($"Type{index}"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));
            }
            metadata.AddMethodImplementation(
                target,
                body,
                body);

            byte[] image = Serialize(metadata);
            PatchMethodImplementationClassToInvalidWideIndex(image);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                target,
                body,
                body,
                body,
                body,
                body,
                body,
                body,
                methodImplementationCount: 1,
                expectedFailureSubject: default);
        }

        internal static Fixture
            CreateWideMalformedTypeDefinitionMethodList(
                bool declarationOwnership)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("WideTypeDefinitionMethodList"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            BlobHandle signature =
                AddBlob(metadata, 0x20, 0x00, 0x01);
            MethodDefinitionHandle body =
                AddMethod(metadata, "Body", signature);
            for (int index = 2; index < ushort.MaxValue + 1; index++)
            {
                _ = AddMethod(metadata, "Filler", signature);
            }
            MethodDefinitionHandle declaration =
                AddMethod(
                    metadata,
                    "Declaration",
                    signature,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.Interface
                    | TypeAttributes.Abstract
                    | TypeAttributes.Public,
                metadata.GetOrAddString("Contracts"),
                metadata.GetOrAddString("IContract"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                declaration);
            TypeDefinitionHandle trailing =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Trailing"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        MetadataTokens.GetRowNumber(declaration) + 1));
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);

            byte[] image = Serialize(metadata);
            PatchTypeDefinitionMethodListToInvalidWideIndex(
                image,
                declarationOwnership ? trailing : target);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                target,
                body,
                body,
                body,
                declaration,
                declaration,
                declaration,
                declaration,
                methodImplementationCount: 1,
                expectedFailureSubject:
                    declarationOwnership
                        ? declaration
                        : body);
        }

        internal static Fixture CreateExternalGenericParameterName(
            string genericParameterName)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("GenericParameterName"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            BlobHandle signature =
                AddBlob(
                    metadata,
                    0x30,
                    0x01,
                    0x01,
                    0x1e,
                    0x00,
                    0x1e,
                    0x00);
            MethodDefinitionHandle body =
                AddMethod(metadata, "Body", signature);
            metadata.AddGenericParameter(
                body,
                GenericParameterAttributes.None,
                metadata.GetOrAddString(genericParameterName),
                index: 0);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            AssemblyReferenceHandle contracts =
                AddAssemblyReference(
                    metadata,
                    "Generic.Contracts");
            TypeReferenceHandle contract =
                metadata.AddTypeReference(
                    contracts,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract"));
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    contract,
                    metadata.GetOrAddString("M"),
                    signature);
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);

            byte[] image = Serialize(metadata);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                target,
                body,
                body,
                body,
                body,
                body,
                body,
                body,
                methodImplementationCount: 1,
                expectedFailureSubject: body);
        }

        internal static Fixture
            CreateMalformedWideMethodDefSignatureHandle()
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("WideMethodSignature"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            BlobHandle signature =
                AddBlob(metadata, 0x20, 0x00, 0x01);
            _ = metadata.GetOrAddBlob(
                new byte[ushort.MaxValue + 1]);
            MethodDefinitionHandle body =
                AddMethod(metadata, "Body", signature);
            MethodDefinitionHandle declaration =
                AddMethod(
                    metadata,
                    "Declaration",
                    signature,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.Interface
                    | TypeAttributes.Abstract
                    | TypeAttributes.Public,
                metadata.GetOrAddString("Contracts"),
                metadata.GetOrAddString("IContract"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                declaration);
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);

            byte[] image = Serialize(metadata);
            PatchMethodDefinitionSignatureToInvalidWideIndex(
                image,
                body);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                target,
                body,
                body,
                body,
                declaration,
                declaration,
                declaration,
                declaration,
                methodImplementationCount: 1,
                expectedFailureSubject: body);
        }

        internal static Fixture CreateMalformedWideStringNameHandle(
            WideStringNameTarget target)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("WideStringName"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            _ = metadata.GetOrAddString(
                new string('W', ushort.MaxValue + 1));
            BlobHandle signature =
                AddBlob(metadata, 0x20, 0x00, 0x01);
            MethodDefinitionHandle body =
                AddMethod(metadata, "Body", signature);
            MethodDefinitionHandle candidate =
                AddMethod(
                    metadata,
                    "Candidate",
                    signature,
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual);
            _ = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle targetType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            TypeDefinitionHandle declarationType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    candidate);
            MemberReferenceHandle memberReference = default;
            EntityHandle declaration;
            if (target
                == WideStringNameTarget
                    .MethodDefinitionDeclaration)
            {
                declaration = candidate;
            }
            else
            {
                memberReference =
                    metadata.AddMemberReference(
                        declarationType,
                        metadata.GetOrAddString("Candidate"),
                        signature);
                declaration = memberReference;
            }
            metadata.AddMethodImplementation(
                targetType,
                body,
                declaration);

            byte[] image = Serialize(metadata);
            EntityHandle expectedFailureSubject;
            switch (target)
            {
                case WideStringNameTarget
                    .MethodDefinitionDeclaration:
                case WideStringNameTarget.LocalCandidate:
                    PatchMethodDefinitionNameToInvalidWideString(
                        image,
                        candidate);
                    expectedFailureSubject = candidate;
                    break;
                case WideStringNameTarget
                    .MemberReferenceDeclaration:
                    PatchMemberReferenceNameToInvalidWideString(
                        image,
                        memberReference);
                    expectedFailureSubject = memberReference;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(target));
            }

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-inspect-methodimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            return new Fixture(
                path,
                mvid,
                targetType,
                body,
                body,
                body,
                candidate,
                candidate,
                candidate,
                candidate,
                methodImplementationCount: 1,
                expectedFailureSubject);
        }

        static void AddGenericRelationship(
            MetadataBuilder metadata,
            TypeDefinitionHandle target,
            MethodDefinitionHandle body,
            TypeDefinitionHandle genericType,
            BlobHandle genericSignature)
        {
            int encodedType =
                MetadataTokens.GetRowNumber(genericType) << 2;
            TypeSpecificationHandle constructed =
                metadata.AddTypeSpecification(
                    AddBlob(
                        metadata,
                        0x15,
                        0x12,
                        checked((byte)encodedType),
                        0x01,
                        0x08));
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    constructed,
                    metadata.GetOrAddString("Echo"),
                    genericSignature);
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);
        }

        static void AddZeroArgumentConstructedRelationship(
            MetadataBuilder metadata,
            TypeDefinitionHandle target,
            MethodDefinitionHandle body,
            TypeDefinitionHandle genericType,
            BlobHandle genericSignature)
        {
            int encodedType =
                MetadataTokens.GetRowNumber(genericType) << 2;
            TypeSpecificationHandle constructed =
                metadata.AddTypeSpecification(
                    AddBlob(
                        metadata,
                        0x15,
                        0x12,
                        checked((byte)encodedType),
                        0x00));
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    constructed,
                    metadata.GetOrAddString("Echo"),
                    genericSignature);
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);
        }

        static void AddDistinctExternalRelationships(
            MetadataBuilder metadata,
            TypeDefinitionHandle target,
            MethodDefinitionHandle body,
            BlobHandle signature)
        {
            AssemblyReferenceHandle contracts =
                AddAssemblyReference(metadata, "Contracts.Library");
            AssemblyReferenceHandle firstAssembly =
                AddAssemblyReference(metadata, "Arguments.One");
            AssemblyReferenceHandle secondAssembly =
                AddAssemblyReference(metadata, "Arguments.Two");
            TypeReferenceHandle contract =
                metadata.AddTypeReference(
                    contracts,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract`1"));
            TypeReferenceHandle firstArgument =
                metadata.AddTypeReference(
                    firstAssembly,
                    metadata.GetOrAddString("Shared"),
                    metadata.GetOrAddString("Token"));
            TypeReferenceHandle secondArgument =
                metadata.AddTypeReference(
                    secondAssembly,
                    metadata.GetOrAddString("Shared"),
                    metadata.GetOrAddString("Token"));
            TypeSpecificationHandle firstOwner =
                AddConstructedType(
                    metadata,
                    contract,
                    firstArgument);
            TypeSpecificationHandle secondOwner =
                AddConstructedType(
                    metadata,
                    contract,
                    secondArgument);
            MemberReferenceHandle first =
                metadata.AddMemberReference(
                    firstOwner,
                    metadata.GetOrAddString("M"),
                    signature);
            MemberReferenceHandle second =
                metadata.AddMemberReference(
                    secondOwner,
                    metadata.GetOrAddString("M"),
                    signature);
            metadata.AddMethodImplementation(target, body, first);
            metadata.AddMethodImplementation(target, body, second);
        }

        static void AddNestedConstructedRelationship(
            MetadataBuilder metadata,
            TypeDefinitionHandle target,
            MethodDefinitionHandle body,
            BlobHandle signature,
            bool invalidArity)
        {
            AssemblyReferenceHandle contracts =
                AddAssemblyReference(metadata, "Nested.Contracts");
            AssemblyReferenceHandle arguments =
                AddAssemblyReference(metadata, "Nested.Arguments");
            TypeReferenceHandle contract =
                metadata.AddTypeReference(
                    contracts,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract`1"));
            TypeReferenceHandle argument =
                metadata.AddTypeReference(
                    arguments,
                    metadata.GetOrAddString("Arguments"),
                    metadata.GetOrAddString("G`1"));

            var nestedArgument = new BlobBuilder();
            nestedArgument.WriteByte(0x15);
            nestedArgument.WriteByte(0x12);
            WriteTypeDefOrRef(nestedArgument, argument);
            nestedArgument.WriteCompressedInteger(
                invalidArity ? 2 : 1);
            nestedArgument.WriteByte(0x08);
            if (invalidArity)
                nestedArgument.WriteByte(0x08);
            var owner = new BlobBuilder();
            owner.WriteByte(0x15);
            owner.WriteByte(0x12);
            WriteTypeDefOrRef(owner, contract);
            owner.WriteCompressedInteger(1);
            owner.WriteBytes(nestedArgument.ToArray());
            TypeSpecificationHandle constructedOwner =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(owner));
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    constructedOwner,
                    metadata.GetOrAddString("M"),
                    signature);
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);
        }

        static TypeSpecificationHandle AddConstructedType(
            MetadataBuilder metadata,
            TypeReferenceHandle genericType,
            TypeReferenceHandle argument)
        {
            int encodedGeneric =
                (MetadataTokens.GetRowNumber(genericType) << 2) | 1;
            int encodedArgument =
                (MetadataTokens.GetRowNumber(argument) << 2) | 1;
            return metadata.AddTypeSpecification(
                AddBlob(
                    metadata,
                    0x15,
                    0x12,
                    checked((byte)encodedGeneric),
                    0x01,
                    0x12,
                    checked((byte)encodedArgument)));
        }

        static BlobHandle AddMethodSignatureWithModifierTypeSpec(
            MetadataBuilder metadata,
            int typeSpecRow)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x01);
            signature.WriteByte(0x20);
            WriteTypeDefOrRef(
                signature,
                MetadataTokens.TypeSpecificationHandle(typeSpecRow));
            signature.WriteByte(0x08);
            return metadata.GetOrAddBlob(signature);
        }

        static BlobHandle AddMethodSignatureWithReturnType(
            MetadataBuilder metadata,
            EntityHandle returnType)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(0);
            signature.WriteByte(0x12);
            WriteTypeDefOrRef(signature, returnType);
            return metadata.GetOrAddBlob(signature);
        }

        static BlobHandle AddMethodSignatureWithRepeatedModifierTypeSpec(
            MetadataBuilder metadata,
            TypeSpecificationHandle typeSpec)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(0);
            signature.WriteByte(0x20);
            WriteTypeDefOrRef(signature, typeSpec);
            signature.WriteByte(0x20);
            WriteTypeDefOrRef(signature, typeSpec);
            signature.WriteByte(0x08);
            return metadata.GetOrAddBlob(signature);
        }

        static BlobHandle AddTypeSpecReference(
            MetadataBuilder metadata,
            int typeSpecRow)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            WriteTypeDefOrRef(
                signature,
                MetadataTokens.TypeSpecificationHandle(typeSpecRow));
            signature.WriteByte(0x08);
            return metadata.GetOrAddBlob(signature);
        }

        static void WriteTypeDefOrRef(
            BlobBuilder signature,
            EntityHandle handle)
        {
            int tag = handle.Kind switch
            {
                HandleKind.TypeDefinition => 0,
                HandleKind.TypeReference => 1,
                HandleKind.TypeSpecification => 2,
                _ => throw new ArgumentException(
                    "Expected a TypeDefOrRef handle.",
                    nameof(handle)),
            };
            signature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(handle) << 2) | tag);
        }

        static MethodDefinitionHandle AddMethod(
            MetadataBuilder metadata,
            string name,
            BlobHandle signature,
            MethodAttributes attributes =
                MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final)
            => metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));

        static AssemblyReferenceHandle AddAssemblyReference(
            MetadataBuilder metadata,
            string name,
            byte[]? publicKey = null) =>
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(name),
                new Version(1, 0, 0, 0),
                default,
                publicKey is null
                    ? default
                    : metadata.GetOrAddBlob(publicKey),
                publicKey is null
                    ? (AssemblyFlags)0
                    : AssemblyFlags.PublicKey,
                default);

        static BlobHandle AddBlob(
            MetadataBuilder metadata,
            params byte[] bytes)
        {
            var builder = new BlobBuilder();
            builder.WriteBytes(bytes);
            return metadata.GetOrAddBlob(builder);
        }

        static byte[] Serialize(MetadataBuilder metadata)
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly)
                .Serialize(image);
            return image.ToArray();
        }

        static int CountMethodImplementations(byte[] image)
        {
            using var reader = new PEReader(
                new MemoryStream(image, writable: false));
            return reader.GetMetadataReader()
                .GetTableRowCount(TableIndex.MethodImpl);
        }

        static void PatchMemberReferenceParentToReservedTag(
            byte[] image,
            MemberReferenceHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            int maxParentRows = new[]
            {
                TableIndex.TypeDef,
                TableIndex.TypeRef,
                TableIndex.ModuleRef,
                TableIndex.MethodDef,
                TableIndex.TypeSpec,
            }.Max(reader.GetTableRowCount);
            int parentIndexSize =
                maxParentRows < (1 << (16 - 3))
                    ? sizeof(ushort)
                    : sizeof(uint);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MemberRef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.MemberRef));
            uint reserved = (1U << 3) | 5U;
            if (parentIndexSize == sizeof(ushort))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    image.AsSpan(offset, sizeof(ushort)),
                    checked((ushort)reserved));
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    image.AsSpan(offset, sizeof(uint)),
                    reserved);
            }
        }

        static void PatchMethodImplementationOperandToInvalidWideIndex(
            byte[] image,
            bool declarationOperand)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(
                reader.GetTableRowCount(TableIndex.MemberRef)
                    >= 32_768);
            int typeDefinitionIndexSize =
                reader.GetTableRowCount(TableIndex.TypeDef)
                    < ushort.MaxValue
                    ? sizeof(ushort)
                    : sizeof(uint);
            int methodDefOrRefSize = sizeof(uint);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MethodImpl)
                + typeDefinitionIndexSize
                + (declarationOperand ? methodDefOrRefSize : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(offset, sizeof(uint)),
                uint.MaxValue);
        }

        static void PatchMethodDefinitionNameToInvalidWideString(
            byte[] image,
            MethodDefinitionHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(
                reader.GetHeapSize(HeapIndex.String)
                    > ushort.MaxValue);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MethodDef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.MethodDef))
                + sizeof(uint)
                + sizeof(ushort)
                + sizeof(ushort);
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(offset, sizeof(uint)),
                uint.MaxValue);
        }

        static void PatchMemberReferenceNameToInvalidWideString(
            byte[] image,
            MemberReferenceHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(
                reader.GetHeapSize(HeapIndex.String)
                    > ushort.MaxValue);
            int maxParentRows = new[]
            {
                TableIndex.TypeDef,
                TableIndex.TypeRef,
                TableIndex.ModuleRef,
                TableIndex.MethodDef,
                TableIndex.TypeSpec,
            }.Max(reader.GetTableRowCount);
            int parentIndexSize =
                maxParentRows < (1 << (16 - 3))
                    ? sizeof(ushort)
                    : sizeof(uint);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MemberRef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.MemberRef))
                + parentIndexSize;
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(offset, sizeof(uint)),
                uint.MaxValue);
        }

        static void PatchMethodImplementationClassToInvalidWideIndex(
            byte[] image)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(
                reader.GetTableRowCount(TableIndex.TypeDef)
                    > ushort.MaxValue);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MethodImpl);
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(offset, sizeof(uint)),
                uint.MaxValue);
        }

        static void PatchTypeDefinitionMethodListToInvalidWideIndex(
            byte[] image,
            TypeDefinitionHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(
                reader.GetTableRowCount(TableIndex.MethodDef)
                    > ushort.MaxValue);
            int stringIndexSize =
                reader.GetHeapSize(HeapIndex.String)
                    <= ushort.MaxValue
                    ? sizeof(ushort)
                    : sizeof(uint);
            int maxTypeDefOrRefRows = new[]
            {
                TableIndex.TypeDef,
                TableIndex.TypeRef,
                TableIndex.TypeSpec,
            }.Max(reader.GetTableRowCount);
            int typeDefOrRefIndexSize =
                maxTypeDefOrRefRows < (1 << (16 - 2))
                    ? sizeof(ushort)
                    : sizeof(uint);
            int fieldIndexSize =
                reader.GetTableRowCount(TableIndex.Field)
                    <= ushort.MaxValue
                    ? sizeof(ushort)
                    : sizeof(uint);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.TypeDef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.TypeDef))
                + sizeof(uint)
                + (2 * stringIndexSize)
                + typeDefOrRefIndexSize
                + fieldIndexSize;
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(offset, sizeof(uint)),
                uint.MaxValue);
        }

        static void PatchMethodDefinitionSignatureToInvalidWideIndex(
            byte[] image,
            MethodDefinitionHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            Assert.True(
                reader.GetHeapSize(HeapIndex.Blob)
                    > ushort.MaxValue);
            int stringIndexSize =
                reader.GetHeapSize(HeapIndex.String)
                    <= ushort.MaxValue
                    ? sizeof(ushort)
                    : sizeof(uint);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MethodDef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.MethodDef))
                + sizeof(uint)
                + sizeof(ushort)
                + sizeof(ushort)
                + stringIndexSize;
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(offset, sizeof(uint)),
                uint.MaxValue);
        }

        static void PatchTypeDefinitionNameToInvalidString(
            byte[] image,
            TypeDefinitionHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            int stringIndexSize =
                reader.GetHeapSize(HeapIndex.String)
                    < ushort.MaxValue
                    ? sizeof(ushort)
                    : sizeof(uint);
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.TypeDef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.TypeDef))
                + sizeof(uint);
            if (stringIndexSize == sizeof(ushort))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    image.AsSpan(offset, sizeof(ushort)),
                    ushort.MaxValue);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    image.AsSpan(offset, sizeof(uint)),
                    uint.MaxValue);
            }
        }

        public void Dispose() => File.Delete(Path);
    }
}
