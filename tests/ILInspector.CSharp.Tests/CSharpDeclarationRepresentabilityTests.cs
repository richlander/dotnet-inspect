using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpDeclarationRepresentabilityTests
{
    [Fact]
    public void CDR001_CDR004_CDR007_PinnedInt32OperatorPostsDecidesAndRenders()
    {
        CSharpMethodDeclarationPost post = CapturePinnedInt32Operator();
        MetadataMethodDeclarationEvidence method = Assert.IsType<
            MetadataMethodDeclarationResult.Posted>(post.Method)
            .Evidence;
        Assert.Equal(
            MethodImplAttributes.IL,
            method.ImplementationAttributes);
        Assert.Equal(
            MethodAttributes.Private
                | MethodAttributes.Static
                | MethodAttributes.HideBySig,
            method.Attributes);
        var local = Assert.IsType<
            MetadataDeclarationDefinitionDisposition.LocalResolved>(
                Assert.Single(post.ImplementationOccurrences)
                    .Relationship.Definition);
        Assert.Equal(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName,
            local.Attributes);

        CSharpDeclarationRepresentabilityResult result =
            CSharpDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp11));
        Assert.True(
            result is
                CSharpDeclarationRepresentabilityResult.Representable,
            result.ToString());
        var represented =
            (CSharpDeclarationRepresentabilityResult.Representable)result;

        Assert.Equal(
            CSharpDeclarationKind.ExplicitInterfaceOperator,
            represented.Request.Kind);
        Assert.Equal(
            CSharpLanguageVersion.CSharp11,
            represented.Request.Profile.Version);
        Assert.Equal(post.Request.Type, represented.Request.ContainingType);
        Assert.Equal(post.Request.Method, represented.Request.Body);
        Assert.Equal(
            Assert.Single(post.ImplementationOccurrences)
                .Relationship.DeclarationOwner,
            represented.Request.ExplicitInterfaceIdentity);
        Assert.Equal(
            method.Signature,
            represented.Request.Signature);
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        Assert.False(containing.IsByRefLike);
        Assert.Equal(
            containing.PrimitiveAlias ?? containing.OpenSelfIdentity,
            represented.Request.ContainingTypeIdentity);
        Assert.Equal(
            "static int "
                + "global::System.Numerics.IAdditionOperators"
                + "<int, int, int>.operator +"
                + "(int left, int right) => throw null;",
            CSharpAcceptedDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR001_MultipleMethodImplOccurrencesAreNotCherryPicked()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 2,
            interfaceImplementationCount: 1);
        CSharpMethodDeclarationPost post = fixture.Capture();

        Assert.Equal(2, post.ImplementationOccurrences.Length);
        Assert.NotEqual(
            post.ImplementationOccurrences[0]
                .Relationship.Relationship,
            post.ImplementationOccurrences[1]
                .Relationship.Relationship);
        var refused = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationRefusalReason
                .MultipleMethodImplementations,
            refused.Reason);
    }

    [Fact]
    public void CDR001_RepeatedInterfaceImplAssociationsAreNotCollapsed()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 2);
        CSharpMethodDeclarationPost post = fixture.Capture();

        var related = Assert.IsType<
            MetadataInterfaceImplementationResult.Related>(
                Assert.Single(post.ImplementationOccurrences)
                    .InterfaceResult);
        Assert.Equal(2, related.Relationships.Length);
        Assert.NotEqual(
            related.Relationships[0].Relationship,
            related.Relationships[1].Relationship);
        var refused = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationRefusalReason
                .MultipleInterfaceImplementations,
            refused.Reason);
    }

    [Fact]
    public void CDR001_LaterRejectedAssociationPreventsMultiplicityRefusal()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 2,
            interfaceImplementationCount: 1);
        CSharpMethodDeclarationPost post = fixture.Capture();
        CSharpMethodImplementationPost second =
            post.ImplementationOccurrences[1];
        MetadataInterfaceImplementationResult rejected =
            fixture.RejectInterface(
                second.Relationship.DeclarationOwner);
        CSharpMethodDeclarationPost incomplete = post with
        {
            ImplementationOccurrences =
                post.ImplementationOccurrences.SetItem(
                    1,
                    second with
                    {
                        InterfaceResult = rejected,
                    }),
        };

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    incomplete,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason
                .InterfaceImplementationRejected,
            unavailable.Reason);
        Assert.Equal(1, unavailable.RelationshipOccurrence);
    }

    [Fact]
    public void CDR002_SameSpellingFromDifferentScopeDoesNotJoin()
    {
        CSharpMethodDeclarationPost post = CapturePinnedInt32Operator();
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            interfaceNamespace: "System.Numerics",
            interfaceName: "IAdditionOperators`3",
            interfaceGenericArity: 3);
        MetadataTypeDeclarationResult otherOwner =
            fixture.PostInterfaceDeclaration();
        CSharpMethodImplementationPost occurrence =
            Assert.Single(post.ImplementationOccurrences);
        CSharpMethodDeclarationPost mismatched = post with
        {
            ImplementationOccurrences =
            [
                occurrence with
                {
                    DeclarationOwner = otherOwner,
                },
            ],
        };

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    mismatched,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason
                .DeclarationOwnerIdentityMismatch,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_InterfaceAbsenceIsUnavailable()
    {
        CSharpMethodDeclarationPost post = CapturePinnedInt32Operator();
        CSharpMethodImplementationPost occurrence =
            Assert.Single(post.ImplementationOccurrences);
        MetadataInterfaceImplementationResult absent =
            CaptureAbsentInterfaceResult(
                post.Request.Type,
                occurrence.Relationship.DeclarationOwner);
        CSharpMethodDeclarationPost missing = post with
        {
            ImplementationOccurrences =
            [
                occurrence with
                {
                    InterfaceResult = absent,
                },
            ],
        };

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    missing,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason
                .InterfaceImplementationAbsent,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_MissingAssociationPostIsUnavailable()
    {
        CSharpMethodDeclarationPost post = CapturePinnedInt32Operator();
        CSharpMethodImplementationPost occurrence =
            Assert.Single(post.ImplementationOccurrences);
        CSharpMethodDeclarationPost missing = post with
        {
            ImplementationOccurrences =
            [
                occurrence with
                {
                    InterfaceRequest = null,
                    InterfaceResult = null,
                },
            ],
        };

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    missing,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason
                .InterfaceImplementationNotPosted,
            unavailable.Reason);
    }

    [Fact]
    public void CDR001_NonInterfaceOwnerRejectsExtraAssociationEvidence()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 0,
            ownerIsInterface: false);
        CSharpMethodDeclarationPost post = fixture.Capture();
        CSharpMethodImplementationPost occurrence =
            Assert.Single(post.ImplementationOccurrences);
        CSharpMethodImplementationPost canonical =
            Assert.Single(
                CapturePinnedInt32Operator()
                    .ImplementationOccurrences);
        CSharpMethodDeclarationPost inconsistent = post with
        {
            ImplementationOccurrences =
            [
                occurrence with
                {
                    InterfaceRequest = canonical.InterfaceRequest,
                    InterfaceResult = canonical.InterfaceResult,
                },
            ],
        };

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    inconsistent,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason
                .InterfaceImplementationMismatch,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_ClassOwnerPrecedesMultiplicityRefusal()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 2,
            interfaceImplementationCount: 0,
            ownerIsInterface: false);
        CSharpMethodDeclarationPost post = fixture.Capture();

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_RejectedDeclarationPublishesNoAcceptedRequest()
    {
        CSharpMethodDeclarationPost post =
            CaptureRejectedPinnedInt32Operator();

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason
                .MethodDeclarationRejected,
            unavailable.Reason);
        Assert.Equal(
            CSharpLanguageVersion.CSharp11,
            unavailable.Profile.Version);
    }

    [Fact]
    public void CDR003_OrdinaryOperatorNameIsNotAuthenticatedAsOperator()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 0,
            interfaceImplementationCount: 0);
        CSharpMethodDeclarationPost post = fixture.Capture();

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_ConversionRemainsOutsideInitialBoundary()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            operatorName: "op_Explicit");
        CSharpMethodDeclarationPost post = fixture.Capture();

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_VoidAdditionIsLanguageUnrepresentable()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            returnsVoid: true);
        CSharpMethodDeclarationPost post = fixture.Capture();

        var refused = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationRefusalReason.UnsupportedOperatorSignature,
            refused.Reason);
    }

    [Fact]
    public void CDR003_VoidOperandIsNotAnAdditionDeclaration()
    {
        MetadataTypeIdentity intType =
            new MetadataTypeIdentity.Primitive(
                new(
                    InertText.TextPolicy.Field,
                    "int"));
        MetadataTypeIdentity voidType =
            new MetadataTypeIdentity.Primitive(
                new(
                    InertText.TextPolicy.Field,
                    "void"));
        var signature = new MetadataMethodSignatureIdentity(
            Header: 0,
            GenericParameterCount: 0,
            RequiredParameterCount: 2,
            ReturnType: intType,
            ParameterTypes: [intType, voidType]);

        Assert.False(
            CSharpDeclarationRepresentability
                .IsAdditionSignatureShape(signature));
    }

    [Fact]
    public void CDR003_StaticContainingClassIsOutsideInitialBoundary()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            targetAttributes:
                TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            targetIsValueType: false);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        Assert.Equal(
            MetadataTypeDeclarationCategory.Class,
            containing.Category);
        Assert.True(
            containing.Attributes.HasFlag(TypeAttributes.Abstract));
        Assert.True(
            containing.Attributes.HasFlag(TypeAttributes.Sealed));

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_VoidArrayReturnIsOutsideInitialBoundary()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            returnsVoidArray: true);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataMethodSignatureIdentity signature = Assert.IsType<
            MetadataMethodDeclarationResult.Posted>(post.Method)
            .Evidence.Signature;
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        MetadataTypeIdentity containingIdentity =
            containing.PrimitiveAlias ?? containing.OpenSelfIdentity;
        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            containing.Category);
        Assert.Contains(
            containingIdentity,
            signature.ParameterTypes);
        Assert.IsType<MetadataTypeIdentity.SzArray>(
            signature.ReturnType);
        Assert.True(
            CSharpDeclarationRepresentability
                .IsAdditionSignatureShape(signature));
        Assert.False(
            CSharpDeclarationRepresentability.TrySpellType(
                signature.ReturnType,
                out _));

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_AuthoredValueTypeNeighborIsRepresentable()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataMethodSignatureIdentity signature = Assert.IsType<
            MetadataMethodDeclarationResult.Posted>(post.Method)
            .Evidence.Signature;
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        MetadataTypeIdentity containingIdentity =
            containing.PrimitiveAlias ?? containing.OpenSelfIdentity;

        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            containing.Category);
        Assert.Contains(
            containingIdentity,
            signature.ParameterTypes);
        Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Representable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
    }

    [Fact]
    public void CDR003_RestrictedClrReturnTypesAreOutsideInitialBoundary()
    {
        foreach (string name in new[]
        {
            "TypedReference",
            "ArgIterator",
            "RuntimeArgumentHandle",
        })
        {
            using AuthoredFixture fixture = AuthoredFixture.Create(
                methodImplementationCount: 1,
                interfaceImplementationCount: 1,
                restrictedReturnType: name);
            CSharpMethodDeclarationPost post = fixture.Capture();
            MetadataMethodSignatureIdentity signature = Assert.IsType<
                MetadataMethodDeclarationResult.Posted>(post.Method)
                .Evidence.Signature;
            MetadataTypeDeclarationEvidence containing = Assert.IsType<
                MetadataTypeDeclarationResult.Posted>(post.ContainingType)
                .Evidence;

            Assert.Equal(
                MetadataTypeDeclarationCategory.Struct,
                containing.Category);
            Assert.Contains(
                containing.PrimitiveAlias
                    ?? containing.OpenSelfIdentity,
                signature.ParameterTypes);
            Assert.True(
                CSharpDeclarationRepresentability
                    .IsAdditionSignatureShape(signature));
            Assert.False(
                CSharpDeclarationRepresentability.TrySpellType(
                    signature.ReturnType,
                    out _));

            var unavailable = Assert.IsType<
                CSharpDeclarationRepresentabilityResult.Unavailable>(
                    CSharpDeclarationRepresentability.Decide(
                        post,
                        new(CSharpLanguageVersion.CSharp11)));
            Assert.Equal(
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary,
                unavailable.Reason);
        }
    }

    public interface IRefStructAddition<TSelf>
        where TSelf : IRefStructAddition<TSelf>, allows ref struct
    {
        static abstract TSelf operator +(TSelf left, TSelf right);
    }

    public ref struct RefStructAddition :
        IRefStructAddition<RefStructAddition>
    {
        static RefStructAddition
            IRefStructAddition<RefStructAddition>.operator +(
                RefStructAddition left,
                RefStructAddition right) =>
            left;
    }

    [Theory]
    [InlineData(SpellingCollisionKind.CompleteDefinition)]
    [InlineData(SpellingCollisionKind.NamespaceNestedDefinition)]
    public void CDR002_DistinctDefinitionsCannotShareAcceptedSpelling(
        SpellingCollisionKind collision)
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            spellingCollision: collision);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataMethodSignatureIdentity signature = Assert.IsType<
            MetadataMethodDeclarationResult.Posted>(post.Method)
            .Evidence.Signature;
        var returnType = Assert.IsType<MetadataTypeIdentity.Named>(
            signature.ReturnType);
        var secondOperand = Assert.IsType<MetadataTypeIdentity.Named>(
            signature.ParameterTypes[1]);

        Assert.NotEqual(
            returnType.Definition.Scope,
            secondOperand.Definition.Scope);
        if (collision
            == SpellingCollisionKind.NamespaceNestedDefinition)
        {
            Assert.Equal(
                "Collision",
                MetadataDeclarationText.RenderNamespace(
                    returnType.Definition));
            Assert.Equal(
                2,
                MetadataDeclarationText.GetSegmentCount(
                    returnType.Definition));
            Assert.Equal(
                "Collision.Outer",
                MetadataDeclarationText.RenderNamespace(
                    secondOperand.Definition));
            Assert.Equal(
                1,
                MetadataDeclarationText.GetSegmentCount(
                    secondOperand.Definition));
        }
        Assert.True(
            CSharpDeclarationRepresentability.TrySpellType(
                returnType,
                out CSharpTypeSpelling? returnSpelling));
        Assert.True(
            CSharpDeclarationRepresentability.TrySpellType(
                secondOperand,
                out CSharpTypeSpelling? operandSpelling));
        Assert.Equal(
            returnSpelling.Source,
            operandSpelling.Source);
        Assert.IsType<MetadataMethodImplementationResult.Related>(
            post.Implementations);
        Assert.False(
            CSharpDeclarationRepresentability
                .NamedTypeSpellingsAreUnambiguous(
                    Assert.IsType<
                        MetadataTypeDeclarationResult.Posted>(
                            post.ContainingType)
                        .Evidence.OpenSelfIdentity,
                    Assert.Single(post.ImplementationOccurrences)
                        .Relationship.DeclarationOwner,
                    signature));

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Theory]
    [InlineData(SpellingCollisionKind.DistinctNestedLeaves)]
    [InlineData(SpellingCollisionKind.NamespaceTypeQualifier)]
    public void CDR002_DistinctQualifierTargetsCannotShareAcceptedSpelling(
        SpellingCollisionKind collision)
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            spellingCollision: collision);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataMethodSignatureIdentity signature = Assert.IsType<
            MetadataMethodDeclarationResult.Posted>(post.Method)
            .Evidence.Signature;
        var returnType = Assert.IsType<MetadataTypeIdentity.Named>(
            signature.ReturnType);
        var secondOperand = Assert.IsType<MetadataTypeIdentity.Named>(
            signature.ParameterTypes[1]);

        Assert.True(
            CSharpDeclarationRepresentability.TrySpellType(
                returnType,
                out CSharpTypeSpelling? returnSpelling));
        Assert.True(
            CSharpDeclarationRepresentability.TrySpellType(
                secondOperand,
                out CSharpTypeSpelling? operandSpelling));
        Assert.Equal(
            "global::Collision.Outer.Right",
            returnSpelling.Source);
        Assert.Equal(
            "global::Collision.Outer.Left",
            operandSpelling.Source);

        MetadataNamedTypeIdentity operandQualifier =
            secondOperand.Definition.GetDefinitionPrefix(1);
        Assert.True(
            CSharpDeclarationRepresentability.TrySpellType(
                new MetadataTypeIdentity.Named(
                    operandQualifier,
                    IsValueType: false),
                out CSharpTypeSpelling? operandQualifierSpelling));
        Assert.Equal(
            "global::Collision.Outer",
            operandQualifierSpelling.Source);
        if (collision == SpellingCollisionKind.DistinctNestedLeaves)
        {
            MetadataNamedTypeIdentity returnQualifier =
                returnType.Definition.GetDefinitionPrefix(1);
            Assert.NotEqual(
                returnQualifier.Scope,
                operandQualifier.Scope);
            Assert.True(
                CSharpDeclarationRepresentability.TrySpellType(
                    new MetadataTypeIdentity.Named(
                        returnQualifier,
                        IsValueType: false),
                    out CSharpTypeSpelling? returnQualifierSpelling));
            Assert.Equal(
                operandQualifierSpelling.Source,
                returnQualifierSpelling.Source);
        }
        else
        {
            Assert.Equal(
                "Collision.Outer",
                MetadataDeclarationText.RenderNamespace(
                    returnType.Definition));
            Assert.Equal(
                1,
                MetadataDeclarationText.GetSegmentCount(
                    returnType.Definition));
        }

        Assert.IsType<MetadataMethodImplementationResult.Related>(
            post.Implementations);
        Assert.False(
            CSharpDeclarationRepresentability
                .NamedTypeSpellingsAreUnambiguous(
                    Assert.IsType<
                        MetadataTypeDeclarationResult.Posted>(
                            post.ContainingType)
                        .Evidence.OpenSelfIdentity,
                    Assert.Single(post.ImplementationOccurrences)
                        .Relationship.DeclarationOwner,
                    signature));

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR003_NonContainingOperandsAreOutsideInitialBoundary()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            signatureUsesContainingType: false);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataMethodSignatureIdentity signature = Assert.IsType<
            MetadataMethodDeclarationResult.Posted>(post.Method)
            .Evidence.Signature;
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        Assert.True(
            CSharpDeclarationRepresentability
                .IsAdditionSignatureShape(signature));
        Assert.DoesNotContain(
            containing.PrimitiveAlias ?? containing.OpenSelfIdentity,
            signature.ParameterTypes);

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void CDR005_LanguageProfileParticipatesInAcceptance()
    {
        CSharpMethodDeclarationPost post = CapturePinnedInt32Operator();

        var refused = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp10)));
        Assert.Equal(
            CSharpDeclarationRefusalReason.UnsupportedLanguageProfile,
            refused.Reason);
        Assert.Equal(
            CSharpLanguageVersion.CSharp10,
            refused.Profile.Version);

        CSharpDeclarationRepresentabilityResult accepted =
            CSharpDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp11));
        Assert.True(
            accepted is
                CSharpDeclarationRepresentabilityResult.Representable,
            accepted.ToString());
    }

    [Fact]
    public void CDR005_RefStructInterfacesRequireCSharp13()
    {
        CSharpMethodDeclarationPost post =
            CaptureCompilerProducedRefStructOperator();
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        Assert.True(containing.IsByRefLike);

        var refused = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));
        Assert.Equal(
            CSharpDeclarationRefusalReason.UnsupportedLanguageProfile,
            refused.Reason);
        Assert.Equal(
            CSharpLanguageVersion.CSharp11,
            refused.Profile.Version);

        CSharpDeclarationRepresentabilityResult accepted =
            CSharpDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp13));
        Assert.True(
            accepted is
                CSharpDeclarationRepresentabilityResult.Representable,
            accepted.ToString());
    }

    [Fact]
    public void CDR005_NestedLookalikeDoesNotRequireCSharp13()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            refLikeMarker: RefLikeMarkerKind.NestedLookalike);
        CSharpMethodDeclarationPost post = fixture.Capture();
        MetadataTypeDeclarationEvidence containing = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(post.ContainingType)
            .Evidence;
        Assert.False(containing.IsByRefLike);

        CSharpDeclarationRepresentabilityResult accepted =
            CSharpDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp11));

        Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Representable>(
                accepted);
    }

    [Fact]
    public void CDR005_UnresolvedAttributeOwnerIsUnavailable()
    {
        using AuthoredFixture fixture = AuthoredFixture.Create(
            methodImplementationCount: 1,
            interfaceImplementationCount: 1,
            refLikeMarker: RefLikeMarkerKind.UnresolvedOwner);
        CSharpMethodDeclarationPost post = fixture.Capture();
        Assert.IsType<MetadataTypeDeclarationResult.Rejected>(
            post.ContainingType);

        var unavailable = Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                CSharpDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp11)));

        Assert.Equal(
            CSharpDeclarationUnavailableReason.ContainingTypeRejected,
            unavailable.Reason);
    }

    [Fact]
    public void CDR004_ParameterSpellingProducesUniqueIdentifiers()
    {
        MetadataParameterMarkerEvidence markers =
            new(
                IsComplete: true,
                IsReadOnlyCount: 0,
                RequiresLocationCount: 0,
                ParamArrayCount: 0,
                ParamCollectionCount: 0,
                ScopedRefCount: 0,
                UnscopedRefCount: 0);
        ImmutableArray<string> names =
            CSharpDeclarationRepresentability.SpellParameterNames(
            [
                new(
                    HasRow: true,
                    Name: new(
                        InertText.TextPolicy.Field,
                        "left"),
                    Attributes: ParameterAttributes.None,
                    Markers: markers),
                new(
                    HasRow: true,
                    Name: new(
                        InertText.TextPolicy.Field,
                        "le\u200Dft"),
                    Attributes: ParameterAttributes.None,
                    Markers: markers),
                new(
                    HasRow: true,
                    Name: new(
                        InertText.TextPolicy.Field,
                        "arg1"),
                    Attributes: ParameterAttributes.None,
                    Markers: markers),
            ]);

        Assert.Equal(["left", "_arg1", "arg1"], names);
    }

    [Fact]
    public void CDR004_FallbackNamesReserveLaterMetadataNames()
    {
        MetadataParameterMarkerEvidence markers =
            new(
                IsComplete: true,
                IsReadOnlyCount: 0,
                RequiresLocationCount: 0,
                ParamArrayCount: 0,
                ParamCollectionCount: 0,
                ScopedRefCount: 0,
                UnscopedRefCount: 0);
        ImmutableArray<string> names =
            CSharpDeclarationRepresentability.SpellParameterNames(
            [
                new(
                    HasRow: false,
                    Name: null,
                    Attributes: ParameterAttributes.None,
                    Markers: markers),
                new(
                    HasRow: true,
                    Name: new(
                        InertText.TextPolicy.Field,
                        "arg0"),
                    Attributes: ParameterAttributes.None,
                    Markers: markers),
            ]);

        Assert.Equal(["_arg0", "arg0"], names);
    }

    [Fact]
    public void CDR003_VarArgAdditionIsNotADeclaration()
    {
        MetadataTypeIdentity intType =
            new MetadataTypeIdentity.Primitive(
                new(
                    InertText.TextPolicy.Field,
                    "int"));
        var signature = new MetadataMethodSignatureIdentity(
            Header: (byte)SignatureCallingConvention.VarArgs,
            GenericParameterCount: 0,
            RequiredParameterCount: 2,
            ReturnType: intType,
            ParameterTypes: [intType, intType]);

        Assert.False(
            CSharpDeclarationRepresentability
                .IsAdditionSignatureShape(signature));
    }

    [Fact]
    public void CDR002_TypeSpellingRejectsIdentityChangingFormatCharacters()
    {
        var scope = new MetadataTypeScopeIdentity(
            MetadataTypeScopeKind.CurrentModule,
            Guid.NewGuid(),
            new(
                InertText.TextPolicy.Field,
                "fixture.dll"),
            Assembly: null);
        var formattedNamespace =
            new MetadataTypeIdentity.Named(
                new(
                    scope,
                    new(
                        InertText.TextPolicy.Field,
                        "Na\u200Dmespace"),
                    [
                        new(
                            InertText.TextPolicy.Field,
                            "Type"),
                    ],
                    [0]),
                IsValueType: false);
        var formattedType =
            new MetadataTypeIdentity.Named(
                new(
                    scope,
                    new(
                        InertText.TextPolicy.Field,
                        "Namespace"),
                    [
                        new(
                            InertText.TextPolicy.Field,
                            "Ty\u200Dpe"),
                    ],
                    [0]),
                IsValueType: false);

        Assert.False(
            CSharpDeclarationRepresentability.TrySpellType(
                formattedNamespace,
                out _));
        Assert.False(
            CSharpDeclarationRepresentability.TrySpellType(
                formattedType,
                out _));
    }

    [Fact]
    public void CDR003_TypeSpellingRejectsCSharpRestrictedTypes()
    {
        var scope = new MetadataTypeScopeIdentity(
            MetadataTypeScopeKind.AssemblyReference,
            Guid.Empty,
            ModuleName: null,
            new(
                new(
                    InertText.TextPolicy.Field,
                    "System.Private.CoreLib"),
                Version: null,
                Culture: null,
                PublicKeyToken: null));
        foreach (string name in new[]
        {
            "Void",
            "TypedReference",
            "ArgIterator",
            "RuntimeArgumentHandle",
        })
        {
            var restricted = new MetadataTypeIdentity.Named(
                new(
                    scope,
                    new(
                        InertText.TextPolicy.Field,
                        "System"),
                    [
                        new(
                            InertText.TextPolicy.Field,
                            name),
                    ],
                    [0]),
                IsValueType: true);

            Assert.False(
                CSharpDeclarationRepresentability.TrySpellType(
                    restricted,
                    out _));
            Assert.False(
                CSharpDeclarationRepresentability.TrySpellType(
                    new MetadataTypeIdentity.SzArray(restricted),
                    out _));
        }
    }

    [Fact]
    public void CDR006_FailureOutcomeDoesNotPublishArtifactText()
    {
        CSharpMethodDeclarationPost post = CapturePinnedInt32Operator();
        CSharpMethodImplementationPost occurrence =
            Assert.Single(post.ImplementationOccurrences);
        const string Hostile = "op_Addition\r\n\u001b[31m";
        CSharpMethodDeclarationPost inconsistent = post with
        {
            ImplementationOccurrences =
            [
                occurrence with
                {
                    Relationship = occurrence.Relationship with
                    {
                        DeclarationName = new(
                            InertText.TextPolicy.Field,
                            Hostile),
                    },
                },
            ],
        };

        CSharpDeclarationRepresentabilityResult result =
            CSharpDeclarationRepresentability.Decide(
                inconsistent,
                new(CSharpLanguageVersion.CSharp11));

        Assert.IsType<
            CSharpDeclarationRepresentabilityResult.Unavailable>(
                result);
        Assert.DoesNotContain(Hostile, result.ToString());
        Assert.All(
            result.ToString(),
            character => Assert.False(
                CSharpText.CSharpIdentifier.IsRenderingHazard(character)));
    }

    [Fact]
    public void CDR007_PublicPostAndOutcomeGraphsCarryNoLiveAuthority()
    {
        Assert.Empty(
            typeof(CSharpMethodDeclarationPost).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(
            typeof(CSharpMethodImplementationPost).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));

        Type[] prohibited =
        [
            typeof(MetadataReader),
            typeof(PEReader),
            typeof(Stream),
            typeof(MetadataDeclarationSession),
            typeof(MetadataOperationContext),
        ];
        Type[] roots =
        [
            typeof(CSharpMethodDeclarationPost),
            typeof(CSharpMethodImplementationPost),
            typeof(CSharpDeclarationRepresentabilityResult),
            typeof(CSharpAcceptedDeclarationRequest),
        ];

        foreach (Type root in roots)
        {
            Assert.DoesNotContain(
                EnumeratePublicGraph(root),
                type => prohibited.Any(prohibitedType =>
                    prohibitedType.IsAssignableFrom(type)));
        }
    }

    static CSharpMethodDeclarationPost CapturePinnedInt32Operator()
    {
        string path = PinnedCoreLibraryPath();
        (MetadataTypeDefinitionAddress type,
            MetadataMethodAddress method) =
            FindPinnedInt32Operator(path);

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return CSharpMethodDeclarationPost.Capture(
            declarations,
            type,
            method,
            TestContext.Current.CancellationToken);
    }

    static CSharpMethodDeclarationPost
        CaptureCompilerProducedRefStructOperator()
    {
        string path = typeof(RefStructAddition).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            MetadataTokens.TypeDefinitionHandle(
                typeof(RefStructAddition).MetadataToken
                    & 0x00ff_ffff);
        MethodDefinitionHandle method = reader.GetTypeDefinition(type)
            .GetMethods()
            .Single(handle => reader.GetString(
                    reader.GetMethodDefinition(handle).Name)
                .EndsWith(".op_Addition", StringComparison.Ordinal));

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return CSharpMethodDeclarationPost.Capture(
            declarations,
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataMethodAddress.Create(reader, method),
            TestContext.Current.CancellationToken);
    }

    static CSharpMethodDeclarationPost
        CaptureRejectedPinnedInt32Operator()
    {
        string path = PinnedCoreLibraryPath();
        (MetadataTypeDefinitionAddress type,
            MetadataMethodAddress method) =
            FindPinnedInt32Operator(path);

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(maxMetadataRows: 0));
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return CSharpMethodDeclarationPost.Capture(
            declarations,
            type,
            method,
            TestContext.Current.CancellationToken);
    }

    static MetadataInterfaceImplementationResult
        CaptureAbsentInterfaceResult(
            MetadataTypeDefinitionAddress type,
            MetadataTypeIdentity owner)
    {
        string path = PinnedCoreLibraryPath();
        MetadataTypeIdentity absentOwner = owner switch
        {
            MetadataTypeIdentity.GenericInstance generic =>
                generic with
                {
                    Definition = generic.Definition with
                    {
                        Namespace = new(
                            InertText.TextPolicy.Field,
                            "Missing"),
                    },
                },
            _ => throw new InvalidOperationException(
                "The pinned owner must be constructed."),
        };

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.Relate(
            type,
            absentOwner,
            TestContext.Current.CancellationToken);
    }

    static (MetadataTypeDefinitionAddress Type,
        MetadataMethodAddress Method)
        FindPinnedInt32Operator(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            return reader.GetString(definition.Namespace) == "System"
                && reader.GetString(definition.Name) == "Int32";
        });
        MethodDefinitionHandle method =
            reader.GetTypeDefinition(type).GetMethods().Single(handle =>
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
        return (
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataMethodAddress.Create(reader, method));
    }

    static string PinnedCoreLibraryPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");

    static IEnumerable<Type> EnumeratePublicGraph(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(root);
        while (pending.TryPop(out Type? type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!seen.Add(type))
                continue;

            yield return type;
            if (type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Guid)
                || type == typeof(Version))
            {
                continue;
            }

            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                    pending.Push(argument);
            }

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Push(property.PropertyType);
            }
        }
    }

    public enum SpellingCollisionKind
    {
        None,
        CompleteDefinition,
        NamespaceNestedDefinition,
        DistinctNestedLeaves,
        NamespaceTypeQualifier,
    }

    enum RefLikeMarkerKind
    {
        None,
        NestedLookalike,
        UnresolvedOwner,
    }

    sealed class AuthoredFixture : IDisposable
    {
        readonly string _path;
        readonly MetadataTypeDefinitionAddress _target;
        readonly MetadataMethodAddress _body;
        readonly MetadataTypeDefinitionAddress _interface;

        AuthoredFixture(
            string path,
            MetadataTypeDefinitionAddress target,
            MetadataMethodAddress body,
            MetadataTypeDefinitionAddress @interface)
        {
            _path = path;
            _target = target;
            _body = body;
            _interface = @interface;
        }

        internal static AuthoredFixture Create(
            int methodImplementationCount,
            int interfaceImplementationCount,
            string interfaceNamespace = "Contracts",
            string interfaceName = "IOperator",
            int interfaceGenericArity = 0,
            string operatorName = "op_Addition",
            bool returnsVoid = false,
            bool ownerIsInterface = true,
            bool returnsVoidArray = false,
            bool signatureUsesContainingType = true,
            TypeAttributes targetAttributes =
                TypeAttributes.Public | TypeAttributes.Sealed,
            bool targetIsValueType = true,
            string? restrictedReturnType = null,
            SpellingCollisionKind spellingCollision =
                SpellingCollisionKind.None,
            RefLikeMarkerKind refLikeMarker =
                RefLikeMarkerKind.None)
        {
            bool hasSpellingCollision =
                spellingCollision != SpellingCollisionKind.None;
            int selectedReturnShapes =
                (returnsVoid ? 1 : 0)
                + (returnsVoidArray ? 1 : 0)
                + (restrictedReturnType is null ? 0 : 1)
                + (hasSpellingCollision ? 1 : 0);
            if (selectedReturnShapes > 1)
            {
                throw new ArgumentException(
                    "A signature must select at most one special return shape.");
            }
            if (hasSpellingCollision
                && (!signatureUsesContainingType
                    || interfaceGenericArity != 0))
            {
                throw new ArgumentException(
                    "The scope-collision scenario owns its generic signature.");
            }
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("CSharpDeclarationFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            AssemblyReferenceHandle coreLibrary =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Private.CoreLib"),
                    new Version(11, 0, 0, 0),
                    default,
                    AddBlob(
                        metadata,
                        0x7c,
                        0xec,
                        0x85,
                        0xd7,
                        0xbe,
                        0xa7,
                        0x79,
                        0x8e),
                    (AssemblyFlags)0,
                    default);
            TypeReferenceHandle valueType =
                metadata.AddTypeReference(
                    coreLibrary,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("ValueType"));
            TypeReferenceHandle restrictedReturn =
                restrictedReturnType is null
                    ? default
                    : metadata.AddTypeReference(
                        coreLibrary,
                        metadata.GetOrAddString("System"),
                        metadata.GetOrAddString(
                            restrictedReturnType));
            TypeReferenceHandle firstCollisionType = default;
            TypeReferenceHandle secondCollisionType = default;
            if (hasSpellingCollision)
            {
                AssemblyReferenceHandle firstAssembly =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("Collision.One"),
                        new Version(1, 0, 0, 0),
                        default,
                        default,
                        (AssemblyFlags)0,
                        default);
                AssemblyReferenceHandle secondAssembly =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("Collision.Two"),
                        new Version(1, 0, 0, 0),
                        default,
                        default,
                        (AssemblyFlags)0,
                        default);
                if (spellingCollision
                    == SpellingCollisionKind
                        .NamespaceNestedDefinition)
                {
                    firstCollisionType = metadata.AddTypeReference(
                        firstAssembly,
                        metadata.GetOrAddString(
                            "Collision.Outer"),
                        metadata.GetOrAddString("Widget"));
                    TypeReferenceHandle outer =
                        metadata.AddTypeReference(
                            secondAssembly,
                            metadata.GetOrAddString("Collision"),
                            metadata.GetOrAddString("Outer"));
                    secondCollisionType =
                        metadata.AddTypeReference(
                            outer,
                            default,
                            metadata.GetOrAddString("Widget"));
                }
                else if (spellingCollision
                    == SpellingCollisionKind.CompleteDefinition)
                {
                    firstCollisionType = metadata.AddTypeReference(
                        firstAssembly,
                        metadata.GetOrAddString("Collision"),
                        metadata.GetOrAddString("Widget"));
                    secondCollisionType =
                        metadata.AddTypeReference(
                            secondAssembly,
                            metadata.GetOrAddString("Collision"),
                            metadata.GetOrAddString("Widget"));
                }
                else
                {
                    TypeReferenceHandle firstOuter =
                        metadata.AddTypeReference(
                            firstAssembly,
                            metadata.GetOrAddString("Collision"),
                            metadata.GetOrAddString("Outer"));
                    firstCollisionType =
                        metadata.AddTypeReference(
                            firstOuter,
                            default,
                            metadata.GetOrAddString("Left"));
                    if (spellingCollision
                        == SpellingCollisionKind
                            .DistinctNestedLeaves)
                    {
                        TypeReferenceHandle secondOuter =
                            metadata.AddTypeReference(
                                secondAssembly,
                                metadata.GetOrAddString("Collision"),
                                metadata.GetOrAddString("Outer"));
                        secondCollisionType =
                            metadata.AddTypeReference(
                                secondOuter,
                                default,
                                metadata.GetOrAddString("Right"));
                    }
                    else
                    {
                        secondCollisionType =
                            metadata.AddTypeReference(
                                secondAssembly,
                                metadata.GetOrAddString(
                                    "Collision.Outer"),
                                metadata.GetOrAddString("Right"));
                    }
                }
            }

            BlobHandle bodySignature;
            BlobHandle declarationSignature;
            if (hasSpellingCollision)
            {
                bodySignature = AddCollisionSignature(
                    openContainingType: false);
                declarationSignature = AddCollisionSignature(
                    openContainingType: true);
            }
            else
            {
                var signatureBlob = new BlobBuilder();
                signatureBlob.WriteByte(0x00);
                signatureBlob.WriteCompressedInteger(2);
                if (returnsVoid)
                {
                    signatureBlob.WriteByte(0x01);
                }
                else if (returnsVoidArray)
                {
                    signatureBlob.WriteByte(0x1d);
                    signatureBlob.WriteByte(0x01);
                }
                else if (restrictedReturnType is not null)
                {
                    WriteTypeReference(
                        signatureBlob,
                        restrictedReturn);
                }
                else
                {
                    WriteSignatureType(signatureBlob);
                }
                WriteSignatureType(signatureBlob);
                WriteSignatureType(signatureBlob);
                bodySignature =
                    metadata.GetOrAddBlob(signatureBlob);
                declarationSignature = bodySignature;
            }
            var bodyInstructions = new BlobBuilder();
            if (returnsVoidArray
                || hasSpellingCollision)
                bodyInstructions.WriteByte((byte)ILOpCode.Ldnull);
            else if (!returnsVoid)
                bodyInstructions.WriteByte((byte)ILOpCode.Ldarg_0);
            bodyInstructions.WriteByte((byte)ILOpCode.Ret);
            var methodBodies = new BlobBuilder();
            int bodyOffset =
                new MethodBodyStreamEncoder(methodBodies)
                    .AddMethodBody(
                        new InstructionEncoder(bodyInstructions),
                        maxStack: returnsVoid ? 0 : 1);
            MethodDefinitionHandle body = metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Static
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(operatorName),
                bodySignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
            MethodDefinitionHandle declaration =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual
                        | MethodAttributes.SpecialName
                        | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(operatorName),
                    declarationSignature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1));

            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                body);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    targetAttributes,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Number"),
                    targetIsValueType ? valueType : default(EntityHandle),
                    MetadataTokens.FieldDefinitionHandle(1),
                    body);
            TypeDefinitionHandle @interface =
                metadata.AddTypeDefinition(
                    ownerIsInterface
                        ? TypeAttributes.Public
                            | TypeAttributes.Interface
                            | TypeAttributes.Abstract
                        : TypeAttributes.Public,
                    metadata.GetOrAddString(interfaceNamespace),
                    metadata.GetOrAddString(
                        hasSpellingCollision
                            ? $"{interfaceName}`1"
                            : interfaceName),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    declaration);
            MemberReferenceHandle unresolvedAttributeConstructor =
                default;
            if (refLikeMarker != RefLikeMarkerKind.None)
            {
                EntityHandle constructorOwner;
                if (refLikeMarker
                    == RefLikeMarkerKind.NestedLookalike)
                {
                    TypeDefinitionHandle outer =
                        metadata.AddTypeDefinition(
                            TypeAttributes.Public,
                            metadata.GetOrAddString(
                                "System.Runtime"),
                            metadata.GetOrAddString(
                                "CompilerServices"),
                            default,
                            MetadataTokens.FieldDefinitionHandle(1),
                            MetadataTokens.MethodDefinitionHandle(3));
                    TypeDefinitionHandle nested =
                        metadata.AddTypeDefinition(
                            TypeAttributes.NestedPublic,
                            default,
                            metadata.GetOrAddString(
                                "IsByRefLikeAttribute"),
                            default,
                            MetadataTokens.FieldDefinitionHandle(1),
                            MetadataTokens.MethodDefinitionHandle(3));
                    metadata.AddNestedType(nested, outer);
                    constructorOwner = nested;
                }
                else
                {
                    constructorOwner = target;
                }

                var constructorSignature = new BlobBuilder();
                constructorSignature.WriteByte(0x20);
                constructorSignature.WriteCompressedInteger(0);
                constructorSignature.WriteByte(0x01);
                MemberReferenceHandle constructor =
                    metadata.AddMemberReference(
                        constructorOwner,
                        metadata.GetOrAddString(".ctor"),
                        metadata.GetOrAddBlob(
                            constructorSignature));
                metadata.AddCustomAttribute(
                    target,
                    constructor,
                    AddBlob(metadata, 0x01, 0x00, 0x00, 0x00));
                if (refLikeMarker
                    == RefLikeMarkerKind.UnresolvedOwner)
                {
                    unresolvedAttributeConstructor = constructor;
                }
            }
            int effectiveInterfaceArity =
                hasSpellingCollision
                    ? 1
                    : interfaceGenericArity;
            for (int index = 0;
                index < effectiveInterfaceArity;
                index++)
            {
                metadata.AddGenericParameter(
                    @interface,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString($"T{index}"),
                    index);
            }

            EntityHandle implementedInterface = @interface;
            EntityHandle methodDeclaration = declaration;
            if (hasSpellingCollision)
            {
                var typeSpecification = new BlobBuilder();
                typeSpecification.WriteByte(0x15);
                typeSpecification.WriteByte(0x12);
                typeSpecification.WriteCompressedInteger(
                    MetadataTokens.GetRowNumber(@interface) << 2);
                typeSpecification.WriteCompressedInteger(1);
                WriteSignatureType(typeSpecification);
                TypeSpecificationHandle constructedInterface =
                    metadata.AddTypeSpecification(
                        metadata.GetOrAddBlob(typeSpecification));
                implementedInterface = constructedInterface;
                methodDeclaration = metadata.AddMemberReference(
                    constructedInterface,
                    metadata.GetOrAddString(operatorName),
                    declarationSignature);
            }

            for (int index = 0;
                index < interfaceImplementationCount;
                index++)
            {
                metadata.AddInterfaceImplementation(
                    target,
                    implementedInterface);
            }

            for (int index = 0;
                index < methodImplementationCount;
                index++)
            {
                metadata.AddMethodImplementation(
                    target,
                    body,
                    methodDeclaration);
            }

            string path = Path.Combine(
                Path.GetTempPath(),
                $"csharp-declaration-{Guid.NewGuid():N}.dll");
            byte[] image = Serialize(metadata, methodBodies);
            if (!unresolvedAttributeConstructor.IsNil)
            {
                PatchMemberReferenceParentToNil(
                    image,
                    unresolvedAttributeConstructor);
            }
            File.WriteAllBytes(
                path,
                image);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    target),
                MetadataMethodAddress.Create(reader, body),
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    @interface));

            void WriteSignatureType(BlobBuilder builder)
            {
                builder.WriteByte(
                    signatureUsesContainingType && targetIsValueType
                        ? (byte)0x11
                        : (byte)0x12);
                builder.WriteCompressedInteger(
                    signatureUsesContainingType
                        ? 0x08
                        : 0x0c);
            }

            void WriteTypeReference(
                BlobBuilder builder,
                TypeReferenceHandle type)
            {
                builder.WriteByte(0x12);
                builder.WriteCompressedInteger(
                    (MetadataTokens.GetRowNumber(type) << 2)
                    | 1);
            }

            BlobHandle AddCollisionSignature(
                bool openContainingType)
            {
                var builder = new BlobBuilder();
                builder.WriteByte(0x00);
                builder.WriteCompressedInteger(2);
                WriteTypeReference(
                    builder,
                    secondCollisionType);
                if (openContainingType)
                {
                    builder.WriteByte(0x13);
                    builder.WriteCompressedInteger(0);
                }
                else
                {
                    WriteSignatureType(builder);
                }
                WriteTypeReference(
                    builder,
                    firstCollisionType);
                return metadata.GetOrAddBlob(builder);
            }
        }

        internal CSharpMethodDeclarationPost Capture()
        {
            using var assembly =
                AssemblyInspectionSession.Open(_path);
            using var operation = new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
            using MetadataDeclarationSession declarations =
                assembly.CreateDeclarationSession(operation);
            return CSharpMethodDeclarationPost.Capture(
                declarations,
                _target,
                _body,
                TestContext.Current.CancellationToken);
        }

        internal MetadataTypeDeclarationResult
            PostInterfaceDeclaration()
        {
            using var assembly =
                AssemblyInspectionSession.Open(_path);
            using var operation = new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
            using MetadataDeclarationSession declarations =
                assembly.CreateDeclarationSession(operation);
            return declarations.PostTypeDeclaration(
                _interface,
                TestContext.Current.CancellationToken);
        }

        internal MetadataInterfaceImplementationResult RejectInterface(
            MetadataTypeIdentity owner)
        {
            using var assembly =
                AssemblyInspectionSession.Open(_path);
            using var operation = new MetadataOperationContext(
                new MetadataOperationPolicy(maxMetadataRows: 0));
            using MetadataDeclarationSession declarations =
                assembly.CreateDeclarationSession(operation);
            return declarations.Relate(
                _target,
                owner,
                TestContext.Current.CancellationToken);
        }

        public void Dispose() => File.Delete(_path);

        static BlobHandle AddBlob(
            MetadataBuilder metadata,
            params byte[] bytes)
        {
            var blob = new BlobBuilder();
            blob.WriteBytes(bytes);
            return metadata.GetOrAddBlob(blob);
        }

        static byte[] Serialize(
            MetadataBuilder metadata,
            BlobBuilder methodBodies)
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                methodBodies,
                flags: CorFlags.ILOnly)
                .Serialize(image);
            return image.ToArray();
        }

        static void PatchMemberReferenceParentToNil(
            byte[] image,
            MemberReferenceHandle handle)
        {
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MemberRef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.MemberRef));
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
            image.AsSpan(offset, parentIndexSize).Clear();
        }
    }
}
