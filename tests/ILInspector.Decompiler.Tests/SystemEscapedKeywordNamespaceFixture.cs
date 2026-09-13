namespace System.@event.Models;

// A helper type whose namespace is System-rooted AND has a keyword segment
// (@event), so the printer emits global::System.@event.Models.X while the
// System.-stripped prefix registration ("event.Models") matches mid-chain,
// after "System.@". Used to exercise the alias-root walk in the shortener
// against a System-rooted @-escaped chain (#3064 review).
public static class SystemNameShadow
{
    public static int M(int x) => x;
}
