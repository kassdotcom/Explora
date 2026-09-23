// Decoder from Esplora JSON to the app's transaction records. Runs on the Fable output, so build first
// (`npm test` does it). The fixtures are real API responses saved on 2025-09-22.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parseTransaction, scriptTypeFromEsplora, satoshisToBitcoin, virtualSizeFromWeight } from "../Esplora.fs.js";

// The fixture's block is 884353; with this tip the transaction has exactly 10 confirmations.
const TIP_HEIGHT = 884362;

function fixture(name) {
  return readFileSync(new URL(`./fixtures/${name}.json`, import.meta.url), "utf8");
}

// Result.Ok is tag 0; Transaction.ConfirmedTransaction is tag 0, UnconfirmedTransaction is tag 1.
function expectConfirmed(result) {
  assert.equal(result.tag, 0, `expected Ok, got ${JSON.stringify(result.fields)}`);
  assert.equal(result.fields[0].tag, 0, "expected a confirmed transaction");
  return result.fields[0].fields[0];
}

function expectUnconfirmed(result) {
  assert.equal(result.tag, 0, `expected Ok, got ${JSON.stringify(result.fields)}`);
  assert.equal(result.fields[0].tag, 1, "expected an unconfirmed transaction");
  return result.fields[0].fields[0];
}

test("a confirmed transaction from mempool.space maps onto the RPC record", () => {
  const tx = expectConfirmed(parseTransaction(TIP_HEIGHT, fixture("confirmed-mempool")));
  assert.equal(tx.txid, "f0a1f254d1f1f62080152713247c897bbc9ca9c3f4215cbb3ba0d55a535eaa5e");
  assert.equal(tx.version, 2);
  assert.equal(tx.size, 349);
  assert.equal(tx.weight, 724);
  assert.equal(tx.vsize, 181);
  assert.equal(tx.locktime, 0);
  assert.equal(tx.fee, 0.00000185);
  assert.equal(tx.blockhash, "00000000000000000001e2a8b5320b3a2142b5d6cd3e119615809303138f4795");
  assert.equal(tx.confirmations, 10);
  assert.equal(tx.time.getTime(), 1739903133000);
  assert.equal(tx.blocktime.getTime(), 1739903133000);

  assert.equal(tx.vin.length, 1);
  assert.equal(tx.vin[0].txid, "2571b5cd9d496cbecd97a03443b8f55e7af83e4d9c5ceb5be39d2e8507fc4dee");
  assert.equal(tx.vin[0].vout, 1);
  assert.equal(tx.vin[0].prevOut.value, 0.00035403);
  assert.equal(tx.vin[0].prevOut.scriptPubKey.scriptType, "witness_v1_taproot");
  assert.equal(tx.vin[0].prevOut.scriptPubKey.address, "bc1p7wvcqr96h9qqrgmcdvqa3yxdp6gs64sgq56jfcmryw2kly3fuq3s097glq");

  assert.equal(tx.vout.length, 2);
  assert.equal(tx.vout[0].value, 0.0000033);
  assert.equal(tx.vout[0].scriptPubKey.scriptType, "witness_v0_keyhash");
  assert.equal(tx.vout[0].scriptPubKey.address, "bc1q7nx7nhzu45p5mxjdvzamaymee6cjq6q4resys9");
  assert.equal(tx.vout[1].value, 0.00034888);
  assert.equal(tx.vout[1].scriptPubKey.scriptType, "witness_v1_taproot");
});

test("blockstream.info decodes to the same transaction as mempool.space", () => {
  const fromMempool = expectConfirmed(parseTransaction(TIP_HEIGHT, fixture("confirmed-mempool")));
  const fromBlockstream = expectConfirmed(parseTransaction(TIP_HEIGHT, fixture("confirmed-blockstream")));
  assert.deepEqual(fromBlockstream, fromMempool);
});

test("a coinbase transaction drops its coinbase input instead of failing", () => {
  const tx = expectConfirmed(parseTransaction(TIP_HEIGHT, fixture("coinbase-mempool")));
  assert.equal(tx.txid, "7f0575fb1a8c3010f3f322d4bc28d8e438df1af494dd90d7d118065c25a85d37");
  assert.equal(tx.vin.length, 0);
  assert.equal(tx.vout.length, 6);
  assert.equal(tx.fee, 0);
  assert.equal(tx.weight, 1576);
  assert.equal(tx.vsize, 394);
});

test("an unconfirmed transaction keeps its outputs and has no block data", () => {
  const tx = expectUnconfirmed(parseTransaction(TIP_HEIGHT, fixture("unconfirmed-mempool")));
  assert.equal(tx.txid, "668d041e74f8674c08d6cca40441e934fde45197c489f13cbcd95189cb910100");
  assert.equal(tx.vout.length, 2);
  assert.equal(tx.weight, 549);
  assert.equal(tx.vsize, 138);
  assert.equal(tx.blockhash, undefined);
});

test("a response without the expected fields is an error, not a crash", () => {
  const result = parseTransaction(TIP_HEIGHT, "{}");
  assert.equal(result.tag, 1);
  assert.match(result.fields[0], /status/);
});

test("Esplora script types translate to the Bitcoin Core names the graph colors by", () => {
  assert.equal(scriptTypeFromEsplora("p2pk"), "pubkey");
  assert.equal(scriptTypeFromEsplora("p2pkh"), "pubkeyhash");
  assert.equal(scriptTypeFromEsplora("p2sh"), "scripthash");
  assert.equal(scriptTypeFromEsplora("v0_p2wpkh"), "witness_v0_keyhash");
  assert.equal(scriptTypeFromEsplora("v0_p2wsh"), "witness_v0_scripthash");
  assert.equal(scriptTypeFromEsplora("v1_p2tr"), "witness_v1_taproot");
  assert.equal(scriptTypeFromEsplora("op_return"), "nulldata");
  assert.equal(scriptTypeFromEsplora("multisig"), "multisig");
  // Anything unknown passes through and the graph paints it as unknown.
  assert.equal(scriptTypeFromEsplora("anchor"), "anchor");
});

test("satoshis become BTC and weight becomes vsize the way Core computes them", () => {
  assert.equal(satoshisToBitcoin(330), 0.0000033);
  assert.equal(satoshisToBitcoin(100000000), 1);
  assert.equal(virtualSizeFromWeight(724), 181);
  assert.equal(virtualSizeFromWeight(4), 1);
  assert.equal(virtualSizeFromWeight(5), 2);
});
