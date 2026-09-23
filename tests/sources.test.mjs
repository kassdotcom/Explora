// Ranking of the source transactions a double click loads. Runs on the Fable output (`npm test`).
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Input, Output, ScriptPubKey } from "../BitcoinRpc.fs.js";
import { rankSources } from "../Types.fs.js";
import { parseTransaction } from "../Esplora.fs.js";

// Values are binary fractions on purpose, so the sums are exact and the expected numbers can be typed.
function input(sourceTxId, value) {
  return new Input(sourceTxId, 0, new Output(value, new ScriptPubKey("", undefined, "witness_v0_keyhash")));
}

test("source transactions rank by the value they bring in, largest first", () => {
  const ranked = rankSources([input("aaa", 0.5), input("bbb", 2), input("ccc", 1)], []);
  assert.deepEqual(ranked, [["bbb", 2], ["ccc", 1], ["aaa", 0.5]]);
});

test("inputs from the same transaction count once, with their values added up", () => {
  const ranked = rankSources([input("aaa", 0.25), input("bbb", 0.625), input("aaa", 0.5)], []);
  assert.deepEqual(ranked, [["aaa", 0.75], ["bbb", 0.625]]);
});

test("source transactions already in the graph are left out", () => {
  const ranked = rankSources([input("aaa", 3), input("bbb", 2)], ["aaa"]);
  assert.deepEqual(ranked, [["bbb", 2]]);
});

test("a real transaction with one input has that one source", () => {
  const json = readFileSync(new URL("./fixtures/confirmed-mempool.json", import.meta.url), "utf8");
  const tx = parseTransaction(884362, json).fields[0].fields[0];
  assert.deepEqual(rankSources(tx.vin, []), [["2571b5cd9d496cbecd97a03443b8f55e7af83e4d9c5ceb5be39d2e8507fc4dee", 0.00035403]]);
});
