// Minimal stub matching the Blazor render-tree builder by namespace + simple name, so a
// fixture method can take it as a parameter without referencing the AspNetCore Components
// package. LibraryBodyIndex.IsBlazorRenderMethod keys on the (namespace, name), so a method
// taking this type exercises the Razor render-method suppression end-to-end.
namespace Microsoft.AspNetCore.Components.Rendering;

public sealed class RenderTreeBuilder
{
    public void Use(System.Func<int> handler) => handler();
}
