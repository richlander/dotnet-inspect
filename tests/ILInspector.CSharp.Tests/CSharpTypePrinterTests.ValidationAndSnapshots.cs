using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed partial class CSharpTypePrinterTests
{

    [Fact]
    public void NestedTypeFailsWithoutItsDeclaringType()
    {
        var type = CreateEmptyType("Samples", "Outer.Inner");
        type.MetadataName = "Outer+Inner";

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("requires its declaring type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTypeRequestsFailInsteadOfEmittingDuplicateDeclarations()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var requests = new[]
        {
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintRequest(type)
        };

        var exception = Assert.Throws<ArgumentException>(() => _printer.PrintBatch(requests));

        Assert.Contains("duplicate C# type 'Samples.Widget'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalMetadataIdentityRejectsDuplicateGenericDeclarations()
    {
        var first = CreateEmptyType("Samples", "Converter`1");
        first.MetadataName = "Converter`1";
        first.TypeParameters = [new TypeParameter { Name = "T" }];
        var second = CreateEmptyType("Samples", "Other`1");
        second.MetadataName = "Converter`1";
        second.TypeParameters = [new TypeParameter { Name = "U" }];

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(first),
                new CSharpTypePrintRequest(second)
            ]));

        Assert.Contains("duplicate C# type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpSpelledGenericTypeNameFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Converter<T>");
        type.TypeParameters = [new TypeParameter { Name = "T" }];

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("must use a metadata name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactGenericTypeWithoutMetadataArityIsNotRendered()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.DefinitionName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Samples",
                    ["Widget"]))
            .Name;
        type.IntroducedTypeParameterCounts = [1];
        type.TypeParameters = [new TypeParameter { Name = "T" }];

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            _outcomePrinter.Print(new CSharpTypePrintRequest(type)));

        Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch>(
            Assert.Single(outcome.SelfNameFailures).Reason);
    }

    [Theory]
    // A canonical `N that disagrees with the parameter count is inconsistent.
    [InlineData("Converter`2", 1, "inconsistent metadata arity")]
    [InlineData("Converter`1", 2, "inconsistent metadata arity")]
    // No canonical `N at all: the name does not carry arity, whatever text
    // follows the backtick. int.TryParse used to accept a signed, padded, or
    // culture-digit count here and let the type print as if it were generic
    // (#4217).
    [InlineData("Converter", 1, "requires metadata arity")]
    [InlineData("Converter`x", 1, "requires metadata arity")]
    [InlineData("Converter`+1", 1, "requires metadata arity")]
    [InlineData("Converter`01", 1, "requires metadata arity")]
    [InlineData("Converter` 1", 1, "requires metadata arity")]
    [InlineData("Converter`\u0661", 1, "requires metadata arity")]
    [InlineData("Converter`1Extra", 1, "requires metadata arity")]
    [InlineData("Converter`65537", 1, "requires metadata arity")]
    public void InconsistentGenericMetadataArityFailsExplicitly(
        string name,
        int parameterCount,
        string expectedMessage)
    {
        var type = CreateEmptyType("Samples", name);
        type.TypeParameters = Enumerable.Range(0, parameterCount)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The canonical bound is inclusive at 65536 — ECMA-335 gives
    /// <c>GenericParam.Number</c> a zero-based ushort — so a name at the bound is
    /// a legal arity spelling and prints.
    /// </summary>
    [Fact]
    public void CanonicalArityAtTheMetadataBoundIsAccepted()
    {
        var type = CreateEmptyType("Samples", "Converter`65536");
        type.TypeParameters = Enumerable.Range(0, 65536)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("class Converter<", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedTypeNameFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Name = null!;

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("non-empty type name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedMemberCollectionFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members = null!;

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("null member collection", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The snapshot has to carry the classified reference-/value-type fact, not just the
    /// constraint strings. An inheriting member must restate that fact -- it decides
    /// whether `T?` binds as a nullable reference type or Nullable&lt;T&gt; -- so a
    /// snapshot that drops it renders an override that does not compile (CS0115/CS0453),
    /// silently, across the whole type-printer path.
    /// </summary>
    [Fact]
    public void SnapshotTypeForRendering_CarriesTheClassifiedTypeParameterKind()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["Samples.BaseType"],
            TypeKind = TypeParameterTypeKind.ReferenceType
        };
        var method = new ApiMember
        {
            Name = "Pick",
            Kind = "method",
            IsOverride = true,
            Signature = "this text must not be used",
            SignatureModel = new ApiSignature
            {
                ReturnType = "T?",
                MemberName = "Pick<T>",
                TypeParameters = [typeParameter],
                Parameters = [new ApiParameter { Type = "T?", Name = "value" }]
            }
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Holder",
            Kind = "class",
            Members = [method]
        };

        var snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            snapshot.Members[0].SignatureModel!.TypeParameters[0].TypeKind);
    }

    [Fact]
    public void SnapshotTypeForRendering_CarriesMethodImplementationEvidence()
    {
        var facts = new ApiMethodImplementationFacts(
            Guid.NewGuid(), 0x06000001,
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.PinvokeImpl,
            System.Reflection.MethodImplAttributes.PreserveSig, false);
        var method = new ApiMember
        {
            Name = "Native",
            Kind = "method",
            MethodImplementation = facts,
            HasMethodBody = false,
        };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            AccessorImplementations = [facts],
        };
        var type = new ApiType { Name = "Example", Kind = "class", Members = [method, property] };

        ApiType snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Same(facts, snapshot.Members[0].MethodImplementation);
        Assert.False(snapshot.Members[0].HasMethodBody);
        Assert.Same(facts, Assert.Single(snapshot.Members[1].AccessorImplementations!.Value));
    }

    [Fact]
    public void SnapshotTypeForRendering_CarriesMethodSemanticsEvidence()
    {
        var accessor = new ApiMember
        {
            Name = "IValue.get_Value",
            Kind = "explicit-interface-implementation",
            MethodSemantics = ApiMethodSemanticsKind.PropertyGetter,
        };
        var type = new ApiType
        {
            Name = "Example",
            Kind = "class",
            Members = [accessor],
        };

        ApiType snapshot =
            CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Equal(
            ApiMethodSemanticsKind.PropertyGetter,
            Assert.Single(snapshot.Members).MethodSemantics);
    }

    [Fact]
    public void SnapshotTypeForRendering_CarriesLayoutFactsWithoutEmittingLayoutSyntax()
    {
        Guid moduleVersionId = Guid.NewGuid();
        var typeFacts = new ApiTypeLayoutFacts(moduleVersionId, 0x02000001, 32, 2);
        var fieldFacts = new ApiFieldLayoutFacts(
            moduleVersionId, typeFacts.TypeToken, 0x04000001, 0);
        var field = new ApiMember
        {
            Name = "Value",
            Kind = "field",
            DeclarationMetadataToken = fieldFacts.FieldToken,
            ReturnType = "int",
            FieldLayout = fieldFacts,
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "LayoutCarrier",
            Kind = "struct",
            MetadataToken = typeFacts.TypeToken,
            Layout = ApiTypeLayout.Explicit,
            LayoutDetails = typeFacts,
            Members = [field],
        };

        ApiType snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Same(typeFacts, snapshot.LayoutDetails);
        Assert.Equal(typeFacts.TypeToken, snapshot.MetadataToken);
        Assert.Same(fieldFacts, Assert.Single(snapshot.Members).FieldLayout);
        Assert.Equal(
            fieldFacts.FieldToken,
            Assert.Single(snapshot.Members).DeclarationMetadataToken);
        Assert.Equal(
            """
            namespace Samples;

            public struct LayoutCarrier
            {
                public int Value;
            }
            """,
            Assert.Single(_printer.Print(new CSharpTypePrintRequest(snapshot)).Units).Source);
    }

    [Fact]
    public void SnapshotTypeForRendering_CarriesSegmentParameterOwnership()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "Outer`1.Inner`1",
            DefinitionName = Assert
                .IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "N",
                        ["Outer`1", "Inner`1"]))
                .Name,
            IntroducedTypeParameterCounts = [2, 0],
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "A" },
                new TypeParameter { Name = "B" },
            ],
        };

        ApiType snapshot =
            CSharpTypePrinter.SnapshotTypeForRendering(type, []);

        Assert.Equal([2, 0], snapshot.IntroducedTypeParameterCounts);
        Assert.Equal(
            "Outer`1.Inner`1",
            CSharpFormatter.FormatTypeName(snapshot));
    }

    [Fact]
    public void RenderingSnapshotDoesNotRetainMutableMetadataAliases()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["System.IDisposable"]
        };
        var parameter = new ApiParameter
        {
            Attributes = ["ParamMarker"],
            Name = "value",
            Type = "T"
        };
        var accessor = new ApiAccessor
        {
            Kind = "get",
            ReturnAttributes = ["AccessorMarker"]
        };
        var method = new ApiMember
        {
            Name = "Transform",
            Kind = "method",
            Attributes = ["MemberMarker"],
            SignatureModel = new ApiSignature
            {
                ReturnType = "T",
                ReturnAttributes = ["ReturnMarker"],
                MemberName = "Transform",
                Parameters = [parameter]
            }
        };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "Value",
                IsRequired = true,
                Accessors = [accessor]
            }
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Container`1",
            MetadataName = "Container`1",
            Kind = "class",
            Attributes = ["TypeMarker"],
            BaseType = "Samples.BaseType",
            Interfaces = ["Samples.IContract"],
            TypeParameters = [typeParameter],
            Members = [method, property]
        };

        var snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        type.Namespace = "Mutated";
        type.Name = "Mutated";
        type.MetadataName = "Mutated";
        type.Kind = "enum";
        type.Attributes[0] = "Mutated";
        type.BaseType = "Mutated";
        type.Interfaces[0] = "Mutated";
        typeParameter.Name = "Mutated";
        typeParameter.Constraints[0] = "Mutated";
        method.Name = "Mutated";
        method.Kind = "field";
        method.Attributes[0] = "Mutated";
        method.SignatureModel!.ReturnType = "Mutated";
        method.SignatureModel.ReturnAttributes[0] = "Mutated";
        method.SignatureModel.MemberName = "Mutated";
        parameter.Attributes[0] = "Mutated";
        parameter.Name = "Mutated";
        parameter.Type = "Mutated";
        property.SignatureModel!.ReturnType = "Mutated";
        property.SignatureModel.MemberName = "Mutated";
        property.SignatureModel.IsRequired = false;
        accessor.Kind = "set";
        accessor.ReturnAttributes[0] = "Mutated";

        var rendered = CSharpDeclarationWriter.RenderTypeUnit(
            snapshot,
            snapshot.Members,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ContextualShort,
                NamespaceMode = CSharpNamespaceMode.Omit,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "[TypeMarker]\npublic class Container<T> : Samples.BaseType, Samples.IContract where T : System.IDisposable",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[MemberMarker]\n    [return: ReturnMarker]\n    public T Transform([ParamMarker] T value);",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public required string Value { [return: AccessorMarker] get; }",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Mutated", rendered.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void NullCallsResolveToExplicitArgumentFailures()
    {
        Assert.Throws<ArgumentNullException>(() => _printer.Print(null!));
        Assert.Throws<ArgumentNullException>(() => _printer.PrintBatch(null!));
    }
}
