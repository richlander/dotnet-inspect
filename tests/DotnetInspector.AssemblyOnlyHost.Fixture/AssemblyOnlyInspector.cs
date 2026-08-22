using ILInspector.Metadata;

namespace DotnetInspector.AssemblyOnlyHost.Fixture;

public static class AssemblyOnlyInspector
{
    public static string ReadAssemblyName(string assemblyPath)
    {
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(assemblyPath);
        return session.AssemblyInfo().AssemblyName
            ?? throw new InvalidDataException($"{assemblyPath} has no assembly name.");
    }
}
