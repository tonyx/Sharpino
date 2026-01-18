namespace Sharpino.Template

open System.Threading
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Cache
open FSharpPlus.Operators
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Storage

open Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.MemoryStorage

open FsToolkit.ErrorHandling
open System
open sharpino.Template.Models

type MaterialManager (messageSenders: MessageSenders, eventStore: IEventStore<string>, materialsViewer: AggregateViewer<Material>, productsViewer: AggregateViewer<Product>, workOrdersViewer: AggregateViewer<WorkOrder>) =
    new() =
        let memoryStorage = new MemoryStorage()
        let materialsViewer: AggregateViewer<Material> = getAggregateStorageFreshStateViewer<Material, MaterialEvents, string> memoryStorage
        let productsViewer: AggregateViewer<Product> =  getAggregateStorageFreshStateViewer<Product, ProductEvents, string> memoryStorage
        let workOrdersViewer: AggregateViewer<WorkOrder> =  getAggregateStorageFreshStateViewer<WorkOrder, WorkOrderEvents, string> memoryStorage
        MaterialManager(NoSender, memoryStorage, materialsViewer, productsViewer, workOrdersViewer)
    
    member this.AddMaterial (material: Material) =
        result
            {
                return!
                    material
                    |> runInit<Material, MaterialEvents, string> eventStore  messageSenders 
            }
    member this.AddProduct (product: Product) =
        result
            {
                let! materialExists =
                    product.Materials
                    |>> fst
                    |> List.traverseResultM (fun id -> materialsViewer id.Value)
                return!
                    product
                    |> runInit<Product, ProductEvents, string> eventStore  messageSenders 
            }

    member this.Consume (id: MaterialId, quantity: Quantity) =
        result
            {
                return!  
                    Consume quantity
                    |> runAggregateCommand<Material, MaterialEvents, string> id.Value eventStore messageSenders
            }
    
    member this.AddQuantity (id: MaterialId, quantity: Quantity) =
        result
            {
                return! 
                    Add quantity
                    |> runAggregateCommand<Material, MaterialEvents, string> id.Value eventStore messageSenders 
            }
    
    member this.GetMaterial (id: MaterialId) =
        result
            {
                let! _, result = materialsViewer id.Value
                return result
            }

    member this.GetMaterialsAsync (?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                let! materials = StateView.getAggregateStatesInATimeIntervalAsync<Material, MaterialEvents, string> eventStore DateTime.MinValue DateTime.MaxValue  (ct |> Some)
                return 
                    materials
                    |>> snd
            }
            
    member this.AddWorkOrder (workOrder: WorkOrder) =
        result
            {
                let! products =
                    workOrder.WorkingItems |> List.map _.ProductId
                    |> List.traverseResultM (fun id -> productsViewer id.Value |> Result.map snd)
               
                let materialIdsWithConsumeCommands =
                    let materialsWithQuantities =
                        products
                        |>> fun x ->
                            x.Materials
                            |>> 
                                fun (id, q) ->
                                    id, (Quantity.New (q.Value * workOrder.GetQuantityPerProduct x.ProductId)).OkValue
                        |> List.concat
                    
                    materialsWithQuantities
                    |> List.groupBy fst
                    |> List.map
                       (fun (id, quantities) ->
                            id, List.sumBy
                                (fun x -> x |> snd|> fun (x: Quantity) -> x.Value) quantities)
                    |>> fun (id, quantity) -> (id.Value, Consume (Quantity.New quantity |> Result.get))
                    
                let materialIds = materialIdsWithConsumeCommands |>> fst
                let consumeCommands: List<AggregateCommand<Material, MaterialEvents>> =
                    materialIdsWithConsumeCommands
                    |>> (snd >> fun x -> x :> AggregateCommand<Material, MaterialEvents>)
                
                return!
                    CommandHandler.runInitAndNAggregateCommandsMd<Material, MaterialEvents, WorkOrder, string>
                        materialIds
                        eventStore
                        messageSenders
                        workOrder
                        ""
                        consumeCommands
            }
    
    member this.StartWorkingItem (workOrderId: WorkOrderId) (productId: ProductId) =
        WorkOrderCommands.Start productId
        |> runAggregateCommand<WorkOrder, WorkOrderEvents, string> workOrderId.Value eventStore messageSenders
    
    member this.FailWorkingItem (workOrderId: WorkOrderId) (productId: ProductId) (quantity: Quantity) =
        result
            {
                let! _, product =
                    productsViewer productId.Value
               
                let! materialsReadds =
                    product.Materials
                    |> List.map (fun (id, q) -> (id, Add q))
                    |> List.traverseResultM (fun (id, cmd) ->
                        preExecuteAggregateCommandMd<Material, MaterialEvents, string>
                            id.Value
                            eventStore
                            MessageSenders.NoSender
                            ""
                            cmd 
                        )
                    
                let! workOrderFail =
                    preExecuteAggregateCommandMd<WorkOrder, WorkOrderEvents, string>
                        workOrderId.Value
                        eventStore
                        MessageSenders.NoSender
                        ""
                        (WorkOrderCommands.Fail (productId, quantity))
            
                return!    
                    runPreExecutedAggregateCommands
                        (workOrderFail :: materialsReadds)
                        eventStore
                        MessageSenders.NoSender
            }
    
    member this.CompleteWorkingItem (workOrderId: WorkOrderId) (productId: ProductId) =
        WorkOrderCommands.Complete productId
        |> runAggregateCommand<WorkOrder, WorkOrderEvents, string> workOrderId.Value eventStore messageSenders 
            
    member this.GetWorkOrder (id: WorkOrderId) =
        workOrdersViewer id.Value |> Result.map snd
    