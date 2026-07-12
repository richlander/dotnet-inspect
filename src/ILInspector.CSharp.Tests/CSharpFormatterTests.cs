using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpFormatterTests
{
    [Fact]
    public void FormatsStructuredMemberDeclaration()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Container`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };
        var member = new ApiMember
        {
            Name = "Map",
            Kind = "method",
            Signature = "compatibility text",
            SignatureModel = new ApiSignature
            {
                ReturnType = "TResult",
                MemberName = "Map<TResult>",
                TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                Parameters = [new ApiParameter { Type = "T", Name = "value" }]
            }
        };

        var declaration = new CSharpFormatter().FormatMember(type, member);

        Assert.Equal(
            "public TResult Map<TResult>(T value) where TResult : class",
            declaration);
    }

    [Fact]
    public void FormatsContextualTypeUnit()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Create",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Samples.Widget",
                        MemberName = "Create"
                    }
                }
            ]
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = "Samples",
            TerminateMemberDeclaration = true
        });

        var declaration = formatter.FormatTypeUnit(type, type.Members);

        Assert.Equal(
            """
            public class Widget
            {
                public Widget Create();
            }
            """,
            declaration.Text);
        Assert.Empty(declaration.Usings);
        Assert.Empty(declaration.Diagnostics);
    }

    [Fact]
    public void FormatsParameterListsWithAttributesDefaultsAndEscapedKeywords()
    {
        var parameters = new ApiParameter[]
        {
            new()
            {
                Attributes = ["System.Runtime.InteropServices.Optional"],
                Type = "System.event.MyClass",
                Name = "event",
                HasDefault = true,
                DefaultValueText = "default"
            },
            new() { Type = "class", Name = "value" },
            new() { Type = "System.Collections.Generic.List<class>", Name = "items" }
        };

        Assert.Equal(
            "([System.Runtime.InteropServices.Optional] System.@event.MyClass @event = default, @class value, System.Collections.Generic.List<@class> items)",
            CSharpFormatter.FormatParameterList(parameters));
    }

    [Fact]
    public void FormatsDelegateWithStructuredAccessibility()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Callback",
            Kind = "delegate",
            Accessibility = "private",
            Members =
            [
                new ApiMember
                {
                    Name = "Invoke",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = "Invoke"
                    }
                }
            ]
        };

        Assert.Equal(
            "private delegate void Callback();",
            new CSharpFormatter().FormatDelegate(type, type.Members.Single()));
    }

    [Fact]
    public void KnownIdentifierEscapingIsIdempotent()
    {
        Assert.Equal(
            "System.Action<@event>",
            CSharpFormatter.EscapeKnownIdentifiers("System.Action<@event>", ["event"]));
        Assert.Equal(
            "System.Action<@event>",
            CSharpFormatter.EscapeKnownIdentifiers("System.Action<event>", ["event"]));
    }

    [Fact]
    public void RejectsUndefinedPolicies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CSharpFormatter(new CSharpFormatOptions
            {
                TypeNamePolicy = (CSharpTypeNamePolicy)42
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CSharpFormatter(new CSharpFormatOptions
            {
                NamespacePolicy = (CSharpNamespacePolicy)42
            }));
    }
}
