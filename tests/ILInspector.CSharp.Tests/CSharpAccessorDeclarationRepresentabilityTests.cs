using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.Metadata.MemorySafetyFixtures;
using ILInspector.MetadataPrimitives;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpAccessorDeclarationRepresentabilityTests
{
    [Fact]
    public void CDR008_CDR009_CDR014_ListCountPostsDecidesAndRenders()
    {
        CSharpAccessorDeclarationPost post = CapturePinnedListCount();
        var association = Assert.IsType<
            MetadataAccessorAssociationResult.Related>(
                post.Association);
        Assert.Equal(
            association.Certificate.Declaration,
            post.Coordinate!.Value.Declaration.Declaration);
        Assert.Equal(
            association.Certificate.Role,
            post.Coordinate.Value.Role);

        CSharpAccessorDeclarationRepresentabilityResult result =
            CSharpAccessorDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp10));
        Assert.True(
            result is
                CSharpAccessorDeclarationRepresentabilityResult.Representable,
            result.ToString());
        var represented =
            (CSharpAccessorDeclarationRepresentabilityResult.Representable)
                result;

        Assert.Equal(
            CSharpAccessorDeclarationKind.Property,
            represented.Request.Kind);
        Assert.Equal(
            post.Coordinate!.Value,
            represented.Request.Coordinate);
        Assert.Same(
            Assert.IsType<MetadataAccessorDeclarationResult.Posted>(
                    post.Declaration)
                .Evidence,
            represented.Request.Aggregate);
        CSharpAcceptedAccessorBinding getter =
            Assert.Single(represented.Request.Accessors);
        Assert.Equal(
            MetadataAccessorSemanticsRole.Getter,
            getter.Occurrence.Role);
        Assert.Equal(
            CSharpAccessorBodyPolicy.SelectedBody,
            getter.BodyPolicy);
        Assert.Equal(
            "public int Count { get => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR008_CDR009_IndexerRetainsSelectedBodyAndSibling()
    {
        PropertyInfo indexer = typeof(MemorySafetyDeclarationFixtures)
            .GetProperty("Item")!;
        CSharpAccessorDeclarationPost post = Capture(
            indexer,
            MetadataAccessorSemanticsRole.Getter,
            indexer.GetMethod!);

        var represented = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));

        Assert.Equal(
            CSharpAccessorDeclarationKind.Indexer,
            represented.Request.Kind);
        Assert.Equal(
            ["index"],
            represented.Request.Parameters
                .Select(parameter => parameter.Name)
                .ToArray());
        Assert.Equal(
            [
                (MetadataAccessorSemanticsRole.Getter,
                    CSharpAccessorBodyPolicy.SelectedBody),
                (MetadataAccessorSemanticsRole.Setter,
                    CSharpAccessorBodyPolicy.SiblingStub),
            ],
            represented.Request.Accessors
                .Select(accessor => (
                    accessor.Occurrence.Role,
                    accessor.BodyPolicy))
                .ToArray());
        Assert.Equal(
            "public int this[int index] "
                + "{ get => throw null; set => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR009_EventRetainsAddAndRemove()
    {
        EventInfo @event = typeof(MemorySafetyDeclarationFixtures)
            .GetEvent(
                nameof(MemorySafetyDeclarationFixtures.CustomEvent))!;
        CSharpAccessorDeclarationPost post = Capture(
            @event,
            MetadataAccessorSemanticsRole.AddOn,
            @event.AddMethod!);

        var represented = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));

        Assert.Equal(
            CSharpAccessorDeclarationKind.Event,
            represented.Request.Kind);
        Assert.Equal(
            [
                MetadataAccessorSemanticsRole.AddOn,
                MetadataAccessorSemanticsRole.RemoveOn,
            ],
            represented.Request.Accessors
                .Select(accessor => accessor.Occurrence.Role)
                .ToArray());
        Assert.Equal(
            "public event global::System.Action CustomEvent "
                + "{ add => throw null; remove => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR009_PropertyPreservesRestrictedSetter()
    {
        PropertyInfo property = typeof(MethodImplementationFixtures)
                .GetProperty(nameof(MethodImplementationFixtures.Property))!;
        CSharpAccessorDeclarationPost post = Capture(
                property,
                MetadataAccessorSemanticsRole.Getter,
                property.GetMethod!);

        var represented = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                    CSharpAccessorDeclarationRepresentability.Decide(
                        post,
                        new(CSharpLanguageVersion.CSharp14)));

        Assert.Equal(
                "public int Property "
                    + "{ get => throw null; private set => throw null; }",
                CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                    represented.Request));
    }

    [Fact]
    public void CDR012_InitSetterRequiresExactModifierAndRendersInit()
    {
        PropertyInfo property = typeof(MemorySafetyDeclarationFixtures)
            .GetProperty("InitProperty")!;
        CSharpAccessorDeclarationPost post = Capture(
            property,
            MetadataAccessorSemanticsRole.Setter,
            property.SetMethod!);

        var represented = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp10)));

        CSharpAcceptedAccessorBinding setter =
            Assert.Single(
                represented.Request.Accessors,
                accessor => accessor.Occurrence.Role
                    == MetadataAccessorSemanticsRole.Setter);
        Assert.Equal("init", setter.Keyword);
        Assert.IsType<MetadataTypeIdentity.Modified>(
            setter.Occurrence.Method.Signature.ReturnType);
        Assert.Equal(
            "public int InitProperty "
                + "{ get => throw null; init => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR012_OtherRequiredVoidModifierIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
                AuthoredAccessorFixture.CreatePropertyWithOtherSetterModifier();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        Assert.IsType<MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);

        var refused = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                    CSharpAccessorDeclarationRepresentability.Decide(
                        post,
                        new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedAccessorSignature,
                refused.Reason);
    }

    [Fact]
    public void CDR015_PropertyRoleMismatchIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreatePropertyWithGetterReturnMismatch();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);
        Assert.Contains(
            posted.Evidence.Accessors,
            accessor => accessor.Correspondence.Role.Status
                == MetadataAccessorRoleCorrespondenceStatus.Mismatch);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorCorrespondence,
            refused.Reason);
    }

    [Fact]
    public void CDR015_DuplicateGetterIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreatePropertyWithDuplicateGetter();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);
        Assert.Equal(
            MetadataPropertyAccessorMultiplicityStatus.NonConventional,
            posted.Evidence.Correspondence.PropertyMultiplicity);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorMultiplicity,
            refused.Reason);
    }

    [Fact]
    public void CDR015_NonOrdinaryGetterIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreatePropertyWithVarargGetter();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);
        Assert.Equal(
            MetadataAccessorOrdinaryCallableStatus.NonOrdinary,
            Assert.Single(posted.Evidence.Accessors)
                .Correspondence.OrdinaryCallable);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorSignature,
            refused.Reason);
    }

    [Fact]
    public void CDR015_EventRoleMismatchIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithAddParameterMismatch();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);
        Assert.Contains(
            posted.Evidence.Accessors,
            accessor => accessor.Correspondence.Role.Status
                == MetadataAccessorRoleCorrespondenceStatus.Mismatch);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorCorrespondence,
            refused.Reason);
    }

    [Fact]
    public void CDR015_EventStaticnessMismatchIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithStaticnessMismatch();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);
        Assert.False(
            posted.Evidence.Correspondence
                .EventAddRemoveStaticnessMatches);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorCorrespondence,
            refused.Reason);
    }

    [Fact]
    public void CDR010_FireAndOtherProduceAtomicLanguageRefusal()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithFireAndOther();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                post.Declaration);
        Assert.Contains(
            posted.Evidence.Accessors,
            accessor => accessor.Role
                == MetadataAccessorSemanticsRole.Fire);
        Assert.Equal(
            2,
            posted.Evidence.Accessors.Count(accessor =>
                accessor.Role
                    == MetadataAccessorSemanticsRole.Other));

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedSemanticOccurrence,
            refused.Reason);
    }

    [Fact]
    public void CDR011_EventWithoutRemoveRejectionIsUnavailable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithoutRemove();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        Assert.IsType<MetadataAccessorDeclarationResult.Rejected>(
            post.Declaration);

        var unavailable = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationUnavailableReason
                .AccessorDeclarationRejected,
            unavailable.Reason);
    }

    [Fact]
    public void CDR010_AbstractTargetWithoutBodyIsUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateAbstractEvent();
        CSharpAccessorDeclarationPost post = fixture.Capture();

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .MissingTargetBody,
            refused.Reason);
    }

    [Fact]
    public void CDR010_UnspellableRootAttributesAreUnrepresentable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithRootAttributes();
        CSharpAccessorDeclarationPost post = fixture.Capture();

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedRootAttributes,
            refused.Reason);
    }

    [Fact]
    public void CDR011_DuplicateAddRejectionIsUnavailable()
    {
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithDuplicateAdd();
        CSharpAccessorDeclarationPost post = fixture.Capture();
        Assert.IsType<MetadataAccessorDeclarationResult.Rejected>(
            post.Declaration);

        var unavailable = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationUnavailableReason
                .AccessorDeclarationRejected,
            unavailable.Reason);
    }

    [Fact]
    public void CDR011_RejectedAssociationIsUnavailable()
    {
        PropertyInfo property = typeof(MemorySafetyDeclarationFixtures)
            .GetProperty(
                nameof(MemorySafetyDeclarationFixtures.Property))!;
        CSharpAccessorDeclarationPost post = Capture(
            property,
            MetadataAccessorSemanticsRole.Getter,
            property.GetMethod!,
            new MetadataOperationPolicy(maxMetadataRows: 0));

        var unavailable = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationUnavailableReason
                .AccessorAssociationRejected,
            unavailable.Reason);
    }

    [Fact]
    public void CDR011_CertifiedAccessorAbsenceIsUnavailable()
    {
        Type type = typeof(List<>);
        MethodInfo method = type.GetMethod(
            nameof(List<int>.Add),
            [type.GetGenericArguments()[0]])!;
        CSharpAccessorDeclarationPost post = Capture(type, method);

        Assert.IsType<MetadataAccessorAssociationResult.Absent>(
            post.Association);
        Assert.Null(post.Coordinate);
        Assert.Null(post.Declaration);
        var unavailable = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationUnavailableReason
                .AccessorAssociationAbsent,
            unavailable.Reason);
    }

    [Fact]
    public void CDR011_TargetRoleMismatchIsUnavailable()
    {
        PropertyInfo property = typeof(MemorySafetyDeclarationFixtures)
            .GetProperty(
                nameof(MemorySafetyDeclarationFixtures.Property))!;
        CSharpAccessorDeclarationPost post = Capture(
            property,
            MetadataAccessorSemanticsRole.Getter,
            property.GetMethod!);
        CSharpAccessorDeclarationPost mismatched = post with
        {
            Coordinate = post.Coordinate!.Value with
            {
                Role = MetadataAccessorSemanticsRole.Setter,
            },
        };

        var unavailable = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    mismatched,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationUnavailableReason
                .RequestMismatch,
            unavailable.Reason);
    }

    [Fact]
    public void CDR016_ExplicitInterfacePropertyComposesOwnerAndAggregate()
    {
        PropertyInfo property = typeof(ExplicitPropertyFixture)
            .GetProperties(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        CSharpAccessorDeclarationPost post = Capture(
            property,
            MetadataAccessorSemanticsRole.Getter,
            property.GetMethod!);

        var represented = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));

        Assert.NotNull(represented.Request.ExplicitInterfaceIdentity);
        Assert.NotNull(represented.Request.ExplicitInterface);
        Assert.NotNull(represented.Request.ExplicitInterfaceAggregate);
        CSharpAcceptedAccessorBinding getter =
            Assert.Single(represented.Request.Accessors);
        Assert.NotNull(getter.ExplicitInterface);
        Assert.Equal(
            "int global::ILInspector.CSharp.Tests."
                + "IExplicitPropertyFixture.Value "
                + "{ get => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR016_ExplicitInterfaceIndexerRetainsSelectedBodyAndSibling()
    {
        PropertyInfo indexer = typeof(ExplicitIndexerFixture)
            .GetProperties(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        CSharpAccessorDeclarationPost post = Capture(
            indexer,
            MetadataAccessorSemanticsRole.Getter,
            indexer.GetMethod!);

        var represented = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));

        Assert.Equal(
            [
                CSharpAccessorBodyPolicy.SelectedBody,
                CSharpAccessorBodyPolicy.SiblingStub,
            ],
            represented.Request.Accessors
                .Select(accessor => accessor.BodyPolicy)
                .ToArray());
        Assert.All(
            represented.Request.Accessors,
            accessor => Assert.NotNull(accessor.ExplicitInterface));
        Assert.Equal(
            "int global::ILInspector.CSharp.Tests."
                + "IExplicitIndexerFixture.this[int index] "
                + "{ get => throw null; set => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR016_ExplicitInterfaceEventRetainsAddAndRemove()
    {
        EventInfo @event = typeof(ExplicitEventFixture)
            .GetEvents(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        CSharpAccessorDeclarationPost post = Capture(
            @event,
            MetadataAccessorSemanticsRole.AddOn,
            @event.AddMethod!);

        var represented = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));

        Assert.All(
            represented.Request.Accessors,
            accessor => Assert.NotNull(accessor.ExplicitInterface));
        Assert.Equal(
            "event global::System.Action "
                + "global::ILInspector.CSharp.Tests."
                + "IExplicitEventFixture.Changed "
                + "{ add => throw null; remove => throw null; }",
            CSharpAcceptedAccessorDeclarationRenderer.RenderStub(
                represented.Request));
    }

    [Fact]
    public void CDR016_MismatchedSiblingInterfaceAggregatesRefuseAtomically()
    {
        using AuthoredExplicitAccessorFixture fixture =
            AuthoredExplicitAccessorFixture.Create(
                splitInterfaceOwners: true);
        CSharpAccessorDeclarationPost post = fixture.Capture();
        Assert.Equal(
            2,
            post.Implementations
                .Select(implementation =>
                    Assert.Single(
                        Assert.IsType<
                            MetadataMethodImplementationResult.Related>(
                                implementation.Method.Implementations)
                            .Relationships)
                        .DeclarationOwner)
                .Distinct()
                .Count());

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedExplicitInterfaceComposition,
            refused.Reason);
    }

    [Fact]
    public void CDR016_DuplicateMethodImplRefusesMultiplicity()
    {
        using AuthoredExplicitAccessorFixture fixture =
            AuthoredExplicitAccessorFixture.Create(
                duplicateGetterImplementation: true);
        CSharpAccessorDeclarationPost post = fixture.Capture();
        Assert.Equal(
            2,
            Assert.IsType<MetadataMethodImplementationResult.Related>(
                    post.Implementations[0].Method.Implementations)
                .Relationships
                .Length);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedExplicitInterfaceMultiplicity,
            refused.Reason);
    }

    [Fact]
    public void CDR016_DeclarationPropertyRoleMismatchIsUnrepresentable()
    {
        using AuthoredExplicitAccessorFixture fixture =
            AuthoredExplicitAccessorFixture.Create(
                mismatchedDeclarationPropertySignature: true);
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var declaration = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Assert.Single(post.Implementations[0].Declarations)
                    .Declaration);
        Assert.Contains(
            declaration.Evidence.Accessors,
            accessor => accessor.Correspondence.Role.Status
                == MetadataAccessorRoleCorrespondenceStatus.Mismatch);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorCorrespondence,
            refused.Reason);
    }

    [Fact]
    public void CDR016_DeclarationPropertyMultiplicityIsUnrepresentable()
    {
        using AuthoredExplicitAccessorFixture fixture =
            AuthoredExplicitAccessorFixture.Create(
                duplicateDeclarationGetter: true);
        CSharpAccessorDeclarationPost post = fixture.Capture();
        var declaration = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Assert.Single(post.Implementations[0].Declarations)
                    .Declaration);
        Assert.Equal(
            MetadataPropertyAccessorMultiplicityStatus.NonConventional,
            declaration.Evidence.Correspondence.PropertyMultiplicity);

        var refused = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unrepresentable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));
        Assert.Equal(
            CSharpAccessorDeclarationRefusalReason
                .UnsupportedAccessorMultiplicity,
            refused.Reason);
    }

    [Fact]
    public void CDR011_FailureTextContainsNoArtifactName()
    {
        const string Hostile = "\u001b[31mhostile\u001b[0m";
        using AuthoredAccessorFixture fixture =
            AuthoredAccessorFixture.CreateEventWithName(Hostile);
        CSharpAccessorDeclarationPost post = fixture.Capture();

        CSharpAccessorDeclarationRepresentabilityResult result =
            CSharpAccessorDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp14));

        Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                result);
        Assert.DoesNotContain(Hostile, result.ToString());
        Assert.All(
            result.ToString(),
            character => Assert.False(
                CSharpText.CSharpIdentifier
                    .IsRenderingHazard(character)));
    }

    [Fact]
    public void CDR013_StandaloneAndDetachedPathsSharePropertyPolicy()
    {
        Assert.True(
            CSharpAccessorDeclarationPolicy
                .PropertyAccessibilityIsRepresentable(
                    "public",
                    [null, "private"]));
        Assert.True(
            CSharpAccessorDeclarationPolicy
                .TrySelectPropertyAccessibility(
                    ["public", "private"],
                    out string accessibility));
        Assert.Equal("public", accessibility);
        Assert.False(
            CSharpAccessorDeclarationPolicy
                .TrySelectPropertyAccessibility(
                    ["protected", "internal"],
                    out _));
    }

    [Fact]
    public void CDR014_ProfileParticipatesInAcceptedRequestIdentity()
    {
        CSharpAccessorDeclarationPost post = CapturePinnedListCount();

        CSharpAccessorDeclarationRepresentabilityResult csharp10Result =
            CSharpAccessorDeclarationRepresentability.Decide(
                post,
                new(CSharpLanguageVersion.CSharp10));
        Assert.True(
            csharp10Result is
                CSharpAccessorDeclarationRepresentabilityResult.Representable,
            csharp10Result.ToString());
        var csharp10 =
            (CSharpAccessorDeclarationRepresentabilityResult.Representable)
                csharp10Result;
        var csharp14 = Assert.IsType<
            CSharpAccessorDeclarationRepresentabilityResult.Representable>(
                CSharpAccessorDeclarationRepresentability.Decide(
                    post,
                    new(CSharpLanguageVersion.CSharp14)));

        Assert.NotEqual(
            csharp10.Request.Profile,
            csharp14.Request.Profile);
        Assert.Equal(
            csharp10.Request.Aggregate,
            csharp14.Request.Aggregate);
    }

    [Fact]
    public void CDR014_PostAndOutcomeGraphsCarryNoLiveAuthority()
    {
        Assert.Empty(
            typeof(CSharpAccessorDeclarationPost).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(
            typeof(CSharpAcceptedAccessorBinding).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(
            typeof(CSharpAccessorImplementationPost).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(
            typeof(CSharpAccessorDeclarationRelationshipPost)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(
            typeof(CSharpAcceptedExplicitAccessorRelationship)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(
            typeof(CSharpAcceptedAccessorDeclarationRequest)
                .GetConstructors(
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
            typeof(CSharpAccessorDeclarationPost),
            typeof(CSharpAccessorDeclarationRepresentabilityResult),
            typeof(CSharpAcceptedAccessorDeclarationRequest),
        ];
        foreach (Type root in roots)
        {
            Assert.DoesNotContain(
                EnumeratePublicGraph(root),
                type => prohibited.Any(prohibitedType =>
                    prohibitedType.IsAssignableFrom(type)));
        }
    }

    static CSharpAccessorDeclarationPost CapturePinnedListCount()
    {
        string path = PinnedCoreLibraryPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            return reader.GetString(definition.Namespace)
                    == "System.Collections.Generic"
                && reader.GetString(definition.Name) == "List`1";
        });
        PropertyDefinitionHandle property =
            reader.GetTypeDefinition(type)
                .GetProperties()
                .Single(handle => reader.GetString(
                        reader.GetPropertyDefinition(handle).Name)
                    == "Count");
        MethodDefinitionHandle getter =
            reader.GetPropertyDefinition(property).GetAccessors().Getter;
        return Capture(
            path,
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataMethodAddress.Create(reader, getter),
            MetadataOperationPolicy.Unbounded);
    }

    static CSharpAccessorDeclarationPost Capture(
        PropertyInfo property,
        MetadataAccessorSemanticsRole role,
        MethodInfo method,
        MetadataOperationPolicy? policy = null)
    {
        string path = property.DeclaringType!.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                property.DeclaringType.MetadataToken);
        CSharpAccessorDeclarationPost post = Capture(
            path,
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataMethodAddress.Create(
                reader,
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                    method.MetadataToken)),
            policy ?? MetadataOperationPolicy.Unbounded);
        if (post.Coordinate is CSharpAccessorDeclarationCoordinate coordinate)
            Assert.Equal(role, coordinate.Role);
        return post;
    }

    static CSharpAccessorDeclarationPost Capture(
        EventInfo @event,
        MetadataAccessorSemanticsRole role,
        MethodInfo method)
    {
        string path = @event.DeclaringType!.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                @event.DeclaringType.MetadataToken);
        CSharpAccessorDeclarationPost post = Capture(
            path,
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataMethodAddress.Create(
                reader,
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                    method.MetadataToken)),
            MetadataOperationPolicy.Unbounded);
        if (post.Coordinate is CSharpAccessorDeclarationCoordinate coordinate)
            Assert.Equal(role, coordinate.Role);
        return post;
    }

    static CSharpAccessorDeclarationPost Capture(
        Type type,
        MethodInfo method)
    {
        string path = type.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return Capture(
            path,
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    type.MetadataToken)),
            MetadataMethodAddress.Create(
                reader,
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                    method.MetadataToken)),
            MetadataOperationPolicy.Unbounded);
    }

    static CSharpAccessorDeclarationPost Capture(
        string path,
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress method,
        MetadataOperationPolicy policy)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(policy);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return CSharpAccessorDeclarationPost.Capture(
            declarations,
            type,
            method,
            TestContext.Current.CancellationToken);
    }

    static IEnumerable<Type> EnumeratePublicGraph(Type root)
    {
        var pending = new Queue<Type>();
        var visited = new HashSet<Type>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out Type? current))
        {
            if (!visited.Add(current))
                continue;
            yield return current;

            Type? element = current.IsArray
                ? current.GetElementType()
                : current.IsGenericType
                    ? null
                    : current;
            if (element is not null && element != current)
                pending.Enqueue(element);
            foreach (Type argument in current.IsGenericType
                ? current.GetGenericArguments()
                : Type.EmptyTypes)
            {
                pending.Enqueue(argument);
            }
            foreach (PropertyInfo property in current.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Enqueue(property.PropertyType);
            }
        }
    }

    static string PinnedCoreLibraryPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");

    sealed class AuthoredAccessorFixture : IDisposable
    {
        AuthoredAccessorFixture(
            string path,
            MetadataTypeDefinitionAddress type,
            MetadataMethodAddress method)
        {
            Path = path;
            Type = type;
            Method = method;
        }

        string Path { get; }

        MetadataTypeDefinitionAddress Type { get; }

        MetadataMethodAddress Method { get; }

        internal static AuthoredAccessorFixture
            CreateEventWithFireAndOther()
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: true,
                duplicateAdd: false,
                rootName: "Value",
                EventAttributes.None);

        internal static AuthoredAccessorFixture
            CreateEventWithoutRemove()
            => CreateEvent(
                includeRemove: false,
                includeFireAndOther: false,
                duplicateAdd: false,
                rootName: "Value",
                EventAttributes.None);

        internal static AuthoredAccessorFixture
            CreateEventWithDuplicateAdd()
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: false,
                duplicateAdd: true,
                rootName: "Value",
                EventAttributes.None);

        internal static AuthoredAccessorFixture CreateEventWithName(
            string rootName)
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: false,
                duplicateAdd: false,
                rootName,
                EventAttributes.None);

        internal static AuthoredAccessorFixture CreateAbstractEvent()
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: false,
                duplicateAdd: false,
                rootName: "Value",
                EventAttributes.None);

        internal static AuthoredAccessorFixture
            CreateEventWithRootAttributes()
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: false,
                duplicateAdd: false,
                rootName: "Value",
                EventAttributes.SpecialName);

        internal static AuthoredAccessorFixture
            CreateEventWithAddParameterMismatch()
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: false,
                duplicateAdd: false,
                rootName: "Value",
                EventAttributes.None,
                mismatchedAddParameter: true);

        internal static AuthoredAccessorFixture
            CreateEventWithStaticnessMismatch()
            => CreateEvent(
                includeRemove: true,
                includeFireAndOther: false,
                duplicateAdd: false,
                rootName: "Value",
                EventAttributes.None,
                mismatchedStaticness: true);

        static AuthoredAccessorFixture CreateEvent(
            bool includeRemove,
            bool includeFireAndOther,
            bool duplicateAdd,
            string rootName,
            EventAttributes rootAttributes,
            bool mismatchedAddParameter = false,
            bool mismatchedStaticness = false)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("AccessorFixture.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("AccessorFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            AssemblyReferenceHandle coreLibrary =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("mscorlib"),
                    new Version(4, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle eventType =
                metadata.AddTypeReference(
                    coreLibrary,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("EventHandler"));
            MethodDefinitionHandle add = AddEventMethod(
                metadata,
                "add_Value",
                eventType,
                mismatchedParameter: mismatchedAddParameter);
            MethodDefinitionHandle remove = AddEventMethod(
                metadata,
                "remove_Value",
                eventType,
                isStatic: mismatchedStaticness);
            MethodDefinitionHandle secondAdd = duplicateAdd
                ? AddEventMethod(
                    metadata,
                    "add_Value_2",
                    eventType)
                : default;
            MethodDefinitionHandle fire = includeFireAndOther
                ? AddVoidMethod(
                    metadata,
                    "raise_Value")
                : default;
            MethodDefinitionHandle other1 = includeFireAndOther
                ? AddVoidMethod(
                    metadata,
                    "other_Value_1")
                : default;
            MethodDefinitionHandle other2 = includeFireAndOther
                ? AddVoidMethod(
                    metadata,
                    "other_Value_2")
                : default;
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                add);
            TypeDefinitionHandle owner = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                add);
            EventDefinitionHandle @event = metadata.AddEvent(
                rootAttributes,
                metadata.GetOrAddString(rootName),
                eventType);
            metadata.AddEventMap(owner, @event);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Adder,
                add);
            if (includeRemove)
            {
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Remover,
                    remove);
            }
            if (duplicateAdd)
            {
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Adder,
                    secondAdd);
            }
            if (includeFireAndOther)
            {
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Raiser,
                    fire);
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Other,
                    other1);
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Other,
                    other2);
            }

            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly)
                .Serialize(image);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"csharp-accessor-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image.ToArray());
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    owner),
                MetadataMethodAddress.Create(reader, add));
        }

        internal static AuthoredAccessorFixture
            CreatePropertyWithOtherSetterModifier()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("AccessorFixture.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("AccessorFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            AssemblyReferenceHandle coreLibrary =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("mscorlib"),
                    new Version(4, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle modifier =
                metadata.AddTypeReference(
                    coreLibrary,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString("IsVolatile"));

            var getterSignature = new BlobBuilder();
            new BlobEncoder(getterSignature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            MethodDefinitionHandle getter = AddMethod(
                metadata,
                "get_Value",
                getterSignature);
            var setterSignature = new BlobBuilder();
            new BlobEncoder(setterSignature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    1,
                    returnType =>
                    {
                        returnType.CustomModifiers()
                            .AddModifier(
                                modifier,
                                isOptional: false);
                        returnType.Void();
                    },
                    parameters => parameters.AddParameter()
                        .Type()
                        .Int32());
            MethodDefinitionHandle setter = AddMethod(
                metadata,
                "set_Value",
                setterSignature);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                getter);
            TypeDefinitionHandle owner = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                getter);
            var propertySignature = new BlobBuilder();
            new BlobEncoder(propertySignature)
                .PropertySignature(isInstanceProperty: true)
                .Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(propertySignature));
            metadata.AddPropertyMap(owner, property);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                getter);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Setter,
                setter);

            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly)
                .Serialize(image);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"csharp-accessor-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image.ToArray());
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    owner),
                MetadataMethodAddress.Create(reader, getter));
        }

        internal static AuthoredAccessorFixture
            CreatePropertyWithGetterReturnMismatch()
            => CreatePropertyCorrespondenceFixture(
                getterReturnsString: true);

        internal static AuthoredAccessorFixture
            CreatePropertyWithDuplicateGetter()
            => CreatePropertyCorrespondenceFixture(
                duplicateGetter: true);

        internal static AuthoredAccessorFixture
            CreatePropertyWithVarargGetter()
            => CreatePropertyCorrespondenceFixture(
                varargGetter: true);

        static AuthoredAccessorFixture CreatePropertyCorrespondenceFixture(
            bool getterReturnsString = false,
            bool duplicateGetter = false,
            bool varargGetter = false)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("AccessorFixture.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("AccessorFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);

            MethodDefinitionHandle getter;
            if (varargGetter)
            {
                getter = AddRawMethod(
                    metadata,
                    "get_Value",
                    [0x25, 0x00, 0x08]);
            }
            else
            {
                var getterSignature = new BlobBuilder();
                new BlobEncoder(getterSignature)
                    .MethodSignature(isInstanceMethod: true)
                    .Parameters(
                        0,
                        returnType =>
                        {
                            if (getterReturnsString)
                                returnType.Type().String();
                            else
                                returnType.Type().Int32();
                        },
                        _ => { });
                getter = AddMethod(
                    metadata,
                    "get_Value",
                    getterSignature);
            }
            MethodDefinitionHandle secondGetter = duplicateGetter
                ? AddMethod(
                    metadata,
                    "get_Value_2",
                    CreateIntGetterSignature())
                : default;
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                getter);
            TypeDefinitionHandle owner = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                getter);
            var propertySignature = new BlobBuilder();
            new BlobEncoder(propertySignature)
                .PropertySignature(isInstanceProperty: true)
                .Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(propertySignature));
            metadata.AddPropertyMap(owner, property);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                getter);
            if (!secondGetter.IsNil)
            {
                metadata.AddMethodSemantics(
                    property,
                    MethodSemanticsAttributes.Getter,
                    secondGetter);
            }

            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly)
                .Serialize(image);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"csharp-accessor-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image.ToArray());
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    owner),
                MetadataMethodAddress.Create(reader, getter));

            static BlobBuilder CreateIntGetterSignature()
            {
                var signature = new BlobBuilder();
                new BlobEncoder(signature)
                    .MethodSignature(isInstanceMethod: true)
                    .Parameters(
                        0,
                        returnType => returnType.Type().Int32(),
                        _ => { });
                return signature;
            }
        }

        internal CSharpAccessorDeclarationPost Capture() =>
            CSharpAccessorDeclarationRepresentabilityTests.Capture(
                Path,
                Type,
                Method,
                MetadataOperationPolicy.Unbounded);

        public void Dispose() => File.Delete(Path);

        static MethodDefinitionHandle AddEventMethod(
            MetadataBuilder metadata,
            string name,
            TypeReferenceHandle eventType,
            bool mismatchedParameter = false,
            bool isStatic = false)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: !isStatic)
                .Parameters(
                    1,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        SignatureTypeEncoder parameter =
                            parameters.AddParameter().Type();
                        if (mismatchedParameter)
                            parameter.String();
                        else
                            parameter.Type(
                                eventType,
                                isValueType: false);
                    });
            return AddMethod(
                metadata,
                name,
                signature,
                isStatic ? MethodAttributes.Static : 0);
        }

        static MethodDefinitionHandle AddVoidMethod(
            MetadataBuilder metadata,
            string name)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    0,
                    returnType => returnType.Void(),
                    _ => { });
            return AddMethod(metadata, name, signature);
        }

        static MethodDefinitionHandle AddMethod(
            MetadataBuilder metadata,
            string name,
            BlobBuilder signature,
            MethodAttributes additionalAttributes = 0) =>
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName
                    | additionalAttributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));

        static MethodDefinitionHandle AddRawMethod(
            MetadataBuilder metadata,
            string name,
            byte[] signatureBytes)
        {
            var signature = new BlobBuilder();
            foreach (byte value in signatureBytes)
                signature.WriteByte(value);
            return AddMethod(metadata, name, signature);
        }
    }

    sealed class AuthoredExplicitAccessorFixture : IDisposable
    {
        AuthoredExplicitAccessorFixture(
            string path,
            MetadataTypeDefinitionAddress type,
            MetadataMethodAddress method)
        {
            Path = path;
            Type = type;
            Method = method;
        }

        string Path { get; }

        MetadataTypeDefinitionAddress Type { get; }

        MetadataMethodAddress Method { get; }

        internal static AuthoredExplicitAccessorFixture Create(
            bool splitInterfaceOwners = false,
            bool duplicateGetterImplementation = false,
            bool mismatchedDeclarationPropertySignature = false,
            bool duplicateDeclarationGetter = false)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("ExplicitAccessorFixture.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("ExplicitAccessorFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);

            BlobHandle getterSignature =
                metadata.GetOrAddBlob(CreateGetterSignature());
            BlobHandle setterSignature =
                metadata.GetOrAddBlob(CreateSetterSignature());
            MethodDefinitionHandle declarationGetter =
                AddDeclarationMethod(
                    metadata,
                    "get_Value",
                    getterSignature);
            MethodDefinitionHandle declarationSetter =
                AddDeclarationMethod(
                    metadata,
                    "set_Value",
                    setterSignature);
            MethodDefinitionHandle secondDeclarationGetter =
                duplicateDeclarationGetter
                    ? AddDeclarationMethod(
                        metadata,
                        "get_Value_2",
                        getterSignature)
                    : default;

            var methodBodies = new BlobBuilder();
            var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
            var getterInstructions = new BlobBuilder();
            getterInstructions.WriteByte((byte)ILOpCode.Ldc_i4_0);
            getterInstructions.WriteByte((byte)ILOpCode.Ret);
            int getterBodyOffset =
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(getterInstructions),
                    maxStack: 1);
            var setterInstructions = new BlobBuilder();
            setterInstructions.WriteByte((byte)ILOpCode.Ret);
            int setterBodyOffset =
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(setterInstructions),
                    maxStack: 0);
            MethodDefinitionHandle bodyGetter = AddBodyMethod(
                metadata,
                "IValue.get_Value",
                getterSignature,
                getterBodyOffset);
            MethodDefinitionHandle bodySetter = AddBodyMethod(
                metadata,
                "IValue.set_Value",
                setterSignature,
                setterBodyOffset);

            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                declarationGetter);
            TypeDefinitionHandle firstInterface =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString(
                        splitInterfaceOwners
                            ? "IGetter"
                            : "IValue"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    declarationGetter);
            TypeDefinitionHandle secondInterface = default;
            if (splitInterfaceOwners)
            {
                secondInterface = metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("ISetter"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    declarationSetter);
            }
            TypeDefinitionHandle target = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Sealed,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                bodyGetter);

            PropertyDefinitionHandle firstProperty = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(
                    CreatePropertySignature(
                        returnsString:
                            mismatchedDeclarationPropertySignature)));
            metadata.AddPropertyMap(firstInterface, firstProperty);
            metadata.AddMethodSemantics(
                firstProperty,
                MethodSemanticsAttributes.Getter,
                declarationGetter);
            if (duplicateDeclarationGetter)
            {
                metadata.AddMethodSemantics(
                    firstProperty,
                    MethodSemanticsAttributes.Getter,
                    secondDeclarationGetter);
            }
            if (!splitInterfaceOwners)
            {
                metadata.AddMethodSemantics(
                    firstProperty,
                    MethodSemanticsAttributes.Setter,
                    declarationSetter);
            }

            if (splitInterfaceOwners)
            {
                PropertyDefinitionHandle secondProperty =
                    metadata.AddProperty(
                        PropertyAttributes.None,
                        metadata.GetOrAddString("Value"),
                        metadata.GetOrAddBlob(
                            CreatePropertySignature()));
                metadata.AddPropertyMap(
                    secondInterface,
                    secondProperty);
                metadata.AddMethodSemantics(
                    secondProperty,
                    MethodSemanticsAttributes.Setter,
                    declarationSetter);
            }

            PropertyDefinitionHandle bodyProperty = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("IValue.Value"),
                metadata.GetOrAddBlob(CreatePropertySignature()));
            metadata.AddPropertyMap(target, bodyProperty);
            metadata.AddMethodSemantics(
                bodyProperty,
                MethodSemanticsAttributes.Getter,
                bodyGetter);
            metadata.AddMethodSemantics(
                bodyProperty,
                MethodSemanticsAttributes.Setter,
                bodySetter);

            metadata.AddInterfaceImplementation(
                target,
                firstInterface);
            if (splitInterfaceOwners)
            {
                metadata.AddInterfaceImplementation(
                    target,
                    secondInterface);
            }
            metadata.AddMethodImplementation(
                target,
                bodyGetter,
                declarationGetter);
            if (duplicateGetterImplementation)
            {
                metadata.AddMethodImplementation(
                    target,
                    bodyGetter,
                    declarationGetter);
            }
            metadata.AddMethodImplementation(
                target,
                bodySetter,
                declarationSetter);

            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                methodBodies,
                flags: CorFlags.ILOnly)
                .Serialize(image);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"csharp-explicit-accessor-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image.ToArray());
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    target),
                MetadataMethodAddress.Create(reader, bodyGetter));
        }

        internal CSharpAccessorDeclarationPost Capture() =>
            CSharpAccessorDeclarationRepresentabilityTests.Capture(
                Path,
                Type,
                Method,
                MetadataOperationPolicy.Unbounded);

        public void Dispose() => File.Delete(Path);

        static MethodDefinitionHandle AddDeclarationMethod(
            MetadataBuilder metadata,
            string name,
            BlobHandle signature) =>
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                signature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));

        static MethodDefinitionHandle AddBodyMethod(
            MetadataBuilder metadata,
            string name,
            BlobHandle signature,
            int bodyOffset) =>
            metadata.AddMethodDefinition(
                MethodAttributes.Private
                    | MethodAttributes.Final
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                signature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));

        static BlobBuilder CreateGetterSignature()
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            return signature;
        }

        static BlobBuilder CreateSetterSignature()
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    1,
                    returnType => returnType.Void(),
                    parameters =>
                        parameters.AddParameter().Type().Int32());
            return signature;
        }

        static BlobBuilder CreatePropertySignature(
            bool returnsString = false)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .PropertySignature(isInstanceProperty: true)
                .Parameters(
                    0,
                    returnType =>
                    {
                        SignatureTypeEncoder type = returnType.Type();
                        if (returnsString)
                            type.String();
                        else
                            type.Int32();
                    },
                    _ => { });
            return signature;
        }
    }
}

interface IExplicitPropertyFixture
{
    int Value { get; }
}

sealed class ExplicitPropertyFixture : IExplicitPropertyFixture
{
    int IExplicitPropertyFixture.Value => 42;
}

interface IExplicitIndexerFixture
{
    int this[int index] { get; set; }
}

sealed class ExplicitIndexerFixture : IExplicitIndexerFixture
{
    int IExplicitIndexerFixture.this[int index]
    {
        get => index;
        set { }
    }
}

interface IExplicitEventFixture
{
    event Action Changed;
}

sealed class ExplicitEventFixture : IExplicitEventFixture
{
    event Action IExplicitEventFixture.Changed
    {
        add { }
        remove { }
    }
}
