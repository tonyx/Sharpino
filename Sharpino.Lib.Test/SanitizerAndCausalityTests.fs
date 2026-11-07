namespace Sharpino.Lib.Test

open System
open Expecto
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
        testList "Sanitizer and Causality Tests" [

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
                        member this.OnEvolveAnomaly(ctx) = capturedContext <- Some ctx }
                let prevHandler = currentAnomalyHandler
                currentAnomalyHandler <- testHandler
                try
                    let initial = { Value = 50; History = [] }
                    let badEvents = [ Sub 100 ]
                    let res = evolve initial badEvents
                    Expect.isError res "evolve must return error"
                    Expect.isSome capturedContext "anomaly handler must be invoked"
                    let ctx = capturedContext.Value
                    Expect.stringContains ctx.ErrorMessage "Underflow" "error message must describe failure"
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
        ]
