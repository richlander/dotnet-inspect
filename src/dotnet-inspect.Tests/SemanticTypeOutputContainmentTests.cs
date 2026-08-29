using System.Text.Json;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.Metadata;
using InertText;
using Markout;

namespace DotnetInspector.Tests;

public sealed class SemanticTypeOutputContainmentTests
{
    [Fact]
    public void TypePresentation_CarriesContainedTextWithoutChangingIdentity()
    {
        var type = new ApiType
        {
            Namespace = "Dotnet\u200BInspect.Metadata",
            Name = "Arity`1",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "T\u2060Value" },
            ],
        };
        var surface = new ApiSurface
        {
            Name = "Fixture",
            Types = [type],
        };

        var shape = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: []);
        var (table, _) = ApiOutputFormatter.BuildSurfaceTableView(
            surface,
            new ApiOptions());
        var (document, _) = ApiOutputFormatter.BuildFullApiView(
            surface,
            new ApiOptions { Verbosity = Verbosity.Minimal });

        Assert.Equal(
            @"Dotnet\u200BInspect.Metadata.Arity<T\u2060Value>",
            shape.FullNameText.ToString());
        Assert.Equal(
            shape.FullNameText,
            Assert.Single(table.Rows!).TypeText);
        Assert.Equal(
            shape.FullNameText,
            Assert.Single(document.Classes!).TypeText);

        Assert.Equal("Dotnet\u200BInspect.Metadata", type.Namespace);
        Assert.Equal("T\u2060Value", Assert.Single(type.TypeParameters).Name);
    }

    [Fact]
    public void TypeJson_ContainsDecodedValuesAndPreservesRawModel()
    {
        var member = new ApiMember
        {
            Kind = "method",
            Name = "Run\u202E",
            ReturnType = "Result\u202E",
            Signature = "Result\u202E Run\u202E()",
            Documentation = new DocComment
            {
                Summary = "before\u202Eafter",
            },
        };
        var type = new ApiType
        {
            Namespace = "名前空間",
            Name = "Arity`1",
            MetadataName = @"Type\u202E",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter
                {
                    Name = "型\u202EValue",
                    Variance = "out",
                },
            ],
            Members = [member],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiArtifactJson.Type);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement root = document.RootElement;
        Assert.Equal("名前空間", root.GetProperty("namespace").GetString());
        Assert.Equal(
            @"Type\\u202E",
            root.GetProperty("metadata_name").GetString());
        Assert.Equal(
            @"型\u202EValue",
            root.GetProperty("type_parameters")[0]
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            @"out 型\u202EValue",
            root.GetProperty("type_parameters")[0]
                .GetProperty("display_name")
                .GetString());
        JsonElement memberJson = root.GetProperty("members")[0];
        Assert.Equal(@"Run\u202E", memberJson.GetProperty("name").GetString());
        Assert.Equal(
            @"Result\u202E Run\u202E()",
            memberJson.GetProperty("signature").GetString());
        Assert.Equal(
            @"before\u202Eafter",
            memberJson.GetProperty("documentation")
                .GetProperty("summary")
                .GetString());
        Assert.All(
            EnumerateJsonStrings(root)
                .Where(value => value != @"Type\\u202E"),
            value => Assert.DoesNotContain(@"\\u202E", value));

        Assert.Equal(@"Type\u202E", type.MetadataName);
        Assert.Equal("型\u202EValue", Assert.Single(type.TypeParameters).Name);
        Assert.Equal("Run\u202E", member.Name);
        Assert.Equal(
            "Result\u202E Run\u202E()",
            member.Signature);
    }

    [Fact]
    public void CSharpField_PreservesEscapesAndContainsResidualScalars()
    {
        const string rendered =
            "void Meth\\\\u0041(string path = \"C:\\\\temp\")";
        Assert.Equal(
            rendered,
            ApiViewText.CSharpField(rendered).ToString());

        string surrogate = ApiViewText.CSharpField(
            "string M(string value = \"A\uD800B\")").ToString();
        Assert.Equal(
            "string M(string value = \"A\\uD800B\")",
            surrogate);
        Assert.DoesNotContain('\uD800', surrogate);

        Assert.Equal(
            @"void M()\u001B",
            ApiViewText.CSharpField("void M()\u001B").ToString());
    }

    [Fact]
    public void CSharpField_MixedCSharpAndVisualEscapes_PreservesSpellingWithInertEvidence()
    {
        const string safeCSharp = """string M() => "\v\n\"\\\u202E";""";
        string rendered = safeCSharp + '\u202E';

        CSharpPresentationText contained = ApiViewText.CSharpField(rendered);

        Assert.Equal(safeCSharp + @"\u202E", contained.ToString());
        Assert.Equal(
            new InertString(TextPolicy.Field, rendered),
            contained.Evidence);
    }

    [Fact]
    public void CSharpCodeText_PreservesContainmentEvidence()
    {
        CSharpPresentationText contained =
            ApiViewText.CSharpField("void M()\u202E");

        CSharpPresentationText code =
            MarkoutInline.CodeText(contained);

        Assert.Equal(
            @"<code>void M()\u202E</code>",
            code.ToString());
        Assert.Equal(contained.Evidence, code.Evidence);
        Assert.True(code.Evidence.RequiredContainment);
    }

    [Fact]
    public void SourceLocationRows_UseTypedContainmentCurrencies()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Widget",
            Kind = "class",
            SourceUrl = "https://example.invalid/T\u200Bype.cs",
            Members =
            [
                new ApiMember
                {
                    Name = "Run\u200BHidden",
                    Kind = "method",
                    Accessibility = "public",
                    Signature = "void Run\u200BHidden()",
                    SourceFilePath = "/src/M\u200Bember.cs",
                    SourceUrl = "https://example.invalid/M\u200Bember.cs",
                    SourceLineNumber = 10,
                },
            ],
        };
        TypeView view = ApiOutputFormatter.BuildTypeView(
            type,
            foundIn: "Test.dll",
            packageName: null,
            packageVersion: null,
            apiSource: "local",
            selectedTfm: null,
            new TypeOptions());

        ApiOutputFormatter.PopulateMemberSourceLocations(
            view,
            type,
            new TypeOptions());

        TypeSourceFileRow typeSource = Assert.Single(view.SourceFileRows!);
        MemberSourceLocationRow memberSource =
            Assert.Single(view.SourceLocationRows!);
        string rendered = string.Join(
            ' ',
            typeSource.Url,
            memberSource.Selector,
            memberSource.Signature,
            memberSource.File,
            memberSource.Url);
        Assert.DoesNotContain('\u200B', rendered);
        Assert.Contains(@"\u200B", rendered);
        Assert.Equal(
            "https://example.invalid/T\u200Bype.cs",
            typeSource.RawUrl);
        Assert.Equal(
            "https://example.invalid/M\u200Bember.cs",
            memberSource.RawUrl);
    }

    [Fact]
    public void FullApiTitle_ContainsArtifactName()
    {
        var api = new ApiSurface
        {
            Name = "Fixture\u202E\n## Forged",
        };

        var (view, _) = ApiOutputFormatter.BuildFullApiView(
            api,
            new ApiOptions());

        Assert.NotNull(view.Name);
        string name = view.Name;
        Assert.Equal(
            @"Fixture\u202E\^J## Forged",
            name);
        Assert.DoesNotContain('\u202E', name);
        Assert.DoesNotContain('\n', name);
    }

    [Fact]
    public void RawTypePresentation_DistinguishesLiteralEscapeFromScalar()
    {
        const string literal = @"Ns.Lit\u202EType";
        const string scalar = "Ns.Lit\u202EType";
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "ReturnsLiteral",
                    Kind = "method",
                    Signature = $"{literal} ReturnsLiteral()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = literal,
                        MemberName = "ReturnsLiteral",
                    },
                },
                new ApiMember
                {
                    Name = "ReturnsScalar",
                    Kind = "method",
                    Signature = $"{scalar} ReturnsScalar()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = scalar,
                        MemberName = "ReturnsScalar",
                    },
                },
            ],
        };

        Assert.Equal(
            @"Ns.Lit\\u202EType",
            ApiViewText.RawTypeField(literal).ToString());
        Assert.Equal(
            @"Ns.Lit\u202EType",
            ApiViewText.RawTypeField(scalar).ToString());

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        JsonElement members = document.RootElement.GetProperty("members");
        string literalSignature =
            members[0].GetProperty("signature").GetString()!;
        string scalarSignature =
            members[1].GetProperty("signature").GetString()!;

        Assert.Contains(@"Ns.Lit\\u202EType", literalSignature);
        Assert.Contains(@"Ns.Lit\u202EType", scalarSignature);
        Assert.NotEqual(literalSignature, scalarSignature);
        Assert.Equal(
            $"{literal} ReturnsLiteral()",
            type.Members[0].Signature);

        var surface = new ApiSurface
        {
            Name = "Fixture",
            Types = [type],
        };
        ApiArtifactJson.Prepare(surface);
        using JsonDocument surfaceDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(
                surface,
                ApiArtifactJson.Surface));
        string surfaceSignature = surfaceDocument.RootElement
            .GetProperty("types")[0]
            .GetProperty("members")[0]
            .GetProperty("signature")
            .GetString()!;
        Assert.Contains(@"Ns.Lit\\u202EType", surfaceSignature);
    }

    [Fact]
    public void PreparedJsonSignature_PreservesCSharpLiteralEscapes()
    {
        const string signature =
            "string Echo(string value = \"line\\n\")";
        var member = new ApiMember
        {
            Name = "Echo",
            Kind = "method",
            Signature = signature,
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "Echo",
                Parameters =
                [
                    new ApiParameter
                    {
                        Name = "value",
                        Type = "string",
                        HasDefault = true,
                        DefaultValueText = "\"line\\n\"",
                    },
                ],
            },
        };
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members = [member],
        };

        ApiArtifactJson.Prepare(type);
        foreach (var jsonType in
            new[] { ApiArtifactJson.Type, ApiArtifactJson.CompactType })
        {
            using JsonDocument document = JsonDocument.Parse(
                JsonSerializer.Serialize(type, jsonType));
            string prepared = document.RootElement
                .GetProperty("members")[0]
                .GetProperty("signature")
                .GetString()!;

            Assert.Equal(signature, prepared);
        }

        Assert.Equal(signature, member.Signature);
    }

    [Fact]
    public void PreparedJsonSignature_UsesOwningKeywordGenericContext()
    {
        const string genericSignature = "int Echo(int value)";
        const string literalSignature =
            "string Literal(string value = \"line\\n\")";
        var genericMember = new ApiMember
        {
            Name = "Echo",
            Kind = "method",
            Signature = genericSignature,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Echo",
                Parameters =
                [
                    new ApiParameter { Name = "value", Type = "int" },
                ],
            },
        };
        var literalMember = new ApiMember
        {
            Name = "Literal",
            Kind = "method",
            Signature = literalSignature,
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "Literal",
                Parameters =
                [
                    new ApiParameter
                    {
                        Name = "value",
                        Type = "string",
                        HasDefault = true,
                        DefaultValueText = "\"line\\n\"",
                    },
                ],
            },
        };
        var type = new ApiType
        {
            Name = "Probe`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "int" }],
            Members = [genericMember, literalMember],
        };

        ApiArtifactJson.Prepare(type);
        foreach (var jsonType in
            new[] { ApiArtifactJson.Type, ApiArtifactJson.CompactType })
        {
            using JsonDocument document = JsonDocument.Parse(
                JsonSerializer.Serialize(type, jsonType));
            JsonElement members = document.RootElement.GetProperty("members");

            Assert.Equal(
                "@int Echo(@int value)",
                members[0].GetProperty("signature").GetString());
            Assert.Equal(
                literalSignature,
                members[1].GetProperty("signature").GetString());
        }

        Assert.Equal(genericSignature, genericMember.Signature);
        Assert.Equal(literalSignature, literalMember.Signature);
    }

    [Fact]
    public void PreparedJsonSignature_DistinguishesRawParameterNameEscapes()
    {
        const string literalName = @"arg\u202E";
        const string scalarName = "arg\u202E";
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members =
            [
                Method("Literal", literalName),
                Method("Scalar", scalarName),
            ],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        JsonElement members = document.RootElement.GetProperty("members");
        string literalSignature =
            members[0].GetProperty("signature").GetString()!;
        string scalarSignature =
            members[1].GetProperty("signature").GetString()!;

        Assert.Contains(@"arg\\u202E", literalSignature);
        Assert.Contains(@"arg\u202E", scalarSignature);
        Assert.NotEqual(literalSignature, scalarSignature);
        Assert.Equal(
            "string Literal(string arg\\u202E)",
            type.Members[0].Signature);

        static ApiMember Method(string name, string parameterName)
            => new()
            {
                Name = name,
                Kind = "method",
                Signature = $"string {name}(string {parameterName})",
                SignatureModel = new ApiSignature
                {
                    ReturnType = "string",
                    MemberName = name,
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = parameterName,
                            Type = "string",
                        },
                    ],
                },
            };
    }

    [Fact]
    public void PreparedJsonSignature_DoesNotPromoteParameterAttributes()
    {
        const string signature = @"bool Validate(string value\marker)";
        var member = new ApiMember
        {
            Name = "Validate",
            Kind = "method",
            Signature = signature,
            SignatureModel = new ApiSignature
            {
                ReturnType = "bool",
                MemberName = "Validate",
                Parameters =
                [
                    new ApiParameter
                    {
                        Attributes = ["System.Diagnostics.CodeAnalysis.NotNullWhen(false)"],
                        Type = "string",
                        Name = @"value\marker",
                    },
                ],
            },
        };
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members = [member],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(type, ApiArtifactJson.Type));
        string prepared = document.RootElement
            .GetProperty("members")[0]
            .GetProperty("signature")
            .GetString()!;

        Assert.Equal(@"bool Validate(string value\\marker)", prepared);
        Assert.DoesNotContain("NotNullWhen", prepared, StringComparison.Ordinal);
        Assert.Equal(signature, member.Signature);
    }

    [Fact]
    public void PreparedJsonSignature_DegradedFallbackRemainsVisible()
    {
        const string signature = "Legacy.Type Run(Legacy.Type value)";
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            Signature = signature,
            SignatureDecodeStatus = SignatureDecodeStatus.Degraded,
        };
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members = [member],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        JsonElement memberJson = document.RootElement
            .GetProperty("members")[0];

        Assert.Contains(
            signature,
            memberJson.GetProperty("signature").GetString()!,
            StringComparison.Ordinal);
        Assert.Equal(
            "Degraded",
            memberJson.GetProperty("signature_decode_status").GetString());
        Assert.Equal(signature, member.Signature);
    }

    [Fact]
    public void PreparedJsonSignature_ModelFreeHazardsRemainDistinctAndVisible()
    {
        const string literal = @"Ns.Lit\u202EType";
        const string scalar = "Ns.Lit\u202EType";
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "ReturnsLiteral",
                    Kind = "method",
                    Signature = $"{literal} ReturnsLiteral()",
                },
                new ApiMember
                {
                    Name = "ReturnsScalar",
                    Kind = "method",
                    Signature = $"{scalar} ReturnsScalar()",
                },
            ],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        JsonElement members = document.RootElement.GetProperty("members");
        string literalSignature =
            members[0].GetProperty("signature").GetString()!;
        string scalarSignature =
            members[1].GetProperty("signature").GetString()!;

        Assert.Contains(@"Ns.Lit\\u202EType", literalSignature);
        Assert.Contains(@"Ns.Lit\u202EType", scalarSignature);
        Assert.NotEqual(literalSignature, scalarSignature);
        Assert.Equal(
            "Degraded",
            members[0].GetProperty("signature_decode_status").GetString());
        Assert.Equal(
            "Degraded",
            members[1].GetProperty("signature_decode_status").GetString());
        Assert.Equal(
            $"{literal} ReturnsLiteral()",
            type.Members[0].Signature);
        Assert.Equal(
            $"{scalar} ReturnsScalar()",
            type.Members[1].Signature);
    }

    [Fact]
    public void CompatibilitySignature_PersistenceRoundTripIsPresentationIdempotent()
    {
        const string literalType = "N.Bad= \"A\\u202EB\"Type";
        const string scalarType = "N.Bad= \"A\u202EB\"Type";
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Literal",
                    Kind = "method",
                    Signature =
                        $"{ApiPresentationText.RawTypeField(literalType)} Literal()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = literalType,
                        MemberName = "Literal",
                    },
                },
                new ApiMember
                {
                    Name = "Scalar",
                    Kind = "method",
                    Signature =
                        $"{ApiPresentationText.RawTypeField(scalarType)} Scalar()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = scalarType,
                        MemberName = "Scalar",
                    },
                },
            ],
        };

        string persistenceJson = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        Assert.Contains(
            "\"signature_model\"",
            persistenceJson,
            StringComparison.Ordinal);
        ApiType restored = JsonSerializer.Deserialize(
            persistenceJson,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.All(restored.Members, member =>
            Assert.NotNull(member.SignatureModel));
        string[] original = PreparedSignatures(type);
        string[] roundTripped = PreparedSignatures(restored);
        Assert.Equal(original, roundTripped);
        Assert.NotEqual(original[0], original[1]);
        AssertArtifactOmitsSignatureModel(
            JsonSerializer.Serialize(
                restored,
                ApiArtifactJson.CompactType));
        var surface = new ApiSurface { Types = [restored] };
        ApiArtifactJson.Prepare(surface);
        AssertArtifactOmitsSignatureModel(
            JsonSerializer.Serialize(
                surface,
                ApiArtifactJson.Surface));

        static string[] PreparedSignatures(ApiType value)
        {
            ApiArtifactJson.Prepare(value);
            using JsonDocument document = JsonDocument.Parse(
                JsonSerializer.Serialize(value, ApiArtifactJson.Type));
            Assert.False(
                document.RootElement.GetProperty("members")[0]
                    .TryGetProperty(
                        "signature_model",
                        out _));
            return document.RootElement
                .GetProperty("members")
                .EnumerateArray()
                .Select(member =>
                    member.GetProperty("signature").GetString()!)
                .ToArray();
        }

        static void AssertArtifactOmitsSignatureModel(string json)
        {
            Assert.DoesNotContain(
                "\"signature_model\"",
                json,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreparedJsonSignature_MetadataEqualsQuoteRemainsContainedAndDegraded()
    {
        const string literal =
            "N.Bad= \"A\\u202EB\"Type Literal()";
        const string scalar =
            "N.Bad= \"A\u202EB\"Type Scalar()";
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Literal",
                    Kind = "method",
                    Signature = literal,
                },
                new ApiMember
                {
                    Name = "Scalar",
                    Kind = "method",
                    Signature = scalar,
                },
            ],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(type, ApiArtifactJson.Type));
        JsonElement members = document.RootElement.GetProperty("members");
        string literalSignature =
            members[0].GetProperty("signature").GetString()!;
        string scalarSignature =
            members[1].GetProperty("signature").GetString()!;

        Assert.DoesNotContain('\u202E', scalarSignature);
        Assert.NotEqual(literalSignature, scalarSignature);
        Assert.All(
            members.EnumerateArray(),
            member => Assert.Equal(
                "Degraded",
                member.GetProperty("signature_decode_status").GetString()));
    }

    [Fact]
    public void TypeParameterJson_PreservesSyntaxAndContainsRawTypes()
    {
        var parameter = new TypeParameter
        {
            Name = "T",
            Constraints =
            [
                "class",
                @"Ns.Lit\u202EType",
                "new()",
            ],
            StructuredConstraints =
            [
                new("class", IsTypeName: false),
                new(@"Ns.Lit\u202EType", IsTypeName: true),
                new("new()", IsTypeName: false),
            ],
        };
        var type = new ApiType
        {
            Name = "Probe",
            Kind = "class",
            TypeParameters = [parameter],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        JsonElement parameterJson = document.RootElement
            .GetProperty("type_parameters")[0];

        Assert.Equal(
            ["class", @"Ns.Lit\\u202EType", "new()"],
            parameterJson.GetProperty("constraints")
                .EnumerateArray()
                .Select(static value => value.GetString()!)
                .ToArray());
        Assert.Equal(
            @"class, Ns.Lit\\u202EType, new()",
            parameterJson.GetProperty("constraints_summary")
                .GetString());
        Assert.Equal(
            @"Ns.Lit\u202EType",
            parameter.Constraints[1]);
        Assert.False(parameterJson.TryGetProperty(
            "structured_constraints",
            out _));
        Assert.False(parameterJson.TryGetProperty("type_kind", out _));
    }

    [Fact]
    public void TypeParameterJson_UsesOwningGenericContext()
    {
        var keywordParameter = new TypeParameter
        {
            Name = "int",
        };
        var constrainedParameter = new TypeParameter
        {
            Name = "U",
            Constraints = ["int"],
            StructuredConstraints =
            [
                new TypeParameterConstraint(
                    "int",
                    IsTypeName: true),
            ],
        };
        var type = new ApiType
        {
            Name = "Holder`2",
            Kind = "class",
            TypeParameters =
            [
                keywordParameter,
                constrainedParameter,
            ],
        };

        ApiArtifactJson.Prepare(type);
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        JsonElement parameterJson = document.RootElement
            .GetProperty("type_parameters")[1];

        Assert.Equal(
            ["@int"],
            parameterJson.GetProperty("constraints")
                .EnumerateArray()
                .Select(static value => value.GetString()!)
                .ToArray());
        Assert.Equal(
            "@int",
            parameterJson.GetProperty("constraints_summary")
                .GetString());
        Assert.Equal("int", constrainedParameter.Constraints[0]);
    }

    [Fact]
    public void TypeParameterPersistence_RetainsPrinterProvenance()
    {
        var type = new ApiType
        {
            Name = "GlobalKeywordType`1",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter
                {
                    Name = "T",
                    Constraints = ["struct", "struct"],
                    StructuredConstraints =
                    [
                        new("struct", IsTypeName: false),
                        new("struct", IsTypeName: true),
                    ],
                },
            ],
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    IsOverride = true,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "T?",
                        MemberName = "M",
                        TypeParameters =
                        [
                            new TypeParameter
                            {
                                Name = "T",
                                TypeKind =
                                    TypeParameterTypeKind.ReferenceType,
                            },
                        ],
                    },
                },
            ],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement
            .GetProperty("type_parameters")[0]
            .TryGetProperty("structured_constraints", out _));
        Assert.True(document.RootElement
            .GetProperty("members")[0]
            .GetProperty("signature_model")
            .GetProperty("type_parameters")[0]
            .TryGetProperty("type_kind", out _));

        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;
        TypeParameter restoredTypeParameter =
            Assert.Single(restored.TypeParameters);
        Assert.Equal(
            [
                new TypeParameterConstraint("struct", IsTypeName: false),
                new TypeParameterConstraint("struct", IsTypeName: true),
            ],
            restoredTypeParameter.StructuredConstraints);
        TypeParameter restoredMethodParameter = Assert.Single(
            Assert.Single(restored.Members)
                .SignatureModel!
                .TypeParameters);
        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            restoredMethodParameter.TypeKind);

        var formatter = new CSharpFormatter();
        Assert.Contains(
            "where T : struct, @struct",
            formatter.FormatTypeDeclaration(restored),
            StringComparison.Ordinal);
        Assert.Contains(
            "where T : class",
            formatter.FormatMember(
                restored,
                Assert.Single(restored.Members)),
            StringComparison.Ordinal);

        using JsonDocument artifact = JsonDocument.Parse(
            JsonSerializer.Serialize(type, ApiArtifactJson.Type));
        JsonElement artifactParameter = artifact.RootElement
            .GetProperty("type_parameters")[0];
        Assert.False(artifactParameter.TryGetProperty(
            "structured_constraints",
            out _));
        Assert.False(artifactParameter.TryGetProperty("type_kind", out _));

        using JsonDocument compactArtifact = JsonDocument.Parse(
            JsonSerializer.Serialize(type, ApiArtifactJson.CompactType));
        AssertImplementationProvenanceAbsent(
            compactArtifact.RootElement.GetProperty("type_parameters")[0]);

        var surface = new ApiSurface { Types = [type] };
        using JsonDocument surfaceArtifact = JsonDocument.Parse(
            JsonSerializer.Serialize(surface, ApiArtifactJson.Surface));
        AssertImplementationProvenanceAbsent(
            surfaceArtifact.RootElement
                .GetProperty("types")[0]
                .GetProperty("type_parameters")[0]);

        static void AssertImplementationProvenanceAbsent(
            JsonElement parameter)
        {
            Assert.False(parameter.TryGetProperty(
                "structured_constraints",
                out _));
            Assert.False(parameter.TryGetProperty("type_kind", out _));
        }
    }

    [Fact]
    public void DocumentationEncoding_PreservesLiteralEscapeIdentity()
    {
        var comment = new DocComment
        {
            Summary = @"Escape \u0041 and C:\temp",
        };

        Assert.Equal(
            @"Escape \\u0041 and C:\\temp",
            comment.Summary);
        Assert.Equal(
            comment.Summary,
            ApiViewText.EncodedField(comment.Summary!).ToString());
    }

    [Fact]
    public void DocumentationEncoding_RoundTripsThroughPersistenceJson()
    {
        var type = new ApiType
        {
            Documentation = new DocComment
            {
                Summary = "before\u202Eafter literal \\u0041",
            },
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "before\u202Eafter literal \\u0041",
            document.RootElement
                .GetProperty("documentation")
                .GetProperty("summary")
                .GetString());

        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.Equal(
            type.Documentation.Summary,
            restored.Documentation.Summary);
    }

    [Fact]
    public void DocumentationPersistence_LegacyHazardousEscapeRemainsLiteral()
    {
        const string json =
            """{"documentation":{"summary":"literal \\u202E scalar \u202E"}}""";

        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.Equal(
            @"literal \\u202E scalar \u202E",
            restored.Documentation.Summary);
    }

    [Fact]
    public void DocumentationPersistence_LegacyLiteralEscapeRemainsLiteral()
    {
        const string json =
            """{"documentation":{"summary":"literal \\u0041"}}""";

        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.Equal(
            @"literal \\u0041",
            restored.Documentation.Summary);
    }

    [Fact]
    public void DocumentationPersistence_LegacyLineTokenRemainsLiteral()
    {
        const string json =
            """{"documentation":{"summary":"literal \\^J"}}""";

        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.Equal(
            @"literal \\^J",
            restored.Documentation.Summary);
    }

    [Fact]
    public void SurfaceDescription_ImportsDocumentationEncodingOnce()
    {
        var type = new ApiType
        {
            Namespace = "Docs",
            Name = "Widget",
            Kind = "class",
            Documentation = new DocComment
            {
                Summary = "before\u202Eafter literal \\u0041",
            },
        };

        var (view, _) = ApiOutputFormatter.BuildSurfaceTableView(
            new ApiSurface { Types = [type] },
            new ApiOptions { ShowDocs = true });
        ApiSurfaceTableRow row = Assert.Single(
            view.RowsWithDescription!);
        Assert.Equal(
            @"before\u202Eafter literal \\u0041",
            row.DescriptionText!.Value.ToString());
    }

    [Fact]
    public void SurfaceDescription_TruncatesWithoutSplittingEncodedToken()
    {
        var type = new ApiType
        {
            Namespace = "Docs",
            Name = "Widget",
            Kind = "class",
            Documentation = new DocComment
            {
                Summary =
                    "\u202E" + new string('a', 70) + "\u202Etail",
            },
        };

        var (view, _) = ApiOutputFormatter.BuildSurfaceTableView(
            new ApiSurface { Types = [type] },
            new ApiOptions { ShowDocs = true });

        Assert.Equal(
            @"\u202E" + new string('a', 70) + "...",
            Assert.Single(view.RowsWithDescription!).Description);
    }

    [Fact]
    public void MemberTableAndShape_CarryContainedArtifactText()
    {
        var type = new ApiType
        {
            Namespace = "Dotnet\u200BInspect",
            Name = "Widget",
            Kind = "class",
            BaseType = "Base\u202EType",
            Interfaces =
            [
                "I\u200BContract",
            ],
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run\u202E",
                    ReturnType = "Result\u202E",
                    Signature = "Result\u202E Run\u202E()",
                },
            ],
        };

        var (table, _) = ApiOutputFormatter.BuildTypeTableView(
            type,
            new ApiOptions());
        var row = Assert.Single(table.Rows!);
        Assert.Equal(@"Run\u202E", row.NameText.ToString());
        Assert.Equal(
            @"Result\u202E",
            row.ReturnTypeText.ToString());

        var methods = new MethodsView();
        var view = new TypeView();
        ApiOutputFormatter.PopulateMemberSections(
            view,
            methods,
            new OperatorsView(),
            new ExplicitInterfaceImplementationsView(),
            new ExtensionMethodsView(),
            new EventsView(),
            type,
            new ApiOptions());
        var detailed = Assert.Single(methods.Rows!);
        Assert.Equal(@"Run\u202E", detailed.NameText.ToString());
        Assert.Contains(
            @"Run\u202E",
            detailed.SignatureText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            '\u202E',
            detailed.SignatureText.ToString());
        Assert.DoesNotContain(
            @"\\u202E",
            detailed.SignatureText.ToString(),
            StringComparison.Ordinal);

        ApiOutputFormatter.PopulateMemberSignature(
            view,
            type,
            new ApiOptions());
        var signature = Assert.Single(view.SignatureRows!);
        Assert.DoesNotContain(
            @"\\u202E",
            signature.SignatureText.ToString(),
            StringComparison.Ordinal);

        var index = Assert.Single(
            ApiOutputFormatter.BuildMemberIndexRows(
                type,
                type.Members));
        Assert.Equal(
            @"<code>Run\u202E</code>",
            index.SelectorText.ToString());

        var shape = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: []);
        Assert.Equal(
            @"Dotnet\u200BInspect.Widget",
            shape.FullNameText.ToString());
        Assert.All(
            Flatten(shape.Members),
            text =>
            {
                Assert.DoesNotContain('\u200B', text);
                Assert.DoesNotContain('\u202E', text);
                Assert.DoesNotContain(
                    @"\\u202E",
                    text,
                    StringComparison.Ordinal);
            });

        static IEnumerable<string> Flatten(IEnumerable<TreeNode> nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node.Text;
                foreach (string child in Flatten(node.Children ?? []))
                    yield return child;
            }
        }
    }

    private static IEnumerable<string> EnumerateJsonStrings(
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    foreach (string value in EnumerateJsonStrings(item))
                        yield return value;
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                    foreach (string value in EnumerateJsonStrings(property.Value))
                        yield return value;
                break;
        }
    }

    [Fact]
    public void BenignInternationalTypeName_IsByteNeutral()
    {
        var type = new ApiType
        {
            Namespace = "日本語",
            Name = "Über",
            Kind = "class",
        };

        Assert.Equal(
            "日本語.Über",
            ApiOutputFormatter.FormatGenericFullName(type).ToString());

        var surface = new ApiSurface
        {
            Name = "Fixture",
            Types = [type],
        };

        AssertEquivalentJson(
            JsonSerializer.Serialize(
                surface,
                ApiJsonContext.Default.ApiSurface),
            JsonSerializer.Serialize(
                surface,
                ApiArtifactJson.Surface));
        AssertEquivalentJson(
            JsonSerializer.Serialize(
                type,
                ApiTypeJsonContext.Default.ApiType),
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        AssertEquivalentJson(
            JsonSerializer.Serialize(
                type,
                ApiTypeCompactJsonContext.Default.ApiType),
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.CompactType));

        static void AssertEquivalentJson(
            string expected,
            string actual)
        {
            using JsonDocument expectedDocument =
                JsonDocument.Parse(expected);
            using JsonDocument actualDocument =
                JsonDocument.Parse(actual);
            Assert.True(
                JsonElement.DeepEquals(
                    expectedDocument.RootElement,
                    actualDocument.RootElement));
        }
    }
}
