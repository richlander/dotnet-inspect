import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { type TestContext } from "node:test";
import {
  recordCoreClrReadyToRunEvidence,
  verifyCoreClrReadyToRunEvidence,
} from "../scripts/verify-coreclr-r2r-publication.ts";

interface EvidenceView {
  readonly managedAssets: {
    readonly publishedCount: number;
    readonly readyToRunCount: number;
    readonly inspectWebApplicationReadyToRunCount: number;
    readonly ilOnly: readonly { readonly virtualPath: string }[];
  };
  readonly frameworkPayload: {
    readonly brotli: {
      readonly fileCount: number;
      readonly bytes: number;
    };
    readonly gzip: {
      readonly fileCount: number;
      readonly bytes: number;
    };
  };
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null;
}

function isEvidenceView(value: unknown): value is EvidenceView {
  if (!isRecord(value) || !isRecord(value.managedAssets)) {
    return false;
  }
  const managedAssets = value.managedAssets;
  if (
    typeof managedAssets.publishedCount !== "number"
    || typeof managedAssets.readyToRunCount !== "number"
    || typeof managedAssets.inspectWebApplicationReadyToRunCount !== "number"
    || !Array.isArray(managedAssets.ilOnly)
    || !managedAssets.ilOnly.every(
      asset => isRecord(asset) && typeof asset.virtualPath === "string",
    )
    || !isRecord(value.frameworkPayload)
    || !isRecord(value.frameworkPayload.brotli)
    || !isRecord(value.frameworkPayload.gzip)
  ) {
    return false;
  }
  return typeof value.frameworkPayload.brotli.fileCount === "number"
    && typeof value.frameworkPayload.brotli.bytes === "number"
    && typeof value.frameworkPayload.gzip.fileCount === "number"
    && typeof value.frameworkPayload.gzip.bytes === "number";
}

function wasm(payload: number): Buffer {
  return Buffer.from([0x00, 0x61, 0x73, 0x6d, payload]);
}

function createPublication(context: TestContext): {
  readonly site: string;
  readonly readyToRun: string;
  readonly cohort: string;
} {
  const root = mkdtempSync(join(tmpdir(), "inspect-web-r2r-"));
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const site = join(root, "wwwroot");
  const framework = join(site, "_framework");
  const readyToRun = join(root, "R2R");
  const cohort = join(root, "runtime-cohort");
  mkdirSync(framework, { recursive: true });
  mkdirSync(readyToRun);
  mkdirSync(cohort);

  const assets = [
    {
      virtualPath: "System.Private.CoreLib.wasm",
      name: "System.Private.CoreLib.core.wasm",
      bytes: wasm(1),
      readyToRun: true,
    },
    {
      virtualPath: "InspectWeb.Engine.wasm",
      name: "InspectWeb.Engine.app.wasm",
      bytes: wasm(2),
      readyToRun: true,
    },
    {
      virtualPath: "System.wasm",
      name: "System.facade.wasm",
      bytes: Buffer.from("webcil"),
      readyToRun: false,
    },
  ] as const;
  for (const asset of assets) {
    writeFileSync(join(framework, asset.name), asset.bytes);
    if (asset.readyToRun) {
      writeFileSync(
        join(readyToRun, asset.virtualPath.replace(/\.wasm$/, ".dll")),
        asset.bytes,
      );
    }
  }
  writeFileSync(join(framework, "dotnet.native.runtime.wasm"), wasm(3));
  writeFileSync(join(framework, "dotnet.native.runtime.wasm.br"), "br");
  writeFileSync(join(framework, "dotnet.native.runtime.wasm.gz"), "gzip");
  const loader = `/*json-start*/${JSON.stringify({
      resources: {
        coreAssembly: [
          {
            virtualPath: assets[0].virtualPath,
            name: assets[0].name,
          },
        ],
        assembly: assets.slice(1).map(asset => ({
          virtualPath: asset.virtualPath,
          name: asset.name,
        })),
      },
    })}/*json-end*/`;
  writeFileSync(join(framework, "dotnet.fingerprint.js"), loader);
  writeFileSync(join(framework, "dotnet.js"), loader);
  writeFileSync(join(framework, "dotnet.native.js"), "export default {};");
  writeFileSync(join(framework, "dotnet.runtime.js"), "export {};");
  writeFileSync(
    join(cohort, "runtime-cohort.json"),
    `${JSON.stringify({
      schema: 2,
      configuration: {
        publishReadyToRun: true,
        publishReadyToRunComposite: false,
        readyToRunContainer: "wasm",
      },
    }, null, 2)}\n`,
  );
  return { site, readyToRun, cohort };
}

test("records R2R evidence with SDK runtime aliases present", context => {
  const publication = createPublication(context);
  recordCoreClrReadyToRunEvidence(
    publication.site,
    publication.readyToRun,
    publication.cohort,
  );
  verifyCoreClrReadyToRunEvidence(
    publication.site,
    publication.readyToRun,
    publication.cohort,
  );

  const evidence: unknown = JSON.parse(readFileSync(
    join(publication.cohort, "ready-to-run-assets.json"),
    "utf8",
  ));
  assert.ok(isEvidenceView(evidence));
  assert.equal(evidence.managedAssets.publishedCount, 3);
  assert.equal(evidence.managedAssets.readyToRunCount, 2);
  assert.equal(evidence.managedAssets.inspectWebApplicationReadyToRunCount, 1);
  assert.deepEqual(
    evidence.managedAssets.ilOnly.map(asset => asset.virtualPath),
    ["System.wasm"],
  );
  assert.deepEqual(evidence.frameworkPayload.brotli, {
    fileCount: 1,
    bytes: 2,
  });
  assert.deepEqual(evidence.frameworkPayload.gzip, {
    fileCount: 1,
    bytes: 4,
  });
});

test("rejects a published asset that differs from its Crossgen2 output", context => {
  const publication = createPublication(context);
  writeFileSync(
    join(publication.site, "_framework", "InspectWeb.Engine.app.wasm"),
    wasm(9),
  );
  assert.throws(
    () => recordCoreClrReadyToRunEvidence(
      publication.site,
      publication.readyToRun,
      publication.cohort,
    ),
    /differs from 'InspectWeb\.Engine\.dll'/,
  );
});

test("rejects ReadyToRun evidence after published bytes change", context => {
  const publication = createPublication(context);
  recordCoreClrReadyToRunEvidence(
    publication.site,
    publication.readyToRun,
    publication.cohort,
  );
  writeFileSync(
    join(publication.site, "_framework", "System.Private.CoreLib.core.wasm"),
    wasm(8),
  );
  assert.throws(
    () => verifyCoreClrReadyToRunEvidence(
      publication.site,
      publication.readyToRun,
      publication.cohort,
    ),
    /differs from 'System\.Private\.CoreLib\.dll'/,
  );
});
