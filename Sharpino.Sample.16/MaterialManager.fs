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
                let productIds = workOrder.WorkingItems |> List.map (fun x -> x.ProductId)
                let! products =
                    productIds
                    |> List.traverseResultM (fun id -> productsViewer id.Value |> Result.map snd)
                    
                let materialsWithQuantities =
                    products |> List.map _.Materials
                    |> List.concat
               
                let materialIdsWithConsumingCommands =
                    materialsWithQuantities
                    |> List.groupBy fst
                    |> List.map
                           (fun (id, quantities) ->
                                (id, List.sumBy
                                         (fun x -> x |> snd|> fun (x: Quantity) -> x.Value) quantities))
                    |>> (fun (id, quantity) -> (id, Quantity.New quantity |> Result.get))
                    |>> (fun (id, quantity) -> (id.Value, Consume quantity))
                    
                let materialIds = materialIdsWithConsumingCommands |> List.map fst
                let consumingCommands: List<AggregateCommand<Material, MaterialEvents>> =
                    materialIdsWithConsumingCommands
                    |>> (snd >> fun x -> x :> AggregateCommand<Material, MaterialEvents>)
                
                let! result =
                    CommandHandler.runInitAndNAggregateCommandsMd<Material, MaterialEvents, WorkOrder, string>
                        materialIds
                        eventStore
                        messageSenders
                        workOrder
                        ""
                        consumingCommands
                return result        
            }
            
    member this.GetWorkOrder (id: WorkOrderId) =
        result
            {
                let! _, result = workOrdersViewer id.Value
                return result
            }
    