import type {
  PackageQueryDurableRowPresentationLayout,
  PackageQueryDurableRowTerm,
} from "../src/package-query-durable-row.ts";

const TERMS: readonly [
  PackageQueryDurableRowTerm,
  string,
][] = [
  ["package-id", "Package"],
  ["version", "Version"],
  ["tier", "Acquisition Tier"],
  ["answers", "Answers"],
  ["evidence", "Evidence"],
  ["total-downloads", "Lifetime Downloads"],
  ["verified", "Verified"],
  ["producer", "Source"],
  ["description", "Description"],
  ["root-request", "Root Request"],
  ["owners", "Owners"],
  ["manifest", "Manifest"],
  ["ecosystem-admission", "Ecosystem Admission"],
];

export const packageQueryDurableRowLayoutFixture:
  PackageQueryDurableRowPresentationLayout = {
    contract: "package-query.durable-row",
    schemaIdentity: "sha256:test-schema",
    descriptorIdentity: "sha256:test-descriptor",
    vocabularySnapshotIdentity: "sha256:test-vocabulary",
    fields: TERMS.map(([term, displayLabel], ordinal) => ({
      ordinal,
      schemaLocation: `/prefixItems/${ordinal}`,
      schema: {},
      term: {
        identity: { value: term },
        displayLabel,
        summary: `${displayLabel} summary.`,
        mapEntries: [],
      },
    })),
  };
