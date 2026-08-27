import { createLogger, type Logger, type LogOptions } from "vite";

// Vite parses `index.html` with parse5 on every build, and for most rejections it logs
// the failure and carries on. `vite build` prints
//
//   Unable to parse HTML; parse5 error code end-tag-with-trailing-solidus
//
// and still exits 0, emitting the document it could not parse. Round 1 (Opus) found that
// message while demonstrating that `</script/>` runs a script body, so the build was
// already being told the markup was malformed and was throwing the answer away.
//
// That verdict is worth more than the one `test/toolchain.test.ts` reaches by itself,
// because it comes from the parser that actually processes the file rather than from one
// this project wrote. The tokenizer in the test suite reports markup *it* cannot read;
// this reports markup *parse5* cannot read. Neither subsumes the other, and a document
// has to satisfy both.
//
// Making the warning fatal is the whole change. Vite offers `customLogger` for exactly
// this, so no plugin, no dependency and no second parse are involved.
//
// Round 3 (Gemini) found the claim above overstated, and it is worth being exact about
// the seam. Vite returns early for five parse5 codes before its logger is called at all:
// `missing-doctype`, `abandoned-head-element-child`, `duplicate-attribute`,
// `non-void-html-element-start-tag-with-trailing-solidus` and
// `unexpected-question-mark-instead-of-tag-name`. Nothing here can make those fatal,
// because nothing here is told. Three cannot carry code; the other two can, and
// `test/toolchain.test.ts` rejects each of them on its own -- pinned by the test named
// "the parse errors Vite discards are caught by the markup scan", so that coverage cannot
// drift back to a channel that discards it.
export function isHtmlParseFailure(message: string): boolean {
  return message.includes("Unable to parse HTML; parse5 error code");
}

export function failOnHtmlParseErrors(base: Logger = createLogger()): Logger {
  const refuse = (message: string): never => {
    throw new Error(`${message.trim()}\n\n`
      + "This document did not parse, so what a browser runs is whatever error recovery "
      + "invents -- which is not what anyone reviewed. Vite reports this and would "
      + "otherwise ship the document anyway; `scripts/html-parse-gate.ts` makes it fatal.");
  };

  return {
    ...base,
    warn(message: string, options?: LogOptions): void {
      if (isHtmlParseFailure(message)) { refuse(message); }
      base.warn(message, options);
    },
    warnOnce(message: string, options?: LogOptions): void {
      if (isHtmlParseFailure(message)) { refuse(message); }
      base.warnOnce(message, options);
    },
  };
}
