namespace ILInspector.Decompiler.Tests;

// Local functions whose bodies LocalFunctionRaisingPass declines to raise, next to one
// it accepts. The declined ones must not print a call to a name they never declare.
public static class UnraisedLocalFunctionSamples
{
    public static void CallsEmpty()
    {
        F();
        static void F() { }
    }

    // Declined: IsPrintableBody rejects a try body.
    public static int CallsUnraisedTry(int x)
    {
        return F(x);
        static int F(int n) { try { return n / 2; } catch { return 0; } }
    }

    // Declined: IsPrintableBody rejects a foreach body.
    public static int CallsUnraisedForeach(int[] x)
    {
        return F(x);
        static int F(int[] a) { int t = 0; foreach (int v in a) t += v; return t; }
    }

    // Control: an if body is raised, so the declaration is emitted and the call keeps
    // its source spelling.
    public static int CallsRaisedIf(int x)
    {
        return F(x);
        static int F(int n) { if (n > 0) return n; return -n; }
    }
}

public static class LocalFunctionArgumentSamples
{
    public static int RefArgument(int value)
    {
        return Read(ref value);
        static int Read(ref int source) => source + 1;
    }

    public static bool OutArgument(out int value)
    {
        return Assign(out value);
        static bool Assign(out int target) => TryValue("42", out target);
    }

    static bool TryValue(string text, out int value) => int.TryParse(text, out value);

    public static int InArgument(int value)
    {
        return Read(in value);
        static int Read(in int source) => source + 1;
    }

    public static int ValueArgument(int value)
    {
        return Read(value);
        static int Read(int source) => source + 1;
    }
}

// Two local functions in DISJOINT scopes sharing one source name, so the compiler emits
// <M>g__Pick|0_0 and <M>g__Pick|0_1. One is raised and one is declined: the declined
// call must not be spelled `Pick`, which would silently bind to the raised function.
public static class DuplicateLocalFunctionNameSamples
{
    public static int PickOne(bool b, int[] xs)
    {
        if (b)
        {
            int Pick(int n) => n + 1;                    // raised
            return Pick(1);
        }
        else
        {
            int Pick(int n)                              // declined: foreach body
            {
                int t = 0;
                foreach (int v in xs) t += v * n;
                return t;
            }
            return Pick(2);
        }
    }

    public static int BothRaise(bool b, int x)
    {
        if (b)
        {
            int Pick(int n) => n + 1;
            return Pick(x);
        }
        else
        {
            int Pick(int n) => n * 2;
            return Pick(x);
        }
    }

    public static int BothRaiseWithIfBodies(bool b, int x)
    {
        if (b)
        {
            int Pick(int n) { if (n > 0) return n + 1; return n; }
            return Pick(x);
        }
        else
        {
            int Pick(int n) { if (n > 0) return n * 2; return n; }
            return Pick(x);
        }
    }
}

// A local function converted to a delegate lowers to `ldftn` with NO call site, so
// raising never sees it and nothing declares it. Its name must still be spelled
// honestly rather than decoded into a member that does not exist.
public static class LocalFunctionMethodGroupSamples
{
    public static int UsesMethodGroup(int x)
    {
        static int F(int n) => n + 1;
        System.Func<int, int> d = F;
        return d(x);
    }
}

// A local function's address taken as a function pointer. `ldftn` imports as
// LoadFunctionPointer and only becomes AddressOfMethod in a LATER pass, so a sweep
// that matched only the latter would never stamp it.
public static unsafe class LocalFunctionAddressSamples
{
    public static delegate*<int, int> TakesAddress()
    {
        static int F(int x) { try { return x + 1; } catch { return 0; } }  // declined
        return &F;
    }
}

// A local function both CALLED and converted to a delegate. RaiseCalls rewrites only
// Call nodes, so the method group survives the raise — and because the declaration is
// emitted, it must be spelled `F` unqualified rather than stamped declined.
public static class RaisedLocalFunctionMethodGroupSamples
{
    public static int CallsAndConverts(int x)
    {
        static int F(int n) => n + 1;
        System.Func<int, int> d = F;
        return F(x) + d(x);
    }
}

// Generic local functions. LocalFunctionStatement carries no type-parameter list, so
// raising one would declare `static int Tag()` and drop `<int>`/`<string>` from every
// call site — uncompilable C# reported as Full (#3631's failure mode). The pass must
// decline them, which routes both shapes through the honest-spelling path instead.
// The type parameter is deliberately absent from the signature: a generic local
// function whose signature MENTIONS T is already declined for an unrelated reason
// (unprintable body), so it could not prove the generic gate does anything.
public static class GenericLocalFunctionSamples
{
    public static int TwoInstantiations()
    {
        static int Tag<T>() => 1;
        return Tag<int>() + Tag<string>();
    }

    public static int CalledAndUsedAsMethodGroup()
    {
        static int Tag<T>() => 1;
        System.Func<int> d = Tag<int>;
        return Tag<int>() + d();
    }
}

// A NON-generic local function inside a GENERIC method. Roslyn gives the lowered
// method the enclosing method's type parameters, so its call sites carry non-empty
// TypeArguments even though nothing here is generic in the source sense. Those
// parameters are already in scope at the declaration site, so this must still raise:
// declining it on the presence of TypeArguments alone regressed real framework code
// (System.Runtime.Intrinsics.VectorMath.HypotSingle) from Full to Partial.
public static class LocalFunctionInGenericMethodSamples
{
    public static T Passthrough<T>(T value)
    {
        static T Core(T v) => v;
        return Core(value);
    }
}

// Non-generic local functions inside a generic TYPE. Calls to their synthesized
// methods use a MemberRef whose declaring type is the host's self-instantiation
// (GenericTypeLocalFunctionSamples<T>), while the method body lives on the generic
// type definition. Cross-method import must address that definition without losing
// the type parameters that are already in scope at the recovered declaration site.
public static class GenericTypeLocalFunctionSamples<T>
{
    public static int NoTypeParameter(int value)
    {
        static int Own(int input) => input + 1;
        return Own(value);
    }

    public static T TypeParameterOnly(T value)
    {
        static T Own(T input) => input;
        return Own(value);
    }

    public static U TypeAndMethodParameters<U>(T typeValue, U methodValue)
    {
        static U Own(T _, U value) => value;
        return Own(typeValue, methodValue);
    }

    public static int OwnMethodParameter(T value)
    {
        static int Own<U>(U input) => 2;
        return Own<T>(value);
    }
}

// A local function with its OWN generic parameter, inside a generic method, called
// only with the host's type argument. Every call-site type argument is then a method
// generic parameter, so judging genericity from the CALL SITE raised it and declared
// `static int Own(U u)` with `U` bound to nothing — CS0246/CS1503 at Full. The body,
// not the call site, is what knows: `U` is declared by the local function and the
// host has no such name, so it cannot be written down.
public static class OwnGenericInGenericMethodSamples
{
    public static int CalledWithHostTypeArgument<T>(T value)
    {
        static int Own<U>(U u) => 2;
        return Own<T>(value);
    }
}

// A non-generic local function inside a GENERIC method, referenced by address and as a
// method group rather than called. The reference inherits the host's type arguments in
// metadata, so spelling them (`&Core<T>`) against the raised, non-generic declaration
// `static T Core(T v)` is CS0308. A raised local function takes no type arguments; a
// local function that declares its own is declined instead, so nothing is lost.
public static class RaisedLocalFunctionReferenceSamples
{
    public static unsafe T ByAddress<T>(T value)
    {
        static T Core(T v) => v;
        delegate*<T, T> pointer = &Core;
        return pointer(Core(value));
    }

    public static T ByMethodGroup<T>(T value)
    {
        static T Core(T v) => v;
        Func<T, T> group = Core;
        return group(Core(value));
    }
}

// A local function may declare a type parameter whose NAME shadows one of the host's;
// C# permits it with only a warning (CS8387). The name is then a host name while the
// parameter is not the host's, so judging genericity from the body's names alone raised
// these and dropped the type-parameter list, producing CS1503 at Full. The call sites
// are what disambiguate: the substitution must be the identity.
#pragma warning disable CS8387
public static class ShadowedGenericLocalFunctionSamples
{
    // Instantiated with two different concrete types, so no single non-generic
    // declaration can serve both call sites: `Own(1)` and `Own("x")` against
    // `static int Own(T u)` is CS1503 twice.
    public static int DifferingInstantiations<T>(T value)
    {
        static int Own<T>(T x) => 3;
        return Own<int>(1) + Own<string>("x");
    }

    // Instantiated with a method generic parameter that is NOT the one the body's own
    // parameter shadows, so every call-site argument is a method generic parameter and
    // the body's name is a host name — yet `Own(u)` against `static int Own(T x)` is
    // still CS1503. Only matching names POSITIONALLY rejects this.
    public static int ForeignHostParameter<T, U>(T t, U u)
    {
        static int Own<T>(T x) => 5;
        return Own<U>(u);
    }

    // The shadowing case that DOES raise, and must keep raising: the only instantiation
    // is the host parameter the body's own parameter shadows, so dropping the list is
    // the identity substitution and `static int Own(T x)` means exactly what the
    // original meant. An arity test would decline this for nothing.
    public static int IdenticalInstantiation<T>(T value)
    {
        static int Own<T>(T x) => 7;
        return Own<T>(value);
    }
}
#pragma warning restore CS8387

// A local function is not only CALLED. A method group or `&F` survives the raise
// untouched, so it still spells whatever declaration the raise produced — which makes
// it a vote on whether raising is sound, not a bystander. Here the calls instantiate
// with the host's `T` while the address-of instantiates with `int`, so no single
// declaration without a type-parameter list can serve both: raising emitted
// `delegate*<int, int> f = &Own;` against `static int Own(T x)` (CS8757) at Full.
#pragma warning disable CS8387
public static class MixedInstantiationReferenceSamples
{
    public static unsafe int CallAndAddressOfDisagree<T>(T value)
    {
        static int Own<T>(T x) => 3;
        delegate*<int, int> pointer = &Own<int>;
        return Own<T>(value) + pointer(1);
    }

    // The BODY references itself, and RewriteSelfCalls drops that reference's type
    // arguments exactly as the host call sites' are dropped — so it gets a vote too.
    // Self-references do not appear in the host's descendants, so judging on the host
    // alone raised this to `static int Own(T x, bool again)` calling itself as
    // `Own(1, false)`: CS1503, at Full.
    public static int RecursiveCallDisagrees<T>(T value)
    {
        static int Own<T>(T x, bool again) => again ? Own<int>(1, false) : 1;
        return Own<T>(value, true);
    }

    // The DelegateCreation form of the same disagreement. This one does not even fail to
    // compile: raising produced `new Func<Type>(L)` bound to the HOST's `U`, so the
    // decompiled method returned a different Type at run time than the original — a
    // silent miscompile reported as Full.
    public static Type DelegateCreationDisagrees<U, V>()
    {
        static Type Own<U, V>() => typeof(U);
        Own<U, V>();
        Func<Type> viaDelegate = Own<int, string>;
        return viaDelegate();
    }
}
#pragma warning restore CS8387

// A reference held by a SIBLING local function's body. The referee's raise gate gathers
// references from the host and from the referee's own body — never from a sibling's — and
// the gate that declines a body for touching a foreign local function only looked at
// calls. So `&A<int>` inside `B` was invisible twice over: `B` raised, and its printed
// body bound `&A` to the raised `A`, which carries the HOST's type argument rather than
// the `int` the IL names. That compiles and returns the wrong Type at run time.
#pragma warning disable CS8387
public static class SiblingLocalFunctionReferenceSamples
{
    public static unsafe Type[] ReferenceFromSiblingBody<T>()
    {
        return [B(), A<T>()];

        static Type A<T>() => typeof(T);

        static Type B()
        {
            delegate*<Type> pointer = &A<int>;
            return pointer();
        }
    }
}
#pragma warning restore CS8387

// The premise that makes the local-function generic gate's pre-pipeline vote safe.
//
// That vote runs before IrPasses.Run, which can ADD reference nodes: LambdaRaisingPass
// imports a lambda's body and attaches it to the tree. A self-reference written inside a
// lambda is therefore not a node when the vote happens. The pass now re-votes afterwards,
// but that arm is unreachable today for a reason that has nothing to do with this pass —
// a lambda in a generic context is never raised (#3665), and a local function only has
// type parameters worth judging when its host is generic.
//
// These three exist so that coincidence is a tested fact rather than an assumption.
public static class GenericContextLambdaSamples
{
    public static string NonGenericHost()
    {
        Func<string> f = () => "x";
        return f();
    }

    public static string GenericHostCachedLambda<T>(T t)
    {
        Func<string> f = () => typeof(T).Name;
        return f();
    }

    public static string GenericHostCapturingLambda<T>(T t)
    {
        Func<string> f = () => t!.ToString()!;
        return f();
    }
}
