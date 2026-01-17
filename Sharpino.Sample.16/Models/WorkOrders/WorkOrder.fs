namespace sharpino.Template.Models

open Sharpino
open Sharpino.Template.Commons
open FSharpPlus.Operators
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling

    type WorkingItemState =
        | Pending
        | Started
        | Completed
        | Failed of Quantity
        
    type WorkingItem =
        {
            ProductId: ProductId
            Quantity: Quantity
            State: WorkingItemState
        }
        static member New productId quantity =
            {
                ProductId = productId
                Quantity = quantity
                State = Pending
            }
        
    type WorkOrderState =
        | Initialized
        | InProgress
        | FullyCompleted
        | SomeFailed of List<ProductId * Quantity>
        
    type WorkOrder =
        {
            Id: WorkOrderId
            Name: string
            WorkingItems: List<WorkingItem>
        }
        static member New name workingItems =
            result
                {
                    let! workingItemsAreNotEmpty =
                        workingItems
                        |> List.isEmpty
                        |> not
                        |> Result.ofBool "Working items must not be empty"
                    
                    let! allProductsAreUnique =
                        workingItems
                        |>> _.ProductId
                        |> List.distinct
                        |> List.length = workingItems.Length
                        |> Result.ofBool "All working items must be unique"
                        
                    let! allWorkingItemsPending =
                        workingItems
                        |> List.forall (fun (x: WorkingItem) -> x.State = Pending)
                        |> Result.ofBool "All working items must be pending"
                        
                    return {
                        Id = WorkOrderId.New
                        Name = name
                        WorkingItems = workingItems
                    }
                }
        member
            this.QuantityPerProduct =
                this.WorkingItems
                |> List.map (fun x -> (x.ProductId, x.Quantity))
                |> List.groupBy fst
                |> List.map
                    (fun (id, quantities) ->
                        (id, List.sumBy (fun x -> x |> snd|> fun (x: Quantity) -> x.Value) quantities)
                    )
                |> Map.ofList    
        
        member this.GetQuantityPerProduct productId=
            this.QuantityPerProduct[productId]
                
        member
            this.Start productId =
                result {
                    let! workingItem =
                        this.WorkingItems |> List.tryFind (fun x -> x.ProductId = productId)
                        |> Result.ofOption "Working item not found"
                    let! stateMustBePending =
                        match workingItem.State with
                        | Pending -> Ok ()
                        | _ -> Error "Working item must be pending"
                    
                    return {
                        this with
                            WorkingItems =
                                this.WorkingItems
                                |>>
                                   fun x ->
                                        if x.ProductId = productId then
                                            { x with State = Started }
                                        else x
                    }    
                }
        member    
            this.Complete productId =
                result {
                    let! workingItem =
                        this.WorkingItems |> List.tryFind (fun x -> x.ProductId = productId)
                        |> Result.ofOption "working item not found"
                    let! stateMustBeStarted =
                        match workingItem.State with
                        | Started -> Ok ()
                        | _ -> Error "Working item must be started"
                    
                    return {
                        this with
                            WorkingItems =
                                this.WorkingItems
                                |>>
                                   fun x ->
                                        if x.ProductId = productId then
                                            { x with State = Completed }
                                        else x
                    }    
                }
        member     
            this.Fail productId quantity =
                result {
                    let! workingItem =
                        this.WorkingItems |> List.tryFind (fun x -> x.ProductId = productId)
                        |> Result.ofOption "working item not found"
                    let! stateMustBeStarted =
                        match workingItem.State with
                        | Started -> Ok ()
                        | _ -> Error "Working item must be started"
                               
                    return {
                        this with
                            WorkingItems =
                                this.WorkingItems
                                |>>
                                   fun x ->
                                        if x.ProductId = productId then
                                            { x with State = Failed quantity }
                                        else x
                                }
                }
                
            member this.WorkOrderState =
                match this.WorkingItems with
                | x when x |> List.forall (fun (x: WorkingItem) -> x.State = Pending) -> Initialized
                | x when x |> List.exists (fun (x: WorkingItem) -> x.State = Started) -> InProgress
                | x when x |> List.forall (fun (x: WorkingItem) -> x.State = Completed) -> FullyCompleted
                | x when
                    x |> List.exists (fun (x: WorkingItem) -> x.State.IsFailed) ->
                        let failedItems =
                            x |> List.filter (fun (x: WorkingItem) -> x.State.IsFailed)
                            |> List.map (fun (x: WorkingItem) -> (x.ProductId, x.Quantity))
                        SomeFailed failedItems
                | _ -> InProgress        
                 
            
            static member SnapshotsInterval = 50
            static member StorageName = "_WorkOrders"
            static member Version = "_01"
            
            member this.Serialize =
                (this, jsonOptions) |> JsonSerializer.Serialize
            
            static member
                Deserialize (data: string) =
                    try
                        JsonSerializer.Deserialize<WorkOrder> (data, jsonOptions) |> Ok
                    with
                        | ex -> Error ex.Message
        
            interface Aggregate<string> with 
                member this.Id = this.Id.Value
                member this.Serialize = this.Serialize
            
            