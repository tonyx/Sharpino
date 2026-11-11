namespace Sharpino.Lib.Test

open System
open System.Threading.Tasks
open Expecto
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Storage
open Sharpino.MemoryStorage
open Sharpino.CommandHandler
open Sharpino.StateView
open Sharpino.Cache
open Sharpino.Sanitizer
open Sharpino.Lib.Test.Models.SampleObject.SampleObject
open Sharpino.Lib.Test.Models.SampleObject.Events
open Sharpino.Lib.Test.Models.SampleObject.Commands
open Sharpino.Lib.Test.Models.ContextObject.SampleContext
open Sharpino.Lib.Test.Models.ContextObject.Events
open Sharpino.Lib.Test.EvolveSkippingErrorsTests

module SanitizerAndCausalityTests =

    [<Tests>]
    let tests =
        testSequenced <| testList "Sanitizer and Causality Tests" [

            testCase "Causality tracking: correlationId and causationId are preserved and queryable" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let corrId = Guid.NewGuid()
                let causeId = Guid.NewGuid()
                let event1 = (SampleObjectRenamed "Initial Name").Serialize
                
                let addRes = storage.AddAggregateEventsWithCausalityMd 0 SampleObject.Version SampleObject.StorageName aggId "metadata" [event1] (Some corrId) (Some causeId)
                Expect.isOk addRes "adding events should succeed"
                let eventIds = addRes |> Result.get
                let evId = eventIds.Head

                let found = storage.FindEventsByCorrelationId SampleObject.Version SampleObject.StorageName corrId
                Expect.isOk found "finding events by correlationId should succeed"
                let foundList = found |> Result.get
                Expect.equal foundList.Length 1 "should find exactly 1 event"
                let (foundAggId, foundEvId, foundPayload) = foundList.Head
                Expect.equal foundAggId aggId "aggregate ID must match"
                Expect.equal foundEvId evId "event ID must match"
                Expect.equal foundPayload event1 "payload must match"

            testCase "Multi-aggregate commands automatically propagate a shared correlationId" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId1 = Guid.NewGuid()
                let aggId2 = Guid.NewGuid()
                let ev1 = (SampleObjectRenamed "Agg 1").Serialize
                let ev2 = (SampleObjectRenamed "Agg 2").Serialize
                
                let multiRes =
                    storage.MultiAddAggregateEventsMd "multi metadata" [
                        (0, [ev1], SampleObject.Version, SampleObject.StorageName, aggId1)
                        (0, [ev2], SampleObject.Version, SampleObject.StorageName, aggId2)
                    ]
                Expect.isOk multiRes "multi add should succeed"
                let ids = multiRes |> Result.get
                let id1 = ids.[0].Head
                let id2 = ids.[1].Head
                
                let ev1Obj = storage.TryGetEvent SampleObject.Version id1 SampleObject.StorageName
                let ev2Obj = storage.TryGetEvent SampleObject.Version id2 SampleObject.StorageName
                Expect.isSome ev1Obj "event 1 should exist"
                Expect.isSome ev2Obj "event 2 should exist"
                Expect.isSome ev1Obj.Value.CorrelationId "event 1 should have correlation ID"
                Expect.isSome ev2Obj.Value.CorrelationId "event 2 should have correlation ID"
                Expect.equal ev1Obj.Value.CorrelationId ev2Obj.Value.CorrelationId "correlation IDs across aggregates in multi-command must be identical"

            testCase "PatchEventWithNoOp replaces event payload and purges snapshots" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let ev1 = (SampleObjectRenamed "Original").Serialize
                let addRes = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1]
                Expect.isOk addRes "add should succeed"
                let evId = (addRes |> Result.get).Head
                
                let snap = (SampleObject.MkSampleObject(aggId, "Original")).Serialize
                let snapRes = storage.SetAggregateSnapshot SampleObject.Version (aggId, evId, snap) SampleObject.StorageName
                Expect.isOk snapRes "snapshot should succeed"
                
                let snapBefore = storage.TryGetLastAggregateSnapshot SampleObject.Version SampleObject.StorageName aggId
                Expect.isOk snapBefore "snapshot query should succeed"
                Expect.isSome (snapBefore |> Result.get |> fst) "snapshot should have eventId"
                
                let noOp = SampleObjectNoOp.Serialize
                let patchRes = storage.PatchEventWithNoOp SampleObject.Version SampleObject.StorageName aggId evId noOp
                Expect.isOk patchRes "patch should succeed"
                
                let patchedEv = storage.TryGetEvent SampleObject.Version evId SampleObject.StorageName
                Expect.isSome patchedEv "event should exist"
                Expect.equal patchedEv.Value.JsonEvent noOp "event payload must be replaced with NoOp"
                
                let snapAfter = storage.TryGetLastAggregateSnapshot SampleObject.Version SampleObject.StorageName aggId
                Expect.isError snapAfter "snapshot at or after patched eventId must be deleted"

            testCase "Evolve anomaly handler captures failure context and halts evolution" <| fun () ->
                let mutable capturedContext: Option<AnomalyContext> = None
                let testHandler =
                    { new IEvolveAnomalyHandler with
                        member this.OnEvolveAnomaly(ctx) = capturedContext <- Some ctx
                        member this.OnEvolveAnomalyAsync(ctx) =
                            capturedContext <- Some ctx
                            Task.FromResult(()) }
                let prevHandler = currentAnomalyHandler
                currentAnomalyHandler <- testHandler
                try
                    // 1. Pure domain evolve and evolveWithContext have zero side effects and return error details directly
                    let initial = { Value = 50; History = [] }
                    let badEvents = [ Sub 100 ]
                    let res = evolve initial badEvents
                    Expect.isError res "evolve must return error"
                    Expect.isNone capturedContext "pure evolve must not invoke anomaly handler directly"
                    let ctxRes = evolveWithContext initial [ (1, Sub 100) ]
                    Expect.isError ctxRes "evolveWithContext must return EvolveError"
                    let evolveErr = match ctxRes with Error e -> e | Ok _ -> failwith "expected error"
                    Expect.equal evolveErr.EventId 1 "EvolveError must capture failing EventId"
                    Expect.stringContains evolveErr.Message "Underflow" "EvolveError must capture failure message"

                    // 2. StateView orchestration invokes OnEvolveAnomalyAsync when evolveWithContext fails
                    AggregateCache3.Instance.Clear()
                    let storage = MemoryStorage() :> IEventStore<string>
                    let aggId = Guid.NewGuid()
                    let initialObj = SampleObject.MkSampleObject(aggId, "SameName")
                    let _ = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initialObj.Serialize
                    let poisonEv = (SampleObjectRenamed "SameName").Serialize
                    let _ = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [poisonEv]
                    let freshRes =
                        getAggregateFreshStateAsync<SampleObject, SampleObjectEvents, string> aggId storage None
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    Expect.isError freshRes "getAggregateFreshStateAsync must return error when handler only logs"
                    Expect.isSome capturedContext "StateView orchestration must invoke OnEvolveAnomalyAsync"
                    let ctx = capturedContext.Value
                    Expect.equal ctx.AggregateId (Some aggId) "AnomalyContext must capture aggregateId"
                    Expect.equal ctx.EventId (Some 1) "AnomalyContext must capture failing eventId"
                finally
                    currentAnomalyHandler <- prevHandler

            testCase "Sanitizer detects poison event, applies NoOp patch, and heals aggregate stream" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let initialObj = SampleObject.MkSampleObject(aggId, "Zero")
                let initRes = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initialObj.Serialize
                Expect.isOk initRes "initial aggregate state should succeed"
                
                // Event 1: Rename to "First"
                let ev1 = (SampleObjectRenamed "First").Serialize
                // Event 2: Poison event -> Rename to "First" again (violates domain rule "this.Name <> name")
                let ev2 = (SampleObjectRenamed "First").Serialize
                // Event 3: Rename to "Second"
                let ev3 = (SampleObjectRenamed "Second").Serialize
                
                let addRes = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1; ev2; ev3]
                Expect.isOk addRes "adding raw events succeeds at storage level"
                
                // Evolution without sanitizer fails due to unforgiving evolve at Event 2
                let freshBefore = getAggregateFreshState<SampleObject, SampleObjectEvents, string> aggId storage
                Expect.isError freshBefore "unforgiving evolve must halt on duplicate rename"
                
                // Run Sanitizer
                let noOp = SampleObjectNoOp
                let sanitizeRes = sanitizeAggregateStream<SampleObject, SampleObjectEvents, string> storage aggId noOp None
                Expect.isOk sanitizeRes "sanitization should succeed"
                let report = sanitizeRes |> Result.get
                Expect.isFalse report.IsClean "stream should not have been clean originally"
                Expect.equal report.Actions.Length 1 "exactly 1 action should be taken"
                match report.Actions.Head with
                | PatchedToNoOp(eventId, _) -> Expect.equal eventId 2 "poisoned event 2 should be patched"
                | _ -> failwith "Expected PatchedToNoOp action"
                
                // Fresh state should now compute cleanly to "Second"
                let freshAfter = getAggregateFreshState<SampleObject, SampleObjectEvents, string> aggId storage
                Expect.isOk freshAfter "stream must now evolve cleanly"
                let (lastId, state) = freshAfter |> Result.get
                Expect.equal lastId 3 "last event ID must be 3"
                Expect.equal state.Name "Second" "final state must be 'Second'"

            testCase "Cross-aggregate ex-post compensation when sibling stream is patched" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                let aggAId = Guid.NewGuid()
                let aggBId = Guid.NewGuid()
                let sharedCorrId = Guid.NewGuid()
                
                let initialA = SampleObject.MkSampleObject(aggAId, "ZeroA")
                let initialB = SampleObject.MkSampleObject(aggBId, "ZeroB")
                let _ = storage.SetInitialAggregateState aggAId SampleObject.Version SampleObject.StorageName initialA.Serialize
                let _ = storage.SetInitialAggregateState aggBId SampleObject.Version SampleObject.StorageName initialB.Serialize
                
                // Multi-aggregate transaction: Agg A gets "NameA", Agg B gets "NameB"
                let evA1 = (SampleObjectRenamed "NameA").Serialize
                let evB1 = (SampleObjectRenamed "NameB").Serialize
                let _ = storage.AddAggregateEventsWithCausalityMd 0 SampleObject.Version SampleObject.StorageName aggAId "md" [evA1] (Some sharedCorrId) None
                let _ = storage.AddAggregateEventsWithCausalityMd 0 SampleObject.Version SampleObject.StorageName aggBId "md" [evB1] (Some sharedCorrId) None
                
                // Agg A gets a poisoned event: rename to "NameA" again sharing the same correlationId
                let evA2_poison = (SampleObjectRenamed "NameA").Serialize
                let _ = storage.AddAggregateEventsWithCausalityMd 1 SampleObject.Version SampleObject.StorageName aggAId "md" [evA2_poison] (Some sharedCorrId) None
                
                // Track compensation execution
                let mutable compensatedAggB = false
                let compensator (corrId: Guid) (poisonEventId: EventId) =
                    let siblings = storage.FindEventsByCorrelationId SampleObject.Version SampleObject.StorageName corrId
                    match siblings with
                    | Ok events ->
                        let bEvents = events |> List.filter (fun (id, _, _) -> id = aggBId)
                        if not bEvents.IsEmpty then
                            let compEvent = (SampleObjectRenamed "CompensatedAfterPoison").Serialize
                            let causeGuid = Guid.NewGuid()
                            let _ = storage.AddAggregateEventsWithCausalityMd 1 SampleObject.Version SampleObject.StorageName aggBId "compensation" [compEvent] (Some corrId) (Some causeGuid)
                            compensatedAggB <- true
                            Ok (sprintf "Compensated Aggregate B %A" aggBId)
                        else
                            Error "No sibling event on Aggregate B found"
                    | Error e -> Error e
                    
                // Run Sanitizer on Aggregate A with the compensator registered
                let sanitizeRes = sanitizeAggregateStream<SampleObject, SampleObjectEvents, string> storage aggAId SampleObjectNoOp (Some compensator)
                Expect.isOk sanitizeRes "sanitization of Agg A should succeed"
                Expect.isTrue compensatedAggB "compensator should have been invoked for Agg B"
                
                // Verify Aggregate B state now reflects the compensating event
                let freshB = getAggregateFreshState<SampleObject, SampleObjectEvents, string> aggBId storage
                Expect.isOk freshB "Agg B should evolve cleanly"
                let (_, stateB) = freshB |> Result.get
                Expect.equal stateB.Name "CompensatedAfterPoison" "Agg B should have received compensating state change"

            testCase "Context stream sanitization detects corrupt event and heals stream" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                
                let ev1 = (SampleContextRenamed "ContextName").Serialize
                let ev2 = "invalid json payload that cannot deserialize"
                let ev3 = (SampleContextTagAdded Music).Serialize
                
                let addRes = storage.AddEventsMd 0 SampleContext.Version SampleContext.StorageName "md" [ev1; ev2; ev3]
                Expect.isOk addRes "adding events should succeed"
                
                let freshBefore = getFreshState<SampleContext, SampleContextEvents, string> storage
                Expect.isError freshBefore "unforgiving evolve must halt on deserialization failure"
                
                let noOp = SampleContextNoOp
                let sanitizeRes = sanitizeContextStream<SampleContext, SampleContextEvents, string> storage noOp
                Expect.isOk sanitizeRes "context sanitization should succeed"
                let report = sanitizeRes |> Result.get
                Expect.isFalse report.IsClean "stream was not clean"
                Expect.equal report.Actions.Length 1 "1 action taken"
                
                let freshAfter = getFreshState<SampleContext, SampleContextEvents, string> storage
                Expect.isOk freshAfter "context must now evolve cleanly"
                let (lastId, state) = freshAfter |> Result.get
                Expect.equal lastId 3 "last event ID must be 3"
                Expect.equal state.Name "ContextName" "name must match"
                Expect.equal state.Tags [Music] "tags must match"

            testCase "Sanitization evicts dependent views from DetailsCache for healed aggregate" <| fun () ->
                DetailsCache.Instance.Clear()
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let initial = SampleObject.MkSampleObject(aggId, "InitialState")
                let _ = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initial.Serialize
                
                // Event 1: Rename to "First"
                let ev1 = (SampleObjectRenamed "First").Serialize
                let _ = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1]
                
                // Memoize a view depending on aggId
                let viewKey = DetailsCacheKey ("SampleObjectView", aggId)
                let viewBuilder () =
                    let refreshable = mkRefreshableAsyncFromSync (fun () ->
                        getAggregateFreshState<SampleObject, SampleObjectEvents, string> aggId storage
                        |> Result.map (fun (_, s) -> s.Name)
                    )
                    Ok (refreshable, [aggId])
                
                let memoized = DetailsCache.Instance.Memoize viewBuilder viewKey
                Expect.isOk memoized "memoizing view should succeed"
                Expect.isTrue (DetailsCache.Instance.ContainsKey viewKey) "view must be cached in DetailsCache"
                
                // Poison the aggregate with an invalid rename to "First" again
                let ev2_poison = (SampleObjectRenamed "First").Serialize
                let _ = storage.AddAggregateEventsMd 1 SampleObject.Version SampleObject.StorageName aggId "md" [ev2_poison]
                
                // Sanitize the aggregate
                let sanitizeRes = sanitizeAggregateStream<SampleObject, SampleObjectEvents, string> storage aggId SampleObjectNoOp None
                Expect.isOk sanitizeRes "sanitization should succeed"
                let report = sanitizeRes |> Result.get
                Expect.isFalse report.IsClean "stream should have had anomalies"
                
                // DetailsCache MUST now be evicted for this viewKey!
                Expect.isFalse (DetailsCache.Instance.ContainsKey viewKey) "dependent view must be evicted from DetailsCache after sanitization"
                Expect.isNone (DetailsCache.Instance.TryGet viewKey) "TryGet must return None for evicted view"

            testCase "Sanitization evicts DetailsCache across correlated sibling aggregates" <| fun () ->
                DetailsCache.Instance.Clear()
                let storage = MemoryStorage() :> IEventStore<string>
                let aggAId = Guid.NewGuid()
                let aggBId = Guid.NewGuid()
                let sharedCorrId = Guid.NewGuid()
                
                let initialA = SampleObject.MkSampleObject(aggAId, "AggA_Initial")
                let initialB = SampleObject.MkSampleObject(aggBId, "AggB_Initial")
                let _ = storage.SetInitialAggregateState aggAId SampleObject.Version SampleObject.StorageName initialA.Serialize
                let _ = storage.SetInitialAggregateState aggBId SampleObject.Version SampleObject.StorageName initialB.Serialize
                
                // Legitimate multi-aggregate transaction stamped with shared correlation ID
                let evA1 = (SampleObjectRenamed "AggA_Valid").Serialize
                let evB1 = (SampleObjectRenamed "AggB_Valid").Serialize
                let _ = storage.AddAggregateEventsWithCausalityMd 0 SampleObject.Version SampleObject.StorageName aggAId "md" [evA1] (Some sharedCorrId) None
                let _ = storage.AddAggregateEventsWithCausalityMd 0 SampleObject.Version SampleObject.StorageName aggBId "md" [evB1] (Some sharedCorrId) None
                
                // Memoize views for both Agg A and Agg B
                let viewAKey = DetailsCacheKey ("ViewA", aggAId)
                let viewBKey = DetailsCacheKey ("ViewB", aggBId)
                
                let viewABuilder () =
                    let refreshable = mkRefreshableAsyncFromSync (fun () ->
                        getAggregateFreshState<SampleObject, SampleObjectEvents, string> aggAId storage
                        |> Result.map (fun (_, s) -> s.Name)
                    )
                    Ok (refreshable, [aggAId])
                    
                let viewBBuilder () =
                    let refreshable = mkRefreshableAsyncFromSync (fun () ->
                        getAggregateFreshState<SampleObject, SampleObjectEvents, string> aggBId storage
                        |> Result.map (fun (_, s) -> s.Name)
                    )
                    Ok (refreshable, [aggBId])
                
                let _ = DetailsCache.Instance.Memoize viewABuilder viewAKey
                let _ = DetailsCache.Instance.Memoize viewBBuilder viewBKey
                
                Expect.isTrue (DetailsCache.Instance.ContainsKey viewAKey) "View A must be cached"
                Expect.isTrue (DetailsCache.Instance.ContainsKey viewBKey) "View B must be cached"
                
                // Poison Agg A with an invalid event that shares the same correlation ID
                let evA2_poison = (SampleObjectRenamed "AggA_Valid").Serialize
                let _ = storage.AddAggregateEventsWithCausalityMd 1 SampleObject.Version SampleObject.StorageName aggAId "md" [evA2_poison] (Some sharedCorrId) None
                
                // Sanitize Agg A
                let sanitizeRes = sanitizeAggregateStream<SampleObject, SampleObjectEvents, string> storage aggAId SampleObjectNoOp None
                Expect.isOk sanitizeRes "sanitization should succeed"
                
                // Both View A and correlated sibling View B must be evicted from DetailsCache!
                Expect.isFalse (DetailsCache.Instance.ContainsKey viewAKey) "View A must be evicted from DetailsCache"
                Expect.isFalse (DetailsCache.Instance.ContainsKey viewBKey) "Correlated View B must also be evicted from DetailsCache"

            testCase "PatchEventWithNoOp preserves genesis snapshot (event_id IS NULL) while purging subsequent snapshots" <| fun () ->
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let initialObj = SampleObject.MkSampleObject(aggId, "Genesis")
                let initRes = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initialObj.Serialize
                Expect.isOk initRes "initial aggregate state should succeed"

                let ev1 = (SampleObjectRenamed "Updated").Serialize
                let addRes = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1]
                Expect.isOk addRes "add should succeed"
                let evId = (addRes |> Result.get).Head

                let snap = (SampleObject.MkSampleObject(aggId, "Updated")).Serialize
                let snapRes = storage.SetAggregateSnapshot SampleObject.Version (aggId, evId, snap) SampleObject.StorageName
                Expect.isOk snapRes "snapshot should succeed"

                let noOp = SampleObjectNoOp.Serialize
                let patchRes = storage.PatchEventWithNoOp SampleObject.Version SampleObject.StorageName aggId evId noOp
                Expect.isOk patchRes "patch should succeed"

                // Genesis snapshot (event_id IS NULL) must be preserved
                let firstSnapAfter = storage.TryGetFirstSnapshot SampleObject.Version SampleObject.StorageName aggId
                Expect.isOk firstSnapAfter "genesis snapshot must be preserved after PatchEventWithNoOp"
                let lastSnapAfter = storage.TryGetLastAggregateSnapshot SampleObject.Version SampleObject.StorageName aggId
                Expect.isOk lastSnapAfter "TryGetLastAggregateSnapshot must fall back to genesis snapshot"
                Expect.isNone (lastSnapAfter |> Result.get |> fst) "remaining snapshot must have event_id = None (genesis)"

                let idsAfter = storage.GetUndeletedAggregateIds SampleObject.Version SampleObject.StorageName |> Result.get
                Expect.contains idsAfter aggId "aggregate must remain visible to GetUndeletedAggregateIds"

            testCase "Automated interceptor sanitization: evolve anomaly triggers automatic healing and retries once in StateView" <| fun () ->
                AggregateCache3.Instance.Clear()
                resetCooldowns()
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let initialObj = SampleObject.MkSampleObject(aggId, "InitialState")
                let _ = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initialObj.Serialize

                // Event 1: Valid rename to "Step1"
                let ev1 = (SampleObjectRenamed "Step1").Serialize
                // Event 2: Poison event -> duplicate rename to "Step1" (violates domain rule "this.Name <> name")
                let ev2 = (SampleObjectRenamed "Step1").Serialize
                // Event 3: Valid rename to "Step2"
                let ev3 = (SampleObjectRenamed "Step2").Serialize

                let _ = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1; ev2; ev3]

                // Set up an automated interceptor anomaly handler (simulating an ASP.NET Core service/handler)
                let mutable healedAggregates = []
                let autoHealingHandler =
                    { new IEvolveAnomalyHandler with
                        member this.OnEvolveAnomaly(ctx: AnomalyContext) =
                            match ctx.AggregateId with
                            | Some id when ctx.AggregateType = typeof<SampleObject> ->
                                let res = sanitizeAggregateStream<SampleObject, SampleObjectEvents, string> storage id SampleObjectNoOp None
                                if Result.isOk res then
                                    healedAggregates <- id :: healedAggregates
                            | _ -> ()
                        member this.OnEvolveAnomalyAsync(ctx: AnomalyContext) =
                            task {
                                match ctx.AggregateId with
                                | Some id when ctx.AggregateType = typeof<SampleObject> ->
                                    let! res = sanitizeAggregateStreamAsync<SampleObject, SampleObjectEvents, string> storage id SampleObjectNoOp None
                                    if Result.isOk res then
                                        healedAggregates <- id :: healedAggregates
                                | _ -> ()
                            }
                    }

                let prevHandler = currentAnomalyHandler
                currentAnomalyHandler <- autoHealingHandler
                try
                    // Initial fresh state read encounters the poison event during evolveWithContext,
                    // triggers the autoHealingHandler to patch the stream, and automatically retries once!
                    let freshAttempt1 =
                        getAggregateFreshStateAsync<SampleObject, SampleObjectEvents, string> aggId storage None
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    Expect.isOk freshAttempt1 "First read must succeed after OnEvolveAnomalyAsync heals the stream and StateView retries once"
                    Expect.contains healedAggregates aggId "Anomaly interceptor must have been triggered with the aggregate ID and healed the stream"
                    let (_, finalState1) = freshAttempt1 |> Result.get
                    Expect.equal finalState1.Name "Step2" "State should evolve cleanly to Step2 on first read"

                    // Subsequent read also succeeds cleanly
                    let freshAttempt2 =
                        getAggregateFreshStateAsync<SampleObject, SampleObjectEvents, string> aggId storage None
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    Expect.isOk freshAttempt2 "Subsequent read must succeed on the healed stream"
                    let (_, finalState2) = freshAttempt2 |> Result.get
                    Expect.equal finalState2.Name "Step2" "State should evolve cleanly to Step2"
                finally
                    currentAnomalyHandler <- prevHandler

            testCase "getAllFilteredAggregateStatesUnskippedErrorsAsync heals and returns multiple poisoned aggregates in a single call" <| fun () ->
                AggregateCache3.Instance.Clear()
                resetCooldowns()
                let storage = MemoryStorage() :> IEventStore<string>
                let aggIds = [ for _ in 1..5 -> Guid.NewGuid() ]

                for (i, aggId) in List.indexed aggIds do
                    let initialObj = SampleObject.MkSampleObject(aggId, sprintf "Init_%d" i)
                    let _ = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initialObj.Serialize
                    let ev1 = (SampleObjectRenamed (sprintf "Valid1_%d" i)).Serialize
                    let ev2_poison = (SampleObjectRenamed (sprintf "Valid1_%d" i)).Serialize
                    let ev3 = (SampleObjectRenamed (sprintf "Final_%d" i)).Serialize
                    let _ = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1; ev2_poison; ev3]
                    ()

                let healedLock = obj()
                let mutable healedAggregates = []
                let autoHealingHandler =
                    { new IEvolveAnomalyHandler with
                        member this.OnEvolveAnomaly(ctx: AnomalyContext) =
                            match ctx.AggregateId with
                            | Some id when ctx.AggregateType = typeof<SampleObject> ->
                                let res = sanitizeAggregateStream<SampleObject, SampleObjectEvents, string> storage id SampleObjectNoOp None
                                if Result.isOk res then
                                    lock healedLock (fun () -> healedAggregates <- id :: healedAggregates)
                            | _ -> ()
                        member this.OnEvolveAnomalyAsync(ctx: AnomalyContext) =
                            task {
                                match ctx.AggregateId with
                                | Some id when ctx.AggregateType = typeof<SampleObject> ->
                                    let! res = sanitizeAggregateStreamAsync<SampleObject, SampleObjectEvents, string> storage id SampleObjectNoOp None
                                    if Result.isOk res then
                                        lock healedLock (fun () -> healedAggregates <- id :: healedAggregates)
                                | _ -> ()
                            }
                    }

                let prevHandler = currentAnomalyHandler
                currentAnomalyHandler <- autoHealingHandler
                try
                    let allRes =
                        getAllFilteredAggregateStatesUnskippedErrorsAsync<SampleObject, SampleObjectEvents, string>
                            (fun _ -> true)
                            storage
                            None
                        |> Async.AwaitTask
                        |> Async.RunSynchronously

                    Expect.isOk allRes "getAllFilteredAggregateStatesUnskippedErrorsAsync must heal and return all aggregates in a single call"
                    let states = allRes |> Result.get
                    Expect.equal states.Length 5 "All 5 aggregates must be returned"
                    Expect.equal (healedAggregates |> List.distinct |> List.length) 5 "All 5 aggregates must have been healed in one batch"
                    for (i, aggId) in List.indexed aggIds do
                        let found = states |> List.tryFind (fun (_, s) -> s.Id = aggId)
                        Expect.isSome found (sprintf "Aggregate %d must be present" i)
                        Expect.equal (snd found.Value).Name (sprintf "Final_%d" i) "Final state must reflect post-poison event"
                finally
                    currentAnomalyHandler <- prevHandler

            testCase "Concurrent async reads on poisoned aggregate deduplicate sanitization and heal without deadlock" <| fun () ->
                AggregateCache3.Instance.Clear()
                resetCooldowns()
                let storage = MemoryStorage() :> IEventStore<string>
                let aggId = Guid.NewGuid()
                let initialObj = SampleObject.MkSampleObject(aggId, "InitialState")
                let _ = storage.SetInitialAggregateState aggId SampleObject.Version SampleObject.StorageName initialObj.Serialize

                let ev1 = (SampleObjectRenamed "Step1").Serialize
                let ev2_poison = (SampleObjectRenamed "Step1").Serialize
                let ev3 = (SampleObjectRenamed "Step2").Serialize
                let _ = storage.AddAggregateEventsMd 0 SampleObject.Version SampleObject.StorageName aggId "md" [ev1; ev2_poison; ev3]

                let countLock = obj()
                let mutable actualPatchRuns = 0
                let mutable cooldownSkips = 0

                let autoHealingHandler =
                    { new IEvolveAnomalyHandler with
                        member this.OnEvolveAnomaly(_ctx: AnomalyContext) = ()
                        member this.OnEvolveAnomalyAsync(ctx: AnomalyContext) =
                            task {
                                match ctx.AggregateId with
                                | Some id when ctx.AggregateType = typeof<SampleObject> ->
                                    let! res = sanitizeAggregateStreamAsync<SampleObject, SampleObjectEvents, string> storage id SampleObjectNoOp None
                                    match res with
                                    | Ok report ->
                                        lock countLock (fun () ->
                                            if not report.IsClean then
                                                actualPatchRuns <- actualPatchRuns + 1
                                            else
                                                cooldownSkips <- cooldownSkips + 1
                                        )
                                    | Error _ -> ()
                                | _ -> ()
                            }
                    }

                let prevHandler = currentAnomalyHandler
                currentAnomalyHandler <- autoHealingHandler
                try
                    let tasks =
                        [| for _ in 1..10 ->
                            Task.Run<Result<EventId * SampleObject, string>>(fun () ->
                                getAggregateFreshStateAsync<SampleObject, SampleObjectEvents, string> aggId storage None
                            ) |]
                    let results =
                        Task.WhenAll(tasks)
                        |> Async.AwaitTask
                        |> Async.RunSynchronously

                    for r in results do
                        Expect.isOk r "Every concurrent caller must succeed without deadlock"
                        let (_, s) = r |> Result.get
                        Expect.equal s.Name "Step2" "Every concurrent caller must observe the healed state"

                    Expect.equal actualPatchRuns 1 "Single-flight lock + cooldown must ensure only 1 actual DB sanitization patch occurs"
                finally
                    currentAnomalyHandler <- prevHandler
        ]
