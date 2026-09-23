module Explora.Types

open BitcoinRpc
open Browser.Types
open Fable.Core

type OutputMetadata = {
    txid: string
    index: int
    output: Output 
}

type TxMetadata = {
    tx: ConfirmedTransaction
}
    
type [<AllowNullLiteral>] GraphNode =
    inherit Vis.Node
    abstract metadata: U2<OutputMetadata, TxMetadata> with get, set

type NodeId = string
type EdgeId = string

type NodeModelKind =
    | Tx
    | Txo
    
type NodeModel = {
    Id: NodeId
    Title: HTMLElement
    Selected: bool
    Highlighted: bool
    Marked: bool
    Kind: NodeModelKind
    Metadata: U2<OutputMetadata, TxMetadata>
}

type EdgeModel = {
    Id: EdgeId
    From: NodeId
    To: NodeId
    Value: float
    Title: HTMLElement
    Selected: bool
    Highlighted: bool
    Marked: bool
    OutputData: OutputMetadata
}

// The source transactions of a transaction's inputs that are not loaded yet. A CoinJoin can have
// hundreds of inputs, so a double click loads them in batches and the graph shows what is left as
// one "+N" node instead of fetching everything at once.
type PendingSources = {
    TxId: NodeId
    SourceTxIds: string list   // distinct, most valuable first
    Value: float               // BTC those inputs bring in
}

type GraphModel = {
    Nodes: NodeModel list
    Edges: EdgeModel list
    Pending: PendingSources list
}

module GraphModel =
    let getNode (id: NodeId) (g: GraphModel) =
        g.Nodes |> List.tryFind (fun m -> id = m.Id)
        
    let getEdge (id: EdgeId) (g: GraphModel) =
        g.Edges |> List.tryFind (fun e -> id = e.Id)
        
    let getTransactionNodes (g: GraphModel) =
        g.Nodes |> List.filter (_.Metadata.IsCase2)
   
    let getTransactions (g: GraphModel) =
        g
        |> getTransactionNodes
        |> List.map (_.Metadata)
        |> List.map (function
            | (U2.Case2 m) -> m.tx 
            | _ -> failwith "Not possible.")
        
    let getSpenderNode (txid: string) (index: int) (g: GraphModel) =
        g
        |> getTransactions
        |> List.collect (fun tx -> tx.vin |> Array.map(fun inp -> (inp, tx.txid)) |> List.ofArray)
        |> List.filter (fun (inp, _) -> inp.txid = txid && inp.vout = index)
        |> List.map snd
        |> List.tryExactlyOne
        |> Option.bind (fun txid -> getNode txid g)
        
    let getEdgesConnectedTo (id: NodeId) (g: GraphModel) =
        g.Edges |> List.filter (fun e -> e.To = id || e.From = id)
        
    let getAddressReused (g: GraphModel) =
        g.Edges
        |> List.choose (_.OutputData.output.scriptPubKey.address)
        |> List.groupBy (id)
        |> List.filter (fun (_, es) -> List.length es > 1) 
        |> List.map fst

    let pendingNodeId (txid: NodeId) = $"pending-{txid}"
    let pendingEdgeId (txid: NodeId) = $"pending-{txid}-edge"

    // The transaction a "+N" node belongs to, if the id is one of those.
    let pendingOwner (nodeId: NodeId) (g: GraphModel) =
        g.Pending
        |> List.tryFind (fun pending -> pendingNodeId pending.TxId = nodeId)
        |> Option.map (fun pending -> pending.TxId)

    // Replaces what is pending for `txid`; an empty list removes the "+N" node.
    let setPending (txid: NodeId) (sources: (string * float) list) (g: GraphModel) =
        let others = g.Pending |> List.filter (fun pending -> pending.TxId <> txid)
        match sources with
        | [] -> { g with Pending = others }
        | _ ->
            let pending = { TxId = txid; SourceTxIds = sources |> List.map fst; Value = sources |> List.sumBy snd }
            { g with Pending = pending :: others }

// Ranks the source transactions of `inputs`: first the ones that reuse an address, then by the value
// they bring in, largest first; the ones already in the graph are left out. Several inputs can come
// from the same transaction: it counts once, with the values added up.
//
// An address is reused when it funds more than one input of this transaction, or when it is already
// somewhere on the map (`knownAddresses`). That is the signal the graph labels "reused", and in a
// CoinJoin, where every source is worth about the same, it is what a student wants to see first.
let rankSources (inputs: Input[]) (loadedTxIds: string[]) (knownAddresses: string[]) : (string * float)[] =
    let fundingCounts =
        inputs
        |> Array.choose (fun input -> input.prevOut.scriptPubKey.address)
        |> Array.countBy id
        |> Map.ofArray
    let isReused (input: Input) =
        match input.prevOut.scriptPubKey.address with
        | None -> false
        | Some address -> Map.find address fundingCounts > 1 || Array.contains address knownAddresses
    inputs
    |> Array.filter (fun input -> not (Array.contains input.txid loadedTxIds))
    |> Array.groupBy (fun input -> input.txid)
    |> Array.map (fun (txid, group) ->
        let reused = group |> Array.exists isReused
        let value = group |> Array.sumBy (fun input -> input.prevOut.value)
        txid, reused, value)
    |> Array.sortByDescending (fun (_, reused, value) -> reused, value)
    |> Array.map (fun (txid, _, value) -> txid, value)
