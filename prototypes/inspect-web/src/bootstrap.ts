// The entry point `index.html` loads, and the prototype's startup failure path.
//
// This is a module rather than a `<script>` block inside `index.html` because the gates
// account for files: the compiler builds a program from `.ts` files and oxlint is handed
// a file list, so neither can see script written inside a document. The version of this
// code that lived in `index.html` dereferenced `document.querySelector` without checking
// it -- exactly what `no-unsafe-member-access` exists to catch -- and shipped that way for
// as long as the file existed, because nothing read it. Issue #4783.
//
// `test/toolchain.test.ts` keeps the arrangement: it fails if any HTML document this
// project owns carries a script body, so the gap cannot reopen by someone writing a few
// lines inline again.

function describe(error: unknown): string {
  if (error instanceof Error) {
    return error.stack ?? `${error.name}: ${error.message}`;
  }
  return String(error);
}

// Startup failed before the application could render anything, so this writes directly to
// the mount point rather than going through the app's own rendering. Styles are inline for
// the same reason: the stylesheet may be exactly what failed to load.
function reportStartupFailure(error: unknown): void {
  const detail = describe(error);
  const app = document.querySelector("#app");
  if (!(app instanceof HTMLElement)) {
    throw new Error(`Prototype startup failed, and #app is not in the document\n\n${detail}`);
  }
  app.style.cssText =
    "padding:24px;color:#e8e9e4;background:#10110f;font:14px/1.6 monospace;white-space:pre-wrap";
  app.textContent = `Prototype startup failed\n\n${detail}`;
}

// Deliberately a dynamic import. It keeps this handler registered before the application
// module is fetched or evaluated, so a failure in either is reported rather than lost to
// the console.
void import("./dotnet-inspect.ts").catch(reportStartupFailure);
