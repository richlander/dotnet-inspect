using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class ApiDiffAnalyzerTests
{
    #region Helpers

    private static ApiSurface Surface(params ApiType[] types)
    {
        var surface = new ApiSurface();
        surface.Types.AddRange(types);
        return surface;
    }

    private static ApiSurface ApiAxisSurface(params ApiType[] types)
    {
        ApiSurface surface = Surface(types);
        surface.ExtractionScope = ApiSurfaceExtractionScope.ApiAxis;
        return surface;
    }

    private static ApiType Type(
        string name,
        string kind = "class",
        string? ns = "TestNamespace",
        bool isSealed = false,
        bool isAbstract = false,
        string? baseType = null,
        List<string>? interfaces = null,
        List<TypeParameter>? typeParameters = null,
        List<ApiMember>? members = null)
    {
        return new ApiType
        {
            Name = name,
            Kind = kind,
            Namespace = ns,
            IsSealed = isSealed,
            IsAbstract = isAbstract,
            IsStatic = isSealed && isAbstract,
            BaseType = baseType,
            Interfaces = interfaces ?? [],
            TypeParameters = typeParameters ?? [],
            Members = members ?? []
        };
    }

    private static ApiMember Method(
        string name,
        string signature,
        bool isVirtual = false,
        bool isAbstract = false,
        List<TypeParameter>? typeParameters = null)
    {
        return new ApiMember
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            SignatureModel = typeParameters is null
                ? null
                : new ApiSignature { TypeParameters = typeParameters },
            IsVirtual = isVirtual,
            IsAbstract = isAbstract
        };
    }

    private static ApiMember Property(string name, string signature)
    {
        return new ApiMember { Name = name, Kind = "property", Signature = signature };
    }

    private static ApiMember Field(string name, long? enumValue = null)
    {
        return new ApiMember { Name = name, Kind = "field", EnumValue = enumValue };
    }

    private static ApiMember Event(string name)
    {
        return new ApiMember { Name = name, Kind = "event" };
    }

    #endregion

    [Fact]
    public void IdenticalSurfaces_ReturnsEmptyDiff()
    {
        var type = Type("Foo", members: [Method("Bar", "void Bar()")]);
        var oldSurface = Surface(type);
        var newSurface = Surface(type);

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.True(diff.IsEmpty);
        Assert.Equal(0, diff.TotalBreaking);
        Assert.Equal(0, diff.TotalAdditive);
        Assert.Equal(0, diff.TotalPotentiallyBreaking);
    }

    [Fact]
    public void TypeAdded_IsAdditive()
    {
        var oldSurface = Surface();
        var newSurface = Surface(Type("Foo"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Single(diff.TypeDiffs);
        var typeDiff = diff.TypeDiffs[0];
        Assert.True(typeDiff.IsAdded);
        Assert.False(typeDiff.IsRemoved);
        var change = Assert.Single(typeDiff.Changes);
        Assert.Equal(ApiChangeCategory.Signature, change.Category);
        Assert.Equal(ApiChangeSubjectKind.Type, change.Subject?.Kind);
        Assert.Equal("TestNamespace.Foo", change.Subject?.TypeFullName);
        Assert.Equal(1, diff.TotalAdditive);
        Assert.Equal(0, diff.TotalBreaking);
    }

    [Fact]
    public void TypeRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo"));
        var newSurface = Surface();

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Single(diff.TypeDiffs);
        var typeDiff = diff.TypeDiffs[0];
        Assert.True(typeDiff.IsRemoved);
        Assert.False(typeDiff.IsAdded);
        Assert.Equal(1, diff.TotalBreaking);
    }

    [Fact]
    public void ExactTypeIdentities_DoNotCollapseFlattenedNameCollisions()
    {
        static ApiType Exact(
            string @namespace,
            params string[] segments) =>
            new()
            {
                Namespace = @namespace,
                Name = string.Join('.', segments),
                Kind = "class",
                DefinitionName = Assert
                    .IsType<MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            @namespace,
                            [.. segments]))
                    .Name,
            };

        ApiType literal = Exact("N.A", "B");
        ApiType nested = Exact("N", "A", "B");
        var diff = ApiDiffAnalyzer.Compare(
            Surface(literal, nested),
            Surface(nested));

        TypeDiff removed = Assert.Single(diff.TypeDiffs);
        Assert.True(removed.IsRemoved);
        Assert.Equal(@"N.A.B", removed.TypeFullName);
        Assert.Equal(1, diff.TotalBreaking);
    }

    [Fact]
    public void LegacyTypeDoesNotDisableExactMatchingForUnrelatedTypes()
    {
        static ApiType Exact(
            string @namespace,
            params string[] segments) =>
            new()
            {
                Namespace = @namespace,
                Name = string.Join('.', segments),
                Kind = "class",
                DefinitionName = Assert
                    .IsType<MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            @namespace,
                            [.. segments]))
                    .Name,
            };

        ApiType literal = Exact("N.A", "B");
        ApiType nested = Exact("N", "A", "B");
        ApiType legacy = Type("Legacy");
        var diff = ApiDiffAnalyzer.Compare(
            Surface(literal, nested, legacy),
            Surface(nested, legacy));

        TypeDiff removed = Assert.Single(diff.TypeDiffs);
        Assert.True(removed.IsRemoved);
        Assert.Equal("N.A.B", removed.TypeFullName);
    }

    [Fact]
    public void AmbiguousLegacyFallbackIsRejected()
    {
        static ApiType Exact(
            string @namespace,
            params string[] segments) =>
            new()
            {
                Namespace = @namespace,
                Name = string.Join('.', segments),
                Kind = "class",
                DefinitionName = Assert
                    .IsType<MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            @namespace,
                            [.. segments]))
                    .Name,
            };

        ApiType legacy = new()
        {
            Namespace = "N",
            Name = "A.B",
            Kind = "class",
        };

        Assert.Throws<InvalidOperationException>(() =>
            ApiDiffAnalyzer.Compare(
                Surface(
                    Exact("N.A", "B"),
                    Exact("N", "A", "B")),
                Surface(legacy)));
    }

    [Fact]
    public void TypeKindChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", kind: "class"));
        var newSurface = Surface(Type("Foo", kind: "struct"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeKindChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("class", change.OldValue);
        Assert.Equal("struct", change.NewValue);
    }

    [Fact]
    public void SealedAdded_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", isSealed: false));
        var newSurface = Surface(Type("Foo", isSealed: true));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.SealedAdded);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void SealedRemoved_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", isSealed: true));
        var newSurface = Surface(Type("Foo", isSealed: false));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.SealedRemoved);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void AbstractAdded_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", isAbstract: false));
        var newSurface = Surface(Type("Foo", isAbstract: true));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.AbstractAdded);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void AbstractRemoved_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", isAbstract: true));
        var newSurface = Surface(Type("Foo", isAbstract: false));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.AbstractRemoved);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void StaticType_NoSpuriousSealedOrAbstractChanges()
    {
        // Static types have both IsSealed and IsAbstract true — should not report changes
        var oldSurface = Surface(Type("Foo", isSealed: true, isAbstract: true));
        var newSurface = Surface(Type("Foo", isSealed: true, isAbstract: true));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void InspectionFailurePreservesSourceAssemblyPath()
    {
        var oldSurface = Surface();
        oldSurface.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "type identity",
                0x02000002,
                MetadataTypeNameFailureMechanism.Metadata,
                "MalformedMetadata",
                "Type identity failed.")
            {
                SourceAssemblyPath = "/inputs/Old.dll",
            });

        ApiDiff diff =
            ApiDiffAnalyzer.Compare(
                oldSurface,
                Surface());

        Assert.Equal(
            "/inputs/Old.dll",
            Assert.Single(diff.InspectionFailures)
                .SourceAssemblyPath);
    }

    [Fact]
    public void BaseTypeChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", baseType: "System.Object"));
        var newSurface = Surface(Type("Foo", baseType: "System.Exception"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.BaseTypeChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("System.Object", change.OldValue);
        Assert.Equal("System.Exception", change.NewValue);
    }

    [Fact]
    public void InterfaceRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", interfaces: ["IDisposable", "ICloneable"]));
        var newSurface = Surface(Type("Foo", interfaces: ["IDisposable"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.InterfaceRemoved);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Contains("ICloneable", change.Message);
    }

    [Fact]
    public void InterfaceAddedOnClass_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", kind: "class", interfaces: ["IDisposable"]));
        var newSurface = Surface(Type("Foo", kind: "class", interfaces: ["IDisposable", "ICloneable"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.InterfaceAdded);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void InterfaceAddedOnInterface_IsPotentiallyBreaking()
    {
        var oldSurface = Surface(Type("IFoo", kind: "interface"));
        var newSurface = Surface(Type("IFoo", kind: "interface", interfaces: ["IBar"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.InterfaceAdded);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, change.Classification);
    }

    [Fact]
    public void GenericParamCountChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }]));
        var newSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }, new TypeParameter { Name = "U" }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterCountChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void GenericVarianceChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("IFoo", kind: "interface", typeParameters:
            [new TypeParameter { Name = "T", Variance = "out" }]));
        var newSurface = Surface(Type("IFoo", kind: "interface", typeParameters:
            [new TypeParameter { Name = "T", Variance = "in" }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterVarianceChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ConstraintTightened_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }]));
        var newSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T", Constraints = { "class" } }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterConstraintTightened);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ConstraintLoosened_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T", Constraints = { "class" } }]));
        var newSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterConstraintLoosened);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void MethodConstraintChanged_ReportsExactConstraintNames()
    {
        var oldSurface = Surface(Type("Foo", members:
        [
            Method(
                "Apply",
                "void Apply<T>()",
                typeParameters:
                [
                    new TypeParameter
                    {
                        Name = "T",
                        Constraints = { "Dependency.BeforeConstraint" },
                    },
                ]),
        ]));
        var newSurface = Surface(Type("Foo", members:
        [
            Method(
                "Apply",
                "void Apply<T>()",
                typeParameters:
                [
                    new TypeParameter
                    {
                        Name = "T",
                        Constraints = { "Dependency.AfterConstraint" },
                    },
                ]),
        ]));

        ApiDiff diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        ApiChange[] changes =
        [
            .. Assert.Single(diff.TypeDiffs).Changes,
        ];
        Assert.Collection(
            changes,
            tightened =>
            {
                Assert.Equal(
                    ChangeKind.TypeParameterConstraintTightened,
                    tightened.Kind);
                Assert.Equal(
                    "Dependency.AfterConstraint",
                    tightened.NewValue);
                Assert.Equal(
                    ApiChangeSubjectKind.Member,
                    tightened.Subject?.Kind);
            },
            loosened =>
            {
                Assert.Equal(
                    ChangeKind.TypeParameterConstraintLoosened,
                    loosened.Kind);
                Assert.Equal(
                    "Dependency.BeforeConstraint",
                    loosened.OldValue);
                Assert.Equal(
                    ApiChangeSubjectKind.Member,
                    loosened.Subject?.Kind);
            });
    }

    [Theory]
    [InlineData(
        false,
        true,
        ChangeKind.TypeParameterConstraintTightened,
        ChangeClassification.Breaking)]
    [InlineData(
        true,
        false,
        ChangeKind.TypeParameterConstraintLoosened,
        ChangeClassification.Additive)]
    public void MethodConstraintAddedOrRemoved_IsClassified(
        bool oldHasConstraint,
        bool newHasConstraint,
        ChangeKind expectedKind,
        ChangeClassification expectedClassification)
    {
        var oldParameter = new TypeParameter { Name = "T" };
        if (oldHasConstraint)
            oldParameter.Constraints.Add("Dependency.Constraint");
        var newParameter = new TypeParameter { Name = "T" };
        if (newHasConstraint)
            newParameter.Constraints.Add("Dependency.Constraint");
        var oldSurface = Surface(Type("Foo", members:
        [
            Method(
                "Apply",
                "void Apply<T>()",
                typeParameters: [oldParameter]),
        ]));
        var newSurface = Surface(Type("Foo", members:
        [
            Method(
                "Apply",
                "void Apply<T>()",
                typeParameters: [newParameter]),
        ]));

        ApiChange change = Assert.Single(
            Assert.Single(
                ApiDiffAnalyzer.Compare(
                    oldSurface,
                    newSurface).TypeDiffs).Changes);

        Assert.Equal(expectedKind, change.Kind);
        Assert.Equal(expectedClassification, change.Classification);
        Assert.Equal(ApiChangeSubjectKind.Member, change.Subject?.Kind);
    }

    [Fact]
    public void MemberAddedOnClass_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", members: [Method("Bar", "void Bar()")]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
        Assert.Contains("Baz", change.Message);
        Assert.Equal(ApiChangeSubjectKind.Member, change.Subject?.Kind);
        Assert.Equal("TestNamespace.Foo", change.Subject?.TypeFullName);
        Assert.Equal("Baz", change.Subject?.MemberName);
        Assert.Null(change.Subject?.OldIdentity);
        Assert.Equal("Baz~1baf89a970", change.Subject?.NewIdentity);
        Assert.Equal("M:TestNamespace.Foo.Baz()", change.Subject?.NewMember?.Anchor?.CanonicalSignature);
        Assert.Equal("1baf89a970", change.Subject?.NewMember?.Anchor?.Fingerprint);
    }

    [Fact]
    public void MemberAddedOnInterface_IsPotentiallyBreaking()
    {
        var oldSurface = Surface(Type("IFoo", kind: "interface",
            members: [Method("Bar", "void Bar()")]));
        var newSurface = Surface(Type("IFoo", kind: "interface",
            members: [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, change.Classification);
    }

    [Fact]
    public void MemberRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));
        var newSurface = Surface(Type("Foo", members: [Method("Bar", "void Bar()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberRemoved);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Contains("Baz", change.Message);
    }

    [Fact]
    public void MemberSignatureChanged_PairsAndReportsBreaking()
    {
        var oldSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar(int x)")]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar(string x)")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberSignatureChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("void Bar(int x)", change.OldValue);
        Assert.Equal("void Bar(string x)", change.NewValue);
        Assert.Equal(ApiChangeSubjectKind.Member, change.Subject?.Kind);
        Assert.Equal("Bar", change.Subject?.MemberName);
        Assert.Equal("Bar~ea48d737b9", change.Subject?.OldIdentity);
        Assert.Equal("Bar~081bea7f62", change.Subject?.NewIdentity);
        Assert.Equal("M:TestNamespace.Foo.Bar(int)", change.Subject?.OldMember?.Anchor?.CanonicalSignature);
        Assert.Equal("M:TestNamespace.Foo.Bar(string)", change.Subject?.NewMember?.Anchor?.CanonicalSignature);
    }

    [Fact]
    public void DegradedSignature_DoesNotCollideWithGenuineSignature()
    {
        var degraded = Method("Bar", "object Bar()");
        degraded.SignatureDecodeStatus = SignatureDecodeStatus.Degraded;
        var genuine = Method("Bar", "object Bar()");

        var diff = ApiDiffAnalyzer.Compare(
            Surface(Type("Foo", members: [degraded])),
            Surface(Type("Foo", members: [genuine])));

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberSignatureChanged);
        Assert.Equal("object Bar() [decode: degraded]", change.OldValue);
        Assert.Equal("object Bar()", change.NewValue);
        Assert.Equal(ApiChangeSubjectKind.Member, change.Subject?.Kind);
        Assert.Equal("Bar", change.Subject?.MemberName);
        Assert.NotEqual(change.Subject?.OldIdentity, change.Subject?.NewIdentity);
        Assert.Equal(
            change.Subject?.OldMember?.Anchor?.CanonicalSignature,
            change.Subject?.NewMember?.Anchor?.CanonicalSignature);
    }

    [Fact]
    public void DegradedSignature_DoesNotPreventAttributeOnlyComparison()
    {
        var degraded = Method("Bar", "object Bar()");
        degraded.SignatureDecodeStatus = SignatureDecodeStatus.Degraded;
        degraded.Attributes.Add("OldAttribute");
        var genuine = Method("Bar", "object Bar()");
        genuine.Attributes.Add("NewAttribute");

        var diff = ApiDiffAnalyzer.Compare(
            Surface(Type("Foo", members: [degraded])),
            Surface(Type("Foo", members: [genuine])),
            new ApiDiffOptions(ApiDiffScope.Attributes));

        var changes = Assert.Single(diff.TypeDiffs).Changes;
        Assert.Contains(changes, change => change.Kind == ChangeKind.MemberAttributeRemoved);
        Assert.Contains(changes, change => change.Kind == ChangeKind.MemberAttributeAdded);
    }

    [Fact]
    public void VirtualRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()", isVirtual: true)]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()", isVirtual: false)]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.VirtualRemoved);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Theory]
    [InlineData(null, "protected", ChangeClassification.Breaking)]
    [InlineData("protected internal", "protected", ChangeClassification.NonBreaking)]
    [InlineData("protected", null, ChangeClassification.Additive)]
    public void MemberAccessibilityChange_WithinTheApiAxis_IsClassifiedByDirection(
        string? oldAccessibility,
        string? newAccessibility,
        ChangeClassification classification)
    {
        ApiMember oldMember = Method("Bar", "void Bar()");
        oldMember.Accessibility = oldAccessibility;
        ApiMember newMember = Method("Bar", "void Bar()");
        newMember.Accessibility = newAccessibility;

        var diff = ApiDiffAnalyzer.Compare(
            ApiAxisSurface(Type("Foo", members: [oldMember])),
            ApiAxisSurface(Type("Foo", members: [newMember])));

        var change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeKind.MemberAccessibilityChanged, change.Kind);
        Assert.Equal(classification, change.Classification);
    }

    private static ApiSurface IncludeAllSurface(params ApiType[] types)
    {
        ApiSurface surface = Surface(types);
        surface.ExtractionScope = ApiSurfaceExtractionScope.IncludeAll;
        return surface;
    }

    [Theory]
    [InlineData("public", "protected internal", ChangeClassification.Breaking)]
    [InlineData("public", "protected", ChangeClassification.Breaking)]
    [InlineData("public", "internal", ChangeClassification.Breaking)]
    [InlineData("public", "private protected", ChangeClassification.Breaking)]
    [InlineData("public", "private", ChangeClassification.Breaking)]
    [InlineData("protected internal", "protected", ChangeClassification.NonBreaking)]
    [InlineData("protected internal", "internal", ChangeClassification.Breaking)]
    [InlineData("protected", "internal", ChangeClassification.Breaking)]
    [InlineData("protected", "private protected", ChangeClassification.Breaking)]
    [InlineData("protected", "private", ChangeClassification.Breaking)]
    [InlineData("protected", "protected internal", ChangeClassification.NonBreaking)]
    [InlineData("protected", "public", ChangeClassification.Additive)]
    [InlineData("internal", "protected", ChangeClassification.Additive)]
    [InlineData("internal", "public", ChangeClassification.Additive)]
    [InlineData("private", "protected internal", ChangeClassification.Additive)]
    [InlineData("private protected", "public", ChangeClassification.Additive)]
    [InlineData("internal", "private", ChangeClassification.NonBreaking)]
    [InlineData("private", "internal", ChangeClassification.NonBreaking)]
    [InlineData("private protected", "internal", ChangeClassification.NonBreaking)]
    [InlineData("internal", "private protected", ChangeClassification.NonBreaking)]
    [InlineData("private", "private protected", ChangeClassification.NonBreaking)]
    [InlineData("private protected", "private", ChangeClassification.NonBreaking)]
    public void AccessibilityMove_UnderIncludeAll_IsClassifiedByExternalReach(
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiMember oldMember = Method("Bar", "void Bar()");
        oldMember.Accessibility = oldAccessibility;
        ApiMember newMember = Method("Bar", "void Bar()");
        newMember.Accessibility = newAccessibility;
        ApiType oldNested = Type("Foo.Nested");
        oldNested.Accessibility = oldAccessibility;
        ApiType newNested = Type("Foo.Nested");
        newNested.Accessibility = newAccessibility;

        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(Type("Foo", members: [oldMember]), oldNested),
            IncludeAllSurface(Type("Foo", members: [newMember]), newNested));

        ApiChange member = Assert.Single(
            Assert.Single(diff.TypeDiffs, type => type.TypeFullName == "TestNamespace.Foo").Changes);
        Assert.Equal(ChangeKind.MemberAccessibilityChanged, member.Kind);
        Assert.Equal(classification, member.Classification);
        Assert.Equal(oldAccessibility, member.OldValue);
        Assert.Equal(newAccessibility, member.NewValue);
        ApiChange type = Assert.Single(
            Assert.Single(diff.TypeDiffs, type => type.TypeFullName == "TestNamespace.Foo.Nested").Changes);
        Assert.Equal(ChangeKind.TypeAccessibilityChanged, type.Kind);
        Assert.Equal(classification, type.Classification);
    }

    [Fact]
    public void AccessibilityMove_UnderIncludeAll_CountsNonBreakingChanges()
    {
        ApiMember oldMember = Method("Bar", "void Bar()");
        oldMember.Accessibility = "internal";
        ApiMember newMember = Method("Bar", "void Bar()");
        newMember.Accessibility = "private";

        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(Type("Foo", members: [oldMember])),
            IncludeAllSurface(Type("Foo", members: [newMember])));

        Assert.Equal(1, diff.TotalNonBreaking);
        Assert.Equal(1, Assert.Single(diff.TypeDiffs).NonBreakingCount);
        Assert.Equal(0, diff.TotalBreaking + diff.TotalAdditive + diff.TotalPotentiallyBreaking);
    }

    [Theory]
    [InlineData("private", ChangeKind.MemberAdded)]
    [InlineData("internal", ChangeKind.MemberAdded)]
    [InlineData("private", ChangeKind.MemberRemoved)]
    [InlineData("private protected", ChangeKind.MemberRemoved)]
    public void MemberOutsideTheApiAxis_UnderIncludeAll_IsNonBreaking(
        string accessibility,
        ChangeKind kind)
    {
        ApiMember member = Method("Helper", "void Helper()");
        member.Accessibility = accessibility;
        ApiType withMember = Type("Foo", members: [member]);
        ApiType without = Type("Foo");

        var diff = kind == ChangeKind.MemberAdded
            ? ApiDiffAnalyzer.Compare(IncludeAllSurface(without), IncludeAllSurface(withMember))
            : ApiDiffAnalyzer.Compare(IncludeAllSurface(withMember), IncludeAllSurface(without));

        ApiChange change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Fact]
    public void PrivateMemberSignatureChange_UnderIncludeAll_IsNonBreaking()
    {
        ApiMember oldMember = Method("Helper", "int Helper()");
        oldMember.Accessibility = "private";
        ApiMember newMember = Method("Helper", "long Helper()");
        newMember.Accessibility = "private";

        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(Type("Foo", members: [oldMember])),
            IncludeAllSurface(Type("Foo", members: [newMember])));

        ApiChange change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Fact]
    public void InternalTypeRemoved_UnderIncludeAll_IsNonBreaking()
    {
        ApiType removed = Type("Hidden");
        removed.Accessibility = "internal";

        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(removed),
            IncludeAllSurface());

        ApiChange change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeKind.TypeRemoved, change.Kind);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Fact]
    public void PublicMemberOfAnInternalType_UnderIncludeAll_IsNonBreaking()
    {
        ApiType oldType = Type("Hidden", members: [Method("Bar", "int Bar()")]);
        oldType.Accessibility = "internal";
        ApiType newType = Type("Hidden", members: [Method("Bar", "long Bar()")]);
        newType.Accessibility = "internal";

        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(oldType),
            IncludeAllSurface(newType));

        ApiChange change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Fact]
    public void PublicNestedTypeOfAnUnreachableType_UnderIncludeAll_IsNonBreaking()
    {
        ApiType oldType = Type("Hidden.Nested", members: [Method("Bar", "int Bar()")]);
        oldType.EnclosingTypesExternallyReachable = false;
        ApiType newType = Type("Hidden.Nested", members: [Method("Bar", "long Bar()")]);
        newType.EnclosingTypesExternallyReachable = false;

        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(oldType),
            IncludeAllSurface(newType));

        ApiChange change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Fact]
    public void PublicMemberChange_UnderIncludeAll_KeepsItsClassification()
    {
        var diff = ApiDiffAnalyzer.Compare(
            IncludeAllSurface(Type("Foo", members: [Method("Bar", "int Bar()")])),
            IncludeAllSurface(Type("Foo", members: [Method("Bar", "long Bar()")])));

        ApiChange change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ChangeOutsideTheApiAxis_OnApiAxisSurfaces_KeepsItsClassification()
    {
        ApiMember oldMember = Method("Bar", "int Bar()");
        oldMember.Accessibility = "internal";
        ApiMember newMember = Method("Bar", "long Bar()");
        newMember.Accessibility = "internal";

        var diff = ApiDiffAnalyzer.Compare(
            ApiAxisSurface(Type("Foo", members: [oldMember])),
            ApiAxisSurface(Type("Foo", members: [newMember])));

        Assert.Equal(
            ChangeClassification.Breaking,
            Assert.Single(Assert.Single(diff.TypeDiffs).Changes).Classification);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ApiSurfaceExtractionScope.Public)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll)]
    [InlineData(ApiSurfaceExtractionScope.PublicWithNonPublicTypes)]
    public void MemberAccessibilityChange_OutsideApiAxisSurfaces_IsNotCompared(
        ApiSurfaceExtractionScope? scope)
    {
        ApiMember oldMember = Method("Bar", "void Bar()");
        ApiMember newMember = Method("Bar", "void Bar()");
        newMember.Accessibility = "protected";
        ApiSurface oldSurface = Surface(Type("Foo", members: [oldMember]));
        ApiSurface newSurface = ApiAxisSurface(Type("Foo", members: [newMember]));
        oldSurface.ExtractionScope = scope;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Empty(diff.TypeDiffs);
    }

    [Fact]
    public void AbstractMemberAdded_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", isAbstract: true, members:
            [Method("Bar", "void Bar()", isAbstract: false, isVirtual: true)]));
        var newSurface = Surface(Type("Foo", isAbstract: true, members:
            [Method("Bar", "void Bar()", isAbstract: true, isVirtual: true)]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.AbstractMemberAdded);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void EnumValueChanged_IsPotentiallyBreaking()
    {
        var oldSurface = Surface(Type("MyEnum", kind: "enum", members:
            [Field("One", enumValue: 1), Field("Two", enumValue: 2)]));
        var newSurface = Surface(Type("MyEnum", kind: "enum", members:
            [Field("One", enumValue: 1), Field("Two", enumValue: 3)]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.EnumValueChanged);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, change.Classification);
        Assert.Equal("2", change.OldValue);
        Assert.Equal("3", change.NewValue);
    }

    [Fact]
    public void CompilerGeneratedMembers_AreFiltered()
    {
        var oldSurface = Surface(Type("Foo", members:
        [
            Method("Bar", "void Bar()"),
            new ApiMember { Name = "<Clone>$", Kind = "method", Signature = "Foo <Clone>$()" }
        ]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        // Compiler-generated <Clone>$ should be filtered, so no diff
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void MultipleChangesOnOneType_AllCaptured()
    {
        var oldSurface = Surface(Type("Foo",
            isSealed: false,
            interfaces: ["IDisposable"],
            members: [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));
        var newSurface = Surface(Type("Foo",
            isSealed: true,
            interfaces: ["IDisposable", "ICloneable"],
            members: [Method("Bar", "void Bar()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var typeDiff = Assert.Single(diff.TypeDiffs);
        Assert.Equal(3, typeDiff.Changes.Count);

        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.SealedAdded);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.InterfaceAdded);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.MemberRemoved);
    }

    [Fact]
    public void AggregateCounts_AreCorrect()
    {
        var oldSurface = Surface(
            Type("Foo", members: [Method("Bar", "void Bar()")]),
            Type("Removed"));
        var newSurface = Surface(
            Type("Foo", members: [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]),
            Type("Added"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        // Removed type = 1 breaking, Added type = 1 additive, Baz added = 1 additive
        Assert.Equal(1, diff.TotalBreaking);
        Assert.Equal(2, diff.TotalAdditive);
        Assert.Equal(0, diff.TotalPotentiallyBreaking);
    }

    [Fact]
    public void TypesAreSortedByName()
    {
        var oldSurface = Surface(Type("Zebra"), Type("Alpha"));
        var newSurface = Surface();

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Equal(2, diff.TypeDiffs.Count);
        Assert.Equal("TestNamespace.Alpha", diff.TypeDiffs[0].TypeFullName);
        Assert.Equal("TestNamespace.Zebra", diff.TypeDiffs[1].TypeFullName);
    }

    [Fact]
    public void EmptySurfaces_ReturnsEmptyDiff()
    {
        var diff = ApiDiffAnalyzer.Compare(new ApiSurface(), new ApiSurface());

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void FieldMembers_MatchByKindAndName()
    {
        // Fields don't have signatures — matched by kind:name key
        var oldSurface = Surface(Type("Foo", members:
            [Field("MaxValue")]));
        var newSurface = Surface(Type("Foo", members:
            [Field("MaxValue")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void EventMember_AddedAndRemoved()
    {
        var oldSurface = Surface(Type("Foo", members: [Event("Click")]));
        var newSurface = Surface(Type("Foo", members: [Event("Tap")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var typeDiff = Assert.Single(diff.TypeDiffs);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.MemberRemoved);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.MemberAdded);
    }

    #region API status (docs/design/api-status-analysis.md)

    private static ApiMember StatusMethod(
        bool isHidden = false,
        bool isObsolete = false,
        string? message = null,
        bool isError = false)
    {
        ApiMember member = Method("Bar", "void Bar()");
        member.IsHidden = isHidden;
        member.IsObsolete = isObsolete;
        member.ObsoleteMessage = message;
        member.ObsoleteIsError = isError;
        return member;
    }

    [Theory]
    [InlineData(false, true, "visible", "hidden")]
    [InlineData(true, false, "hidden", "visible")]
    public void HiddenTransition_OnTheApiAxis_IsNonBreaking(
        bool oldHidden,
        bool newHidden,
        string oldValue,
        string newValue)
    {
        ApiType oldType = Type("Foo", members: [StatusMethod(isHidden: oldHidden)]);
        oldType.IsHidden = oldHidden;
        ApiType newType = Type("Foo", members: [StatusMethod(isHidden: newHidden)]);
        newType.IsHidden = newHidden;

        var diff = ApiDiffAnalyzer.Compare(ApiAxisSurface(oldType), ApiAxisSurface(newType));

        IReadOnlyList<ApiChange> changes = Assert.Single(diff.TypeDiffs).Changes;
        Assert.Equal(2, changes.Count);
        Assert.All(changes, change =>
        {
            Assert.Equal(ApiChangeCategory.Status, change.Category);
            Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
            Assert.Equal(oldValue, change.OldValue);
            Assert.Equal(newValue, change.NewValue);
        });
        Assert.Contains(changes, change => change.Kind == ChangeKind.TypeHiddenChanged
            && change.Subject?.Kind == ApiChangeSubjectKind.Type);
        Assert.Contains(changes, change => change.Kind == ChangeKind.MemberHiddenChanged
            && change.Subject?.NewMember?.Member.Name == "Bar");
    }

    [Theory]
    [InlineData(false, null, false, true, "m", false, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]")]
    [InlineData(false, null, false, true, "m", true, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\", true)]")]
    [InlineData(false, null, false, true, null, false, ChangeClassification.Breaking, "not obsolete", "[Obsolete]")]
    [InlineData(true, "m", false, true, "m", true, ChangeClassification.Breaking, "[Obsolete(\"m\")]", "[Obsolete(\"m\", true)]")]
    [InlineData(true, "m", true, true, "m", false, ChangeClassification.NonBreaking, "[Obsolete(\"m\", true)]", "[Obsolete(\"m\")]")]
    [InlineData(true, "m", false, false, null, false, ChangeClassification.NonBreaking, "[Obsolete(\"m\")]", "not obsolete")]
    [InlineData(true, "m", true, false, null, false, ChangeClassification.NonBreaking, "[Obsolete(\"m\", true)]", "not obsolete")]
    [InlineData(true, "before", false, true, "after", false, ChangeClassification.NonBreaking, "[Obsolete(\"before\")]", "[Obsolete(\"after\")]")]
    [InlineData(true, "before", true, true, "after", true, ChangeClassification.NonBreaking, "[Obsolete(\"before\", true)]", "[Obsolete(\"after\", true)]")]
    [InlineData(true, "a \"b\"", false, true, null, false, ChangeClassification.NonBreaking, "[Obsolete(\"a \\\"b\\\"\")]", "[Obsolete]")]
    public void ObsoleteTransition_OnTheApiAxis_IsBreakingWhenItIsAddedOrBecomesAnError(
        bool oldObsolete,
        string? oldMessage,
        bool oldError,
        bool newObsolete,
        string? newMessage,
        bool newError,
        ChangeClassification classification,
        string oldValue,
        string newValue)
    {
        ApiType oldType = Type("Foo", members: [StatusMethod(isObsolete: oldObsolete, message: oldMessage, isError: oldError)]);
        oldType.IsObsolete = oldObsolete;
        oldType.ObsoleteMessage = oldMessage;
        oldType.ObsoleteIsError = oldError;
        ApiType newType = Type("Foo", members: [StatusMethod(isObsolete: newObsolete, message: newMessage, isError: newError)]);
        newType.IsObsolete = newObsolete;
        newType.ObsoleteMessage = newMessage;
        newType.ObsoleteIsError = newError;

        var diff = ApiDiffAnalyzer.Compare(ApiAxisSurface(oldType), ApiAxisSurface(newType));

        IReadOnlyList<ApiChange> changes = Assert.Single(diff.TypeDiffs).Changes;
        Assert.Equal(
            new[] { ChangeKind.TypeObsoleteChanged, ChangeKind.MemberObsoleteChanged },
            changes.Select(change => change.Kind));
        Assert.All(changes, change =>
        {
            Assert.Equal(ApiChangeCategory.Status, change.Category);
            Assert.Equal(classification, change.Classification);
            Assert.Equal(oldValue, change.OldValue);
            Assert.Equal(newValue, change.NewValue);
        });
    }

    [Fact]
    public void UnchangedStatus_OnTheApiAxis_HasNoRow()
    {
        ApiMember oldMember = StatusMethod(isHidden: true, isObsolete: true, message: "m", isError: true);
        ApiMember newMember = StatusMethod(isHidden: true, isObsolete: true, message: "m", isError: true);

        var diff = ApiDiffAnalyzer.Compare(
            ApiAxisSurface(Type("Foo", members: [oldMember])),
            ApiAxisSurface(Type("Foo", members: [newMember])));

        Assert.Empty(diff.TypeDiffs);
    }

    [Fact]
    public void StatusTransition_OutsideTheApiAxis_UnderIncludeAll_IsNonBreaking()
    {
        ApiType oldType = Type("Foo", members: [StatusMethod()]);
        oldType.Accessibility = "internal";
        ApiType newType = Type("Foo", members: [StatusMethod(isHidden: true, isObsolete: true, message: "m", isError: true)]);
        newType.Accessibility = "internal";
        newType.IsObsolete = true;
        newType.ObsoleteIsError = true;

        var diff = ApiDiffAnalyzer.Compare(IncludeAllSurface(oldType), IncludeAllSurface(newType));

        IReadOnlyList<ApiChange> changes = Assert.Single(diff.TypeDiffs).Changes;
        Assert.Equal(
            new[] { ChangeKind.TypeObsoleteChanged, ChangeKind.MemberHiddenChanged, ChangeKind.MemberObsoleteChanged },
            changes.Select(change => change.Kind));
        Assert.All(changes, change => Assert.Equal(ChangeClassification.NonBreaking, change.Classification));
        Assert.Equal(3, diff.TotalNonBreaking);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ApiSurfaceExtractionScope.Public)]
    [InlineData(ApiSurfaceExtractionScope.PublicWithNonPublicTypes)]
    public void StatusTransition_OutsideTransitionScopes_IsNotCompared(
        ApiSurfaceExtractionScope? scope)
    {
        ApiSurface oldSurface = Surface(Type("Foo", members: [StatusMethod()]));
        ApiSurface newSurface = Surface(Type("Foo", members: [StatusMethod(isHidden: true, isObsolete: true, isError: true)]));
        oldSurface.ExtractionScope = scope;
        newSurface.ExtractionScope = scope;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Empty(diff.TypeDiffs);
    }

    #endregion
}
