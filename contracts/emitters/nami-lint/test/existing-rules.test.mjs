// The five rules that existed before this library grew. Their code is unchanged;
// these tests pin what they report so that later work on the shared policy
// cannot change it unnoticed.
import test from "node:test";
import { confusableIdentityDocumentedRule } from "../dist/rules/confusable-identity-documented.js";
import { httpParamSnakeCaseRule } from "../dist/rules/http-param-snake-case.js";
import { identityBindingRule } from "../dist/rules/identity-binding.js";
import { identityEdgeTypedRule } from "../dist/rules/identity-edge-typed.js";
import { wireNameFixId, wireNameSnakeCaseRule } from "../dist/rules/wire-name-snake-case.js";
import { code, testRule } from "./harness.mjs";

const common = `namespace Nami.Api.Contracts.Common { scalar TraceId extends string; scalar SessionId extends string; }`;

test("wire-name-snake-case: a camelCase property without an encoded name, and its code fix", async () => {
  const source = `
    namespace Nami.Api.Contracts.Tools;
    model Input {
      maxBytes: int32;
    }
  `;
  await (await testRule(wireNameSnakeCaseRule)).expect(source).toEmitDiagnostics({
    code: code(wireNameSnakeCaseRule),
    message: /^Property 'maxBytes' ships the camelCase wire name 'maxBytes'\. Add @encodedName\("application\/json", "max_bytes"\)/,
  });
  await (await testRule(wireNameSnakeCaseRule)).expect(source).applyCodeFix(wireNameFixId).toEqual(`
    namespace Nami.Api.Contracts.Tools;
    model Input {
      @encodedName("application/json", "max_bytes")
      maxBytes: int32;
    }
  `);
});

test("wire-name-snake-case: a dotted wire name", async () => {
  await (await testRule(wireNameSnakeCaseRule)).expect(`
    namespace Nami.Api.Contracts.Tools;
    model Input { @encodedName("application/json", "service.name") serviceName: string; }
  `).toEmitDiagnostics({ code: code(wireNameSnakeCaseRule), message: /ships the dotted wire name 'service\.name'/ });
});

test("wire-name-snake-case: snake_case names, declared or already so, and names outside the product contract", async () => {
  await (await testRule(wireNameSnakeCaseRule)).expect(`
    namespace Nami.Api.Contracts.Tools { model Input { @encodedName("application/json", "max_bytes") maxBytes: int32; service: string; } }
    namespace Nami.Models.Storage { model Row { projectId: string; } }
  `).toBeValid();
});

test("http-param-snake-case: a camelCase query parameter", async () => {
  await (await testRule(httpParamSnakeCaseRule)).expect(`
    @service namespace Nami.Api.Contracts;
    @route("/traces") @get op read(@query traceId: string): void;
  `).toEmitDiagnostics({
    code: code(httpParamSnakeCaseRule),
    message: `Query parameter 'traceId' ships as 'traceId'. Rename it at the decorator that owns the wire name: @query("trace_id").`,
  });
});

test("http-param-snake-case: a parameter renamed at its decorator", async () => {
  await (await testRule(httpParamSnakeCaseRule)).expect(`
    @service namespace Nami.Api.Contracts;
    @route("/traces/{trace_id}") @get op read(@path("trace_id") traceId: string, @query("max_bytes") maxBytes?: int32): void;
  `).toBeValid();
});

test("identity-binding: a reserved name typed as a bare string, and an identity under another name", async () => {
  await (await testRule(identityBindingRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools {
      model Hit {
        @encodedName("application/json", "trace_id") traceId: string;
        candidate: Nami.Api.Contracts.Common.TraceId;
      }
    }
  `).toEmitDiagnostics([
    { code: code(identityBindingRule), message: /^Property 'traceId' uses the name reserved for the 'TraceId' identity but is typed 'string'\./ },
    { code: code(identityBindingRule), message: /^Property 'candidate' carries the shared identity 'TraceId' but is named neither 'traceId' or a registered edge/ },
  ]);
});

test("identity-binding: an identity renamed on the wire", async () => {
  await (await testRule(identityBindingRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools { model Hit { @encodedName("application/json", "trace") traceId: Nami.Api.Contracts.Common.TraceId; } }
  `).toEmitDiagnostics({ code: code(identityBindingRule), message: /^Identity property 'traceId' is renamed on the wire to 'trace'\./ });
});

test("identity-binding: the subject name, its plural and a registered edge", async () => {
  await (await testRule(identityBindingRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools {
      model Hit {
        @encodedName("application/json", "trace_id") traceId: Nami.Api.Contracts.Common.TraceId;
        @encodedName("application/json", "trace_ids") traceIds: Nami.Api.Contracts.Common.TraceId[];
        @encodedName("application/json", "example_trace_id") exampleTraceId?: Nami.Api.Contracts.Common.TraceId;
      }
    }
  `).toBeValid();
});

test("identity-edge-typed: a registered edge typed as a built-in", async () => {
  await (await testRule(identityEdgeTypedRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools { model Run { ciRunId: string; } }
  `).toEmitDiagnostics({
    code: code(identityEdgeTypedRule),
    message: /^Property 'ciRunId' is a registered edge of the 'SessionId' identity but is typed 'string'\./,
  });
});

test("identity-edge-typed: a registered edge typed as a declared scalar", async () => {
  await (await testRule(identityEdgeTypedRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools { model Run { ciRunId: Nami.Api.Contracts.Common.SessionId; other: string; } }
  `).toBeValid();
});

test("confusable-identity-documented: a look-alike scalar that never says how it relates", async () => {
  await (await testRule(confusableIdentityDocumentedRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools { @doc("A runner session.") scalar RunnerSessionId extends string; }
  `).toEmitDiagnostics({
    code: code(confusableIdentityDocumentedRule),
    message: /^Scalar 'RunnerSessionId' reads as a variant of the shared identity 'SessionId' but never says how the two relate\./,
  });
});

test("confusable-identity-documented: a look-alike scalar that names the identity it is not", async () => {
  await (await testRule(confusableIdentityDocumentedRule)).expect(`
    ${common}
    namespace Nami.Api.Contracts.Tools {
      @doc("A runner session. Not joinable with Nami.Api.Contracts.Common.SessionId.") scalar RunnerSessionId extends string;
      scalar Cursor extends string;
    }
  `).toBeValid();
});
