using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed partial class CSharpTypePrinterTests
{

    [Fact]
    public void SourceDefaultsToUsingsWithoutPragmaOrAssemblyAttributes()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["System.Collections.Generic", "System"]
            });

        Assert.Equal(
            "using System;\nusing System.Collections.Generic;\nnamespace Samples;\n\npublic class Widget\n{\n}\n",
            result.Source);
        Assert.DoesNotContain("#pragma", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("[assembly:", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceOmitsUsingsWhenIncludeUsingsIsFalse()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["System"],
                IncludeUsings = false
            });

        Assert.DoesNotContain("using System;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceEmitsPragmaAndAssemblyAndModuleAttributesWhenRequested()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                EmitPragmaWarningDisable = true,
                AssemblyAttributes = ["System.Reflection.AssemblyMetadata(\"k\", \"v\")"],
                ModuleAttributes = ["System.Security.UnverifiableCode"],
                Usings = ["System"]
            });

        Assert.StartsWith(
            "#pragma warning disable\n"
            + "[assembly: System.Reflection.AssemblyMetadata(\"k\", \"v\")]\n"
            + "[module: System.Security.UnverifiableCode]\n"
            + "using System;\n",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalAttributesAreEscapedAndDiagnoseGlobalRootConflicts()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("", "System")),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                AssemblyAttributes = ["System.CLSCompliantAttribute(true)"],
                ModuleAttributes = ["event.Marker"]
            });

        Assert.Contains(
            "[assembly: global::System.CLSCompliantAttribute(true)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("[module: @event.Marker]", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.TypeName == "<assembly>"
                && diagnostic.Message.Contains("conflicts with global type 'System'", StringComparison.Ordinal));
    }

    [Fact]
    public void SynthesizedObsoleteAttributeCannotBindToSiblingType()
    {
        var obsolete = CreateEmptyType("Samples", "Obsolete");
        var widget = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "int";
        member.IsObsolete = true;
        widget.Members.Add(member);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(obsolete), new CSharpTypePrintRequest(widget)]);

        Assert.Contains("[System.Obsolete] public int Get();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SynthesizedObsoleteReportsGlobalSystemConflict()
    {
        var system = CreateEmptyType("", "System");
        var widget = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "int";
        member.IsObsolete = true;
        widget.Members.Add(member);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(widget)]);

        Assert.Contains("[global::System.Obsolete]", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "conflicts with global type 'System'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GenericGlobalTypeDoesNotConflictWithNamespaceRoot()
    {
        var generic = CreateEmptyType("", "Foo`1");
        generic.TypeParameters = [new TypeParameter { Name = "T" }];
        var namespaced = CreateEmptyType("Foo.Bar", "Worker");

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(generic), new CSharpTypePrintRequest(namespaced)]);

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Namespace root 'Foo' conflicts with global type 'Foo'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalNestedTypeReferenceDoesNotReportNamespaceRootConflict()
    {
        var host = CreateEmptyType("", "Host");
        var kind = CreateEmptyType("", "Kind");
        var method = CreateMethod("GetKind");
        method.SignatureModel!.ReturnType = "Host.Kind";
        host.Members.Add(method);

        var result = _printer.Print(new CSharpTypePrintRequest(
            host,
            nestedTypes: [new CSharpTypePrintRequest(kind)]));

        Assert.Contains("public global::Host.Kind GetKind();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Type name 'Host.Kind' conflicts with global type 'Host'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SourceEscapesDeduplicatesAndSortsEmittedUsings()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["Alpha", "event", "System", "System", "Some.namespace.Value"]
            });

        Assert.StartsWith(
            "using @event;\nusing Alpha;\nusing Some.@namespace.Value;\nusing System;\n",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("using Some.@namespace.Value;", result.Source, StringComparison.Ordinal);
        Assert.Single(
            result.Source.Split('\n'),
            line => line == "using System;");
    }

    [Fact]
    public void SourceUsesBlockScopedNamespaceForMultipleRequests()
    {
        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "First")),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Second"))
        ]);

        Assert.Contains("namespace Samples\n{\n", result.Source, StringComparison.Ordinal);
        Assert.Contains("namespace Other\n{\n", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockScopedBatchPreservesNamespaceIndentationForMultilineInitializers()
    {
        var field = new ApiMember
        {
            Name = "Values",
            Kind = "field",
            ReturnType = "int[]"
        };
        var type = CreateEmptyType("Samples", "First");
        type.Members.Add(field);

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        field,
                        CSharpBodyPolicy.Full,
                        new CSharpFieldInitializer(
                            """
                            [
                                1,
                                2
                            ]
                            """))
                ]),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Second"))
        ]);

        Assert.Contains(
            """
            namespace Samples
            {
                public class First
                {
                    public int[] Values = [
                    1,
                    2
                ];
                }
            }
            """,
            result.Source,
            StringComparison.Ordinal);
    }
}
