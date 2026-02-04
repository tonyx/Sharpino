module Tests

open System.Threading
open System.Threading.Tasks
open DotNetEnv

open Expecto
open System
open FSharpPlus
open Sharpino
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Template.Models
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Template
open Sharpino.Template.Manager
open Sharpino.Template.Commons
open FsToolkit.ErrorHandling
open sharpino.Template.Models

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let userId = Environment.GetEnvironmentVariable("userId")
let port = Environment.GetEnvironmentVariable("port")
let database = Environment.GetEnvironmentVariable("database")
let connection =
    "Host=127.0.0.1;" +
    $"Port={port};" +
    $"Database={database};" +
    $"User Id={userId};" +
    $"Password={password}"

let pgEventStore = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Material.Version Material.StorageName |> ignore
    pgEventStore.ResetAggregateStream Material.Version Material.StorageName |> ignore
    pgEventStore.Reset Product.Version Product.StorageName |> ignore
    pgEventStore.ResetAggregateStream Product.Version Product.StorageName |> ignore
    pgEventStore.Reset WorkOrder.Version WorkOrder.StorageName |> ignore
    pgEventStore.ResetAggregateStream WorkOrder.Version WorkOrder.StorageName |> ignore
    
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()

let materialsViewer =  getAggregateStorageFreshStateViewer<Material, MaterialEvents, string> pgEventStore
let productsViewer = getAggregateStorageFreshStateViewer<Product, ProductEvents, string> pgEventStore
let workOrdersViewer =  getAggregateStorageFreshStateViewer<WorkOrder, WorkOrderEvents, string> pgEventStore
let messagesReceiver = MailBoxProcessors.Processors.Instance.createProcessor ()

    
let emptyMessageSender =
    fun _ ->
        ValueTask.CompletedTask

let workOrderFailReactor =
    (fun x ->
        let deserialized = AggregateMessage<WorkOrder, WorkOrderEvents>.Deserialize x
        
        let productIdsWithQuantitiesOfFailedWorkingItems =
            match deserialized with
            | Ok workOrder ->
                match workOrder.Message with
                | MessageType.Events eventMessage ->
                    let events = eventMessage.Events
                    let eventsFailed =
                        events |> List.filter (fun x -> x.IsFailed)
                    let failedProductIdsWithQuantities =
                        eventsFailed
                        |>> fun x ->
                            match x with
                            | Failed (productid, quantity) -> (productid.Value, quantity.Value)
                            | _ ->  (Guid.Empty, 0)
                    failedProductIdsWithQuantities        
                | _ ->
                    []
            | _ ->
                []
                
        let productsAndQuantitiesInvolved =
            productIdsWithQuantitiesOfFailedWorkingItems
            |>> (fun (x, y) -> (productsViewer x, y))
            |> List.filter (fun (x, _) -> x.IsOk)
            |>> fun (x, y) -> x.OkValue |> snd, y
        
        let materialsWithQuantitiesInvolved =
            productsAndQuantitiesInvolved
            |>> fun (x, y) -> (x.Materials |> List.map (fun (m, q) -> (m, q.Value * y)))
            |> List.concat
           
        let materialsReAddCommands =
            materialsWithQuantitiesInvolved
            |>> (fun (x, y) -> (x, MaterialCommands.Add (Quantity.New y |> Result.get)))
           
        let aggregateIds =
            materialsReAddCommands
            |>> (fun (x, _) -> x.Value)
       
        let readdCommands =
            materialsReAddCommands
            |>> (fun (_, y) -> y :> AggregateCommand<Material, MaterialEvents>)
         
        let executeCommandToCompensateFailures =
            CommandHandler.runNAggregateCommands<Material, MaterialEvents, string>
                aggregateIds
                pgEventStore
                MessageSenders.NoSender
                readdCommands
        ValueTask.CompletedTask)

let messageSenders =
    MessageSender (fun x ->
        if (x = (WorkOrder.Version + WorkOrder.StorageName)) then
            workOrderFailReactor |> Ok
        else
            emptyMessageSender |> Ok
    )

[<Tests>]
let tests =
    testList "material tests" [
        let materialManager = MaterialManager (messageSenders, pgEventStore, materialsViewer, productsViewer, workOrdersViewer)
        testCase "add new materials" <| fun _ ->
            setUp ()
            let pistacchio = Material.New "Pistacchio" (Quantity.New 10 |> Result.get)
            let cream = Material.New "Cream" (Quantity.New 10 |> Result.get)
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            Expect.isOk addPistacchio "error in adding material"
            Expect.isOk addCream "error in adding material"
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId
            Expect.isOk retrievePistacchio "error in retrieving material"
            Expect.isOk retrieveCream "error in retrieving material"
            Expect.equal retrievePistacchio.OkValue.Availability.Value 10 "should be 10"
            Expect.equal retrieveCream.OkValue.Availability.Value 10 "should be 10"
       
        testCase "add a material and add a product based on that material" <| fun _ ->
            setUp ()
            let pistacchio = Material.New "Pistacchio" (Quantity.New 1 |> Result.get)
            let addPistacchio = materialManager.AddMaterial pistacchio
            Expect.isOk addPistacchio "error in adding material"
            let pistacchioIceCream =
                Product.New "Pistacchio Ice Cream" [pistacchio.MaterialId, (Quantity.New 1).OkValue]
            let addPistacchioIceCream = materialManager.AddProduct pistacchioIceCream
            Expect.isOk addPistacchioIceCream "error in adding product"
            
        testCase "cannot add a product based on an unexisting material"  <| fun _ ->
            setUp ()
            let pistacchioIceCream =
                Product.New "Pistacchio Ice Cream" [MaterialId.New, (Quantity.New 1).OkValue]
            let addPistacchioIceCream = materialManager.AddProduct pistacchioIceCream
            Expect.isError addPistacchioIceCream "error in adding product"
        
        testCase "add a material and a product based on that material, then add a workingItem of that single product, retrieve that work order"  <| fun _ ->
            setUp ()
            let pistacchio = Material.New "Pistacchio" (Quantity.New 1 |> Result.get)
            let addPistacchio = materialManager.AddMaterial pistacchio
            Expect.isOk addPistacchio "error in adding material"
            let pistacchioIceCream =
                Product.New "Pistacchio Ice Cream" [pistacchio.MaterialId, (Quantity.New 1).OkValue]
            let addPistacchioIceCream = materialManager.AddProduct pistacchioIceCream
            Expect.isOk addPistacchioIceCream "error in adding product"
            // when
            let workOrder =
                WorkOrder.New "Work Order" [WorkingItem.New pistacchioIceCream.ProductId (Quantity.New 1).OkValue]
                |> Result.get
            let addWorkOrder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkOrder "error in adding work order"
            // then
            let workOrder = materialManager.GetWorkOrder workOrder.WorkOrderId
            Expect.isOk workOrder "error in getting work order"
        
        testCase "a material is added, a product on that material is added, a work order is added, then the material results consumed" <| fun _ ->
            setUp ()
            let pistacchio = Material.New "Pistacchio" (Quantity.New 1 |> Result.get)
            let addPistacchio = materialManager.AddMaterial pistacchio
            Expect.isOk addPistacchio "error in adding material"
            let pistacchioIceCream =
                Product.New "Pistacchio Ice Cream" [pistacchio.MaterialId, (Quantity.New 1).OkValue]
            let addPistacchioIceCream = materialManager.AddProduct pistacchioIceCream
            Expect.isOk addPistacchioIceCream "error in adding product"
            // when
            let workOrder =
                WorkOrder.New "Work Order" [WorkingItem.New pistacchioIceCream.ProductId (Quantity.New 1).OkValue]
                |> Result.get
            let addWorkOrder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkOrder "error in adding work order"
            
            // then
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            Expect.isOk retrievePistacchio "error in retrieving material"
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            
            let retrieveWorkOrder = materialManager.GetWorkOrder workOrder.WorkOrderId
            Expect.isOk retrieveWorkOrder "error in retrieving work order"
          
        testCase "add two materials, a product on those materials and then a workOrder, verify both the materials are consumed" <| fun _ ->
            setUp ()
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 1).OkValue
            let cream =
                Material.New "Cream" (Quantity.New 1).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            
            let twoFlavoursIceCream =
                Product.New "Pistacchio cream icecream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue); (cream.
                                                                                                                 MaterialId, (Quantity.New 1).OkValue)]
                
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"
            
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.ProductId (Quantity.New 1).OkValue]
                |> Result.get
            
            let addWorkOrder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkOrder "error in adding work order"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId

            Expect.isOk retrievePistacchio "error in retrieving material"
            Expect.isOk retrieveCream "error in retrieving material"
            
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 0 "should be 0"
            
        testCase "add two materials, a product on those materials and then a workOrder of two items, verify both the materials are consumed accordingly" <| fun _ ->
            setUp ()
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 2).OkValue
            let cream =
                Material.New "Cream" (Quantity.New 2).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            
            let twoFlavoursIceCream =
                Product.New "Pistacchio cream icecream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue); (cream.
                                                                                                                 MaterialId, (Quantity.New 1).OkValue)]
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"    
                
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.ProductId (Quantity.New 2).OkValue]
                |> Result.get
            
            let addWorkorder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkorder "should be ok"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId

            Expect.isOk retrievePistacchio "should be ok"
            Expect.isOk retrieveCream "should be ok"
            
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 0 "should be 0"
            
        testCase "when the material is partially insufficient then the work order is not added and any quantity stays the same" <| fun _ ->
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 1).OkValue
            let cream =
                Material.New "Cream" (Quantity.New 2).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            
            let twoFlavoursIceCream =
                Product.New "Pistacchio cream icecream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue); (cream.
                                                                                                                 MaterialId, (Quantity.New 1).OkValue)]
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"
            
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.ProductId (Quantity.New 2).OkValue]
                |> Result.get
            
            let addWorkorder = materialManager.AddWorkOrder workOrder
            Expect.isError addWorkorder "should be ok"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId

            Expect.equal retrievePistacchio.OkValue.Availability.Value 1 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 2 "should be 0"
            
        testCase "When no workingItem has started yet, then the workOrder state is initialized" <| fun _ ->
            setUp ()
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 2).OkValue
            let cream =
                Material.New "Cream" (Quantity.New 2).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            
            let twoFlavoursIceCream =
                Product.New "Pistacchio cream icecream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue); (cream.
                                                                                                                 MaterialId, (Quantity.New 1).OkValue)]
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"    
                
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.ProductId (Quantity.New 2).OkValue]
                |> Result.get
            
            let addWorkorder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkorder "should be ok"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId

            Expect.isOk retrievePistacchio "should be ok"
            Expect.isOk retrieveCream "should be ok"
            
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 0 "should be 0"
            
            let retrievedWorkOrder =
                materialManager.GetWorkOrder workOrder.WorkOrderId

            Expect.isOk retrievedWorkOrder "should be ok"
            Expect.equal (retrievedWorkOrder.OkValue).WorkOrderState  WorkOrderState.Initialized "should be equal"
            
        testCase "when one workingItem has started, then the workOrder state is InProgress" <| fun _ ->
            setUp ()
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 2).OkValue
            let cream =
                Material.New "Cream" (Quantity.New 2).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            
            let twoFlavoursIceCream =
                Product.New "Pistacchio cream icecream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue); (cream.
                                                                                                                 MaterialId, (Quantity.New 1).OkValue)]
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"    
                
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.ProductId (Quantity.New 2).OkValue]
                |> Result.get
            
            let addWorkorder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkorder "should be ok"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId

            Expect.isOk retrievePistacchio "should be ok"
            Expect.isOk retrieveCream "should be ok"
            
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 0 "should be 0"
            
            let startWorkingItem = materialManager.StartWorkingItem workOrder.WorkOrderId twoFlavoursIceCream.ProductId

            let retrievedWorkOrder =
                materialManager.GetWorkOrder workOrder.WorkOrderId

            Expect.isOk retrievedWorkOrder "should be ok"
            Expect.equal (retrievedWorkOrder.OkValue).WorkOrderState  WorkOrderState.InProgress "should be equal"
            
        testCase "if all workingitem are started and then completed, then the whole order is fullyCompleted" <| fun _ ->
            setUp ()
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 2).OkValue
            let cream =
                Material.New "Cream" (Quantity.New 2).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            
            let twoFlavoursIceCream =
                Product.New "Pistacchio cream icecream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue); (cream.
                                                                                                                 MaterialId, (Quantity.New 1).OkValue)]
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"    
                
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.ProductId (Quantity.New 2).OkValue]
                |> Result.get
            
            let addWorkorder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkorder "should be ok"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            let retrieveCream = materialManager.GetMaterial cream.MaterialId

            Expect.isOk retrievePistacchio "should be ok"
            Expect.isOk retrieveCream "should be ok"
            
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 0 "should be 0"
            
            let startWorkingItem1 = materialManager.StartWorkingItem workOrder.WorkOrderId twoFlavoursIceCream.ProductId
            let completeWorkingItem1 = materialManager.CompleteWorkingItem workOrder.WorkOrderId twoFlavoursIceCream.ProductId

            let retrievedWorkOrder =
                materialManager.GetWorkOrder workOrder.WorkOrderId

            Expect.isOk retrievedWorkOrder "should be ok"
            Expect.equal (retrievedWorkOrder.OkValue).WorkOrderState  WorkOrderState.FullyCompleted "should be equal"
            
        testCase "a workOrder with a single workingitem starts and then it fails, the related material quantities are restored" <| fun _ ->
            setUp ()
            let pistacchio =
                Material.New "Pistacchio" (Quantity.New 1).OkValue
            let addPistacchio = materialManager.AddMaterial pistacchio
            let iceCream = Product.New "Ice Cream" [(pistacchio.MaterialId, (Quantity.New 1).OkValue)]
            let addIceCream = materialManager.AddProduct iceCream
            
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New iceCream.ProductId (Quantity.New 1).OkValue]
                |> Result.get
            let addWorkorder = materialManager.AddWorkOrder workOrder
            let startWorkingItem1 = materialManager.StartWorkingItem workOrder.WorkOrderId iceCream.ProductId
            let retrievePistacchio = materialManager.GetMaterial pistacchio.MaterialId
            Expect.isOk retrievePistacchio "should be ok"
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            
            let failWorkingItem1 = materialManager.FailWorkingItem workOrder.WorkOrderId iceCream.ProductId (Quantity.New 1).OkValue
            Async.Sleep 1000 |> Async.RunSynchronously
            Expect.isOk failWorkingItem1 "should be ok"
            let retrievedWorkOrder = materialManager.GetWorkOrder workOrder.WorkOrderId |> Result.get
            Expect.isTrue retrievedWorkOrder.WorkOrderState.IsSomeFailed "should be true"
            let (SomeFailed productsQuantities) = retrievedWorkOrder.WorkOrderState
            Expect.equal productsQuantities [iceCream.ProductId, (Quantity.New 1).OkValue] "should be equal"
            
            let material = materialManager.GetMaterial pistacchio.MaterialId
            Expect.isOk material "should be ok"
            Expect.equal material.OkValue.Availability.Value 1 "should be 1"
             
    ] 
    |> testSequenced
