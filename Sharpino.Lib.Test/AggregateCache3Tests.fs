module Sharpino.Lib.Test.AggregateCache3Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Options
open Sharpino
open Sharpino.Core
open Sharpino.Cache
open Sharpino.Storage
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.EventBroker
open Sharpino.MemoryStorage
open Sharpino.Lib.Test.Models.SampleObject.SampleObject
open Sharpino.Lib.Test.Models.SampleObject.Events
open Sharpino.Lib.Test.Models.SampleObject.Commands

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

            // Assert L2 distributed cache contains the entry (FusionCache 2.x prefixes wire keys with "v2:")
            let l2Bytes = distCache.Get("v2:statePerAggregate:" + sampleId.ToString())
            Expect.isNotNull (box l2Bytes) "L2 distributed cache must contain the entry under v2:statePerAggregate:<id>"

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

        testCase "runAggregateCommand stores events, updates L1 and L2 cache, and another node resolves L1 miss via L2 hit" <| fun () ->
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let memStore = MemoryStorage() :> IEventStore<string>
            let sampleId = Guid.NewGuid()
            let initialSample = SampleObject.MkSampleObject(sampleId, "InitialName")

            // Initialize aggregate
            let initRes = runInit<SampleObject, SampleObjectEvents, string> memStore MessageSenders.NoSender initialSample
            Expect.isOk initRes "runInit should succeed"

            // Verify initial state in cache
            Expect.equal (AggregateCache3.Instance.LastEventId sampleId) (Some 0) "Initial event id should be 0"

            // Act 1: Execute runAggregateCommand on Node 1
            let renameCmd = RenameSampleObject "UpdatedName" :> AggregateCommand<SampleObject, SampleObjectEvents>
            let cmdRes = runAggregateCommand<SampleObject, SampleObjectEvents, string> sampleId memStore MessageSenders.NoSender renameCmd
            Expect.isOk cmdRes "runAggregateCommand should succeed"

            // Assert: L1 on Node 1 is updated with the new state
            let node1StateRes = AggregateCache3.Instance.GetState sampleId
            Expect.isOk node1StateRes "GetState on Node 1 should return Ok"
            let node1Sample = node1StateRes |> Result.get :?> SampleObject
            Expect.equal node1Sample.Name "UpdatedName" "Node 1 L1 state should be 'UpdatedName'"
            Expect.equal (AggregateCache3.Instance.LastEventId sampleId) (Some 1) "EventId should be 1"

            // Wait briefly for async L2 writes to settle
            System.Threading.Thread.Sleep(200)

            // Assert: L2 distributed cache was updated by runAggregateCommand (FusionCache 2.x uses "v2:statePerAggregate:<id>")
            let l2Bytes = distCache.Get("v2:statePerAggregate:" + sampleId.ToString())
            Expect.isNotNull (box l2Bytes) "L2 distributed cache must contain the entry for the aggregate"

            // Act 2: Simulate any other node (Node 2) evicting its L1 cache entry (via backplane notification)
            // ClearL1() purges memory cache while preserving L2 distributed cache
            AggregateCache3.Instance.ClearL1()

            // To strictly prove that Node 2 does NOT rebuild state from the event store on an L1 miss,
            // we use an empty event store: if it attempted to query the event store, it would fail to find events.
            let wipingStore = MemoryStorage() :> IEventStore<string>

            // Act 3: Node 2 resolves aggregate fresh state
            let node2FreshStateRes = getAggregateFreshState<SampleObject, SampleObjectEvents, string> sampleId wipingStore
            Expect.isOk node2FreshStateRes "Node 2 should successfully get fresh state via L2 cache hit without querying event store"

            let (eventId, node2Sample) = node2FreshStateRes |> Result.get
            Expect.equal eventId 1 "Node 2 eventId resolved from L2 must be 1"
            Expect.equal node2Sample.Name "UpdatedName" "Node 2 state resolved from L2 must be 'UpdatedName'"

        testCaseAsync "runAggregateCommandMdAsync stores events, updates L1 and L2 cache, and resolves L1 miss via L2 hit asynchronously" <| async {
            let distCache = createMemoryDistributedCache ()
            AggregateCache3.Instance.SetupL2AndBackplane(Some distCache, Some serializer, None)
            AggregateCache3.Instance.Clear()

            let memStore = MemoryStorage() :> IEventStore<string>
            let sampleId = Guid.NewGuid()
            let initialSample = SampleObject.MkSampleObject(sampleId, "AsyncInitialName")

            let initRes = runInit<SampleObject, SampleObjectEvents, string> memStore MessageSenders.NoSender initialSample
            Expect.isOk initRes "runInit should succeed"

            // Act 1: Execute runAggregateCommandMdAsync
            let renameCmd = RenameSampleObject "AsyncUpdatedName" :> AggregateCommand<SampleObject, SampleObjectEvents>
            let! cmdRes = runAggregateCommandMdAsync<SampleObject, SampleObjectEvents, string> sampleId memStore MessageSenders.NoSender Metadata.Empty renameCmd None |> Async.AwaitTask
            Expect.isOk cmdRes "runAggregateCommandMdAsync should succeed"

            // Assert L2 has entry (FusionCache 2.x uses "v2:statePerAggregate:<id>")
            do! Task.Delay(200) |> Async.AwaitTask
            let l2Bytes = distCache.Get("v2:statePerAggregate:" + sampleId.ToString())
            Expect.isNotNull (box l2Bytes) "L2 cache must contain entry after async command"

            // Act 2: Evict L1 (simulate Node 2 L1 eviction)
            AggregateCache3.Instance.ClearL1()

            // Act 3: Query fresh state asynchronously on Node 2 using empty event store
            let wipingStore = MemoryStorage() :> IEventStore<string>
            let! node2FreshStateRes = getAggregateFreshStateAsync<SampleObject, SampleObjectEvents, string> sampleId wipingStore None |> Async.AwaitTask
            Expect.isOk node2FreshStateRes "Node 2 async fresh state should succeed via L2 cache hit"

            let (eventId, node2Sample) = node2FreshStateRes |> Result.get
            Expect.equal eventId 1 "Node 2 eventId resolved from L2 must be 1"
            Expect.equal node2Sample.Name "AsyncUpdatedName" "Node 2 state resolved from L2 must be 'AsyncUpdatedName'"
        }
    ]
