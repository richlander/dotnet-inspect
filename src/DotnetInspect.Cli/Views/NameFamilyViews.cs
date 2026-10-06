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

[MarkoutSerializable]
public sealed class ArchitecturalFamilyRow
{
    public ArchitecturalFamilyRow(
        string family,
        string kind,
        int types,
        int foundation,
        int hub,
        int orchestrator,
        int noRole,
        int seaLevel,
        int mountainPeak,
        int namespaces,
        string population,
        string provenance,
        string structuralEvidence)
    {
        Family = LibraryViewText.Contain(family);
        Kind = LibraryViewText.Contain(kind);
        Types = types;
        Foundation = foundation;
        Hub = hub;
        Orchestrator = orchestrator;
        NoRole = noRole;
        SeaLevel = seaLevel;
        MountainPeak = mountainPeak;
        Namespaces = namespaces;
        Population = LibraryViewText.Contain(population);
        Provenance = LibraryViewText.Contain(provenance);
        StructuralEvidence =
            LibraryViewText.Contain(structuralEvidence);
    }

    public string Family { get; }
    public string Kind { get; }
    public int Types { get; }
    public int Foundation { get; }
    public int Hub { get; }
    public int Orchestrator { get; }
    public int NoRole { get; }
    public int SeaLevel { get; }
    public int MountainPeak { get; }
    public int Namespaces { get; }
    public string Population { get; }
    public string Provenance { get; }
    public string StructuralEvidence { get; }
}

[MarkoutSerializable]
public sealed class ArchitecturalFamilyTypeRow
{
    public ArchitecturalFamilyTypeRow(
        string type,
        string typeKey,
        string kind,
        string oneWordFamily,
        string twoWordFamily,
        string? source,
        int? incoming,
        int? outgoing,
        string? role,
        string? pole,
        string population,
        string structuralEvidence)
    {
        Type = LibraryViewText.Contain(type);
        TypeKey = LibraryViewText.Contain(typeKey);
        Kind = LibraryViewText.Contain(kind);
        OneWordFamily =
            LibraryViewText.Contain(oneWordFamily);
        TwoWordFamily =
            LibraryViewText.Contain(twoWordFamily);
        Source = Contain(source);
        Incoming = incoming;
        Outgoing = outgoing;
        Role = Contain(role);
        Pole = Contain(pole);
        Population = LibraryViewText.Contain(population);
        StructuralEvidence =
            LibraryViewText.Contain(structuralEvidence);
    }

    public string Type { get; }
    public string TypeKey { get; }
    public string Kind { get; }
    public string OneWordFamily { get; }
    public string TwoWordFamily { get; }
    public string? Source { get; }
    public int? Incoming { get; }
    public int? Outgoing { get; }
    public string? Role { get; }
    public string? Pole { get; }
    public string Population { get; }
    public string StructuralEvidence { get; }

    private static string? Contain(string? value) =>
        value is null
            ? null
            : LibraryViewText.Contain(value);
}
