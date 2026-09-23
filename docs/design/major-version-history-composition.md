# Major-version History composition

## Status and scope

Status: **partially implemented**. Package Version Selection support landed in
[#8279](https://github.com/richlander/dotnet-inspect/pull/8279); Diff History
adoption is tracked by
[#8266](https://github.com/richlander/dotnet-inspect/issues/8266), under
[#8263](https://github.com/richlander/dotnet-inspect/issues/8263).

This thin composition map sequences two focused owners:

- [Package Version Selection](version-resolution.md) owns major-bound
  population settlement and deterministic major-representative projection.
- [Diff History](diff-history.md) owns the representative policy requested by
  each Finding producer and the resulting evaluation, correlation, and
  reporting semantics.

It does not redefine either owner's internal policy.

## Typed handoff

Package Version Selection settles one complete, direction-preserving
`PackageVersionVector` from configured-authority discovery. Under its explicit
major-bound policy, both boundary majors must be represented even when a
literal stable boundary coordinate is unpublished. Its representative
projection returns addresses from that exact vector, preserving position,
version, and reporting-authority correspondence.

Diff History retains the complete vector as its population currency and stores
the owner-issued representative policy in its evaluation plan. Servicing
versions omitted from evaluation therefore remain visible as unevaluated gaps;
History does not reconstruct major buckets, semantic ordering, or prerelease
admission.

## Production composition

`diff --history --major-versions` requests major-bound settlement, then maps
API Finding producers to `FirstStable` and body-level Analysis producers to
`Latest`. The Analysis source is the first selected representative in caller
direction, preserving the one-source-receipt contract without implicitly
evaluating a servicing version outside the selected sample.

The motivating request is:

```bash
dotnet-inspect diff --history --major-versions --preview \
  --package System.Text.Json@8.0.0..11.0.0 \
  --type System.Text.Json.JsonDocument
```

Complete discovery admits the published 11.x release candidate below the
unpublished stable upper bound. Ordinary range consumers retain exact-endpoint
settlement.
