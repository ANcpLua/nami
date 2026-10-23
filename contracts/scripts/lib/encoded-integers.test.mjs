import assert from "node:assert/strict";
import test from "node:test";
import { encodedIntegerPattern } from "./encoded-integers.mjs";

test("encoded uint64 keeps narrower TypeSpec bounds", () => {
  const pattern = new RegExp(encodedIntegerPattern({ format: "uint64", minimum: 10, maximum: 20 }, "fixture"), "u");
  for (const value of ["10", "11", "19", "20"]) assert(pattern.test(value), value);
  for (const value of ["0", "1", "9", "21", "100", "010", "+10", "10\n"]) assert(!pattern.test(value), value);
});

test("encoded signed ranges preserve negative and exclusive bounds", () => {
  const pattern = new RegExp(encodedIntegerPattern({ format: "int64", exclusiveMinimum: -12, exclusiveMaximum: 12 }, "fixture"), "u");
  for (const value of ["-11", "-1", "0", "1", "11"]) assert(pattern.test(value), value);
  for (const value of ["-12", "12", "-0", "+1", "01", "-01"]) assert(!pattern.test(value), value);
});

test("encoded 64-bit endpoints survive without conversion to Number", () => {
  const signed = new RegExp(encodedIntegerPattern({ format: "int64" }, "signed"), "u");
  const unsigned = new RegExp(encodedIntegerPattern({ format: "uint64" }, "unsigned"), "u");
  assert(signed.test("-9223372036854775808"));
  assert(signed.test("9223372036854775807"));
  assert(!signed.test("9223372036854775808"));
  assert(!signed.test("-9223372036854775809"));
  assert(unsigned.test("18446744073709551615"));
  assert(!unsigned.test("18446744073709551616"));
});
