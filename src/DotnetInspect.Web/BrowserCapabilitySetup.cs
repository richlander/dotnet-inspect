using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web;

public static partial class InspectionEngine
{
    // The host selects facade contributions; Core alone composes their product
    // types. Providers remain unevaluated until capability search is requested.
    internal static void InitializeCapabilityComposition() =>
        BrowserCapabilityComposition.Configure(
            [BrowserCapabilityComposition.CreatePackageQueryContribution,
                BrowserCompareCapabilityContribution.Create,
                BrowserFindingDiffAnyCapabilityContribution.Create]);
}
