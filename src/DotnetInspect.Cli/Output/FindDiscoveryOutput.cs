using DotnetInspect.Cli.Models;
using DotnetInspector.Presentation;

namespace DotnetInspect.Cli.Output;

internal static class FindDiscoveryOutput
{
    internal static FindDiscoveryRow Project(TypeFindResult row) =>
        FindDiscoveryPresentation.Project(row);

    internal static FindDiscoveryRow Project(MemberFindResult row) =>
        FindDiscoveryPresentation.Project(row);
}
