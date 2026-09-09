namespace @event.Models;

// A helper type whose namespace's first segment is the C# keyword 'event', so
// the decompiler's printer escapes it as @event.Models when spelling a
// fully-qualified reference. Used by MemberRenderSpecimen to exercise the
// alias-qualified shortening guard against an @-escaped namespace (#3064 review).
public static class TypeNameShadow
{
    public static int M(int x) => x;
}
