module Tests
open DotNetEnv

open Expecto
open System
open Sharpino
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Template.Models
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Template
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

[<Tests>]
let tests =
    testList "material tests" [
        let materialManager = MaterialManager (NoSender, pgEventStore, materialsViewer, productsViewer, workOrdersViewer)
        testCase "add new materials" <| fun _ ->
            setUp ()
            let pistacchio = Material.New "Pistacchio" (Quantity.New 10 |> Result.get)
            let cream = Material.New "Cream" (Quantity.New 10 |> Result.get)
            let addPistacchio = materialManager.AddMaterial pistacchio
            let addCream = materialManager.AddMaterial cream
            Expect.isOk addPistacchio "error in adding material"
            Expect.isOk addCream "error in adding material"
            let retrievePistacchio = materialManager.GetMaterial pistacchio.Id
            let retrieveCream = materialManager.GetMaterial cream.Id
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
                Product.New "Pistacchio Ice Cream" [pistacchio.Id, (Quantity.New 1).OkValue]
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
                Product.New "Pistacchio Ice Cream" [pistacchio.Id, (Quantity.New 1).OkValue]
            let addPistacchioIceCream = materialManager.AddProduct pistacchioIceCream
            Expect.isOk addPistacchioIceCream "error in adding product"
            // when
            let workOrder =
                WorkOrder.New "Work Order" [WorkingItem.New pistacchioIceCream.Id (Quantity.New 1).OkValue]
                |> Result.get
            let addWorkOrder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkOrder "error in adding work order"
            // then
            let workOrder = materialManager.GetWorkOrder workOrder.Id
            Expect.isOk workOrder "error in getting work order"
        
        testCase "a material is added, a product on that material is added, a work order is added, then the material results consumed" <| fun _ ->
            setUp ()
            let pistacchio = Material.New "Pistacchio" (Quantity.New 1 |> Result.get)
            let addPistacchio = materialManager.AddMaterial pistacchio
            Expect.isOk addPistacchio "error in adding material"
            let pistacchioIceCream =
                Product.New "Pistacchio Ice Cream" [pistacchio.Id, (Quantity.New 1).OkValue]
            let addPistacchioIceCream = materialManager.AddProduct pistacchioIceCream
            Expect.isOk addPistacchioIceCream "error in adding product"
            // when
            let workOrder =
                WorkOrder.New "Work Order" [WorkingItem.New pistacchioIceCream.Id (Quantity.New 1).OkValue]
                |> Result.get
            let addWorkOrder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkOrder "error in adding work order"
            
            // then
            let retrievePistacchio = materialManager.GetMaterial pistacchio.Id
            Expect.isOk retrievePistacchio "error in retrieving material"
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            
            let retrieveWorkOrder = materialManager.GetWorkOrder workOrder.Id
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
                Product.New "Pistacchio cream icecream" [(pistacchio.Id, (Quantity.New 1).OkValue); (cream.Id, (Quantity.New 1).OkValue)]
                
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"
            
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.Id (Quantity.New 1).OkValue]
                |> Result.get
            
            let addWorkOrder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkOrder "error in adding work order"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.Id
            let retrieveCream = materialManager.GetMaterial cream.Id
            
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
                Product.New "Pistacchio cream icecream" [(pistacchio.Id, (Quantity.New 1).OkValue); (cream.Id, (Quantity.New 1).OkValue)]
            let addTwoFlavoursIceCream = materialManager.AddProduct twoFlavoursIceCream
            Expect.isOk addTwoFlavoursIceCream "error in adding product"    
                
            let workOrder =
                WorkOrder.New "workOrder" [WorkingItem.New twoFlavoursIceCream.Id (Quantity.New 2).OkValue]
                |> Result.get
            
            let addWorkorder = materialManager.AddWorkOrder workOrder
            Expect.isOk addWorkorder "should be ok"
            
            let retrievePistacchio = materialManager.GetMaterial pistacchio.Id
            let retrieveCream = materialManager.GetMaterial cream.Id
            
            Expect.isOk retrievePistacchio "should be ok"
            Expect.isOk retrieveCream "should be ok"
            
            Expect.equal retrievePistacchio.OkValue.Availability.Value 0 "should be 0"
            Expect.equal retrieveCream.OkValue.Availability.Value 0 "should be 0"
            
            
    ] 
    |> testSequenced
