# Local repository source acquisition

This document specifies the existing `LocalRepoSourceAcquisition` adapter.
[Issue #5880](https://github.com/richlander/dotnet-inspect/issues/5880) tracks
the contract and its consumer documentation. It does not introduce a new
source-acquisition path or change product behavior.

## Decision

`LocalRepoSourceAcquisition` owns one claim:

> A caller-supplied Git clone may satisfy one PDB document request only with
> bytes accepted by that document's checksum verifier; an unsuccessful local
> lookup declines the request so acquisition can continue.

The result establishes correspondence with the supplied PDB, not independent
authenticity of the PDB, package publisher, GitHub repository, or local clone.
A successful local lookup is not an immutable-URL certificate.

## Owner and boundaries

The adapter consumes a resolved source locator, the checksum for the same PDB
document, and an ordered list of caller-supplied clone paths. Its result is
either the verified blob bytes or no local result (`null`). It does not return
an absence Finding or a source-provenance receipt.

| Supporting owner | Role in this contract |
| --- | --- |
| [PDB acquisition](../pdb-acquisition.md) | Owns acquisition ordering, PDB-recorded local-file reads, source decoding/slicing, and remote outcome interpretation. |
| `PdbSourceAcquisition.VerifyChecksum` | Supplies the checksum-verification classification consumed by this adapter. |
| Metadata and SourceLink | Supply document/checksum observations and resolved source locators. |
| SourceLink provenance and the host source fetcher | Own immutable-origin classification and remote destination admission; local lookup does not replace either. |
| CLI | Accepts explicit clone paths and presents the acquired source through existing views. |

The resolved locator and checksum must describe the same document. A caller
must not substitute another document's checksum or infer it from display text.
This is a consumed association, not a new PDB selection or identity contract.

### Existing consumer and host scope

The production consumer is already shipped. Desktop `--repo` supplies clone
paths to member PDB Source, printable type Source Files, printable member
Source Locations, and implementation-diff PDB source. Reusable service paths
reach the adapter through `PdbSourceAcquisition`; the CLI implementation-diff
source resolver also invokes it directly.

This preserves the host split owned by [PDB acquisition](../pdb-acquisition.md):
desktop hosts can supply fully qualified filesystem clone paths; Browser/Wasm
hosts do not supply those paths and use their host-authorized source fetcher.
Git must be available for a local clone to answer. Its absence declines this
optional lookup rather than making a clone capability available in a browser.

The tracker has one documentation delivery step and zero remaining production
adoption steps. There is no replacement architecture to retire. The result is
bytes, not a new presentation model; existing source views retain rendering
ownership.

## Local locator, not origin authority

Local lookup recognizes the raw-GitHub path convention:
`https://raw.githubusercontent.com/<owner>/<repo>/<revision>/<path>`.
Its supported interpretation is narrower than Git's general revision language
and different from remote URL authorization:

- The URI is absolute and its host is `raw.githubusercontent.com`, compared
  without regard to case.
- The revision selector is 7-64 hexadecimal characters. Git must be able to
  use it to resolve the requested blob in a supplied clone.
- The repository-relative path is URI-unescaped, nonempty, NUL-free, and does
  not begin with a hyphen. It is an object-store path, not a request to open
  that path in the working tree.
- The owner and repository segments locate the revision/path fields; they
  do not authenticate the clone's remotes.

The adapter does not separately restrict URI scheme, port, user information,
query, or fragment. Those components neither select a local clone nor supply
credentials to Git. The HTTPS spelling above is the normal producer form, not
an HTTPS-only admission claim. A later remote attempt must still pass the
remote owner's policy independently.

Accepting abbreviated hexadecimal selectors is deliberate: Git's local
object database resolves them, while the PDB checksum decides whether the
returned content qualifies. Resolution can fail or differ between clones.
The adapter does not certify that a selector is a full commit ID, that the
selected object has commit type, or that it is globally unambiguous.

This differs from immutable SourceLink enrollment/provenance: a locally usable
selector plus verified bytes is sufficient for this immediate source request,
but not for cross-run URL-only identity. Requiring clone remote-name equality
would add no independent byte evidence and would reject useful mirrors.

## Byte admission

Each candidate clone must be a caller-supplied, fully qualified directory.
Relative paths, unavailable directories, and unusable repositories decline
that candidate. Candidates are tried in caller order; the first verified blob
wins. A clone that lacks the selector or path cannot prevent a later clone
from answering.

The source is the Git blob addressed by the locator, not the current checkout
of that file. The clone's checked-out branch and uncommitted source edits are
not substitutes for the requested blob. Content conversion belongs to the
source consumer, not this adapter.

A blob is admitted only when the shared PDB checksum verifier reports
`Exact` or `LineEndingNormalized`. Missing or unusable checksum evidence,
unsupported algorithms, and mismatches do not admit bytes. The verifier owns
algorithm support and normalization rules; this document does not define a
second hashing or normalization algorithm.

The adapter returns the original blob bytes, including when normalization was
needed to establish correspondence. It does not rewrite the blob into the
PDB's line-ending form. Consumers that disclose verification status use the
shared verifier's classification rather than calling every accepted result an
exact byte match.

## Decline and fallback

The result has two meanings:

| Local outcome | What the consumer may conclude |
| --- | --- |
| Verified bytes | This blob satisfies the supplied PDB document's checksum policy and can be passed to source interpretation. |
| `null` | This lookup did not supply verified bytes. Other configured acquisition routes may still answer. |

Malformed or unsupported locators, unavailable Git, missing objects, failed
reads, unsuccessful Git exit, rejected blob size, process-wait timeout, and
checksum rejection all yield no local result for their attempted candidate.
There is no detailed per-clone diagnostic in this adapter's result.

That deliberate probe contract does not turn failure into successful empty
source. Nor does it prove that a document is absent, that a remote request
would fail, or that another clone is unusable. Even a zero-length blob must
pass the supplied checksum policy before it can be a successful byte result.
Local misses do not authorize negative caching.

PDB acquisition owns what happens after all clones decline. Its remote
absence/failure distinction is unchanged; a local miss cannot convert a
remote transport or checksum failure into authoritative absence.

## Execution profile and its limits

The adapter uses Git's raw object-content operation,
`git cat-file blob <revision>:<path>`, with separate process arguments. The
source locator is not a shell command. The existing invocation requests
noninteractive operation and disables optional Git locks.

The current per-candidate controls are a **64-MiB blob acceptance cap** and a
**15-second wait for the Git process**. Oversized output and process-wait
expiry decline the candidate and request process-tree termination.
Termination is best effort.

These controls are not an end-to-end deadline or a complete resource budget.
Process startup, redirected-stream draining, checksum work, and later
candidates are not covered by one shared deadline. The blob cap is not a
process-wide allocation limit or a bound on standard error. The synchronous
adapter does not consume the acquisition caller's cancellation token.
Deterministic timeout, overflow, and teardown coverage is **unverified**.

Git's executable, environment, configuration, and object-store behavior remain
part of the caller's local tool environment. This contract does not promise
isolation from that environment, enforce Git-wide network policy, or certify
quiescence of all subprocesses. It does not broaden the repository threat
model to local users, mutable checkouts, symlinks, or hostile Git configuration.

## Convention and comparative evidence

[Git 2.51.0 `cat-file`](https://git-scm.com/docs/git-cat-file/2.51.0) distinguishes
raw object content from explicit `--filters` and `--textconv` conversion.
This adapter uses the raw-content convention rather than recreating a checkout
or borrowing working-tree conversion semantics. Git's object lookup supplies
the candidate bytes, not their correspondence with a Portable PDB.

The adjacent remote path in `PdbSourceAcquisition` likewise gates fetched bytes
through the shared verifier. Local lookup reuses that policy instead of
inventing weaker checksum admission. Its deliberately different locator
admission is justified by the distinction between a local content probe and a
network destination or immutable-origin claim.

These are supporting comparisons, not additional normative owners. No external
code or architecture is transferred by this specification.

## Evidence

The following existing Release tests cover the named observations. They are
not a claim that every parser or process edge case is gated.
`LocalRepoSourceReadTests` skips its real-Git cases when Git is unavailable;
a skipped case supplies no execution evidence.

| Claim | Existing gate |
| --- | --- |
| Normal raw-GitHub locator and escaped path interpretation; selected non-addressable forms decline | `LocalRepoSourceReadTests.ParsesGitHubRawUrl_IntoShaAndPath`, `ParsesGitHubRawUrl_UnescapesPath`, `RejectsNonAddressableUrls` |
| A real committed blob is returned on checksum match and refused on mismatch | `LocalRepoSourceReadTests.ReadsBlob_WhenChecksumMatches`, `ReturnsNull_WhenChecksumMismatches` |
| Missing selector/path or a non-repository directory supplies no bytes | `LocalRepoSourceReadTests.ReturnsNull_WhenCommitNotPresent`, `ReturnsNull_WhenPathNotPresent`, `ReturnsNull_WhenDirectoryIsNotAGitRepo` |
| A missing object in the first clone does not hide the second clone's verified blob | `LocalRepoSourceReadTests.ReadsBlob_FromSecondRepo_WhenFirstLacksCommit` |
| Shared verifier distinguishes accepted line-ending normalization | `PdbSourceAcquisitionTests.VerifyChecksum_AcceptsLineEndingNormalization` |
| Member, type, and printable-projection service paths use a clone when PDB-recorded local-file reads are disabled | `LocalRepoSourceAcquisitionIntegrationTests.ServiceLocalClone_SatisfiesMemberAndTypeSourceWithoutRemoteFetch` |
| The CLI accepts and propagates `--repo` for printable type Source Files while its HTTP path is offline | `LocalRepoSourceProjectionTests.TypeSourceFilesPrint_AcceptsRepoAtCliBoundaryWhileOffline` |

Full URI-edge coverage, clone-relative selector ambiguity, working-tree
divergence, normalized-checksum acceptance through the real-Git adapter, and
the process-limit/cleanup profile remain **unverified** by dedicated adapter
fixtures. The shared normalization test is not a substitute for that
end-to-end local case. The service and CLI gates establish their stated
consumer paths, not every `--repo` surface or Git-wide network isolation.

The boundary fixture already separating useful lookup from misleading
availability evidence is the missing-first-clone/present-second-clone case.
The neighboring checksum-mismatch fixture prevents an existing but wrong blob
from being presented as source. Further evidence should target a stated
adapter claim rather than introduce a generalized subprocess framework.
