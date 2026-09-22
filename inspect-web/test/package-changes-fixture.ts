import type {
  BrowserPackageChangesFailure,
  BrowserPackageChangesInspection,
  BrowserPackageChangesProgress,
  BrowserPackageChangesRow,
} from "../src/facades/inspect-web-package.d.ts";
import { dateTimeOffsetString } from "./date-time-offset-string-fixture.ts";

export const progress: BrowserPackageChangesProgress = {
  phase: "Catalog",
  completed: 1,
  total: null,
  capturedHorizon: dateTimeOffsetString("2026-04-01T00:00:00+00:00"),
  catalogPagesAcquired: 1,
  catalogHttpAttempts: 1,
  catalogDecodedBytes: 2048,
};

export function changeRow(
  packageId: string,
  options: {
    readonly currentAvailability?: string;
    readonly fixedAvailability?: string;
    readonly securityRelease?: boolean;
    readonly advisoryUrl?: string;
  } = {},
): BrowserPackageChangesRow {
  const advisory = {
    ghsaId: "GHSA-1234-5678-9012",
    cveId: "CVE-2026-1234",
    severity: "High",
    advisoryUrl: options.advisoryUrl ?? "https://github.com/advisories/GHSA-1234-5678-9012",
    publishedAt: dateTimeOffsetString("2026-03-01T00:00:00+00:00"),
    updatedAt: dateTimeOffsetString("2026-03-02T00:00:00+00:00"),
  };
  const securityRelease = options.securityRelease === true;
  return {
    catalogActivity: {
      packageId,
      version: "1.2.3",
      normalizedPackageId: packageId.toLowerCase(),
      normalizedVersion: "1.2.3",
      leafUrl: "https://api.nuget.org/v3/catalog0/page/leaf.json",
      commitId: "0123456789abcdef",
      commitTimestamp: dateTimeOffsetString(
        "2026-03-31T00:00:00+00:00"),
      catalogKind: "Details",
      activity: "SnapshotObserved",
    },
    currentAdvisoryContext: {
      availability: options.currentAvailability ?? "Complete",
      advisories: [advisory],
    },
    fixedVersionEvidence: {
      availability: options.fixedAvailability ?? "Complete",
      advisories: [advisory],
    },
    packageReceipt: securityRelease
      ? {
          receivedAt: dateTimeOffsetString(
            "2026-03-31T00:00:00+00:00"),
          basis: "PackageMetadata",
        }
      : null,
    securityReleaseStatus: securityRelease
      ? "EvidencedInInterval"
      : "CheckedNoFixedVersionAssociation",
    securityRelease: securityRelease
      ? {
          receipt: {
            receivedAt: dateTimeOffsetString(
              "2026-03-31T00:00:00+00:00"),
            basis: "PackageMetadata",
          },
          advisories: [advisory],
        }
      : null,
    isSecurityRelevant: securityRelease,
  };
}

export const failure: BrowserPackageChangesFailure = {
  provider: "Advisory",
  catalogFailure: null,
  advisoryFailure: "SourceUnavailable",
  packageReceiptFailure: null,
};

export function inspection(
  rows: readonly BrowserPackageChangesRow[],
  failures: readonly BrowserPackageChangesFailure[] = [],
  completion = "Complete",
): BrowserPackageChangesInspection {
  return {
    content: {
      schemaVersion: 1,
      request: {
        referenceTime: dateTimeOffsetString(
          "2026-04-01T00:00:00+00:00"),
        fromExclusive: dateTimeOffsetString(
          "2026-03-01T00:00:00+00:00"),
        throughInclusive: dateTimeOffsetString(
          "2026-04-01T00:00:00+00:00"),
        usedDefaultInterval: true,
        packageScope: {
          kind: "PackageSet",
          selectionId: "package-set.example",
          prefix: null,
          packageIds: ["Example.Package"],
        },
        securitySelection: "AllActivity",
        maximumRows: 100,
        maximumCandidateEvents: 1000,
        maximumReceiptRequests: 100,
      },
      source: {
        producerKey: "nuget.org",
        producer: "NuGet.org",
        transportKind: "NuGetV3",
      },
      progress: [progress],
      rows,
      failures,
      summary: {
        capturedHorizon: dateTimeOffsetString(
          "2026-04-01T00:00:00+00:00"),
        catalogCompletion: "WindowExhausted",
        catalogFailure: null,
        catalogPagesAcquired: 1,
        catalogHttpAttempts: 1,
        catalogDecodedBytes: 2048,
        catalogInWindowEventCount: rows.length,
        matchingEventCount: rows.length,
        retainedEventCount: rows.length,
        candidateLimitReached: false,
        advisoryEvidence: {
          packageProducerKey: "nuget.org",
          advisoryProducer: "GitHub Advisory Database",
          observedAt: dateTimeOffsetString(
            "2026-04-01T00:00:00+00:00"),
          apiRequests: 1,
          responseBytes: 1024,
          complete: completion === "Complete",
          failures: failures.length ? ["SourceUnavailable"] : [],
          packages: [],
        },
        receiptCandidates: rows.length,
        receiptRequests: rows.length,
        receiptSuccesses: rows.filter(row => row.packageReceipt !== null).length,
        receiptFailures: [],
        receiptLimitReached: false,
        currentContextUnevaluableRows: 0,
        securityReleaseUnevaluableRows: 0,
        eligibleRowCount: rows.length,
        returnedRowCount: rows.length,
        resultLimitReached: completion === "ResultLimitReached",
        completion,
      },
    },
    share: {
      kind: "NonProjectable",
      fullUrl: null,
      packet: null,
      path: "package-changes/share",
      reason: "Package Activity reports are not shareable yet.",
    },
    diagnostics: [],
  };
}
