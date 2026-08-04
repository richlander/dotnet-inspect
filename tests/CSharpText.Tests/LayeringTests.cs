namespace CSharpText.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void CSharpText_ReferencesOnlyFrameworkAssemblies()
    {
        var references = typeof(DeclarationIndex).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(references);
    }
}
