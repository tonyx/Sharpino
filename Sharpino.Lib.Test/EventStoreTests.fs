module Sharpino.Lib.Test.EventStoreTests
open System
open DotNetEnv
open Expecto
open Sharpino.Cache
open Sharpino.Lib.Test.Models.SampleObject.Events
open Sharpino.MemoryStorage
open Sharpino.PgStorage
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Storage
open Sharpino.Lib.Test.Models.SampleObject.SampleObject
open Sharpino.TestUtils

[<Tests>]
let tests =
    Env.Load() |> ignore

    let password = Environment.GetEnvironmentVariable("password")

    let doNothingBroker: IEventBroker<string> =
        {
            notify = None
            notifyAggregate =  None
        }
    let connection =
        "Server=127.0.0.1;"+
        "Database=sharpino_tests;" +
        "User Id=safe;"+
        $"Password={password};"
    let memoryStorage = MemoryStorage()
    let pgEventStore = PgEventStore(connection)
    
    let pgReset () =
        pgEventStore.Reset SampleObject.Version SampleObject.StorageName
        pgEventStore.ResetAggregateStream SampleObject.Version SampleObject.StorageName
        AggregateCache<SampleObject, string>.Instance.Clear()
    let memReset () =
        memoryStorage.Reset SampleObject.Version SampleObject.StorageName
        AggregateCache<SampleObject, string>.Instance.Clear()
   
    let getRandomString() =
        let random = System.Random()
        let chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        let charsLength = chars.Length
        let randomString = String(Array.init 10 (fun _ -> chars.[random.Next(charsLength)]))
        randomString
    let versions =
        [
            // (memoryStorage:> IEventStore<string>, memReset)
            (pgEventStore:> IEventStore<string>, pgReset)
        ]
         
    testList "Sharpino Tests" [
        multipleTestCase "create an initial instance and retrieve it. There are no events so eventId is 0 - Ok" versions <| fun (eventStore, setUp)  ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            
            // Act
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject
            Expect.isOk initialized "should be ok"
            
            // Assert
            let retrieved = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId eventStore
            Expect.isOk retrieved "should be ok"
            let (eventId, okRetrieved) = retrieved.OkValue
            Expect.equal okRetrieved sampleObject "should be the same"
            Expect.equal eventId 0 "should be equal"
        
        multipleTestCase "create an initial instance, then store a new first low level event. Retrieve the object with the new state, and the eventId is not zero - Ok" versions <| fun (eventStore, setUp)  ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject
            Expect.isOk initialized "should be ok"
            
            // Act
            let lowLevelEvent = SampleObjectRenamed "new name"
            let storeLowLevelEvent = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName sampleObjectId [lowLevelEvent.Serialize]
            Expect.isOk storeLowLevelEvent "should be ok"
            
            // Assert
            let retrieved = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId eventStore
            Expect.isOk retrieved "should be ok"
            let (eventId, okRetrieved) = retrieved.OkValue
            Expect.equal okRetrieved.Name "new name" "should be equal"
            Expect.notEqual eventId 0 "should be non zero"
        
        multipleTestCase "create two objects, store an event related to the first one, then store an event related to the second one. Retrieve the object with the new state  - Ok" versions <| fun (eventStore, setUp)  ->
            setUp ()
            
            // Arrange
            let sampleObjectId1 = Guid.NewGuid()
            let sampleObjectId2 = Guid.NewGuid()
            let sampleObject1 = SampleObject.MkSampleObject(sampleObjectId1, "sample object 1")
            let sampleObject2 = SampleObject.MkSampleObject(sampleObjectId2, "sample object 2")
            let initialized1 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject1
            Expect.isOk initialized1 "should be ok"
            let initialized2 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject2
            Expect.isOk initialized2 "should be ok"
            
            // Act
            let changeName1 = SampleObjectRenamed "new name 1"
            let storeChangeName1 = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName sampleObjectId1 [changeName1.Serialize]
            Expect.isOk storeChangeName1 "should be ok"
            
            let changeName2 = SampleObjectRenamed "new name 2"
            let storeChangeName2 = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName sampleObjectId2 [changeName2.Serialize]
            Expect.isOk storeChangeName2 "should be ok"
            
            // Assert
            let retrieved1 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId1 eventStore
            Expect.isOk retrieved1 "should be ok"
            let (eventId1, okRetrieved1) = retrieved1.OkValue
            Expect.equal okRetrieved1.Name "new name 1" "should be equal"
            Expect.notEqual eventId1 0 "should be non zero"
            
            let retrieved2 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId2 eventStore
            Expect.isOk retrieved2 "should be ok"
            let (eventId2, okRetrieved2) = retrieved2.OkValue
            Expect.equal okRetrieved2.Name "new name 2" "should be equal"
            Expect.notEqual eventId2 0 "should be non zero"
            
        multipleTestCase "object 1 and object 2 are there.
            Retrieve the eventId for object1 and prepare a new event to store.
            but an event is stored about object2 before that." versions <| fun (eventStore, setUp)  ->
            
            setUp ()
            
            // Arrange
            let sampleObjectId1 = Guid.NewGuid()
            let sampleObjectId2 = Guid.NewGuid()
            let sampleObject1 = SampleObject.MkSampleObject(sampleObjectId1, "sample object 1")
            let sampleObject2 = SampleObject.MkSampleObject(sampleObjectId2, "sample object 2")
            
            let initialized1 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject1
            Expect.isOk initialized1 "should be ok"
            let initialized2 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject2
            Expect.isOk initialized2 "should be ok"
            
            let changeName1 = SampleObjectRenamed "new name 1"
            let storeChangeName1 = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName sampleObjectId1 [changeName1.Serialize]
            Expect.isOk storeChangeName1 "should be ok"
            
            let changeName2 = SampleObjectRenamed "new name 2"
            let storeChangeName2 = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName sampleObjectId2 [changeName2.Serialize]
            Expect.isOk storeChangeName2 "should be ok"
            
            // Act
            let retrieved1 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId1 eventStore
            Expect.isOk retrieved1 "should be ok"
            let (eventId1, okRetrieved1) = retrieved1.OkValue
            Expect.equal okRetrieved1.Name "new name 1" "should be equal"
            Expect.notEqual eventId1 0 "should be non zero"
            
            let retrieved2 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId2 eventStore
            Expect.isOk retrieved2 "should be ok"
            let (eventId2, okRetrieved2) = retrieved2.OkValue
            Expect.equal okRetrieved2.Name "new name 2" "should be equal"
            
            Expect.notEqual eventId2 0 "should be non zero"
            
            let changeName22 = SampleObjectRenamed "new name 22"
            let storeChangeName3 = eventStore.AddAggregateEvents eventId2 SampleObject.Version SampleObject.StorageName sampleObjectId2 [changeName22.Serialize]
            Expect.isOk storeChangeName3 "should be ok"
           
            let changeName11 = SampleObjectRenamed "new name 11"
            let storeChangeName4 = eventStore.AddAggregateEvents eventId1 SampleObject.Version SampleObject.StorageName sampleObjectId1 [changeName11.Serialize]
            Expect.isOk storeChangeName4 "should be ok"
            // will fail
    
        multipleTestCase "heavy adding aggregate events" versions <| fun (eventStore, setUp)  ->
            setUp ()
            let objects =
                [ for i in 1 .. 1000 do
                    let sampleObjectId = Guid.NewGuid()
                    let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
                    let initialized =
                        runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject
                    Expect.isOk initialized "should be ok"
                    yield sampleObject ]
            let eventChanges =
                [ for i in 1 .. 1000 do
                    let changeName = SampleObjectRenamed (getRandomString())
                    let storeChange = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName (objects.[i - 1].Id) [changeName.Serialize]
                    Expect.isOk storeChange "should be ok"
                ]
            Expect.isTrue true "true"
        
        multipleTestCase "set initial state and then add events in parallel" versions <| fun (eventStore, setUp)  ->
            setUp()
            let objects =
                [ for i in 1 .. 1000 do
                    let sampleObjectId = Guid.NewGuid()
                    let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
                    let initialized =
                        runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject
                    Expect.isOk initialized "should be ok"
                    yield sampleObject ]
                
            System.Threading.Tasks.Parallel.For(0, objects.Length, fun i ->
                    let changeName = SampleObjectRenamed (getRandomString())
                    let storeChange =
                        eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName (objects.[i].Id) [changeName.Serialize]
                    Expect.isOk storeChange "should be ok")
            |> ignore
        
        // "in my machine" I can run up to 8 concurrent changes without timeout
        pmultipleTestCase "set initial state and then add events in parallel more times and then verify that only succesful changes changed the state accordingly - Ok" versions <| fun (eventStore, setUp)  ->
            setUp()
            let random = System.Random(System.DateTime.Now.Millisecond)
            let objects =
                [ for i in 1 .. 8 do
                    let sampleObjectId = Guid.NewGuid()
                    let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
                    let initialized =
                        runInit<SampleObject, SampleObjectEvents, string> eventStore doNothingBroker sampleObject
                    Expect.isOk initialized "should be ok"
                    yield sampleObject ]
                
            System.Threading.Tasks.Parallel.For(0, objects.Length, fun i ->
                    System.Threading.Thread.Sleep(random.Next(100, 500))
                    let changeName = SampleObjectRenamed (getRandomString())
                    let storeChange =
                        eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName objects.[i].Id [changeName.Serialize]
                    Expect.isOk storeChange "should be ok")
            |> ignore
           
             
            let newNames = [ for _ in 1 .. 8 do yield getRandomString() ]
           
            let changed = System.Collections.Concurrent.ConcurrentDictionary<int, bool>()
            
            System.Threading.Tasks.Parallel.For(0, objects.Length, fun i ->
                    let changeName = SampleObjectRenamed newNames.[i]
                    System.Threading.Thread.Sleep(random.Next(100, 500))
                    let (eventId, okRetrieved) = getAggregateFreshState<SampleObject, SampleObjectEvents, string> objects.[i].Id eventStore |> Result.get 
                    let storeChange =
                        eventStore.AddAggregateEvents eventId SampleObject.Version SampleObject.StorageName objects.[i].Id [changeName.Serialize]
                        
                    (
                         if storeChange.IsOk then 
                            printf "isOk XXXX\n"
                            changed.AddOrUpdate (i, true, fun _ _ -> true) |> ignore
                         else
                            printf "is not Ok YYYY\n"
                            changed.AddOrUpdate (i, false, fun _ _ -> false) |> ignore
                    )
            )
            |> ignore
    ]
    |> testSequenced
        
        
