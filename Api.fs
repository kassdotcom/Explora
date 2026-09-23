module Explora.Api

let getTransaction (txId: string)  =
    promise {
        let! response = Esplora.getTransaction txId
        return response |> Result.mapError (fun message -> $"Failed to fetch transaction: %s{message}")
    }
        
let getSpenderTransaction (txId: string) (index: int) =
    promise {
        try
            let! response = MempoolSpace.getSpenderTransaction txId index
            return Ok response
        with ex ->
            return Error $"Failed to fetch spender tx: %s{ex.Message}"
    }
