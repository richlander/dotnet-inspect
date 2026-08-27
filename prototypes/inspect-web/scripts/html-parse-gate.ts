import { createLogger, type Logger, type LogOptions } from "vite";

// Vite parses `index.html` with parse5 on every build, and when parse5 rejects the
// document Vite logs the failure and carries on. `vite build` prints
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
