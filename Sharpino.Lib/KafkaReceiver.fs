
namespace Sharpino

open FsToolkit.ErrorHandling
open FSharpPlus
open Sharpino
open Sharpino.Commons
open Sharpino.CommandHandler
open Sharpino.Storage
open Sharpino.Definitions
open Confluent.Kafka
open Sharpino.Core
open Sharpino.Utils
open Sharpino.KafkaBroker
open System.Collections
open System
open log4net
open log4net.Config
open Confluent.Kafka
open FsKafka
open FSharpPlus.Operators

// to be removed/rewritten

// in progress: don't use it
module KafkaReceiver =
    let getStrAggregateMessage message =
        ResultCE.result {
            let base64Decoded = message |> Convert.FromBase64String 
            let! binaryDecoded = binarySerializer.Deserialize<BrokerAggregateMessageRef> base64Decoded
            return binaryDecoded
        }

    let getFromMessage<'E> value =
        ResultCE.result {
            let! okBinaryDecoded = getStrAggregateMessage value

            let id = okBinaryDecoded.AggregateId
            let eventId = okBinaryDecoded.EventId

            let message = okBinaryDecoded.BrokerEvent
            let actual = 
                match message with
                    | StrEvent x -> jsonPicklerSerializer.Deserialize<'E> x |> Result.get
                    | BinaryEvent x -> binPicklerSerializer.Deserialize<'E> x |> Result.get
            return (eventId, id, actual)
        }

    let logger = LogManager.GetLogger (System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    // beware of the string generic type in the aggregate. should work also with other types
    type ConsumerX<'A, 'E when 'E :> Event<'A> and 'A:> Aggregate<string>> 
        (topic: string, clientId: string, bootStrapServers: string, groupId: string, timeOut: int, fallBackViewer: AggregateViewer<'A>) =
        let mutable currentStates: Map<Guid, EventId * 'A>  = [] |> Map.ofList
        let mutable gMessages = []
        member this.GMessages = gMessages

        member this.GetEventsByAggregate aggregateId =
            ResultCE.result {
                let! messages = 
                    this.GMessages |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getFromMessage<'E>)
                let filtered = messages |> List.filter (fun (_, id, _) -> id = aggregateId)

                let sorted = filtered |> List.sortBy (fun (evId, _, _) -> evId)
                return sorted |>> fun (_, _, x) -> x
            }
        member this.GetEventsByAggregateNew aggregateId =
            ResultCE.result {
                let! messages = 
                    this.GMessages |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getFromMessage<'E>)
                let filtered = messages |> List.filter (fun (_, id, _) -> id = aggregateId)

                let sorted = filtered |> List.sortBy (fun (evId, _, _) -> evId)
                return sorted |>> fun (eventId, _, x) -> (eventId, x)
            }

        member this.GetMessages =
            gMessages 
            |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getStrAggregateMessage)

        member this.Consuming () =
            let log = Serilog.LoggerConfiguration().CreateLogger()
            let handler (messages : ConsumeResult<string, string> []) = async {
                // printf "\n\nWWWWWW. messages length %A\n\n\n" messages.Length
                for m in messages do
                    gMessages <- gMessages @ [m]
            } 

            let cfgGood1 = KafkaConsumerConfig.Create(clientId, bootStrapServers, [topic], groupId, AutoOffsetReset.Earliest)
            let timeOutConsumer (consumer: BatchedConsumer) =
                Async.Sleep timeOut |> Async.RunSynchronously
                consumer.Stop()
            async {
                use consumer = BatchedConsumer.Start(log, cfgGood1, handler)
                use _ = KafkaMonitor(log).Start(consumer.Inner, cfgGood1.Inner.GroupId)
                let result = consumer.AwaitWithStopOnCancellation()
                timeOutConsumer consumer
                return! result
            } |> Async.RunSynchronously

        member this.Update() =
            this.Consuming()
            let involvedAggregates = 
                this.GetMessages 
                |> Result.get |> List.map (fun x -> x.AggregateId)
                |> List.distinct

            // printf "involvedAggregates length %A\n" involvedAggregates.Length

            let currentAggregateStates = 
                involvedAggregates 
                |> List.map (fun id -> this.GetState id) 

            let cleanStates =
                currentAggregateStates
                |> List.filter (fun x -> Result.isOk x)
                |> List.map (fun x -> x |> Result.get)

            let cleanStatesMapWithEventid =
                cleanStates
                |>> fun x -> ((x |> snd).Id, x |> fst, (x |> snd))

            let eventsPerAggregatesWithEventId =
                involvedAggregates 
                |> List.map (fun id -> (id, this.GetEventsByAggregateNew id))
                |> List.filter (fun (_,   x) -> Result.isOk x)
                |> List.map (fun (id, x) -> (id, x |> Result.get))
                |> Map.ofList

            let applicableEventsByEventId =
                let cleanStateIds =  cleanStatesMapWithEventid |> List.map (fun (x, _, _) -> x)
                let cleanEventStatesIds = eventsPerAggregatesWithEventId |> Map.keys |> List.ofSeq
                let result = 
                    cleanStateIds
                    |> List.filter (fun x -> cleanEventStatesIds |> List.contains x)
                    |> List.map (fun x -> (x, eventsPerAggregatesWithEventId.[x]))
                result

            let filteredApplicableEvents =
                let currentEventIdByGuids = cleanStatesMapWithEventid |>> fun (x, y, z) -> (x, y)
                let applicableEventsWithEventIdGreaterThanCurrentState =
                    applicableEventsByEventId
                    |> List.map (fun (x, y) -> 
                        let currentEventId = currentEventIdByGuids |> List.filter (fun (id, _) -> id = x) |> List.head |> snd
                        let filtered = y |> List.filter (fun (eventId, _) -> eventId > currentEventId)
                        (x, filtered)
                    )
                applicableEventsWithEventIdGreaterThanCurrentState

            let stateAndEvents =
                cleanStatesMapWithEventid
                |> List.map (fun (id, eventId, state) -> 
                    let events = filteredApplicableEvents |> List.filter (fun (x, _) -> x = id) |> List.map snd |> List.head
                    (id, eventId, state, events)
                )

            let noGaps ints = 
                true

            // todo: introduce logic to make sure that out of sync events are not processed and refresh is done against the source of truth
            let noOutOfSyncStateAndEvents =
                stateAndEvents  

            let yesOutOfSyncStateAndEvents =
                []

                // sketch of the main idea
                // stateAndEvents  
                // |> List.filter (fun (_, eventId, _, events) ->
                //     printf "YYYYYY. eventId %A\n" eventId
                //     printf "YYYYYY. head %A\n" (if (events.IsEmpty) then 0 else (events |>> fst).Head)
                //     (not events.IsEmpty) &&
                //     (eventId <> 0 && eventId <> (events |>> fst).Head + 1) ||  
                //     not (noGaps (events |>> fst)))


            let evolvedStates =
                noOutOfSyncStateAndEvents
                |> List.map (fun (id, eventId, state, events) -> 
                    // let _ = printf "XXXXX. Evolving yeah! %A events are of length %A\n" id (events.Length)
                    let result = evolveUNforgivingErrors state (events |>> snd)
                    (id, result)
                )

            let okEvolvedStates =
                evolvedStates
                |> List.filter (fun (_, x) -> Result.isOk x)
                |> List.map (fun (id, x) -> (id, x |> Result.get)) 

            let okStatesGuid =
                okEvolvedStates |>> fst

            currentStates <- 
                currentStates |> Map.filter (fun x _ -> okStatesGuid |> List.contains x)
            let _ =
                okEvolvedStates
                |> List.iter (fun (id, state) -> 
                    currentStates <- currentStates.Add(id, (0, state))
                )

            let refreshedOutOfSync =
                yesOutOfSyncStateAndEvents
                |> List.iter (fun (id, _, _, _) -> 
                    logger.Error (sprintf "Aggregate %A is out of sync" id)
                )
                let outofsincIds = yesOutOfSyncStateAndEvents |> List.map (fun (id, _, _, _) -> id)
                let freshStates = outofsincIds |> List.map (fun id -> (id, fallBackViewer id))
                let okFreshStates = freshStates |> List.filter (fun (_,x) -> Result.isOk x) |> List.map (fun (id, x) -> (id, x |> Result.get))
                okFreshStates

            currentStates <-
                currentStates |> Map.filter (fun x _ -> (refreshedOutOfSync  |> List.map (fun (id, _) -> id) |> List.contains x) |> not)

            let _ =
                refreshedOutOfSync 
                |> List.iter  (fun (id, state) -> 
                    currentStates <- currentStates.Add(id, state))

            gMessages <- []
            ()

        member this.GetState (id: Guid): Result<EventId * 'A, string> =
            // printf "getting state for id %A\n" id
            let foundState = currentStates.TryFind id
            match foundState with
            | Some state -> 
                // printf "GGGGGG. found\n"; 
                state |> Result.Ok
            | _ -> 
                // printf "QQQ. not found"
                let state = fallBackViewer id
                match state with
                | Ok (eventId, state) -> 
                    currentStates <- currentStates.Add(id, (eventId, state))
                    (eventId, state) |> Result.Ok
                | Error e -> e |> Result.Error

    let getStrContextMessage message =
        ResultCE.result {
            let base64Decoded = message |> Convert.FromBase64String 
            let! binaryDecoded = binarySerializer.Deserialize<BrokerMessageRef> base64Decoded
            return binaryDecoded
        }

    let getContextEventFromMessage<'E> value =
        ResultCE.result {
            let! okBinaryDecoded = getStrContextMessage value

            let eventId = okBinaryDecoded.EventId

            let message = okBinaryDecoded.BrokerEvent
            let actual = 
                match message with
                    | StrEvent x -> jsonPicklerSerializer.Deserialize<'E> x |> Result.get
                    | BinaryEvent x -> binPicklerSerializer.Deserialize<'E> x |> Result.get
            return (eventId, id, actual)
        }
    

    type ConsumerY<'A, 'E when 'E :> Event<'A> >
        (topic: string, clientId: string, bootStrapServers: string, groupId: string, timeOut: int, start: 'A, fallbackViewer: StateViewer<'A>) =
            let mutable currentState: 'A = start
            // note that "start" must correspond to the same as fallbackViewer appied
            let mutable gMessages = []

            member this.GMessages = gMessages

            member this.GetEvents () =
                ResultCE.result {
                    let! messages = 
                        this.GMessages |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getContextEventFromMessage<'E>)
                    let sorted = messages |> List.sortBy (fun (evId, _, _) -> evId)
                    return sorted
                }

            member this.GetMessages =
                gMessages
                |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getStrContextMessage)

            member this.Consuming () =
                let log = Serilog.LoggerConfiguration().CreateLogger()
                let handler (messages : ConsumeResult<string, string> []) = async {
                    // printf "\n\nWWWWWW. messages length %A\n\n\n" messages.Length
                    for m in messages do
                        gMessages <- gMessages @ [m]
                } 

                let cfgGood1 = KafkaConsumerConfig.Create(clientId, bootStrapServers, [topic], groupId, AutoOffsetReset.Earliest)
                let timeOutConsumer (consumer: BatchedConsumer) =
                    Async.Sleep timeOut |> Async.RunSynchronously
                    consumer.Stop()
                async {
                    use consumer = BatchedConsumer.Start(log, cfgGood1, handler)
                    use _ = KafkaMonitor(log).Start(consumer.Inner, cfgGood1.Inner.GroupId)
                    let result = consumer.AwaitWithStopOnCancellation()
                    timeOutConsumer consumer
                    return! result
                } |> Async.RunSynchronously

            member this.Update () =
                this.Consuming () 
                let events = this.GetEvents ()
                if Result.isError events then
                    log.Error "error" 
                    ()
                    // logger.Error (events |> Result.getError)
                else
                    let sortedEvents = 
                        events |> Result.get
                        |> List.sortBy (fun (evId, _, _) -> evId)
                        |> List.map (fun (evId, _, x) -> x )
                    // printf "this is the length of the events %A\n" sortedEvents.Length
                    let result = evolveUNforgivingErrors currentState  sortedEvents
                    match result with
                    | Ok state -> currentState <- state
                    | Error e -> log.Error e
                    gMessages <- []
                    ()
                    
            member this.GetState () =
                (0, currentState) |> Ok





                    



