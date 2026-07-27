namespace ILInspector.Metadata.Tests;

public class MemberTargetResolverTests
{
    [Theory]
    [InlineData("Name", "Name", null, null, null, null)]
    [InlineData("Name:2", "Name", 2, null, null, null)]
    [InlineData("Name~abc123", "Name", null, "abc123", null, null)]
    [InlineData("M<T>", "M", null, null, null, 1)]
    [InlineData("M<TKey,TValue>:3", "M", 3, null, null, 2)]
    [InlineData(".ctor", ".ctor", null, null, null, null)]
    [InlineData(".cctor", ".cctor", null, null, null, null)]
    [InlineData("operator:op_Implicit:1", "op_Implicit", 1, null, "operator", null)]
    [InlineData("explicit:System.IConvertible.ToBoolean", "System.IConvertible.ToBoolean", null, null, "explicit-interface-implementation", null)]
    [InlineData("extension:AsMemory~abcdef", "AsMemory", null, "abcdef", "extension-method", null)]
    public void Parse_PreservesSelectorSemantics(
        string input,
        string name,
        int? overloadIndex,
        string? digest,
        string? kind,
        int? genericArity)
    {
        var selector = MemberTargetSelector.Parse(input);

        Assert.Equal(name, selector.Name);
        Assert.Equal(overloadIndex, selector.OverloadIndex);
        Assert.Equal(digest, selector.DigestPrefix);
        Assert.Equal(kind, selector.Kind);
        Assert.Equal(genericArity, selector.GenericArity);
    }

    [Fact]
    public void Resolve_NameSelectsOnlyOverload()
    {
        var type = CreateSurface().Types[0];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Count"));

        Assert.True(result.Found);
        Assert.Equal("Count", result.Target!.ApiMember.Member.Name);
        Assert.Equal(MemberTargetKind.Property, result.Target.Kind);
        Assert.Equal("P:Sample.Widget.Count", result.Target.Anchor.CanonicalSignature);
    }

    [Fact]
    public void Resolve_OverloadIndexUsesMemberIndexDisplayOrderButReturnsDeclaringIndex()
    {
        var type = CreateSurface().Types[0];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Run:1"));

        Assert.True(result.Found);
        Assert.Equal("void Run()", result.Target!.ApiMember.Member.Signature);
        Assert.Equal(1, result.Target.SelectorIndex);
        Assert.Equal(2, result.Target.DeclaringOverloadIndex);
    }

    [Fact]
    public void Resolve_DigestPrefixSelectsStableAnchor()
    {
        var type = CreateSurface().Types[0];
        var targetMember = type.Members.Single(member => member.Signature == "void Run(string value)");
        var digest = ApiMemberIdentity.GetMemberAnchor(type, targetMember).Fingerprint[..6];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse($"Run~{digest}"));

        Assert.True(result.Found);
        Assert.Equal(targetMember.Signature, result.Target!.ApiMember.Member.Signature);
        Assert.Equal(digest, result.Target.DigestPrefix);
    }

    [Fact]
    public void Resolve_DigestPrefixCollisionReturnsTypedDiagnostic()
    {
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Collision",
            Members =
            [
                new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" },
                new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" }
            ]
        };
        var first = type.Members[0];
        var digest = ApiMemberIdentity.GetMemberAnchor(type, first).Fingerprint[..1];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse($"Run~{digest}"));

        Assert.False(result.Found);
        Assert.Equal(MemberTargetDiagnosticKind.DigestAmbiguous, result.Diagnostic!.Kind);
        Assert.Equal(2, result.Diagnostic.Candidates.Count);
    }

    [Fact]
    public void Resolve_KindQualifiedExplicitMemberKeepsDottedMemberNameWhole()
    {
        var type = CreateSurface().Types[0];

        var result = MemberTargetResolver.Resolve(
            type,
            MemberTargetSelector.Parse("explicit:System.IConvertible.ToBoolean:1"));

        Assert.True(result.Found);
        Assert.Equal("System.IConvertible.ToBoolean", result.Target!.ApiMember.Member.Name);
        Assert.Equal(MemberTargetKind.ExplicitInterfaceImplementation, result.Target.Kind);
    }

    [Fact]
    public void Resolve_GenericArityFiltersGenericMethod()
    {
        var type = CreateSurface().Types[0];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Map<T>:1"));

        Assert.True(result.Found);
        Assert.Equal("T Map<T>(T value)", result.Target!.ApiMember.Member.Signature);
    }

    [Fact]
    public void Resolve_GenericArityPreservesDeclaringOverloadIndex()
    {
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "GenericChoice",
            Members =
            [
                new ApiMember { Name = "Choose", Kind = "method", Signature = "string Choose(string value)" },
                new ApiMember
                {
                    Name = "Choose",
                    Kind = "method",
                    Signature = "T Choose<T>(T value)",
                    SignatureModel = new ApiSignature
                    {
                        MemberName = "Choose",
                        TypeParameters = [new TypeParameter { Name = "T" }],
                        Parameters = [new ApiParameter { Name = "value", Type = "T" }]
                    }
                }
            ]
        };

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Choose<T>:1"));

        Assert.True(result.Found);
        Assert.Equal("T Choose<T>(T value)", result.Target!.ApiMember.Member.Signature);
        Assert.Equal(1, result.Target.SelectorIndex);
        Assert.Equal(2, result.Target.DeclaringOverloadIndex);
    }

    [Fact]
    public void Resolve_ExtensionMethodCarriesPhysicalBodyTarget()
    {
        var type = CreateSurface().Types[0];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("extension:Normalize:1"));

        Assert.True(result.Found);
        Assert.Equal(MemberTargetKind.ExtensionMethod, result.Target!.Kind);
        Assert.NotNull(result.Target.Body);
        Assert.Equal("Sample.WidgetExtensions", result.Target.Body!.DeclaringType);
        Assert.Equal(7, result.Target.Body.DeclaringOverloadIndex);
    }

    [Fact]
    public void Resolve_MissingMemberReturnsTypedDiagnostic()
    {
        var type = CreateSurface().Types[0];

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Missing"));

        Assert.False(result.Found);
        Assert.Equal(MemberTargetDiagnosticKind.MissingMember, result.Diagnostic!.Kind);
        Assert.Empty(result.Diagnostic.Candidates);
    }

    [Fact]
    public void Resolve_ReadWritePropertyDefaultsToGetterAccessorBody()
    {
        var type = CreateAccessorSurface();

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Value"));

        Assert.True(result.Found);
        Assert.Equal(MemberTargetKind.Property, result.Target!.Kind);
        Assert.NotNull(result.Target.Body);
        // Accessor ordinal 1 addresses the getter; the body carries the getter token.
        Assert.Equal(1, result.Target.Body!.DeclaringOverloadIndex);
        Assert.Equal(0x06000101, result.Target.Body.MetadataToken);
    }

    [Fact]
    public void Resolve_ReadWritePropertySetterSelectableByAccessorOrdinal()
    {
        var type = CreateAccessorSurface();

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Value:2"));

        Assert.True(result.Found);
        Assert.Equal(MemberTargetKind.Property, result.Target!.Kind);
        // Accessor ordinal 2 addresses the setter; body sections select it via this index.
        Assert.Equal(2, result.Target.Body!.DeclaringOverloadIndex);
        // The body token names the setter accessor, not the getter (issue #3265).
        Assert.Equal(0x06000102, result.Target.Body.MetadataToken);
    }

    [Fact]
    public void Resolve_ReadOnlyPropertyRejectsSetterOrdinal()
    {
        var type = CreateAccessorSurface();

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("ReadOnly:2"));

        Assert.False(result.Found);
        Assert.Equal(MemberTargetDiagnosticKind.OverloadOutOfRange, result.Diagnostic!.Kind);
        Assert.Contains("ReadOnly:1 through ReadOnly:1", result.Diagnostic.Message);
    }

    [Fact]
    public void Resolve_EventRemoverSelectableByAccessorOrdinal()
    {
        var type = CreateAccessorSurface();

        var result = MemberTargetResolver.Resolve(type, MemberTargetSelector.Parse("Changed:2"));

        Assert.True(result.Found);
        Assert.Equal(MemberTargetKind.Event, result.Target!.Kind);
        // Accessor ordinal 1 = adder, 2 = remover.
        Assert.Equal(2, result.Target.Body!.DeclaringOverloadIndex);
        // The body token names the remover accessor, not the adder (issue #3265).
        Assert.Equal(0x06000105, result.Target.Body.MetadataToken);
    }

    static ApiType CreateAccessorSurface()
        => new()
        {
            Namespace = "Sample",
            Name = "Accessors",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    Signature = "int Value { get; set; }",
                    GetterToken = 0x06000101,
                    SetterToken = 0x06000102
                },
                new ApiMember
                {
                    Name = "ReadOnly",
                    Kind = "property",
                    Signature = "int ReadOnly { get; }",
                    GetterToken = 0x06000103
                },
                new ApiMember
                {
                    Name = "Changed",
                    Kind = "event",
                    Signature = "System.EventHandler Changed",
                    AdderToken = 0x06000104,
                    RemoverToken = 0x06000105
                }
            ]
        };

    static ApiSurface CreateSurface()
        => new()
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "Sample",
                    Name = "Widget",
                    Members =
                    [
                        new ApiMember { Name = "Run", Kind = "method", Signature = "void Run(string value)" },
                        new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" },
                        new ApiMember
                        {
                            Name = "Map",
                            Kind = "method",
                            Signature = "T Map<T>(T value)",
                            SignatureModel = new ApiSignature
                            {
                                MemberName = "Map",
                                TypeParameters = [new TypeParameter { Name = "T" }],
                                Parameters = [new ApiParameter { Name = "value", Type = "T" }]
                            }
                        },
                        new ApiMember { Name = "Count", Kind = "property" },
                        new ApiMember
                        {
                            Name = "System.IConvertible.ToBoolean",
                            Kind = "explicit-interface-implementation",
                            Signature = "bool System.IConvertible.ToBoolean(System.IFormatProvider? provider)"
                        },
                        new ApiMember
                        {
                            Name = "Normalize",
                            Kind = "extension-method",
                            Signature = "public static string Normalize(this Sample.Widget widget)",
                            DeclaringType = "Sample.WidgetExtensions",
                            DeclaringOverloadIndex = 7,
                            MetadataToken = 0x06000007
                        }
                    ]
                }
            ]
        };
}
