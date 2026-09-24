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
    public void CDR003_TypeSpellingRejectsNamedSystemVoid()
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
        var systemVoid = new MetadataTypeIdentity.Named(
            new(
                scope,
                new(
                    InertText.TextPolicy.Field,
                    "System"),
                [
                    new(
                        InertText.TextPolicy.Field,
                        "Void"),
                ],
                [0]),
            IsValueType: true);

        Assert.False(
            CSharpDeclarationRepresentability.TrySpellType(
                systemVoid,
                out _));
        Assert.False(
            CSharpDeclarationRepresentability.TrySpellType(
                new MetadataTypeIdentity.SzArray(systemVoid),
                out _));
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
            bool targetIsValueType = true)
        {
            if (returnsVoid && returnsVoidArray)
            {
                throw new ArgumentException(
                    "A signature cannot return both void and void[].");
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
                    default,
                    (AssemblyFlags)0,
                    default);
            TypeReferenceHandle valueType =
                metadata.AddTypeReference(
                    coreLibrary,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("ValueType"));

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
            else
            {
                WriteSignatureType(signatureBlob);
            }
            WriteSignatureType(signatureBlob);
            WriteSignatureType(signatureBlob);
            BlobHandle signature =
                metadata.GetOrAddBlob(signatureBlob);
            var bodyInstructions = new BlobBuilder();
            if (returnsVoidArray)
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
                signature,
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
                    signature,
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
                    metadata.GetOrAddString(interfaceName),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    declaration);
            for (int index = 0;
                index < interfaceGenericArity;
                index++)
            {
                metadata.AddGenericParameter(
                    @interface,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString($"T{index}"),
                    index);
            }

            for (int index = 0;
                index < interfaceImplementationCount;
                index++)
            {
                metadata.AddInterfaceImplementation(
                    target,
                    @interface);
            }

            for (int index = 0;
                index < methodImplementationCount;
                index++)
            {
                metadata.AddMethodImplementation(
                    target,
                    body,
                    declaration);
            }

            string path = Path.Combine(
                Path.GetTempPath(),
                $"csharp-declaration-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(
                path,
                Serialize(metadata, methodBodies));
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
    }
}
