module Sharpino.Lib.Test.EventStoreTests
open System
open System.Threading.Tasks
open DotNetEnv
open Expecto
open Sharpino.Cache
open Sharpino.EventBroker
open Sharpino.Lib.Test.Models.SampleObject.Events
open Sharpino.MemoryStorage
open Sharpino.PgStorage
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Storage
open Sharpino.Lib.Test.Models.ContextObject.Events
open Sharpino.Lib.Test.Models.ContextObject.SampleContext
open Sharpino.Lib.Test.Models.SampleObject.SampleObject

open Sharpino.TestUtils

[<Tests>]
let tests =
    Env.Load() |> ignore

    let password = Environment.GetEnvironmentVariable("password")
        
    let connection =
        "Server=127.0.0.1;"+
        "Database=sharpino_tests;" +
        "User Id=safe;"+
        $"Password={password};"
    let memoryStorage = MemoryStorage()
    let pgEventStore = PgEventStore connection
    
    let pgReset () =
        pgEventStore.Reset SampleObject.Version SampleObject.StorageName
        pgEventStore.Reset SampleContext.Version SampleContext.StorageName
        pgEventStore.ResetAggregateStream SampleObject.Version SampleObject.StorageName
        AggregateCache3.Instance.Clear()
    let memReset () =
        memoryStorage.Reset SampleObject.Version SampleObject.StorageName
        AggregateCache3.Instance.Clear()
   
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
        multipleTestCase "async: create an initial instance, then store a new first low level event via AddAggregateEventsMdAsync - Ok" versions <| fun (eventStore, setUp)  ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
            Expect.isOk initialized "should be ok"
            // Act
            let lowLevelEvent = SampleObjectRenamed "new name async"
            let storeLowLevelEventTask = eventStore.AddAggregateEventsMdAsync(0, SampleObject.Version, SampleObject.StorageName, sampleObjectId, Metadata.Empty, [lowLevelEvent.Serialize])
            let storeLowLevelEvent = storeLowLevelEventTask.Result
            Expect.isOk storeLowLevelEvent "should be ok"
            // Assert
            AggregateCache3.Instance.Clear ()
            let retrieved = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId eventStore
            Expect.isOk retrieved "should be ok"
            let (eventId, okRetrieved) = retrieved.OkValue
            let retrievedWithCast = okRetrieved :?> SampleObject
            Expect.equal retrievedWithCast.Name "new name async" "should be equal"
            Expect.notEqual eventId 0 "should be non zero"

        multipleTestCase "async: mismatched expected event id should fail in AddAggregateEventsMdAsync - Error" versions <| fun (eventStore, setUp)  ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
            Expect.isOk initialized "should be ok"
            // Act: pass a non-zero expected id when there are no aggregate events yet
            let lowLevelEvent = SampleObjectRenamed "bad id async"
            let res = eventStore.AddAggregateEventsMdAsync(999, SampleObject.Version, SampleObject.StorageName, sampleObjectId, Metadata.Empty, [lowLevelEvent.Serialize]).Result
            // Assert
            Expect.isError res "should be error for wrong expected event id"

        multipleTestCase "async: GetAggregateEventsAsync returns empty list after init - Ok" versions <| fun (eventStore, setUp) ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
            Expect.isOk initialized "should be ok"
            // Act
            let retrievedEventsTask = eventStore.GetAggregateEventsAsync(SampleObject.Version, SampleObject.StorageName, sampleObjectId)
            let retrievedEvents = retrievedEventsTask.Result
            // Assert
            Expect.isOk retrievedEvents "should be ok"
            let events = retrievedEvents.OkValue |> List.map snd
            Expect.equal events.Length 0 "no events expected"

        multipleTestCase "async: AddAggregateEventsMdAsync twice, then GetAggregateEventsAsync returns both events in order - Ok" versions <| fun (eventStore, setUp) ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
            Expect.isOk initialized "should be ok"
            // Act
            let e1 = SampleObjectRenamed "n1"
            let r1 = eventStore.AddAggregateEventsMdAsync(0, SampleObject.Version, SampleObject.StorageName, sampleObjectId, Metadata.Empty, [e1.Serialize]).Result
            Expect.isOk r1 "first append should be ok"
            let lastId1 = (eventStore.TryGetLastAggregateEventId SampleObject.Version SampleObject.StorageName sampleObjectId).Value
            let e2 = SampleObjectRenamed "n2"
            let r2 = eventStore.AddAggregateEventsMdAsync(lastId1, SampleObject.Version, SampleObject.StorageName, sampleObjectId, Metadata.Empty, [e2.Serialize]).Result
            Expect.isOk r2 "second append should be ok"
            // Retrieve
            let retrievedEventsTask = eventStore.GetAggregateEventsAsync(SampleObject.Version, SampleObject.StorageName, sampleObjectId)
            let retrievedEvents = retrievedEventsTask.Result
            Expect.isOk retrievedEvents "should be ok"
            let events = retrievedEvents.OkValue
            Expect.equal events.Length 2 "two events expected"
            let ev1 = events.[0] |> snd |> SampleObjectEvents.Deserialize
            let ev2 = events.[1] |> snd |> SampleObjectEvents.Deserialize
            Expect.isOk ev1 "should be ok"
            Expect.isOk ev2 "should be ok"
            let ev1Value = ev1.OkValue
            let ev2Value = ev2.OkValue
            Expect.equal ev1Value (SampleObjectRenamed "n1") "first name"
            Expect.equal ev2Value (SampleObjectRenamed "n2") "second name"
            
            
        multipleTestCase "create an initial instance and retrieve it. There are no events so eventId is 0 - Ok" versions <| fun (eventStore, setUp)  ->
            setUp ()
            // Arrange
            let sampleObjectId = Guid.NewGuid()
            let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
            
            // Act
            let initialized =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
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
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
            Expect.isOk initialized "should be ok"
            
            // Act
            let lowLevelEvent = SampleObjectRenamed "new name"
            // let storeLowLevelEvent = eventStore.AddAggregateEvents 0 SampleObject.Version SampleObject.StorageName sampleObjectId [lowLevelEvent.Serialize]
            let storeLowLevelEvent = eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName sampleObjectId "" [lowLevelEvent.Serialize]
            Expect.isOk storeLowLevelEvent "should be ok"
            
            // Assert
            AggregateCache3.Instance.Clear ()
            let retrieved = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId eventStore
            Expect.isOk retrieved "should be ok"
            let (eventId, okRetrieved) = retrieved.OkValue
            let retrievedWithCast = okRetrieved :?> SampleObject
            Expect.equal retrievedWithCast.Name "new name" "should be equal"
            Expect.notEqual eventId 0 "should be non zero"
       
        multipleTestCase "async: GetEventsInATimeIntervalAsync returns events within time range - Ok" versions <| fun (eventStore, setUp) ->
            
            setUp ()
            // Arrange
           
            let musicTagEvent = SampleContextTagAdded Music 
           
            let musicTagStored = eventStore.AddEventsMd 0 SampleContext.Version SampleContext.StorageName "" [musicTagEvent.Serialize]
            Expect.isOk musicTagStored "should be ok"
            
            let eventsRetrieved = eventStore.GetEventsInATimeIntervalAsync (SampleContext.Version, SampleContext.StorageName, DateTime.MinValue, DateTime.MaxValue)
            let result = eventsRetrieved.Result
            Expect.isOk result "should be ok"
            let eventRetrieved = result.OkValue |> List.head |> snd
            let eventDeserialized = SampleContextEvents.Deserialize eventRetrieved
            Expect.isOk eventDeserialized "should be ok"
            let eventDeserializedValue = eventDeserialized.OkValue
            Expect.equal eventDeserializedValue musicTagEvent "should be equal"
        
        multipleTestCase "create two objects, store an event related to the first one, then store an event related to the second one. Retrieve the object with the new state  - Ok" versions <| fun (eventStore, setUp)  ->
            setUp ()
            
            // Arrange
            let sampleObjectId1 = Guid.NewGuid()
            let sampleObjectId2 = Guid.NewGuid()
            let sampleObject1 = SampleObject.MkSampleObject(sampleObjectId1, "sample object 1")
            let sampleObject2 = SampleObject.MkSampleObject(sampleObjectId2, "sample object 2")
            let initialized1 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject1
            Expect.isOk initialized1 "should be ok"
            let initialized2 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject2
            Expect.isOk initialized2 "should be ok"
            
            // Act
            let changeName1 = SampleObjectRenamed "new name 1"
            let storeChangeName1 = eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName sampleObjectId1 "" [changeName1.Serialize]
            Expect.isOk storeChangeName1 "should be ok"
            
            let changeName2 = SampleObjectRenamed "new name 2"
            let storeChangeName2 = eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName sampleObjectId2 "" [changeName2.Serialize]
            Expect.isOk storeChangeName2 "should be ok"
            
            // Assert
            AggregateCache3.Instance.Clear ()
            let retrieved1 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId1 eventStore
            Expect.isOk retrieved1 "should be ok"
            let (eventId1, okRetrieved1) = retrieved1.OkValue
            Expect.equal (okRetrieved1 |> unbox).Name "new name 1" "should be equal"
            Expect.notEqual eventId1 0 "should be non zero"
            
            let retrieved2 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId2 eventStore
            Expect.isOk retrieved2 "should be ok"
            let (eventId2, okRetrieved2) = retrieved2.OkValue
            Expect.equal (okRetrieved2 |> unbox).Name "new name 2" "should be equal"
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
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject1
            Expect.isOk initialized1 "should be ok"
            let initialized2 =
                runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject2
            Expect.isOk initialized2 "should be ok"
            
            let changeName1 = SampleObjectRenamed "new name 1"
            let storeChangeName1 = eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName sampleObjectId1 Metadata.Empty [changeName1.Serialize]
            Expect.isOk storeChangeName1 "should be ok"
            
            let changeName2 = SampleObjectRenamed "new name 2"
            let storeChangeName2 = eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName sampleObjectId2 Metadata.Empty [changeName2.Serialize]
            Expect.isOk storeChangeName2 "should be ok"
            
            // Act
            AggregateCache3.Instance.Clear ()
            let retrieved1 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId1 eventStore
            Expect.isOk retrieved1 "should be ok"
            let (eventId1, okRetrieved1) = retrieved1.OkValue
            Expect.equal (okRetrieved1 |> unbox).Name "new name 1" "should be equal"
            Expect.notEqual eventId1 0 "should be non zero"
            
            let retrieved2 = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleObjectId2 eventStore
            Expect.isOk retrieved2 "should be ok"
            let (eventId2, okRetrieved2) = retrieved2.OkValue
            Expect.equal (okRetrieved2 |> unbox).Name "new name 2" "should be equal"
            
            Expect.notEqual eventId2 0 "should be non zero"
            
            let changeName22 = SampleObjectRenamed "new name 22"
            let storeChangeName3 = eventStore.AddAggregateEventsMd eventId2 SampleObject.Version SampleObject.StorageName sampleObjectId2 Metadata.Empty [changeName22.Serialize]
            Expect.isOk storeChangeName3 "should be ok"
           
            let changeName11 = SampleObjectRenamed "new name 11"
            let storeChangeName4 = eventStore.AddAggregateEventsMd eventId1 SampleObject.Version SampleObject.StorageName sampleObjectId1 Metadata.Empty [changeName11.Serialize]
            Expect.isOk storeChangeName4 "should be ok"
            // will fail
    
        multipleTestCase "heavy adding aggregate events" versions <| fun (eventStore, setUp)  ->
            setUp ()
            let objects =
                [ for i in 1 .. 1000 do
                    let sampleObjectId = Guid.NewGuid()
                    let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
                    let initialized =
                        runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
                    Expect.isOk initialized "should be ok"
                    yield sampleObject ]
            let eventChanges =
                [ for i in 1 .. 1000 do
                    let changeName = SampleObjectRenamed (getRandomString())
                    let storeChange = eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName (objects.[i - 1].Id) Metadata.Empty [changeName.Serialize]
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
                        runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
                    Expect.isOk initialized "should be ok"
                    yield sampleObject ]
                
            System.Threading.Tasks.Parallel.For(0, objects.Length, fun i ->
                    let changeName = SampleObjectRenamed (getRandomString())
                    let storeChange =
                        eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName (objects.[i].Id) Metadata.Empty [changeName.Serialize]
                    Expect.isOk storeChange "should be ok")
            |> ignore
        
        // "on my machine" I can run up to 8 concurrent changes without timeout
        pmultipleTestCase "set initial state and then add events in parallel more times and then verify that only succesful changes changed the state accordingly - Ok" versions <| fun (eventStore, setUp)  ->
            setUp()
            let random = System.Random(System.DateTime.Now.Millisecond)
            let objects =
                [ for i in 1 .. 8 do
                    let sampleObjectId = Guid.NewGuid()
                    let sampleObject = SampleObject.MkSampleObject(sampleObjectId, "sample object")
                    let initialized =
                        runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject
                    Expect.isOk initialized "should be ok"
                    yield sampleObject ]
                
            System.Threading.Tasks.Parallel.For(0, objects.Length, fun i ->
                    System.Threading.Thread.Sleep(random.Next(100, 500))
                    let changeName = SampleObjectRenamed (getRandomString())
                    let storeChange =
                        eventStore.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName objects.[i].Id Metadata.Empty [changeName.Serialize]
                    Expect.isOk storeChange "should be ok")
            |> ignore
           
             
            let newNames = [ for _ in 1 .. 8 do yield getRandomString() ]
           
            let changed = System.Collections.Concurrent.ConcurrentDictionary<int, bool>()
            
            System.Threading.Tasks.Parallel.For(0, objects.Length, fun i ->
                    let changeName = SampleObjectRenamed newNames.[i]
                    System.Threading.Thread.Sleep(random.Next(100, 500))
                    let (eventId, okRetrieved) = getAggregateFreshState<SampleObject, SampleObjectEvents, string> objects.[i].Id eventStore |> Result.get 
                    let storeChange =
                        eventStore.AddAggregateEventsMd eventId SampleObject.Version SampleObject.StorageName objects.[i].Id Metadata.Empty [changeName.Serialize]
                        
                    (
                         if storeChange.IsOk then 
                            changed.AddOrUpdate (i, true, fun _ _ -> true) |> ignore
                         else
                            changed.AddOrUpdate (i, false, fun _ _ -> false) |> ignore
                    )
            )
            |> ignore
            
        multipleTestCase "async: GetMultipleAggregateEventsInATimeIntervalAsync returns events for multiple aggregates - Ok" versions <| fun (eventStore, setUp) ->
            setUp ()
            // Arrange - create two sample objects
            let sampleObjectId1 = Guid.NewGuid()
            let sampleObject1 = SampleObject.MkSampleObject(sampleObjectId1, "sample object 1")
            let sampleObjectId2 = Guid.NewGuid()
            let sampleObject2 = SampleObject.MkSampleObject(sampleObjectId2, "sample object 2")
            
            // Initialize both objects
            let initialized1 = runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject1
            let initialized2 = runInit<SampleObject, SampleObjectEvents, string> eventStore MessageSenders.NoSender sampleObject2
            
            Expect.isOk initialized1 "First object initialization should succeed"
            Expect.isOk initialized2 "Second object initialization should succeed"
            
            // Add events to both objects with a small delay to ensure different timestamps
            let event1 = SampleObjectRenamed "renamed object 1"
            let event2 = SampleObjectRenamed "renamed object 2"
            
            let storeEvent1 =
                eventStore.AddAggregateEventsMdAsync(0, SampleObject.Version, SampleObject.StorageName, sampleObjectId1, Metadata.Empty, [event1.Serialize])
                |> Async.AwaitTask
                |> Async.RunSynchronously
                
            let storeEvent2 =
                eventStore.AddAggregateEventsMdAsync(0, SampleObject.Version, SampleObject.StorageName, sampleObjectId2, Metadata.Empty, [event2.Serialize])
                |> Async.AwaitTask
                |> Async.RunSynchronously
            
            Expect.isOk storeEvent1 "First event storage should succeed"
            Expect.isOk storeEvent2 "Second event storage should succeed"
            
            // Get the timestamp just before and after our test events
            // note: here I have to use the Now and not the UtcNow (otherwise the test fails), but there is room to investigate about
            let now = System.DateTime.Now
            let beforeEvents = now.AddSeconds(-10)
            let afterEvents = now.AddSeconds(10)
            
            // Act - get events for both aggregates
            let result =
                eventStore.GetMultipleAggregateEventsInATimeIntervalAsync(SampleObject.Version, SampleObject.StorageName, [sampleObjectId1; sampleObjectId2], beforeEvents, afterEvents)
                |> Async.AwaitTask
                |> Async.RunSynchronously
            
            // Assert
            Expect.isOk result "Should successfully retrieve events"
            let events = result.OkValue
            
            // Verify we got both events
            Expect.equal events.Length 2 "Should return exactly two events"
            
            // Verify the events contain the expected data
            let event1Found = events |> List.exists (fun (_, id, json) -> id = sampleObjectId1 && json = event1.Serialize)
            let event2Found = events |> List.exists (fun (_, id, json) -> id = sampleObjectId2 && json = event2.Serialize)
            
            Expect.isTrue event1Found "Should find first event"
            Expect.isTrue event2Found "Should find second event"
    ]
    |> testSequenced
        
        
