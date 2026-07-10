using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpTypePrinterTests
{
    readonly CSharpTypePrinter _printer = new();

    [Fact]
    public void SkeletonPrintsApiProposalStyleSource()
    {
        var type = new ApiType
        {
            Namespace = "System.Text",
            Name = "StringBuilder",
            Kind = "class",
            IsSealed = true,
            Members =
            [
                new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = "this text must not be used",
                    SignatureModel = new ApiSignature()
                },
                new ApiMember
                {
                    Name = "Append",
                    Kind = "method",
                    Signature = "this text must not be used",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Text.StringBuilder",
                        MemberName = "Append",
                        Parameters = [new ApiParameter { Type = "string?", Name = "value" }]
                    }
                },
                new ApiMember
                {
                    Name = "ToString",
                    Kind = "method",
                    IsOverride = true,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "string",
                        MemberName = "ToString"
                    }
                }
            ]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Equal("System.Text", unit.Namespace);
        Assert.Equal(
            """
            namespace System.Text;

            public sealed class StringBuilder
            {
                public StringBuilder();
                public StringBuilder Append(string? value);
                public override string ToString();
            }
            """,
            unit.Source);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void BatchGroupsTypesIntoNamespaceSourceUnits()
    {
        var requests = new[]
        {
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "First")),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Third")),
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Second"))
        };

        var result = _printer.PrintBatch(requests);

        Assert.Collection(
            result.Units,
            unit =>
            {
                Assert.Equal("Samples", unit.Namespace);
                Assert.Equal(
                    """
                    namespace Samples;

                    public class First
                    {
                    }

                    public class Second
                    {
                    }
                    """,
                    unit.Source);
            },
            unit =>
            {
                Assert.Equal("Other", unit.Namespace);
                Assert.Contains("public class Third", unit.Source, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void GlobalNamespaceOmitsNamespaceDeclaration()
    {
        var type = CreateEmptyType(null, "GlobalType");

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Null(unit.Namespace);
        Assert.Equal(
            """
            public class GlobalType
            {
            }
            """,
            unit.Source);
    }

    [Fact]
    public void SkeletonPrefersStructuredGenericSignature()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Converter`1",
            MetadataName = "Converter`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }],
            Members =
            [
                new ApiMember
                {
                    Name = "Convert",
                    Kind = "method",
                    Signature = "broken compatibility signature",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "TResult",
                        MemberName = "Convert<TResult>",
                        TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                        Parameters = [new ApiParameter { Type = "T", Name = "value" }]
                    }
                }
            ]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public class Converter<T>",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public TResult Convert<TResult>(T value) where TResult : class;",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Converter<T><T>", result.Units[0].Source, StringComparison.Ordinal);
        Assert.DoesNotContain("broken compatibility signature", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SkeletonMatchesCSharpFormatter()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Create",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Samples.Widget",
                MemberName = "Create"
            }
        });
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = "Samples",
            NamespacePolicy = CSharpNamespacePolicy.Omit,
            TerminateMemberDeclaration = true
        });
        var expectedDeclaration = formatter.FormatTypeUnit(
            type,
            type.Members);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Equal($"namespace Samples;\n\n{expectedDeclaration.Text}", result.Units[0].Source);
        Assert.Equal(expectedDeclaration.Diagnostics, result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }

    [Fact]
    public void NonSkeletonPolicyFailsInsteadOfDroppingBodies()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(CreateMethod("Run"));
        var request = new CSharpTypePrintRequest(type, CSharpBodyPolicy.Full);

        var exception = Assert.Throws<NotSupportedException>(() => _printer.Print(request));

        Assert.Contains("requires a body provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPolicyOverridesTypeDefault()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Run");
        type.Members.Add(member);
        var request = new CSharpTypePrintRequest(
            type,
            CSharpBodyPolicy.Full,
            memberPolicyOverrides: [new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton)]);

        var result = _printer.Print(request);

        Assert.Contains("public void Run();", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void NonSkeletonMemberPolicyFailsInsteadOfDroppingBody()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Run");
        type.Members.Add(member);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides: [new CSharpMemberPolicy(member, CSharpBodyPolicy.Full)]);

        var exception = Assert.Throws<NotSupportedException>(() => _printer.Print(request));

        Assert.Contains("'Full' for 'Run' requires a body provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPolicyMustTargetSelectedMember()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var selected = CreateMethod("Selected");
        var omitted = CreateMethod("Omitted");
        type.Members.AddRange([selected, omitted]);
        var request = new CSharpTypePrintRequest(
            type,
            members: [selected],
            memberPolicyOverrides: [new CSharpMemberPolicy(omitted, CSharpBodyPolicy.Skeleton)]);

        var exception = Assert.Throws<ArgumentException>(() => _printer.Print(request));

        Assert.Contains("is not in the selected member set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPolicyOverridesMustBeUnique()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Run");
        type.Members.Add(member);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton),
                new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton)
            ]);

        var exception = Assert.Throws<ArgumentException>(() => _printer.Print(request));

        Assert.Contains("multiple policy overrides", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestSnapshotsMembersBeforeValidation()
    {
        var member = CreateMethod("Run");
        var changingMembers = new DifferentEachEnumerationList<ApiMember>(member, null!);

        var request = new CSharpTypePrintRequest(
            CreateEmptyType("Samples", "Widget"),
            members: changingMembers);

        Assert.Same(member, Assert.Single(request.Members!));
    }

    [Fact]
    public void RequestSnapshotsMemberPoliciesBeforeValidation()
    {
        var member = CreateMethod("Run");
        var policy = new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton);
        var changingPolicies = new DifferentEachEnumerationList<CSharpMemberPolicy>(policy, null!);

        var request = new CSharpTypePrintRequest(
            CreateEmptyType("Samples", "Widget"),
            members: [member],
            memberPolicyOverrides: changingPolicies);

        Assert.Same(policy, Assert.Single(request.MemberPolicyOverrides));
    }

    [Theory]
    [InlineData("enum")]
    [InlineData("delegate")]
    public void UnsupportedTypeKindFailsInsteadOfEmittingInvalidSkeleton(string kind)
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Shape",
            Kind = kind
        };

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains($"type kind '{kind}'", exception.Message, StringComparison.Ordinal);
    }

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

    [Theory]
    [InlineData("Converter", 1)]
    [InlineData("Converter`2", 1)]
    [InlineData("Converter`x", 1)]
    public void InconsistentGenericMetadataArityFailsExplicitly(string name, int parameterCount)
    {
        var type = CreateEmptyType("Samples", name);
        type.TypeParameters = Enumerable.Range(0, parameterCount)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains(
            name.Contains('`', StringComparison.Ordinal)
                ? "inconsistent metadata arity"
                : "requires metadata arity",
            exception.Message,
            StringComparison.Ordinal);
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
            "[TypeMarker] public class Container<T> : Samples.BaseType, Samples.IContract where T : System.IDisposable",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[MemberMarker] [return: ReturnMarker] public T Transform([ParamMarker] T value);",
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

    static ApiType CreateEmptyType(string? @namespace, string name)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class"
        };

    static ApiMember CreateMethod(string name)
        => new()
        {
            Name = name,
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = name
            }
        };

    sealed class DifferentEachEnumerationList<T>(T first, T later) : IReadOnlyList<T>
    {
        int _enumerationCount;

        public int Count => 1;

        public T this[int index] => index == 0 ? first : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<T> GetEnumerator()
        {
            yield return _enumerationCount++ == 0 ? first : later;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
