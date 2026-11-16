using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

// docs/design/member-diff.md#inputs: Public API compares the printed
// declaration, whose parts come from the model and never from the text. When
// the parts and the printed text disagree, the side is unidentified.
public sealed class CSharpDeclarationPartsTests
{
    public static TheoryData<string, bool> Corpus => new()
    {
        { "System.Private.CoreLib.dll", false },
        { "System.Private.CoreLib.dll", true },
        { "System.Text.Json.dll", false },
        { "System.Text.Json.dll", true },
    };

    // The gate: for every surface member the parts text is exactly the
    // printed declaration, every identified part slices to its own text in
    // order, and no model-printed member disagrees with its parts.
    [Theory]
    [MemberData(nameof(Corpus))]
    public void RealAssembly_PartsReproduceThePrintedDeclaration(string assembly, bool comparable)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", assembly);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader);
        var options = comparable
            ? new CSharpDeclarationOptions
            {
                IncludeCustomAttributes = true,
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
            }
            : new CSharpDeclarationOptions();

        int identified = 0;
        var unidentified = new Dictionary<string, int>(StringComparer.Ordinal);
        var disagreements = new List<string>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                string printed;
                try
                {
                    printed = CSharpDeclarationWriter.RenderMemberDeclaration(type, member, options);
                }
                catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
                {
                    continue;
                }

                CSharpDeclarationParts parts =
                    CSharpDeclarationWriter.RenderMemberDeclarationParts(type, member, options);
                Assert.Equal(printed, parts.Text);
                if (parts.Parts is { } identifiedParts)
                {
                    identified++;
                    AssertPartsSliceInOrder(parts.Text, identifiedParts);
                    continue;
                }

                string reason = parts.UnidentifiedReason!;
                unidentified[reason] = unidentified.GetValueOrDefault(reason) + 1;
                if (reason.Contains("disagree", StringComparison.Ordinal))
                    disagreements.Add($"{type.FullName}.{member.Name}: {printed}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{assembly} comparable={comparable}: identified {identified}; "
            + string.Join("; ", unidentified.OrderByDescending(entry => entry.Value)
                .Select(entry => $"{entry.Key} {entry.Value}")));
        Assert.True(identified > 0);
        Assert.True(
            disagreements.Count == 0,
            $"{disagreements.Count} disagreement(s):\n" + string.Join("\n", disagreements.Take(25)));
    }

    [Fact]
    public void Method_IdentifiesEveryPart()
    {
        var parts = Render(
            Type(),
            new ApiMember
            {
                Name = "Map",
                Kind = "method",
                Accessibility = "public",
                IsVirtual = true,
                Attributes = ["System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)"],
                AttributeTypes = ["System.ComponentModel.EditorBrowsableAttribute"],
                SignatureModel = new ApiSignature
                {
                    ReturnType = "TResult",
                    MemberName = "Map<TResult>",
                    TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                    Parameters =
                    [
                        new ApiParameter { Type = "int", Name = "count" },
                        new ApiParameter { Type = "string", Name = "name" },
                    ],
                },
            },
            new CSharpDeclarationOptions { IncludeCustomAttributes = true });

        Assert.Equal(
            "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]\n"
            + "public virtual TResult Map<TResult>(int count, string name) where TResult : class",
            parts.Text);
        Assert.Equal(
            [
                (CSharpDeclarationPartKind.Attribute, "System.ComponentModel.EditorBrowsableAttribute",
                    "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]"),
                (CSharpDeclarationPartKind.Modifier, "public", "public"),
                (CSharpDeclarationPartKind.Modifier, "virtual", "virtual"),
                (CSharpDeclarationPartKind.ReturnType, "type", "TResult"),
                (CSharpDeclarationPartKind.Name, "name", "Map<TResult>"),
                (CSharpDeclarationPartKind.Parameter, "0", "int count"),
                (CSharpDeclarationPartKind.Parameter, "1", "string name"),
                (CSharpDeclarationPartKind.Constraint, "TResult", "where TResult : class"),
            ],
            Slices(parts));
    }

    [Fact]
    public void Property_IdentifiesRequiredAndEachAccessor()
    {
        var parts = Render(
            Type(),
            new ApiMember
            {
                Name = "Value",
                Kind = "property",
                Accessibility = "public",
                SignatureModel = new ApiSignature
                {
                    ReturnType = "int",
                    MemberName = "Value",
                    IsRequired = true,
                    Accessors =
                    [
                        new ApiAccessor { Kind = "get" },
                        new ApiAccessor { Kind = "init", Accessibility = "protected" },
                    ],
                },
            });

        Assert.Equal("public required int Value { get; protected init; }", parts.Text);
        Assert.Equal(
            [
                (CSharpDeclarationPartKind.Modifier, "public", "public"),
                (CSharpDeclarationPartKind.Modifier, "required", "required"),
                (CSharpDeclarationPartKind.ReturnType, "type", "int"),
                (CSharpDeclarationPartKind.Name, "name", "Value"),
                (CSharpDeclarationPartKind.Accessor, "get", "get;"),
                (CSharpDeclarationPartKind.Accessor, "init", "protected init;"),
            ],
            Slices(parts));
    }

    [Fact]
    public void ReorderedAttributes_KeepTheirTypeKeys()
    {
        static ApiMember Field(params (string Text, string Type)[] attributes) => new()
        {
            Name = "Value",
            Kind = "field",
            Accessibility = "public",
            ReturnType = "int",
            Attributes = [.. attributes.Select(attribute => attribute.Text)],
            AttributeTypes = [.. attributes.Select(attribute => attribute.Type)],
        };

        var (baseline, current) = CSharpDeclarationWriter.RenderMemberDeclarationPair(
            Type(),
            Field(("N.A(\"old\")", "N.AAttribute"), ("N.B", "N.BAttribute")),
            Type(),
            Field(("N.B", "N.BAttribute"), ("N.A(\"new\")", "N.AAttribute")),
            new CSharpDeclarationOptions { IncludeCustomAttributes = true });

        static string Attribute(CSharpDeclarationParts parts, string key)
        {
            var part = Assert.Single(parts.Parts!, part => part.Key == key);
            return parts.Text.Substring(part.Start, part.Length);
        }
        Assert.Equal("[N.A(\"old\")]", Attribute(baseline!, "N.AAttribute"));
        Assert.Equal("[N.A(\"new\")]", Attribute(current!, "N.AAttribute"));
        Assert.Equal("[N.B]", Attribute(current!, "N.BAttribute"));
    }

    [Fact]
    public void RemovedVirtualAndAddedSetter_AreOneSidedParts()
    {
        static ApiMember Property(bool isVirtual, params string[] accessors) => new()
        {
            Name = "Value",
            Kind = "property",
            Accessibility = "public",
            IsVirtual = isVirtual,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Value",
                Accessors = [.. accessors.Select(kind => new ApiAccessor { Kind = kind })],
            },
        };

        var (baseline, current) = CSharpDeclarationWriter.RenderMemberDeclarationPair(
            Type(),
            Property(isVirtual: true, "get"),
            Type(),
            Property(isVirtual: false, "get", "set"));

        Assert.Contains(baseline!.Parts!, part => part is { Kind: CSharpDeclarationPartKind.Modifier, Key: "virtual" });
        Assert.DoesNotContain(current!.Parts!, part => part.Key == "virtual");
        Assert.DoesNotContain(baseline.Parts!, part => part is { Kind: CSharpDeclarationPartKind.Accessor, Key: "set" });
        Assert.Contains(current.Parts!, part => part is { Kind: CSharpDeclarationPartKind.Accessor, Key: "set" });
    }

    [Fact]
    public void KeywordName_IsEscapedInsideTheNamePart()
    {
        var parts = Render(
            Type(),
            new ApiMember
            {
                Name = "class",
                Kind = "property",
                Accessibility = "public",
                SignatureModel = new ApiSignature
                {
                    ReturnType = "int",
                    MemberName = "class",
                    Accessors = [new ApiAccessor { Kind = "get" }],
                },
            });

        Assert.Equal("public int @class { get; }", parts.Text);
        Assert.Contains((CSharpDeclarationPartKind.Name, "name", "@class"), Slices(parts));
    }

    [Fact]
    public void ReturnAttributes_AreOnePart()
    {
        var parts = Render(
            Type(),
            new ApiMember
            {
                Name = "Get",
                Kind = "method",
                Accessibility = "public",
                SignatureModel = new ApiSignature
                {
                    ReturnType = "string",
                    MemberName = "Get",
                    ReturnAttributes = ["N.NotNull"],
                },
            });

        Assert.Equal("[return: N.NotNull] public string Get()", parts.Text);
        Assert.Contains((CSharpDeclarationPartKind.ReturnAttributes, "return", "[return: N.NotNull]"), Slices(parts));
    }

    // docs/design/member-diff.md#inputs: the same Type is spelled the same way
    // on both sides. The current version adds a second Type named Widget, so
    // neither side may shorten Widget to its simple name.
    [Fact]
    public void Pair_ShortensTypeNamesAgainstBothSides()
    {
        static ApiMember Method(params string[] parameterTypes) => new()
        {
            Name = "Use",
            Kind = "method",
            Accessibility = "public",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = "Use",
                Parameters = [.. parameterTypes.Select((type, index) =>
                    new ApiParameter { Type = type, Name = $"p{index}" })],
            },
        };
        var options = new CSharpDeclarationOptions { TypeNameMode = CSharpTypeNameMode.ShortWithUsings };
        ApiMember baselineMember = Method("A.Widget");
        ApiMember currentMember = Method("A.Widget", "B.Widget");

        string alone = CSharpDeclarationWriter.RenderMemberDeclaration(Type(), baselineMember, options);
        var (baseline, current) = CSharpDeclarationWriter.RenderMemberDeclarationPair(
            Type(),
            baselineMember,
            Type(),
            currentMember,
            options);

        Assert.Equal("public void Use(Widget p0)", alone);
        Assert.Equal("public void Use(A.Widget p0)", baseline!.Text);
        Assert.Equal("public void Use(A.Widget p0, B.Widget p1)", current!.Text);
        Assert.True(baseline.IsIdentified);
        Assert.True(current.IsIdentified);
    }

    [Fact]
    public void Pair_OneSidedMemberUsesThePresentSide()
    {
        var (baseline, current) = CSharpDeclarationWriter.RenderMemberDeclarationPair(
            null,
            null,
            Type(),
            new ApiMember { Name = "Count", Kind = "field", Accessibility = "public", ReturnType = "int" });

        Assert.Null(baseline);
        Assert.Equal("public int Count", current!.Text);
        Assert.True(current.IsIdentified);
    }

    [Fact]
    public void Operator_IsUnidentifiedCompatibilityText()
    {
        var parts = Render(
            Type(),
            new ApiMember
            {
                Name = "op_Addition",
                Kind = "operator",
                Accessibility = "public",
                IsStatic = true,
                Signature = "Widget op_Addition(Widget left, Widget right)",
            });

        Assert.False(parts.IsIdentified);
        Assert.Equal("printed from compatibility text", parts.UnidentifiedReason);
        Assert.Equal(
            CSharpDeclarationWriter.RenderMemberDeclaration(
                Type(),
                new ApiMember
                {
                    Name = "op_Addition",
                    Kind = "operator",
                    Accessibility = "public",
                    IsStatic = true,
                    Signature = "Widget op_Addition(Widget left, Widget right)",
                }),
            parts.Text);
    }

    [Fact]
    public void UnrecordedAttributeTypes_LeaveTheSideUnidentified()
    {
        var parts = Render(
            Type(),
            new ApiMember
            {
                Name = "Value",
                Kind = "field",
                Accessibility = "public",
                ReturnType = "int",
                Attributes = ["N.A"],
            },
            new CSharpDeclarationOptions { IncludeCustomAttributes = true });

        Assert.Equal("[N.A]\npublic int Value", parts.Text);
        Assert.False(parts.IsIdentified);
        Assert.Equal("the attribute Types are not recorded", parts.UnidentifiedReason);
    }

    static ApiType Type() => new() { Namespace = "Samples", Name = "Container", Kind = "class" };

    static CSharpDeclarationParts Render(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions? options = null)
    {
        var parts = CSharpDeclarationWriter.RenderMemberDeclarationParts(type, member, options);
        Assert.Equal(CSharpDeclarationWriter.RenderMemberDeclaration(type, member, options), parts.Text);
        return parts;
    }

    static (CSharpDeclarationPartKind Kind, string Key, string Text)[] Slices(CSharpDeclarationParts parts)
    {
        Assert.True(parts.IsIdentified, parts.UnidentifiedReason);
        AssertPartsSliceInOrder(parts.Text, parts.Parts!);
        return [.. parts.Parts!.Select(part => (part.Kind, part.Key, parts.Text.Substring(part.Start, part.Length)))];
    }

    static void AssertPartsSliceInOrder(string text, IReadOnlyList<CSharpDeclarationPart> parts)
    {
        int end = 0;
        foreach (var part in parts)
        {
            Assert.True(part.Start >= end, $"part {part.Kind} {part.Key} overlaps the part before it");
            Assert.True(part.Length > 0, $"part {part.Kind} {part.Key} is empty");
            Assert.True(part.Start + part.Length <= text.Length, $"part {part.Kind} {part.Key} runs past the text");
            end = part.Start + part.Length;
        }
    }
}
