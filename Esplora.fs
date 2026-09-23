module Explora.Esplora

// Transactions come from the Esplora HTTP API (mempool.space first, blockstream.info as a
// fallback), which needs no API key. The JSON is mapped onto the same records that the
// `getrawtransaction` RPC call used to fill, so nothing past this module knows the provider changed.

open System
open BitcoinRpc
open Fable.Core
open Fetch
open Fetch.Types
open Thoth.Json

let endpoints = [
    "https://mempool.space/api"
    "https://blockstream.info/api"
]

[<Literal>]
let SatoshisPerBitcoin = 100_000_000.0

[<Literal>]
let TipHeightCacheMilliseconds = 60_000.0

[<Literal>]
let HttpNotFound = 404

// Esplora names script types differently from Bitcoin Core. The rest of the app matches on the
// Core names (colors and dashes in Graph.fs), so the translation happens once, here.
let scriptTypeFromEsplora (esploraType: string) =
    match esploraType with
    | "p2pk" -> "pubkey"
    | "p2pkh" -> "pubkeyhash"
    | "p2sh" -> "scripthash"
    | "v0_p2wpkh" -> "witness_v0_keyhash"
    | "v0_p2wsh" -> "witness_v0_scripthash"
    | "v1_p2tr" -> "witness_v1_taproot"
    | "op_return" -> "nulldata"
    | other -> other

// Esplora reports amounts in satoshis; the app works in BTC, as the RPC did.
let satoshisToBitcoin (satoshis: float) = satoshis / SatoshisPerBitcoin

// Esplora reports weight but not vsize. vsize = ceil(weight / 4), in integer arithmetic.
let virtualSizeFromWeight (weight: int) = (weight + 3) / 4

let outputDecoder : Decoder<Output> =
    Decode.object (fun get -> {
        value = get.Required.Field "value" Decode.float |> satoshisToBitcoin
        scriptPubKey = {
            asm = get.Required.Field "scriptpubkey_asm" Decode.string
            address = get.Optional.Field "scriptpubkey_address" Decode.string
            scriptType = get.Required.Field "scriptpubkey_type" Decode.string |> scriptTypeFromEsplora
        }
    })

// A coinbase input has no previous output to follow, and the RPC decoder never handled one
// either (it required `txid`). It decodes to None and is dropped from `vin`.
let inputDecoder : Decoder<Input option> =
    Decode.object (fun get ->
        let isCoinbase = get.Optional.Field "is_coinbase" Decode.bool |> Option.defaultValue false
        if isCoinbase then
            None
        else
            Some {
                txid = get.Required.Field "txid" Decode.string
                vout = get.Required.Field "vout" Decode.int
                prevOut = get.Required.Field "prevout" outputDecoder
            })

type BlockStatus = {
    blockHash: string
    blockHeight: int
    blockTime: DateTimeOffset
}

let blockStatusDecoder : Decoder<BlockStatus> =
    Decode.object (fun get -> {
        blockHash = get.Required.At ["status"; "block_hash"] Decode.string
        blockHeight = get.Required.At ["status"; "block_height"] Decode.int
        blockTime = get.Required.At ["status"; "block_time"] unixDateTimeDecoder
    })

let confirmedTransactionDecoder (tipHeight: int) : Decoder<ConfirmedTransaction> =
    Decode.object (fun get ->
        let weight = get.Required.Field "weight" Decode.int
        let status = get.Required.Raw blockStatusDecoder
        {
            txid = get.Required.Field "txid" Decode.string
            version = get.Required.Field "version" Decode.int
            size = get.Required.Field "size" Decode.int
            vsize = virtualSizeFromWeight weight
            weight = weight
            locktime = get.Required.Field "locktime" Decode.int
            vin = get.Required.Field "vin" (Decode.array inputDecoder) |> Array.choose id
            vout = get.Required.Field "vout" (Decode.array outputDecoder)
            fee = get.Required.Field "fee" Decode.float |> satoshisToBitcoin
            blockhash = status.blockHash
            confirmations = tipHeight - status.blockHeight + 1
            // The RPC reported `time` and `blocktime` separately; for a confirmed transaction
            // both are the timestamp of its block.
            time = status.blockTime
            blocktime = status.blockTime
        })

let unconfirmedTransactionDecoder : Decoder<UnconfirmedTransaction> =
    Decode.object (fun get ->
        let weight = get.Required.Field "weight" Decode.int
        {
            txid = get.Required.Field "txid" Decode.string
            version = get.Required.Field "version" Decode.int
            size = get.Required.Field "size" Decode.int
            vsize = virtualSizeFromWeight weight
            weight = weight
            locktime = get.Required.Field "locktime" Decode.int
            vout = get.Required.Field "vout" (Decode.array outputDecoder)
        })

let transactionDecoder (tipHeight: int) : Decoder<Transaction> =
    Decode.at ["status"; "confirmed"] Decode.bool
    |> Decode.andThen (function
        | true -> confirmedTransactionDecoder tipHeight |> Decode.map ConfirmedTransaction
        | false -> unconfirmedTransactionDecoder |> Decode.map UnconfirmedTransaction)

// Pure entry point, used by the tests: JSON text in, transaction (or the decoding error) out.
let parseTransaction (tipHeight: int) (json: string) : Result<Transaction, string> =
    Decode.fromString (transactionDecoder tipHeight) json

// What one provider said to one request. A 404 is final: the transaction doesn't exist anywhere.
// Anything else that isn't a 2xx (network down, 5xx, rate limit) means "this provider didn't
// answer" and the next one is tried.
type ProviderAnswer =
    | Answered of string
    | NotFound
    | Failed of string

// `GlobalFetch.fetch` is used instead of Fable.Fetch's `fetch` because the latter throws on any
// non-2xx status and the 404 would be indistinguishable from a provider being down.
let requestText (url: string) : JS.Promise<ProviderAnswer> =
    promise {
        let! response = GlobalFetch.fetch (RequestInfo.Url url, requestProps [])
        if response.Ok then
            let! body = response.text ()
            return Answered body
        elif response.Status = HttpNotFound then
            return NotFound
        else
            return Failed $"{url} responded HTTP {response.Status}"
    }
    |> Promise.catch (fun error -> Failed $"{url}: {error.Message}")

// A response that doesn't decode is reported as is, so a change in the API shows up instead of
// being masked by the fallback.
let rec fetchFromFirstAnswering (path: string) (decoder: Decoder<'T>) (remaining: string list) : JS.Promise<Result<'T, string>> =
    promise {
        match remaining with
        | [] ->
            return Error $"No data provider answered for {path}"
        | endpoint :: rest ->
            let! answer = requestText (endpoint + path)
            match answer with
            | Answered body ->
                return
                    Decode.fromString decoder body
                    |> Result.mapError (fun message -> $"Unexpected response from {endpoint}: {message}")
            | NotFound ->
                return Error $"Not found: {path}"
            | Failed _ ->
                return! fetchFromFirstAnswering path decoder rest
    }

// Confirmations = tip - height + 1, and Esplora doesn't compute that for us. The tip is cached for
// a minute so that following a chain of transactions doesn't ask for it on every hop.
let mutable cachedTipHeight : (int * float) option = None

let getTipHeight () : JS.Promise<Result<int, string>> =
    promise {
        let now = JS.Constructors.Date.now()
        match cachedTipHeight with
        | Some (height, fetchedAt) when now - fetchedAt < TipHeightCacheMilliseconds ->
            return Ok height
        | _ ->
            let! result = fetchFromFirstAnswering "/blocks/tip/height" Decode.int endpoints
            result |> Result.iter (fun height -> cachedTipHeight <- Some (height, now))
            return result
    }

let getTransaction (txid: string) : JS.Promise<Result<Transaction, string>> =
    promise {
        let! tipHeight = getTipHeight ()
        match tipHeight with
        | Error message -> return Error message
        | Ok tipHeight -> return! fetchFromFirstAnswering $"/tx/{txid}" (transactionDecoder tipHeight) endpoints
    }
