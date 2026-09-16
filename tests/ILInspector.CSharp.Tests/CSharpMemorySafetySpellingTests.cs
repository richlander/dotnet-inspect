using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpMemorySafetySpellingTests
{
    static readonly Guid ModuleId = Guid.Parse("b6dbdca1-2db5-4777-a021-7aa862120af8");
    static readonly Guid OtherModuleId = Guid.Parse("a091c075-78a5-4b57-bf95-c28f72d28f64");
    const int TypeToken = 0x02000001;

    [Theory]
    [InlineData(
        MemorySafetyRulesState.Updated,
        ContractKind.Explicit,
        MemorySafetyPointerEvidence.Absent,
        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
        true)]
    [InlineData(
        MemorySafetyRulesState.Updated,
        ContractKind.None,
        MemorySafetyPointerEvidence.Present,
        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
        false)]
    [InlineData(
        MemorySafetyRulesState.Legacy,
        ContractKind.Implicit,
        MemorySafetyPointerEvidence.Present,
        CSharpMemorySafetyLanguage.Legacy,
        true)]
    [InlineData(
        MemorySafetyRulesState.Legacy,
        ContractKind.Implicit,
        MemorySafetyPointerEvidence.Present,
        CSharpMemorySafetyLanguage.RelaxedPointerSyntax,
        false)]
    [InlineData(
        MemorySafetyRulesState.Legacy,
        ContractKind.None,
        MemorySafetyPointerEvidence.Absent,
        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
        false)]
    public void MethodModifierMatrixSeparatesBinaryRulesFromLanguageCapability(
        MemorySafetyRulesState rules,
        ContractKind contract,
        MemorySafetyPointerEvidence pointer,
        CSharpMemorySafetyLanguage language,
        bool expectedUnsafe)
    {
        ApiType type = Type(rules);
        ApiMember member = Method("Run", rules, contract, pointer);

        string declaration = Format(type, member, language);

        Assert.Equal(expectedUnsafe, HasWord(declaration, "unsafe"));
        Assert.False(HasWord(declaration, "safe"));
    }

    [Fact]
    public void CompatibilityModeRetainsLegacyIsUnsafeOutput()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            IsStatic = true,
            IsUnsafe = true,
            SignatureModel = new ApiSignature
            {
                MemberName = "Run",
                ReturnType = "int",
            },
        };

        string declaration = new CSharpFormatter().FormatMember(type, member);

        Assert.True(HasWord(declaration, "unsafe"));
    }

    [Fact]
    public void SingleDeclarationOutcomeCarriesModelAwareSpelling()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent);

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, member));

        Assert.True(HasWord(rendered.Declaration.Text, "unsafe"));
        Assert.False(rendered.UsesCompatibilitySpelling);
    }

    [Theory]
    [InlineData(MemorySafetyRulesState.Legacy)]
    [InlineData(MemorySafetyRulesState.Updated)]
    public void SingleDeclarationOutcomeRendersExactEnumValue(
        MemorySafetyRulesState rules)
    {
        ApiType type = Type(rules, kind: "enum");
        ApiMember member = Field(
            "NotIpAddress",
            rules,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            isStatic: true);
        member.IsConst = true;
        member.ReturnType = null;
        member.SignatureModel = null;
        member.EnumValueLiteral = "1";

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, member));

        Assert.Equal("NotIpAddress = 1", rendered.Declaration.Text);
        Assert.False(rendered.UsesCompatibilitySpelling);
    }

    [Fact]
    public void ExtractedEnumValueContainsMetadataName()
    {
        byte[] image = BuildEnumImage("First\nInjected");
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader).Types,
            candidate => candidate.Name == "TargetEnum");
        ApiMember member = Assert.Single(type.Members);

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, member));

        Assert.Equal("First_Injected = 1", rendered.Declaration.Text);
    }

    [Theory]
    [InlineData(MemorySafetyRulesState.Legacy)]
    [InlineData(MemorySafetyRulesState.Updated)]
    public void SingleDeclarationOutcomeRendersContractNeutralProperty(
        MemorySafetyRulesState rules)
    {
        ApiType type = Type(rules);
        ApiMember property = Property(
            "Type",
            rules,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ],
            returnType: "string");

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Equal(
            "public string Type { get; set; }",
            rendered.Declaration.Text);
        Assert.False(rendered.UsesCompatibilitySpelling);
    }

    [Fact]
    public void SingleDeclarationOutcomeRendersContractNeutralGetOnlyProperty()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Count",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Equal(
            "public int Count { get; }",
            rendered.Declaration.Text);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsDegradedPropertySignature()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureDecodeStatus = SignatureDecodeStatus.Degraded;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "complete structured signature",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void SingleDeclarationOutcomeRejectsUnprovenAccessorSignature(
        bool? signatureMatchesProperty)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.Accessors[0].SignatureMatchesProperty =
            signatureMatchesProperty;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "accessor callable signature",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void SingleDeclarationOutcomeRejectsUnprovenAccessorModifiers(
        bool? declarationModifiersMatchProperty)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.Accessors[0]
            .DeclarationModifiersMatchProperty =
                declarationModifiersMatchProperty;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "declaration modifier shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void SingleDeclarationOutcomeRejectsUnrepresentableAccessorModifiers(
        bool? declarationModifiersAreRepresentable)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.Accessors[0]
            .DeclarationModifiersAreRepresentable =
                declarationModifiersAreRepresentable;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "accessor declaration modifier shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsIllegalPropertyModifiers()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);
        property.IsStatic = true;
        property.IsVirtual = true;
        property.IsAbstract = true;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "declaration modifiers",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("class", false, true, false, false, false)]
    [InlineData("class", false, true, true, false, false)]
    [InlineData("class", false, true, false, true, false)]
    [InlineData("class", false, true, false, true, true)]
    [InlineData("class", false, true, true, true, false)]
    [InlineData("interface", false, true, true, false, false)]
    [InlineData("interface", true, true, true, false, false)]
    public void SingleDeclarationOutcomeAcceptsRepresentablePropertyModifiers(
        string typeKind,
        bool isStatic,
        bool isVirtual,
        bool isAbstract,
        bool isOverride,
        bool isSealed)
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            kind: typeKind);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);
        property.IsStatic = isStatic;
        property.IsVirtual = isVirtual;
        property.IsAbstract = isAbstract;
        property.IsOverride = isOverride;
        property.IsSealed = isSealed;

        Assert.IsType<CSharpMemberDeclarationOutcome.Rendered>(
            Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                .FormatMemberOutcome(type, property));
    }

    [Fact]
    public void SingleDeclarationOutcomeRendersValidRequiredProperty()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.IsRequired = true;

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Equal(
            "public required int Value { get; set; }",
            rendered.Declaration.Text);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsStaticRequiredProperty()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.IsStatic = true;
        property.SignatureModel!.IsRequired = true;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "required property shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsGetOnlyRequiredProperty()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);
        property.SignatureModel!.IsRequired = true;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "required property shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsInaccessibleRequiredProperty()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.Accessibility = "internal";
        property.SignatureModel!.IsRequired = true;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "required property shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsInaccessibleRequiredSetter()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.IsRequired = true;
        property.SignatureModel.Accessors
            .Single(static accessor => accessor.Kind == "set")
            .Accessibility = "private";

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "required property shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeAcceptsRequiredSetterVisibleToContainingType()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        type.Accessibility = "internal";
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.IsRequired = true;
        property.SignatureModel.Accessors
            .Single(static accessor => accessor.Kind == "set")
            .Accessibility = "internal";

        Assert.IsType<CSharpMemberDeclarationOutcome.Rendered>(
            Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                .FormatMemberOutcome(type, property));
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsRequiredInterfaceProperty()
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            kind: "interface");
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.IsVirtual = true;
        property.IsAbstract = true;
        property.SignatureModel!.IsRequired = true;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "required property shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsVoidPropertyType()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)],
            returnType: "void");

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "void return type",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsIncomparableAccessorAccessibility()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.Accessibility = "protected";
        property.SignatureModel!.Accessors
            .Single(accessor => accessor.Kind == "get")
            .Accessibility = "internal";

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "accessor accessibility",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void SingleDeclarationOutcomeRejectsUnavailableAccessorAccessibility(
        bool? accessibilityIsRepresentable)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.Accessors
            .Single(accessor => accessor.Kind == "get")
            .AccessibilityIsRepresentable = accessibilityIsRepresentable;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "accessor accessibility",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void SingleDeclarationOutcomeRejectsExplicitInterfaceAccessor(
        bool? isExplicitInterfaceImplementation)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.Accessors
            .Single(accessor => accessor.Kind == "get")
            .IsExplicitInterfaceImplementation =
                isExplicitInterfaceImplementation;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "explicit-interface accessor",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("public", "protected internal")]
    [InlineData("public", "protected")]
    [InlineData("public", "internal")]
    [InlineData("public", "private protected")]
    [InlineData("public", "private")]
    [InlineData("protected internal", "protected")]
    [InlineData("protected internal", "internal")]
    [InlineData("protected internal", "private protected")]
    [InlineData("protected internal", "private")]
    [InlineData("protected", "private protected")]
    [InlineData("protected", "private")]
    [InlineData("internal", "private protected")]
    [InlineData("internal", "private")]
    [InlineData("private protected", "private")]
    public void SingleDeclarationOutcomeAcceptsRestrictedAccessorAccessibility(
        string propertyAccessibility,
        string accessorAccessibility)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.Accessibility = propertyAccessibility;
        property.SignatureModel!.Accessors
            .Single(accessor => accessor.Kind == "get")
            .Accessibility = accessorAccessibility;

        Assert.IsType<CSharpMemberDeclarationOutcome.Rendered>(
            Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                .FormatMemberOutcome(type, property));
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsIndexedPropertyShape()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Item",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);
        property.IndexParameterCount = 1;
        property.SignatureModel!.MemberName = "this[]";
        property.SignatureModel.Parameters =
            [new ApiParameter { Name = "index", Type = "int" }];

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "non-indexed",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsInitOnlyAccessorShape()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [
                ("get", ContractKind.None, MemorySafetyPointerEvidence.Absent),
                ("set", ContractKind.None, MemorySafetyPointerEvidence.Absent),
            ]);
        property.SignatureModel!.Accessors
            .Single(accessor => accessor.Kind == "set")
            .StructuralReturnType =
                "modreq(System.Runtime.CompilerServices.IsExternalInit) void";

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "accessor return shape",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeRejectsReadonlyAccessorShape()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);
        property.SignatureModel!.Accessors.Single().IsReadOnly = true;

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Contains(
            "readonly accessor",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectedPropertyDoesNotPublishBodyOwnedUnsafeContext()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            [("get", ContractKind.None, MemorySafetyPointerEvidence.Absent)]);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                .FormatMemberWithBody(
                    type,
                    property,
                    new CSharpBlockBody("return 42;")
                    {
                        RequiresUnsafeModifier = true,
                    }));

        Assert.Contains(
            "body-owned",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        ContractKind.Explicit,
        MemorySafetyPointerEvidence.Absent,
        ContractKind.None,
        MemorySafetyPointerEvidence.Absent)]
    [InlineData(
        ContractKind.None,
        MemorySafetyPointerEvidence.Present,
        ContractKind.None,
        MemorySafetyPointerEvidence.Absent)]
    [InlineData(
        ContractKind.None,
        MemorySafetyPointerEvidence.Absent,
        ContractKind.Explicit,
        MemorySafetyPointerEvidence.Absent)]
    [InlineData(
        ContractKind.None,
        MemorySafetyPointerEvidence.Absent,
        ContractKind.None,
        MemorySafetyPointerEvidence.Present)]
    public void SingleDeclarationOutcomeKeepsPropertyContractsVisible(
        ContractKind propertyContract,
        MemorySafetyPointerEvidence propertyPointer,
        ContractKind accessorContract,
        MemorySafetyPointerEvidence accessorPointer)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember property = Property(
            "Value",
            MemorySafetyRulesState.Updated,
            propertyContract,
            propertyPointer,
            [("get", accessorContract, accessorPointer)]);

        CSharpMemberDeclarationOutcome.NotRendered notRendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.NotRendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, property));

        Assert.Equal(type.FullName, notRendered.Diagnostic.TypeName);
        Assert.Equal(property.Name, notRendered.Diagnostic.MemberName);
        Assert.Contains(
            propertyContract == ContractKind.Explicit
                || accessorContract == ContractKind.Explicit
                ? "contract"
                : "pointer",
            notRendered.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDeclarationOutcomeDistinguishesOlderSurfaceCompatibility()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Older",
            Kind = "class",
        };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            IsUnsafe = true,
            SignatureModel = new ApiSignature
            {
                MemberName = "Run",
                ReturnType = "void",
            },
        };

        CSharpMemberDeclarationOutcome.Rendered rendered = Assert.IsType<
            CSharpMemberDeclarationOutcome.Rendered>(
                Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                    .FormatMemberOutcome(type, member));

        Assert.True(HasWord(rendered.Declaration.Text, "unsafe"));
        Assert.True(rendered.UsesCompatibilitySpelling);
    }

    [Fact]
    public void UpdatedNoneIgnoresCompatibilityIsUnsafe()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        member.IsUnsafe = true;

        string declaration = Format(
            type,
            member,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.False(HasWord(declaration, "unsafe"));
    }

    [Theory]
    [InlineData(CSharpMemorySafetyLanguage.Legacy)]
    [InlineData(CSharpMemorySafetyLanguage.RelaxedPointerSyntax)]
    public void UpdatedContractsRequireUpdatedCallerContractSyntax(
        CSharpMemorySafetyLanguage language)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(type, member, language));

        Assert.Contains("updated caller contracts", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ApiTypeLayout.Explicit, true)]
    [InlineData(ApiTypeLayout.Auto, false)]
    [InlineData(ApiTypeLayout.Sequential, false)]
    public void UpdatedNoneInstanceFieldsSpellSafeOnlyForRequiredLayouts(
        ApiTypeLayout layout,
        bool expectedSafe)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated, layout: layout);
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);

        string declaration = Format(
            type,
            field,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.Equal(expectedSafe, HasWord(declaration, "safe"));
        Assert.False(HasWord(declaration, "unsafe"));
        Assert.Equal(
            layout == ApiTypeLayout.Explicit,
            declaration.Contains(
                "[global::System.Runtime.InteropServices.FieldOffsetAttribute(0)]",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExtendedLayoutIsVisibleUnavailable()
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Extended);
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                field,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("extended layout", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyExplicitLayoutDoesNotEnterUpdatedLayoutReplay()
    {
        ApiType type = Type(
            MemorySafetyRulesState.Legacy,
            layout: ApiTypeLayout.Explicit);
        type.LayoutDetails = null;
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        field.FieldLayout = null;
        type.Members = [field];

        CSharpTypePrintResult printed = Printed(new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(type),
            Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        Assert.DoesNotContain("StructLayout", printed.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("FieldOffset", printed.Source, StringComparison.Ordinal);
        Assert.Contains("public int Value;", printed.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitLayoutPrinterEmitsTypeAndEveryInstanceFieldOffset()
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Explicit);
        ApiMember safeField = Field(
            "SafeValue",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            offset: 0);
        ApiMember unsafeField = Field(
            "UnsafeValue",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            offset: 4);
        ApiMember staticField = Field(
            "StaticValue",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            isStatic: true);
        type.Attributes = ["Samples.TypeMarker"];
        safeField.Attributes = ["Samples.FieldMarker"];
        type.Members = [safeField, unsafeField, staticField];

        CSharpTypePrintResult printed = Printed(new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(type),
            Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts) with
            {
                IncludeCustomAttributes = true,
            }));

        Assert.Contains(
            "[global::System.Runtime.InteropServices.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = 32, Pack = 2)]\n[Samples.TypeMarker]",
            printed.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.InteropServices.FieldOffsetAttribute(0)]\n    [Samples.FieldMarker]\n    public safe int SafeValue;",
            printed.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.InteropServices.FieldOffsetAttribute(4)]\n    public unsafe int UnsafeValue;",
            printed.Source,
            StringComparison.Ordinal);
        Assert.Contains("public static int StaticValue;", printed.Source, StringComparison.Ordinal);
        string[] lines = printed.Source.Split('\n');
        int staticLine = Array.FindIndex(
            lines,
            line => line.Contains("StaticValue", StringComparison.Ordinal));
        Assert.True(staticLine > 0);
        Assert.DoesNotContain(
            "FieldOffsetAttribute",
            lines[staticLine - 1],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "field-layout")]
    [InlineData("module", "module")]
    [InlineData("type", "TypeDef")]
    [InlineData("field", "FieldDef")]
    [InlineData("offset", "offset")]
    public void ExplicitFieldLayoutEvidenceMustBeUsableAndAssociated(
        string defect,
        string expectedText)
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Explicit);
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        ApiFieldLayoutFacts valid = field.FieldLayout!;
        field.FieldLayout = defect switch
        {
            "missing" => null,
            "module" => valid with { ModuleVersionId = OtherModuleId },
            "type" => valid with { DeclaringTypeToken = TypeToken + 1 },
            "field" => valid with { FieldToken = valid.FieldToken + 1 },
            "offset" => valid with { Offset = null },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        type.Members = [field];

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(type),
                Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        CSharpTypePrintDiagnostic failure = Assert.Single(outcome.MemorySafetyFailures);
        Assert.Equal(type.FullName, failure.TypeName);
        Assert.Contains("Value", failure.Message, StringComparison.Ordinal);
        Assert.Contains(expectedText, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing", "details")]
    [InlineData("module", "module")]
    [InlineData("token", "TypeDef")]
    [InlineData("size", "size")]
    [InlineData("pack", "packing")]
    public void ExplicitTypeLayoutEvidenceMustBeRepresentableAndAssociated(
        string defect,
        string expectedText)
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Explicit);
        ApiTypeLayoutFacts valid = type.LayoutDetails!;
        type.LayoutDetails = defect switch
        {
            "missing" => null,
            "module" => valid with { ModuleVersionId = OtherModuleId },
            "token" => valid with { TypeToken = TypeToken + 1 },
            "size" => valid with { Size = -1 },
            "pack" => valid with { PackingSize = 3 },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        type.Members = [field];

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(type),
                Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        CSharpTypePrintDiagnostic failure = Assert.Single(outcome.MemorySafetyFailures);
        Assert.Equal(type.FullName, failure.TypeName);
        Assert.Contains(expectedText, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatedFieldNeighborsDoNotBorrowInstanceStorageRules()
    {
        ApiType explicitType = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Explicit);
        ApiMember staticField = Field(
            "StaticValue",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            isStatic: true);
        ApiMember unsafeField = Field(
            "UnsafeValue",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Unavailable);
        ApiMember ordinaryField = Field(
            "OrdinaryValue",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        ApiType sequentialType = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Sequential);

        string staticDeclaration = Format(
            explicitType,
            staticField,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);
        string unsafeDeclaration = Format(
            Type(MemorySafetyRulesState.Updated, layout: null),
            unsafeField,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);
        string ordinaryDeclaration = Format(
            sequentialType,
            ordinaryField,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.False(HasWord(staticDeclaration, "safe"));
        Assert.False(HasWord(staticDeclaration, "unsafe"));
        Assert.True(HasWord(unsafeDeclaration, "unsafe"));
        Assert.False(HasWord(unsafeDeclaration, "safe"));
        Assert.False(HasWord(ordinaryDeclaration, "safe"));
        Assert.False(HasWord(ordinaryDeclaration, "unsafe"));
    }

    [Fact]
    public void MissingLayoutIsRequiredOnlyForUpdatedNoneInstanceField()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated, layout: null);
        ApiMember none = Field(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        ApiMember explicitContract = Field(
            "UnsafeValue",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent);
        ApiMember staticField = Field(
            "StaticValue",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            isStatic: true);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                none,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("Value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("layout", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(HasWord(
            Format(
                type,
                explicitContract,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts),
            "unsafe"));
        Assert.False(HasWord(
            Format(
                type,
                staticField,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts),
            "safe"));
    }

    [Fact]
    public void ExplicitExternAndOrdinaryStubShapesRemainDistinct()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember safeExtern = Method(
            "Native",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        safeExtern.HasMethodBody = false;
        ApiMember unsafeExtern = Method(
            "RiskyNative",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent);
        unsafeExtern.HasMethodBody = false;

        string safeDeclaration = Format(
            type,
            safeExtern,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts,
            isExtern: true);
        string unsafeDeclaration = Format(
            type,
            unsafeExtern,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts,
            isExtern: true);
        string ordinaryDeclaration = Format(
            type,
            safeExtern,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.True(HasWord(safeDeclaration, "extern"));
        Assert.True(HasWord(safeDeclaration, "safe"));
        Assert.False(HasWord(safeDeclaration, "unsafe"));
        Assert.True(HasWord(unsafeDeclaration, "extern"));
        Assert.True(HasWord(unsafeDeclaration, "unsafe"));
        Assert.False(HasWord(unsafeDeclaration, "safe"));
        Assert.False(HasWord(ordinaryDeclaration, "extern"));
        Assert.False(HasWord(ordinaryDeclaration, "safe"));
    }

    [Fact]
    public void TypePrinterExternPolicyDoesNotInferExternFromMissingRva()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Native",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        member.HasMethodBody = false;
        member.MethodImplementation = new ApiMethodImplementationFacts(
            ModuleId,
            member.MetadataToken!.Value,
            System.Reflection.MethodAttributes.Public
                | System.Reflection.MethodAttributes.Static,
            System.Reflection.MethodImplAttributes.IL,
            HasBodyRva: false);
        type.Members = [member];
        var options = new CSharpTypePrintOptions
        {
            MemorySafetyLanguage = CSharpMemorySafetyLanguage.UpdatedCallerContracts,
        };

        CSharpTypePrintResult external = Printed(new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(member, CSharpBodyPolicy.Extern),
                ]),
            options));
        CSharpTypePrintResult stub = Printed(new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(member, CSharpBodyPolicy.Stub),
                ]),
            options));

        Assert.True(HasWord(external.Source, "extern"));
        Assert.True(HasWord(external.Source, "safe"));
        Assert.False(HasWord(stub.Source, "extern"));
        Assert.False(HasWord(stub.Source, "safe"));
    }

    [Theory]
    [InlineData("field", "class", false)]
    [InlineData("property", "class", false)]
    [InlineData("event", "class", false)]
    [InlineData("method", "class", true)]
    [InlineData("method", "interface", false)]
    [InlineData("static-constructor", "class", false)]
    [InlineData("finalizer", "class", false)]
    public void ExternPolicyRejectsUnsupportedDeclarationShapes(
        string memberKind,
        string typeKind,
        bool isAbstract)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated, kind: typeKind);
        ApiMember member = memberKind == "field"
            ? Field(
                "Value",
                MemorySafetyRulesState.Updated,
                ContractKind.None,
                MemorySafetyPointerEvidence.Absent)
            : Method(
                memberKind == "static-constructor" ? ".cctor" :
                memberKind == "finalizer" ? "Finalize" : "Run",
                MemorySafetyRulesState.Updated,
                ContractKind.None,
                MemorySafetyPointerEvidence.Absent,
                kind: memberKind == "static-constructor"
                    ? "constructor"
                    : memberKind);
        member.IsStatic = memberKind == "static-constructor" || member.IsStatic;
        member.IsAbstract = isAbstract;
        member.IsFinalizer = memberKind == "finalizer";
        type.Members = [member];

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(
                    type,
                    memberPolicyOverrides:
                    [
                        new CSharpMemberPolicy(member, CSharpBodyPolicy.Extern),
                    ]),
                new CSharpTypePrintOptions
                {
                    MemorySafetyLanguage =
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
                }));

        Assert.Contains(member.Name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternPolicyAcceptsAnOrdinaryInstanceConstructor()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember constructor = Method(
            ".ctor",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "constructor");
        type.Members = [constructor];

        CSharpTypePrintResult printed = Printed(new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        constructor,
                        CSharpBodyPolicy.Extern),
                ]),
            Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        Assert.True(HasWord(printed.Source, "extern"));
        Assert.True(HasWord(printed.Source, "safe"));
    }

    [Fact]
    public void ExternPolicyRejectsAnAttachedBody()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Native",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        type.Members = [member];

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(
                    type,
                    memberPolicyOverrides:
                    [
                        new CSharpMemberPolicy(
                            member,
                            CSharpBodyPolicy.Extern,
                            new CSharpBlockBody("return 0;")),
                    ]),
                new CSharpTypePrintOptions
                {
                    MemorySafetyLanguage =
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
                }));

        Assert.Contains("cannot carry a body", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpMemorySafetyLanguage.Legacy)]
    [InlineData(CSharpMemorySafetyLanguage.RelaxedPointerSyntax)]
    [InlineData(CSharpMemorySafetyLanguage.UpdatedCallerContracts)]
    public void LegacyBodyRequirementsUseAMemberModifier(
        CSharpMemorySafetyLanguage language)
    {
        ApiType type = Type(MemorySafetyRulesState.Legacy);
        ApiMember method = Method(
            "Run",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Unavailable);
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Unavailable);
        var formatter = Formatter(language);

        string methodDeclaration = formatter.FormatMemberWithBody(
            type,
            method,
            new CSharpBlockBody("return 0;")
            {
                RequiresUnsafeModifier = true,
            });
        string fieldDeclaration = formatter.FormatMemberWithBody(
            type,
            field,
            new CSharpFieldInitializer("0")
            {
                RequiresUnsafeModifier = true,
            });

        Assert.True(HasWord(methodDeclaration, "unsafe"));
        Assert.True(HasWord(fieldDeclaration, "unsafe"));
    }

    [Fact]
    public void TypePrinterUsesBodyRequirementBeforeDiagnosingUnavailablePointerEvidence()
    {
        ApiType type = Type(MemorySafetyRulesState.Legacy);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Unavailable);
        type.Members = [member];
        var body = new CSharpBlockBody("return 0;")
        {
            RequiresUnsafeModifier = true,
        };

        CSharpTypePrintResult printed = Printed(new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        member,
                        CSharpBodyPolicy.Full,
                        body),
                ]),
            Options(CSharpMemorySafetyLanguage.Legacy)));

        Assert.True(HasWord(printed.Source, "unsafe"));
    }

    [Theory]
    [InlineData(CSharpMemorySafetyLanguage.Legacy)]
    [InlineData(CSharpMemorySafetyLanguage.RelaxedPointerSyntax)]
    [InlineData(CSharpMemorySafetyLanguage.UpdatedCallerContracts)]
    public void LegacyForceUnsafeSuppliesContextWithoutPointerEvidence(
        CSharpMemorySafetyLanguage language)
    {
        ApiType type = Type(MemorySafetyRulesState.Legacy);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Unavailable);
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            ForceUnsafe = true,
            MemorySafetyLanguage = language,
        });

        string declaration = formatter.FormatMember(type, member);

        Assert.True(HasWord(declaration, "unsafe"));
    }

    [Theory]
    [InlineData(ContractKind.None)]
    [InlineData(ContractKind.Explicit)]
    public void UpdatedBodyRequirementsAreUnavailableEvenWithAContract(
        ContractKind contract)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            contract,
            MemorySafetyPointerEvidence.Absent);
        var body = new CSharpBlockBody("return 0;")
        {
            RequiresUnsafeModifier = true,
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                .FormatMemberWithBody(type, member, body));

        Assert.Contains("Run", exception.Message, StringComparison.Ordinal);
        Assert.Contains("body", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatedFieldInitializerBodyRequirementIsUnavailable()
    {
        ApiType type = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Sequential);
        ApiMember field = Field(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        var initializer = new CSharpFieldInitializer("0")
        {
            RequiresUnsafeModifier = true,
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts)
                .FormatMemberWithBody(type, field, initializer));

        Assert.Contains("Value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("body", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WholeTypeFieldInitializerUsesFinalSafetyShapeAtomically()
    {
        ApiType legacyType = Type(
            MemorySafetyRulesState.Legacy,
            layout: ApiTypeLayout.Sequential);
        ApiMember legacyField = Field(
            "LegacyValue",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Unavailable);
        legacyType.Members = [legacyField];
        var legacyInitializer = new CSharpFieldInitializer("0")
        {
            RequiresUnsafeModifier = true,
        };
        var legacyRequest = new CSharpTypePrintRequest(
            legacyType,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    legacyField,
                    CSharpBodyPolicy.Full,
                    legacyInitializer),
            ]);

        CSharpTypePrintResult legacy = Printed(new CSharpTypePrinter().Print(
            legacyRequest,
            Options(CSharpMemorySafetyLanguage.Legacy)));

        Assert.True(HasWord(legacy.Source, "unsafe"));

        ApiType updatedType = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Sequential);
        ApiMember updatedField = Field(
            "UpdatedValue",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        updatedType.Members = [updatedField];
        var updatedRequest = new CSharpTypePrintRequest(
            updatedType,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    updatedField,
                    CSharpBodyPolicy.Full,
                    new CSharpFieldInitializer("0")
                    {
                        RequiresUnsafeModifier = true,
                    }),
            ]);

        var updated = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                updatedRequest,
                Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        Assert.Empty(updated.SelfNameFailures);
        CSharpTypePrintDiagnostic failure =
            Assert.Single(updated.MemorySafetyFailures);
        Assert.Equal(updatedType.FullName, failure.TypeName);
        Assert.Contains("UpdatedValue", failure.Message, StringComparison.Ordinal);
        Assert.Contains("body", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MemorySafetyRulesState.Unsupported)]
    [InlineData(MemorySafetyRulesState.Malformed)]
    [InlineData(MemorySafetyRulesState.Conflicting)]
    public void UnrecognizedRulesAreUnavailable(MemorySafetyRulesState rules)
    {
        ApiType type = Type(rules);
        ApiMember member = Method(
            "Run",
            rules,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                member,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("rules", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableRuleAcquisitionIsUnavailable()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        type.MemorySafety = new ApiModuleMemorySafetyFacts(
            ModuleId,
            new MemorySafetyRulesResult.Unavailable(
                new MemorySafetyMetadataFailure(
                    MemorySafetyMetadataFailureKind.Malformed,
                    "Synthetic unavailable rules."),
                []));
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                member,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("rules", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingOrUnavailableFactsRemainVisible()
    {
        ApiType missingRules = Type(MemorySafetyRulesState.Updated);
        missingRules.MemorySafety = null;
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember missingMemberFacts = Method(
            "Missing",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        missingMemberFacts.MemorySafety = null;
        ApiMember unavailableContract = Method(
            "Unavailable",
            MemorySafetyRulesState.Updated,
            ContractKind.Unavailable,
            MemorySafetyPointerEvidence.Absent);

        Assert.Contains(
            "memory-safety facts",
            Assert.Throws<NotSupportedException>(() => Format(
                missingRules,
                member,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts)).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Missing",
            Assert.Throws<NotSupportedException>(() => Format(
                type,
                missingMemberFacts,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Unavailable",
            Assert.Throws<NotSupportedException>(() => Format(
                type,
                unavailableContract,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PointerEvidenceIsRequiredOnlyWhenItChangesTheModifier()
    {
        ApiType legacy = Type(MemorySafetyRulesState.Legacy);
        ApiMember unavailablePointer = Method(
            "Pointer",
            MemorySafetyRulesState.Legacy,
            ContractKind.Implicit,
            MemorySafetyPointerEvidence.Unavailable,
            parameterType: "int*");
        ApiType updated = Type(MemorySafetyRulesState.Updated);
        ApiMember explicitContract = Method(
            "Risky",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Unavailable);

        Assert.Throws<NotSupportedException>(() => Format(
            legacy,
            unavailablePointer,
            CSharpMemorySafetyLanguage.Legacy));
        Assert.False(HasWord(
            Format(
                legacy,
                unavailablePointer,
                CSharpMemorySafetyLanguage.RelaxedPointerSyntax),
            "unsafe"));
        Assert.True(HasWord(
            Format(
                updated,
                explicitContract,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts),
            "unsafe"));
    }

    [Theory]
    [InlineData(CSharpMemorySafetyLanguage.Legacy)]
    [InlineData(CSharpMemorySafetyLanguage.RelaxedPointerSyntax)]
    [InlineData(CSharpMemorySafetyLanguage.UpdatedCallerContracts)]
    public void LegacyNonePointerFieldCannotAcquireAnImplicitContract(
        CSharpMemorySafetyLanguage language)
    {
        ApiType type = Type(
            MemorySafetyRulesState.Legacy,
            layout: ApiTypeLayout.Sequential);
        ApiMember field = Field(
            "Buffer",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Present,
            fixedBuffer: MemorySafetyFixedBufferEvidence.Present);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(type, field, language));

        Assert.Contains("Buffer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("implicit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelAwareReplayRejectsDegradedOrUnstructuredSignatures()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember degraded = Method(
            "Degraded",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        degraded.SignatureDecodeStatus = SignatureDecodeStatus.Degraded;
        ApiMember unstructured = Method(
            "Unstructured",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        unstructured.Signature = "int Unstructured()";
        unstructured.SignatureModel = null;

        NotSupportedException degradedException = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                degraded,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));
        NotSupportedException unstructuredException = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                unstructured,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("Degraded", degradedException.Message, StringComparison.Ordinal);
        Assert.Contains("signature", degradedException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unstructured", unstructuredException.Message, StringComparison.Ordinal);
        Assert.Contains("signature", unstructuredException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefiningEvidenceMustMatchModuleAndRulesState()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember wrongModule = Method(
            "WrongModule",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            moduleId: OtherModuleId);
        ApiMember wrongRules = Method(
            "WrongRules",
            MemorySafetyRulesState.Legacy,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        ApiMember wrongToken = Method(
            "WrongToken",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        var wrongTokenContract =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
                wrongToken.MemorySafety!.CallerContract);
        wrongToken.MemorySafety = wrongToken.MemorySafety with
        {
            CallerContract = new MemorySafetyMemberContractResult.None(
                wrongTokenContract.Evidence with
                {
                    MemberToken = wrongTokenContract.Evidence.MemberToken + 1,
                }),
        };
        ApiType explicitType = Type(
            MemorySafetyRulesState.Updated,
            layout: ApiTypeLayout.Explicit);
        ApiMember projectedField = Field(
            "ProjectedField",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            moduleId: OtherModuleId);

        NotSupportedException moduleException = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                wrongModule,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));
        NotSupportedException rulesException = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                wrongRules,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));
        NotSupportedException tokenException = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                wrongToken,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));
        NotSupportedException layoutException = Assert.Throws<NotSupportedException>(
            () => Format(
                explicitType,
                projectedField,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("WrongModule", moduleException.Message, StringComparison.Ordinal);
        Assert.Contains("module", moduleException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WrongRules", rulesException.Message, StringComparison.Ordinal);
        Assert.Contains("rules", rulesException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WrongToken", tokenException.Message, StringComparison.Ordinal);
        Assert.Contains("declaration", tokenException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProjectedField", layoutException.Message, StringComparison.Ordinal);
        Assert.Contains("module", layoutException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("extension-method", "Extend")]
    [InlineData("explicit-interface-implementation", "IWorker.Run")]
    public void MethodLikeFormsPreserveExplicitUpdatedContracts(
        string kind,
        string name)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            name,
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: kind);
        // These Metadata projections retain declaration details in Signature
        // that the shared writer does not yet lower from ApiSignature.
        member.Signature = kind == "extension-method"
            ? $"int {name}(Widget value)"
            : $"int {name}()";
        if (kind == "extension-method")
        {
            member.IsExtension = true;
            member.SignatureModel!.Parameters =
            [
                new ApiParameter
                {
                    Modifier = "this",
                    Type = "Widget",
                    Name = "value",
                },
            ];
        }

        string declaration = Format(
            type,
            member,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.True(HasWord(declaration, "unsafe"));
        Assert.Contains(name, declaration, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("enum", ApiTypeLayout.Auto)]
    [InlineData("delegate", ApiTypeLayout.Auto)]
    [InlineData("struct", ApiTypeLayout.Extended)]
    public void ProjectedExtensionsUseModuleRatherThanReceiverTypeAdmission(
        string receiverKind,
        ApiTypeLayout layout)
    {
        ApiType receiver = Type(
            MemorySafetyRulesState.Updated,
            layout: layout,
            kind: receiverKind);
        ApiMember extension = Method(
            "Examine",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: "extension-method");
        extension.IsExtension = true;
        extension.Signature =
            $"int Examine({receiver.FullName} value)";
        extension.SignatureModel!.Parameters =
        [
            new ApiParameter
            {
                Modifier = "this",
                Type = receiver.FullName,
                Name = "value",
            },
        ];

        string declaration = Format(
            receiver,
            extension,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.True(HasWord(declaration, "unsafe"));
        Assert.Contains("Examine", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectedExternExtensionUsesDeclarationRatherThanInterfaceReceiver()
    {
        ApiType receiver = Type(
            MemorySafetyRulesState.Updated,
            kind: "interface");
        ApiMember extension = Method(
            "Examine",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "extension-method");
        extension.IsExtension = true;
        extension.Signature =
            $"int Examine({receiver.FullName} value)";
        extension.SignatureModel!.Parameters =
        [
            new ApiParameter
            {
                Modifier = "this",
                Type = receiver.FullName,
                Name = "value",
            },
        ];

        string declaration = Format(
            receiver,
            extension,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts,
            isExtern: true);

        Assert.True(HasWord(declaration, "safe"));
        Assert.True(HasWord(declaration, "extern"));
    }

    [Fact]
    public void WholeTypeExternExtensionRetainsInterfaceReceiverRestriction()
    {
        ApiType receiver = Type(
            MemorySafetyRulesState.Updated,
            kind: "interface");
        ApiMember extension = Method(
            "Examine",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "extension-method");
        extension.IsExtension = true;
        extension.Signature =
            $"int Examine({receiver.FullName} value)";
        extension.SignatureModel!.Parameters =
        [
            new ApiParameter
            {
                Modifier = "this",
                Type = receiver.FullName,
                Name = "value",
            },
        ];
        receiver.Members = [extension];

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(
                () => new CSharpTypePrinter().Print(
                    new CSharpTypePrintRequest(
                        receiver,
                        members: [extension],
                        memberPolicyOverrides:
                        [
                            new CSharpMemberPolicy(
                                extension,
                                CSharpBodyPolicy.Extern),
                        ]),
                    Options(
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        Assert.Contains("extern", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
        NotSupportedException unitException =
            Assert.Throws<NotSupportedException>(
                () => new CSharpFormatter(new CSharpFormatOptions
                {
                    IsExtern = true,
                    MemorySafetyLanguage =
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
                }).FormatTypeUnit(
                    receiver,
                    [extension]));

        Assert.Contains("extern", unitException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot", unitException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void WholeTypeProjectedExtensionsAreUnavailable(
        string receiverKind)
    {
        ApiType receiver = Type(
            MemorySafetyRulesState.Updated,
            kind: receiverKind);
        ApiMember extension = Method(
            "Examine",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: "extension-method");
        extension.IsExtension = true;
        extension.Signature =
            $"int Examine({receiver.FullName} value)";
        extension.SignatureModel!.Parameters =
        [
            new ApiParameter
            {
                Modifier = "this",
                Type = receiver.FullName,
                Name = "value",
            },
        ];
        receiver.Members = [extension];

        string standalone = Format(
            receiver,
            extension,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);
        Assert.True(HasWord(standalone, "unsafe"));

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(
                    receiver,
                    CSharpBodyPolicy.Stub,
                    [extension]),
                Options(
                    CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        CSharpTypePrintDiagnostic failure =
            Assert.Single(outcome.MemorySafetyFailures);
        Assert.Contains("projected extension", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("standalone", failure.Message, StringComparison.OrdinalIgnoreCase);

        NotSupportedException unitException =
            Assert.Throws<NotSupportedException>(
                () => new CSharpFormatter(new CSharpFormatOptions
                {
                    MemorySafetyLanguage =
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
                }).FormatTypeUnit(
                    receiver,
                    [extension]));

        Assert.Contains("projected extension", unitException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("standalone", unitException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ApiMethodSemanticsKind.PropertyGetter)]
    [InlineData(ApiMethodSemanticsKind.PropertySetter)]
    [InlineData(ApiMethodSemanticsKind.PropertyOther)]
    [InlineData(ApiMethodSemanticsKind.EventAdder)]
    [InlineData(ApiMethodSemanticsKind.EventRemover)]
    [InlineData(ApiMethodSemanticsKind.EventRaiser)]
    [InlineData(ApiMethodSemanticsKind.EventOther)]
    public void MethodSemanticsAccessorsAreUnavailable(
        ApiMethodSemanticsKind semantics)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "IContract.Value",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: "explicit-interface-implementation");
        member.MethodSemantics = semantics;

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                member,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("accessor", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingMethodSemanticsEvidenceIsUnavailable()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        member.MethodSemantics = null;

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                member,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("MethodSemantics", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorsPreserveOnlySupportedUpdatedContracts()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember constructor = Method(
            ".ctor",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: "constructor");
        ApiMember staticConstructor = Method(
            ".cctor",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: "constructor");
        staticConstructor.IsStatic = true;
        ApiMember finalizer = Method(
            "Finalize",
            MemorySafetyRulesState.Updated,
            ContractKind.Explicit,
            MemorySafetyPointerEvidence.Absent,
            kind: "finalizer");
        finalizer.IsFinalizer = true;
        ApiMember safeStaticConstructor = Method(
            ".cctor",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "constructor");
        safeStaticConstructor.IsStatic = true;
        ApiMember safeFinalizer = Method(
            "Finalize",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "finalizer");
        safeFinalizer.IsFinalizer = true;

        Assert.True(HasWord(
            Format(
                type,
                constructor,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts),
            "unsafe"));
        Assert.Throws<NotSupportedException>(() => Format(
            type,
            staticConstructor,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts));
        Assert.Throws<NotSupportedException>(() => Format(
            type,
            finalizer,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts));
        Assert.False(HasWord(
            Format(
                type,
                safeStaticConstructor,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts),
            "unsafe"));
        Assert.False(HasWord(
            Format(
                type,
                safeFinalizer,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts),
            "unsafe"));
    }

    [Fact]
    public void UnsupportedEventFormIsUnavailable()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember member = Method(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "event");

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Format(
                type,
                member,
                CSharpMemorySafetyLanguage.UpdatedCallerContracts));

        Assert.Contains("Value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("event", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("delegate")]
    [InlineData("enum")]
    public void UnsupportedTypeFormsProduceAtomicFailure(string kind)
    {
        ApiType type = Type(MemorySafetyRulesState.Updated, kind: kind);
        if (kind == "delegate")
        {
            ApiMember invoke = Method(
                "Invoke",
                MemorySafetyRulesState.Updated,
                ContractKind.None,
                MemorySafetyPointerEvidence.Absent);
            invoke.IsStatic = false;
            type.Members =
            [
                invoke,
            ];
        }

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(type),
                Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts)));

        Assert.Empty(outcome.SelfNameFailures);
        CSharpTypePrintDiagnostic failure = Assert.Single(outcome.MemorySafetyFailures);
        Assert.Equal(type.FullName, failure.TypeName);
        Assert.Contains(kind, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrimaryConstructorSyntaxIsUnavailableInOptInMode()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        var formatter = Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => formatter.FormatTypeDeclaration(
                type,
                [new ApiParameter { Type = "int", Name = "value" }]));

        Assert.Contains("primary", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectEntryPointsSurfaceModelAwareRefusals()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember missing = Method(
            "Missing",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        missing.MemorySafety = null;
        ApiMember property = Method(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "property");
        var formatter = Formatter(CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.Throws<NotSupportedException>(() => formatter.FormatMember(type, missing));
        Assert.Throws<NotSupportedException>(() => formatter.FormatMemberUnit(type, missing));
        Assert.Throws<NotSupportedException>(() => formatter.FormatMemberWithBody(
            type,
            missing,
            new CSharpBlockBody("return 0;")));
        Assert.Throws<NotSupportedException>(
            () => formatter.FormatAccessorHead(type, property, "get"));
        Assert.Throws<NotSupportedException>(() => formatter.FormatTypeUnit(type, [property]));
        type.MemorySafety = null;
        Assert.Throws<NotSupportedException>(() => formatter.FormatTypeDeclaration(type));
    }

    [Fact]
    public void SelectedMembersCanExcludeUnsupportedFormsWithoutDroppingSelectedFailures()
    {
        ApiType type = Type(MemorySafetyRulesState.Updated);
        ApiMember method = Method(
            "Run",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent);
        ApiMember property = Method(
            "Value",
            MemorySafetyRulesState.Updated,
            ContractKind.None,
            MemorySafetyPointerEvidence.Absent,
            kind: "property");
        type.Members = [method, property];
        var printer = new CSharpTypePrinter();
        CSharpTypePrintOptions options =
            Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        CSharpTypePrintResult selected = Printed(printer.Print(
            new CSharpTypePrintRequest(type, members: [method]),
            options));
        var selectedFailure = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            printer.Print(
                new CSharpTypePrintRequest(type, members: [method, property]),
                options));

        Assert.Contains("Run", selected.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Value", selected.Source, StringComparison.Ordinal);
        CSharpTypePrintDiagnostic failure =
            Assert.Single(selectedFailure.MemorySafetyFailures);
        Assert.Equal(type.FullName, failure.TypeName);
        Assert.Contains("Value", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedAndBatchFailuresAreAtomicBeforeSourcePublication()
    {
        ApiType valid = Type(MemorySafetyRulesState.Updated, name: "Valid");
        valid.Members =
        [
            Method(
                "Run",
                MemorySafetyRulesState.Updated,
                ContractKind.None,
                MemorySafetyPointerEvidence.Absent),
        ];
        ApiType nested = Type(MemorySafetyRulesState.Updated, name: "Outer.Inner");
        nested.Members =
        [
            Method(
                "Value",
                MemorySafetyRulesState.Updated,
                ContractKind.None,
                MemorySafetyPointerEvidence.Absent,
                kind: "property"),
        ];
        ApiType outer = Type(MemorySafetyRulesState.Updated, name: "Outer");
        var printer = new CSharpTypePrinter();
        CSharpTypePrintOptions options =
            Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        var nestedOutcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            printer.Print(
                new CSharpTypePrintRequest(
                    outer,
                    nestedTypes: [new CSharpTypePrintRequest(nested)]),
                options));
        var batchOutcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            printer.PrintBatch(
                [
                    new CSharpTypePrintRequest(valid),
                    new CSharpTypePrintRequest(
                        outer,
                        nestedTypes: [new CSharpTypePrintRequest(nested)]),
                ],
                options));

        Assert.Empty(nestedOutcome.SelfNameFailures);
        Assert.Equal(nested.FullName, Assert.Single(nestedOutcome.MemorySafetyFailures).TypeName);
        Assert.Empty(batchOutcome.SelfNameFailures);
        Assert.Equal(nested.FullName, Assert.Single(batchOutcome.MemorySafetyFailures).TypeName);
    }

    [Fact]
    public void MixedLegacyAndUpdatedRulesAreAtomicBeforeSourcePublication()
    {
        ApiType legacy = Type(MemorySafetyRulesState.Legacy, name: "Legacy");
        legacy.Members =
        [
            Method(
                "ConsumePointer",
                MemorySafetyRulesState.Legacy,
                ContractKind.Implicit,
                MemorySafetyPointerEvidence.Present,
                parameterType: "int*"),
        ];
        ApiType updated = Type(MemorySafetyRulesState.Updated, name: "Updated");
        updated.Members =
        [
            Method(
                "PointerFreeUnsafe",
                MemorySafetyRulesState.Updated,
                ContractKind.Explicit,
                MemorySafetyPointerEvidence.Absent),
        ];
        CSharpTypePrintOptions options =
            Options(CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        var batch = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().PrintBatch(
                [
                    new CSharpTypePrintRequest(legacy),
                    new CSharpTypePrintRequest(updated),
                ],
                options));
        var nested = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(
                    legacy,
                    nestedTypes: [new CSharpTypePrintRequest(updated)]),
                options));

        Assert.Empty(batch.SelfNameFailures);
        CSharpTypePrintDiagnostic batchFailure =
            Assert.Single(batch.MemorySafetyFailures);
        Assert.Equal("<batch>", batchFailure.TypeName);
        Assert.Contains("legacy", batchFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated", batchFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(nested.SelfNameFailures);
        Assert.Equal(
            "<batch>",
            Assert.Single(nested.MemorySafetyFailures).TypeName);
    }

    static CSharpFormatter Formatter(CSharpMemorySafetyLanguage language)
        => new(new CSharpFormatOptions
        {
            MemorySafetyLanguage = language,
        });

    static string Format(
        ApiType type,
        ApiMember member,
        CSharpMemorySafetyLanguage language,
        bool isExtern = false)
        => new CSharpFormatter(new CSharpFormatOptions
        {
            MemorySafetyLanguage = language,
            IsExtern = isExtern,
        }).FormatMember(type, member);

    static CSharpTypePrintOptions Options(CSharpMemorySafetyLanguage language)
        => new()
        {
            MemorySafetyLanguage = language,
        };

    static CSharpTypePrintResult Printed(CSharpTypePrintOutcome outcome)
        => Assert.IsType<CSharpTypePrintOutcome.Printed>(outcome).Result;

    static byte[] BuildEnumImage(string memberName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("EnumNameFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("EnumNameFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumBase = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("TargetEnum"),
            enumBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        fieldSignature.WriteByte(0x08);
        BlobHandle signature = metadata.GetOrAddBlob(fieldSignature);
        metadata.AddFieldDefinition(
            FieldAttributes.Public
                | FieldAttributes.SpecialName
                | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            signature);
        FieldDefinitionHandle literal = metadata.AddFieldDefinition(
            FieldAttributes.Public
                | FieldAttributes.Static
                | FieldAttributes.Literal,
            metadata.GetOrAddString(memberName),
            signature);
        metadata.AddConstant(literal, 1);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ApiType Type(
        MemorySafetyRulesState rules,
        ApiTypeLayout? layout = ApiTypeLayout.Auto,
        string kind = "class",
        string name = "Widget")
        => new()
        {
            Namespace = "Samples",
            Name = name,
            Kind = kind,
            MetadataToken = TypeToken,
            Layout = layout,
            LayoutDetails = layout == ApiTypeLayout.Explicit
                ? new ApiTypeLayoutFacts(ModuleId, TypeToken, Size: 32, PackingSize: 2)
                : null,
            MemorySafety = new ApiModuleMemorySafetyFacts(
                ModuleId,
                new MemorySafetyRulesResult.Available(rules, [])),
        };

    static ApiMember Method(
        string name,
        MemorySafetyRulesState rules,
        ContractKind contract,
        MemorySafetyPointerEvidence pointer,
        string kind = "method",
        string? parameterType = null,
        Guid? moduleId = null)
    {
        int token = Token(name, 0x06000000);
        return new ApiMember
        {
            Name = name,
            Kind = kind,
            MethodSemantics = ApiMethodSemanticsKind.None,
            MetadataToken = token,
            IsStatic = kind != "constructor" && kind != "finalizer"
                && kind != "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = kind == "constructor" || kind == "finalizer"
                    ? "void"
                    : "int",
                Parameters = parameterType is null
                    ? []
                    : [new ApiParameter { Type = parameterType, Name = "value" }],
            },
            MemorySafety = Facts(
                token,
                rules,
                contract,
                pointer,
                moduleId ?? ModuleId),
        };
    }

    static ApiMember Field(
        string name,
        MemorySafetyRulesState rules,
        ContractKind contract,
        MemorySafetyPointerEvidence pointer,
        bool isStatic = false,
        MemorySafetyFixedBufferEvidence fixedBuffer =
            MemorySafetyFixedBufferEvidence.Absent,
        Guid? moduleId = null,
        int? offset = 0)
    {
        int token = Token(name, 0x04000000);
        return new ApiMember
        {
            Name = name,
            Kind = "field",
            DeclarationMetadataToken = token,
            IsStatic = isStatic,
            ReturnType = pointer == MemorySafetyPointerEvidence.Present ? "int*" : "int",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = pointer == MemorySafetyPointerEvidence.Present ? "int*" : "int",
            },
            MemorySafety = Facts(
                token,
                rules,
                contract,
                pointer,
                moduleId ?? ModuleId,
                fixedBuffer),
            FieldLayout = new ApiFieldLayoutFacts(
                moduleId ?? ModuleId,
                TypeToken,
                token,
                isStatic ? null : offset),
        };
    }

    static ApiMember Property(
        string name,
        MemorySafetyRulesState rules,
        ContractKind contract,
        MemorySafetyPointerEvidence pointer,
        (string Kind, ContractKind Contract, MemorySafetyPointerEvidence Pointer)[]
            accessors,
        string returnType = "int")
    {
        int propertyToken = Token(name, 0x17000000);
        var accessorModels = new List<ApiAccessor>();
        var accessorFacts = new List<ApiMemberMemorySafetyFacts>();
        int? getterToken = null;
        int? setterToken = null;
        foreach ((string kind, ContractKind accessorContract,
                     MemorySafetyPointerEvidence accessorPointer) in accessors)
        {
            int token = Token($"{kind}_{name}", 0x06000000);
            accessorModels.Add(new ApiAccessor
            {
                Kind = kind,
                AccessibilityIsRepresentable = true,
                DeclarationModifiersMatchProperty = true,
                DeclarationModifiersAreRepresentable = true,
                IsExplicitInterfaceImplementation = false,
                SignatureMatchesProperty = true,
            });
            accessorFacts.Add(
                Facts(
                    token,
                    rules,
                    accessorContract,
                    accessorPointer,
                    ModuleId));
            if (kind == "get")
                getterToken = token;
            else if (kind == "set")
                setterToken = token;
        }

        return new ApiMember
        {
            Name = name,
            Kind = "property",
            DeclarationMetadataToken = propertyToken,
            IndexParameterCount = 0,
            GetterToken = getterToken,
            SetterToken = setterToken,
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = returnType,
                ReturnTypeShape = ApiTypeShape.PrimitiveType(
                    returnType switch
                    {
                        "int" => ApiPrimitiveType.Int32,
                        "string" => ApiPrimitiveType.String,
                        "void" => ApiPrimitiveType.Void,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(returnType)),
                    }),
                Accessors = accessorModels,
            },
            MemorySafety = Facts(
                propertyToken,
                rules,
                contract,
                pointer,
                ModuleId),
            AccessorMemorySafety = [.. accessorFacts],
        };
    }

    static ApiMemberMemorySafetyFacts Facts(
        int token,
        MemorySafetyRulesState rules,
        ContractKind contract,
        MemorySafetyPointerEvidence pointer,
        Guid moduleId,
        MemorySafetyFixedBufferEvidence fixedBuffer =
            MemorySafetyFixedBufferEvidence.Absent)
    {
        var evidence = new MemorySafetyMemberContractEvidence(
            token,
            rules,
            pointer,
            fixedBuffer,
            RequiresUnsafeAttributeEvidence.None,
            RequiresUnsafeAttributeEvidence.None,
            AssociatedMemberToken: null);
        MemorySafetyMemberContractResult callerContract = contract switch
        {
            ContractKind.None => new MemorySafetyMemberContractResult.None(evidence),
            ContractKind.Implicit => new MemorySafetyMemberContractResult.Implicit(evidence),
            ContractKind.Explicit => new MemorySafetyMemberContractResult.Explicit(evidence),
            ContractKind.Unavailable => new MemorySafetyMemberContractResult.Unavailable(
                evidence,
                new MemorySafetyMemberContractFailure(
                    MemorySafetyMemberContractFailureKind.MetadataUnavailable,
                    "Synthetic unavailable contract.")),
            _ => throw new ArgumentOutOfRangeException(nameof(contract)),
        };
        return new ApiMemberMemorySafetyFacts(moduleId, callerContract, pointer);
    }

    static bool HasWord(string text, string word)
        => Regex.IsMatch(
            text,
            $@"\b{Regex.Escape(word)}\b",
            RegexOptions.CultureInvariant);

    static int Token(string name, int table)
    {
        int row = 1;
        foreach (char character in name)
            row = ((row * 31) + character) & 0x00ffffff;
        return table | Math.Max(row, 1);
    }

    public enum ContractKind
    {
        None,
        Implicit,
        Explicit,
        Unavailable,
    }
}
