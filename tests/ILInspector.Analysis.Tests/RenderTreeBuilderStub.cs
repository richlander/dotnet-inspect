// An UNTRUSTED lookalike of the Blazor render-tree builder: it reuses the framework type's
// namespace and simple name but lives in the (unsigned) test assembly, so it carries no
// framework public-key-token. LibraryBodyIndex.IsBlazorRenderMethod is trust-gated (#1708),
// so a method taking this type is NOT mistaken for render plumbing and its genuine allocation
// findings are reported. The trusted counterpart lives in ILInspector.Analysis.RenderFixtures
// (the real RenderTreeBuilder, which IS suppressed).
namespace Microsoft.AspNetCore.Components.Rendering;

public sealed class RenderTreeBuilder
{
    public void Use(System.Func<int> handler) => handler();
}
