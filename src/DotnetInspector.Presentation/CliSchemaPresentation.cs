using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

public sealed record CliSchemaPresentationPlan
{
    public CliSchemaPresentationPlan(
        string name,
        string version,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name;
        Version = version;
        Description = description;
    }

    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
}

public static class CliSchemaPresentation
{
    public static void Write(
        CliSchemaPresentationPlan plan,
        Action<IInspectionHierarchySink<string>> writeCommands,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(writeCommands);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine($"# {plan.Name} {plan.Version}");
        output.WriteLine();
        output.WriteLine(plan.Description);
        output.WriteLine();

        var writer = new MarkoutWriter(output, new MarkdownFormatter());
        writer.WriteTree(tree =>
        {
            var sink = new MarkoutHierarchySink<string>(
                tree,
                static text => text);
            writeCommands(sink);
        });
        writer.Flush();
    }
}
