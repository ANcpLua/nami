// Shared setup for the nami-lint rule tests.
//
// The tests use `@typespec/compiler/testing`: an in-memory file system, the
// real compiler and the real rule objects from `dist/`. The in-repo emitter
// library is mounted from disk, declarations and decorator functions alike, so
// a test writes `@duckDbTable`, `@@wireRoot` and `@@tool` exactly as the
// contract does and a rule is exercised against the decorators it reads there.
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { createLinterRuleTester, createTester, mockFile } from "@typespec/compiler/testing";

export const LIBRARY = "@ancplua/typespec-nami-lint";
export const contracts = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

const emitterFile = (path) => readFile(resolve(contracts, "emitters/csharp", path), "utf8");
const emitterDeclarations = await emitterFile("lib/main.tsp");
const emitterPackage = await emitterFile("package.json");
const emitterConfig = await emitterFile("tspconfig.yaml");
const emitterModule = await import(pathToFileURL(resolve(contracts, "emitters/csharp/dist/index.js")).href);

export const libraries = ["@typespec/http", "@typespec/openapi", "@typespec/json-schema"];
/**
 * Mount the in-repo emitter library into a tester and import it into `main.tsp`.
 *
 * It is mounted and imported as a package (`./emit`), as the contracts import it: its `auto dec` declarations compile
 * only under the `auto-decorators` feature of its own tspconfig.yaml, which the compiler reads for a package import.
 */
export const mounted = (tester) =>
  tester
    .files({
      "emit/package.json": emitterPackage,
      "emit/tspconfig.yaml": emitterConfig,
      "emit/lib/main.tsp": emitterDeclarations,
      "emit/dist/index.js": mockFile.js(emitterModule),
    })
    .import("@typespec/http", "@typespec/json-schema", "./emit")
    .using("TypeSpec.Http", "TypeSpec.JsonSchema", "Nami.Api.Schema.Emit.CSharp");

/** Compiles without any linter: the base for testing one rule at a time. */
export const Tester = mounted(createTester(contracts, { libraries }));

/** Compiles with the gate's ruleset, as `bun run lint` does. */
export const GateTester = mounted(
  createTester(contracts, {
    libraries: [...libraries, LIBRARY],
    compilerOptions: { linterRuleSet: { extends: [`${LIBRARY}/all`] } },
  }),
);

/** A rule tester over a fresh in-memory project. */
export async function testRule(rule) {
  return createLinterRuleTester(await Tester.createInstance(), rule, LIBRARY);
}

/** The diagnostic code of a rule of this library. */
export const code = (rule) => `${LIBRARY}/${rule.name}`;
