namespace ILInspector.Analysis.ExceptionBaseFixtures;

public class ExternalFixtureException : Exception
{
    public ExternalFixtureException(string message) : base(message)
    {
    }
}

// Cross-assembly boundary pins (analysis ladder #1623 rung 2). The product is
// SRM-direct and does NOT load referenced assemblies, so the base chain of a type
// defined in THIS assembly cannot be walked from the inspected assembly. Exception
// classification therefore falls back to the conservative "*Exception" name suffix
// for external types. These two types make that documented heuristic explicit (and
// adversarial) rather than silent:
//
//   * ExternalPseudoException ends with "Exception" but does NOT derive from
//     System.Exception — the suffix fallback over-accepts it (a pinned false
//     positive boundary).
//   * ExternalErrorState DOES derive from System.Exception but does not end with
//     "Exception" — the suffix fallback misses it (a pinned false negative
//     boundary).

// Name ends with "Exception" but is a plain reference type, not an exception.
public class ExternalPseudoException
{
}

// A real exception whose simple name does not end with "Exception".
public class ExternalErrorState : Exception
{
    public ExternalErrorState(string message) : base(message)
    {
    }
}
