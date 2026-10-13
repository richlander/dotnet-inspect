using DotnetInspector.Sections;

namespace DotnetInspector.Presentation.Tests;

public class CliSchemaPresentationTests
{
    [Fact]
    public void Write_StreamsTheOwnerIssuedCommandHierarchy()
    {
        using var output = new StringWriter();

        CliSchemaPresentation.Write(
            new CliSchemaPresentationPlan(
                "dotnet-inspect",
                "1.2.3",
                "Inspect .NET artifacts"),
            static sink =>
            {
                sink.WriteNode(
                    "package  Inspect a NuGet package",
                    isLastSibling: false,
                    static children =>
                    {
                        children.WriteNode(
                            "<package>",
                            isLastSibling: false);
                        children.WriteNode(
                            "--json",
                            isLastSibling: true);
                    });
                sink.WriteNode(
                    "type  Discover types",
                    isLastSibling: true);
            },
            output);

        Assert.Equal(
            """
            # dotnet-inspect 1.2.3

            Inspect .NET artifacts

            ├─ package  Inspect a NuGet package
            │  ├─ <package>
            │  └─ --json
            └─ type  Discover types

            """.ReplaceLineEndings(),
            output.ToString());
    }
}
