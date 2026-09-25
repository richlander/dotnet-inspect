# Nightly release candidate

This document owns the identity, readiness, and handoff contract for a
dotnet-inspect release candidate. It composes source validation, immutable
release assets, Deep Inspect evidence, daily operations, and operator-approved
publication without transferring any participating workflow's internal
responsibilities.

The approved scope is intentionally broad: it replaces on-demand release
builds and production promotion of non-production assets with one nightly
candidate that is either published unchanged or declined.

## Candidate identity

A candidate is identified by one GitHub Actions workflow run and attempt. That
run fixes:

- one full commit SHA from `main`;
- the `VersionPrefix` read from that commit;
- one complete package set;
- one production-site artifact;
- the digest of each retained artifact; and
- the required Deep Inspect job outcomes for that same SHA.

The run ID and attempt select the evidence container. The full commit SHA joins
source, assets, and validation. Artifact IDs and digests select the retained
bytes. A version string, branch name, newer workflow run, or ancestry
relationship is not a substitute for any of those identities.

Candidate work does not cancel an active candidate when `main` advances. A
newer scheduled or operator-dispatched run creates another candidate rather
than mutating or superseding retained bytes from an earlier run.

## Construction and qualification

The nightly candidate workflow first validates its exact `main` source and
constructs all assets that production publication may consume. Package and
site verification run against those produced assets before they are retained.
Deep Inspect release certification then observes the same full commit SHA.

The candidate workflow may retain assets when certification reports a
completed non-success outcome. It must not retain a publishable candidate when
source identity is invalid, required assets are missing, an artifact check
fails, or certification is cancelled, incomplete, or structurally missing.
Those distinctions let an operator review a concrete test concern without
turning missing or mutable release material into an override.

Candidate assets are immutable inputs to publication. Publication may
revalidate, download, authenticate, and upload them; it does not rebuild,
normalize, or repair them.

## Readiness

Daily operations owns candidate readiness. A candidate is **ready to ship**
when:

- its exact SHA has successful required `main` CI;
- its version is the intended unreleased project version;
- its complete package and production-site assets exist with their recorded
  identities and digests;
- every required Deep Inspect certification job completed successfully;
- release notes and shipped product skills have been reconciled for that
  candidate's history; and
- any peer skill or plugin update required for that version is prepared for
  coordinated publication.

A candidate is **ready with concerns** only when all identity and asset
requirements hold but one or more completed certification outcomes require an
explicit risk decision. Daily operations reports each concern and attempts the
ordinary repair path; it does not silently convert the candidate to green.

After a release, daily operations advances the project to its intended next
version and keeps release notes, shipped skills, and peer skill publication
material current. If that preparation is incomplete, the next nightly run is
not ready to ship. Daily operations may prepare and review repository changes,
but it does not merge, publish, deploy production, or approve an environment
without the existing authorization.

## Publication

The release operator selects one retained candidate run. The release workflow
revalidates its run identity, attempt, source SHA, version, required job
structure, artifact IDs, artifact digests, and asset receipts before any
publication.

Green candidates need no concern acceptance. For a candidate that is ready
with concerns, the release agent presents the exact completed non-success
outcomes to the operator. Only the operator may accept those concerns for that
specific candidate and authorize publication.

One authorized publication releases the candidate's package set, GitHub
release, and production website as the same `(commit SHA, VersionPrefix)`
release unit. Production remains an operator choice even when a candidate is
green. A schedule, successful candidate, successful comparison deployment, or
prior release authorization never implies publication authority.

Retries retain the selected run, attempt, artifact identities, and digests.
Changing any member selects a different candidate and requires a new operator
decision.

## Comparison websites

Comparison websites are built and deployed by a separate automatic workflow
from the candidate's exact commit SHA. They do not supply production artifacts,
publication evidence, or release authority.

Comparison deployment may proceed without production publication. Its result is
daily operational evidence: a failure or stale deployment is repaired and
reported, but it does not mutate candidate assets. The comparison pipeline
records the candidate run and source SHA so daily operations can prove that all
comparison variants came from the intended commit.

## Evidence and enforcement

Positive workflow contracts and validator self-tests gate:

- exact candidate workflow, event, branch, run, attempt, and SHA identity;
- the required candidate job and artifact set;
- completed certification outcomes and explicit concern acceptance;
- artifact ID, digest, and retained-byte verification;
- package/site release-unit agreement; and
- exact-SHA comparison construction.

Per operator choice, no token-level absence gate scans workflows,
documentation, validators, or skills for staging terminology. The architectural
separation between non-production staging and production publication remains a
design and review responsibility.

## Non-claims

This contract does not:

- make every `main` commit releasable;
- require production publication on a schedule;
- make comparison-site success a publication authorization;
- permit rebuilding a selected candidate during publication;
- treat a failed, cancelled, or missing asset build as an operator-acceptable
  certification concern; or
- authorize daily operations to merge or publish without the normal operator
  decisions.
