---
name: release
description: Use when shipping a new dotnet-inspect version; independently validate one immutable candidate, present concerns, and publish only after the operator decides.
---

# Release dotnet-inspect

Use this maintainer skill only when the user asks to ship, publish, or release a
new dotnet-inspect version. Read
[`docs/release-workflow.md`](../../../docs/release-workflow.md) first; it owns
the release contract and recovery behavior. This skill validates readiness and
executes the operator's decision. Daily operations owns readiness repair.

## Select one candidate

Start from the candidate named by the operator, or the newest candidate that
daily operations reported ready. Record:

- `release-candidate.yml` run ID and attempt;
- full source SHA and `VersionPrefix`;
- final artifact ID, digest, expiry, and URL;
- exact-SHA `ci-required` conclusion;
- every required asset and qualification job conclusion; and
- daily operations' release-note, product-skill, and peer-publication
  reconciliation result.

Require one unexpired `dotnet-inspect-release-candidate` artifact. Do not
substitute a version, branch, newer run, ancestry relationship, or another
attempt for any identity member.

## Independently validate readiness

Confirm that:

1. Source validation, all five native package jobs, portable packages, the
   production site, and immutable assembly succeeded in the selected attempt.
2. Every required Deep Inspect candidate job completed in that same attempt.
3. The exact candidate SHA still has successful `ci-required`.
4. The receipt SHA, version, run, attempt, manifest digests, package census,
   package reach, and production-site checks agree with the retained artifact.
5. `VersionPrefix` is the intended unreleased version and the root shipped
   skill reports that version.
6. Release notes and every product skill changed by the candidate's history
   are current.
7. Any required `richlander/dotnet-skills` update is prepared for coordinated
   publication.

If preparation is stale, stop. Report the exact missing readiness work to daily
operations; do not edit, rebuild, normalize, or repair the candidate during
release.

## Present the decision

A green candidate has successful required qualification and needs no concern
acceptance. For a ready candidate with concerns, list every exact completed
non-success job and its conclusion. Cancelled, skipped, running, missing,
mixed-attempt, expired, or structurally incomplete evidence is not an
operator-acceptable concern.

Present the operator with:

- version and full SHA;
- run ID, attempt, artifact ID, digest, and expiry;
- green status or the complete concern list;
- package/GitHub-release/production-site surfaces that will publish; and
- any coordinated peer publication.

Ask whether to publish this exact candidate. Publication authorization applies
only to the presented identity. A prior release decision, green automation, or
automatic comparison deployment is not authorization.

## Publish the retained bytes

After explicit authorization, dispatch **Publish** on `main`:

```bash
gh workflow run release.yml \
  -f candidate_run_id=<run-id> \
  -f candidate_attempt=<attempt> \
  -f accept_certification_concerns=<false-or-authorized-true> \
  -f confirm=publish
```

The workflow independently revalidates the candidate, publishes its retained
packages and GitHub release, then makes its retained site available to the
production environment. Approve that environment only for the same workflow
run after package and GitHub publication succeeds.

Never dispatch from another branch, change inputs during recovery, rebuild
assets, or publish one surface from another candidate.

## Verify and recover

Verify all seven package IDs and version on nuget.org, the GitHub release tag
and target SHA, the attached package set, and the production site's version and
linked commit.

If publication partially fails, rerun `release.yml` with the same run ID,
attempt, concern decision, and confirmation. Package duplicate skipping is
recovery for the same immutable candidate, not permission to select another
one. If revalidation reports changed or expired identity, stop and obtain a new
operator decision.

After every coordinated surface succeeds, record the released version, release
URL, full SHA, candidate run/attempt, and artifact identity on the successor
tracker. Daily operations then advances the project version and prepares the
next ready candidate; the release agent does not perform that repair as part of
the completed publication.
