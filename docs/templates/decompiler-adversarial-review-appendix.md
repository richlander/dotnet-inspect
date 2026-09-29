# Decompiler evidence-integrity review

Trace every source artifact the changed path parses, compiles, compares, or
reports, from its owner to the resulting verdict.

For each artifact, identify:

- the product or harness owner;
- each transformation before observation or compilation;
- whether the result is product evidence, declaration-only evidence,
  reconstructed context, or a separately labelled control; and
- which verdicts that artifact may issue.

For product C# compiled as validity or fidelity evidence, verify that the
harness preserves the product-issued artifact. Report a finding if the harness
parses and rewrites, normalizes, repairs, re-raises, or replaces target
declarations, signatures, initializers, or bodies before compilation. In
particular, check accessibility and modifier changes, syntax-tree rewriters,
text substitutions, inferred qualifications, reconstructed target members,
and fallbacks that promote a failed or unavailable product artifact into
success-shaped product evidence.

The harness may add independently owned compilation context around an immutable
target, re-indent without changing its token stream, stub bodies for an
explicitly declaration-only oracle, and compile a separately typed and labelled
control. Those paths remain acceptable only when they cannot issue or inherit a
product-artifact validity, fidelity, provenance, or `Exact` claim.

Verify that:

1. Invalid, unbindable, unavailable, or uncheckable product artifacts remain
   visible under their honest result instead of being repaired.
2. Harness-spelled target declarations and bodies retain non-product provenance
   in every success and failure result.
3. Comparison-only normalization never feeds a product compilation.
4. Authored-source or mutation controls cannot inherit product receipts,
   provenance, closure, or `Exact`.
5. Compiler options and reference selection reproduce the declared build
   context without changing the target source to fit that context.
6. The pathological gate would fail if the harness restored the repaired form
   that motivated the change.

Treat a pre-existing path outside the exact-head change as a blocking finding
only when the candidate claims to complete its migration or relies on that path
for its evidence. Otherwise record it as a concrete follow-up observation with
the artifact, transformation, reachable verdict, and owning design section.
