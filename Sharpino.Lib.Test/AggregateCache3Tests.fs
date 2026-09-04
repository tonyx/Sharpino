module Sharpino.Lib.Test.AggregateCache3Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Options
open Sharpino
open Sharpino.Cache
open Sharpino.Storage
open Sharpino.Lib.Test.Models.SampleObject.SampleObject

let createMemoryDistributedCache () =
    let opts = Options.Create(MemoryDistributedCacheOptions())
    new MemoryDistributedCache(opts) :> IDistributedCache

[<Tests>]
let tests =
    testSequenced <| testList "AggregateCache3 L2 and Multi-Tier Cache Tests" [
        testCase "Memoize2 populates L1 and writes serializable entry to L2 distributed cache" <| fun () ->
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let sampleId = Guid.NewGuid()
            let sample = SampleObject.MkSampleObject(sampleId, "FirstObject")
            let eventId = 42

            // Act: memoize state
            AggregateCache3.Instance.Memoize2 (eventId, sample) sampleId

            // Assert L1 hit
            let lastEvId = AggregateCache3.Instance.LastEventId sampleId
            Expect.equal lastEvId (Some 42) "LastEventId should match 42"

            let stateRes = AggregateCache3.Instance.GetState sampleId
            Expect.isOk stateRes "GetState should return Ok"
            let unboxed = stateRes |> Result.get :?> SampleObject
            Expect.equal unboxed.Name "FirstObject" "State should match"

            // Assert L2 distributed cache contains the entry
            let entryOpt = AggregateCache3.Instance.GetEntry sampleId
            Expect.isSome entryOpt "Entry should be retrieved via GetEntry"
            let entry = entryOpt.Value
            Expect.equal entry.EventId 42 "Entry EventId should match"
            Expect.stringContains entry.StateJson "FirstObject" "StateJson should contain FirstObject"

        testCase "ClearL1 removes in-memory state while L2 distributed cache allows rehydration" <| fun () ->
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let sampleId = Guid.NewGuid()
            let sample = SampleObject.MkSampleObject(sampleId, "PersistentObject")
            let eventId = 100

            // Act 1: Populate cache
            AggregateCache3.Instance.Memoize2 (eventId, sample) sampleId

            // Act 2: Clear L1 memory only (simulate memory pressure or app restart with persistent L2)
            AggregateCache3.Instance.ClearL1()

            // Act 3: Read from cache — should hit L2 and rehydrate
            let rehydratedEvId = AggregateCache3.Instance.LastEventId sampleId
            Expect.equal rehydratedEvId (Some 100) "Rehydrated LastEventId from L2 should match 100"

            let rehydratedStateRes = AggregateCache3.Instance.GetState sampleId
            Expect.isOk rehydratedStateRes "GetState should succeed by fetching from L2"
            let rehydratedSample = rehydratedStateRes |> Result.get :?> SampleObject
            Expect.equal rehydratedSample.Name "PersistentObject" "Rehydrated sample name should match"

        testCaseAsync "StateView.getLastAggregateSnapshotAsync uses L1/L2 cache before querying event store" <| async {
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let sampleId = Guid.NewGuid()
            let sample = SampleObject.MkSampleObject(sampleId, "AcceleratedSnapshot")
            let eventId = 77

            // Populate L2
            AggregateCache3.Instance.Memoize2 (eventId, sample) sampleId
            // Flush L1 to prove L2 read-through works
            AggregateCache3.Instance.ClearL1()

            // Create in-memory event store that has NO snapshot in database
            let memStore = MemoryStorage.MemoryStorage() :> IEventStore<string>

            // Act: Call StateView snapshot loader
            let! snapRes = StateView.getLastAggregateSnapshotAsync<SampleObject, string> sampleId SampleObject.Version SampleObject.StorageName memStore None |> Async.AwaitTask
            Expect.isOk snapRes "StateView should return Ok"
            let (evIdOpt, snapshot) = snapRes |> Result.get
            Expect.equal evIdOpt (Some 77) "Snapshot eventId should be 77 from cache"
            Expect.equal snapshot.Name "AcceleratedSnapshot" "Snapshot state should match cached aggregate"
        }

        testCase "StateView.getLastAggregateSnapshot synchronous also utilizes L1/L2 cache" <| fun () ->
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let sampleId = Guid.NewGuid()
            let sample = SampleObject.MkSampleObject(sampleId, "SyncAcceleratedSnapshot")
            let eventId = 88

            AggregateCache3.Instance.Memoize2 (eventId, sample) sampleId
            AggregateCache3.Instance.ClearL1()

            let memStore = MemoryStorage.MemoryStorage() :> IEventStore<string>

            let snapRes = StateView.getLastAggregateSnapshot<SampleObject, string> sampleId SampleObject.Version SampleObject.StorageName memStore
            Expect.isOk snapRes "StateView sync should return Ok"
            let snapOpt = snapRes |> Result.get
            Expect.isSome snapOpt "Snapshot option should be Some"
            let (evIdOpt, snapshot) = snapOpt.Value
            Expect.equal evIdOpt (Some 88) "Snapshot eventId should be 88 from cache"
            Expect.equal snapshot.Name "SyncAcceleratedSnapshot" "Snapshot state should match"

        testCase "Clean removes entry from both L1 and L2" <| fun () ->
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let sampleId = Guid.NewGuid()
            let sample = SampleObject.MkSampleObject(sampleId, "DeletedObject")
            AggregateCache3.Instance.Memoize2 (1, sample) sampleId

            // Verify exists
            Expect.isSome (AggregateCache3.Instance.LastEventId sampleId) "Should exist"

            // Act: Clean
            AggregateCache3.Instance.Clean sampleId

            // Assert: Missing in L1 and L2
            Expect.isNone (AggregateCache3.Instance.LastEventId sampleId) "Should be None after Clean"
            Expect.isError (AggregateCache3.Instance.GetState sampleId) "GetState should return error after Clean"
    ]
