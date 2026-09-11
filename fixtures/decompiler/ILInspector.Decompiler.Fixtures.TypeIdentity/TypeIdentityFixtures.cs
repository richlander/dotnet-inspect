// Type-identity metadata-name fixtures.
//
// Each type below reproduces one exact source shape that
// CompileBackTypeIdentityTests pins against a real compiled assembly. The
// compiler is the producer of the emitted metadata type name (keyword escaping,
// arity ticks, nesting, and file-local mangling), so these must be built by the
// solution rather than described by a hand-written name table.
//
// Namespaces are intentionally short (Sample, @for.@class) so the consuming
// test's expected full names remain unqualified.

namespace Sample
{
    // Keyword type name -> metadata name "class", spelled back as "@class".
    public class @class { }

    // Generic type -> metadata name "Container`1", arity-stripped to "Container".
    public class Container<T> { }

    // Nested type -> metadata name "Inner", full name "Sample.Outer.Inner".
    public class Outer
    {
        public class Inner { }
    }

    // File-local type -> compiler-mangled "<...>__Widget" metadata name.
    file class Widget { }
}

namespace @for.@class
{
    // Keyword namespace segments -> full name "@for.@class.Widget".
    public class Widget { }
}
