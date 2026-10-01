using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable]
public sealed class NameFamilyRow
{
    public NameFamilyRow(
        string family,
        string kind,
        int types,
        int @public,
        int namespaces,
        string examples,
        string population,
        string provenance)
    {
        Family = LibraryViewText.Contain(family);
        Kind = LibraryViewText.Contain(kind);
        Types = types;
        Public = @public;
        Namespaces = namespaces;
        Examples = LibraryViewText.Contain(examples);
        Population = LibraryViewText.Contain(population);
        Provenance = LibraryViewText.Contain(provenance);
    }

    public string Family { get; }
    public string Kind { get; }
    public int Types { get; }
    public int Public { get; }
    public int Namespaces { get; }
    public string Examples { get; }
    public string Population { get; }
    public string Provenance { get; }
}
