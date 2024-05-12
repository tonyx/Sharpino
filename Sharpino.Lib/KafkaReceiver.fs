
namespace Sharpino

open FsToolkit.ErrorHandling
open FSharpPlus
open Sharpino
open Sharpino.Commons
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

    type ConsumerX<'A, 'E when 'E :> Event<'A>> (initStates: List<'A>, topic: string, clientId: string, bootStrapServers: string, groupId: string, timeOut: int) =
        let mutable gMessages = []
        member this.GMessages = gMessages

        member this.GetEvents =
            ResultCE.result {
                let! messages = 
                    this.GMessages |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getFromMessage<'E>)
                return messages |>> fun (_, _, x) -> x
            }

        member this.GetEventsByAggregate aggregateId =
            ResultCE.result {
                let! messages = 
                    this.GMessages |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getFromMessage<'E>)
                let filtered = messages |> List.filter (fun (_, id, _) -> id = aggregateId)

                let sorted = filtered |> List.sortBy (fun (evId, _, _) -> evId)
                return sorted |>> fun (_, _, x) -> x
            }

        member this.GetMessages =
            gMessages 
            |> List.map (fun (m: ConsumeResult<string, string>) -> m.Message.Value) |> List.traverseResultM (fun m -> m |> getStrAggregateMessage)

        member this.Consuming () =
            let log = Serilog.LoggerConfiguration().CreateLogger()
            let handler (messages : ConsumeResult<string, string> []) = async {
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









































    // let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // let config = 
    //     try
    //         Conf.config ()
    //     with
    //     | :? _ as ex -> 
    //         // if appSettings.json is missing
    //         log.Error (sprintf "appSettings.json file not found using defult!!! %A\n" ex)
    //         Conf.defaultConf
            
    // type KafkaSubscriber(bootStrapServer: string, version: string, name: string, groupId: string) =
    //     let topic = name + "-" + version |> String.replace "_" ""

    //     let config = ConsumerConfig()
    //     let _ = config.GroupId <- groupId
    //     let _ = config.BootstrapServers <- bootStrapServer
    //     let _ = config.AutoOffsetReset <- AutoOffsetReset.Earliest
    //     let _ = config.EnableAutoCommit <- false

    //     let consumer = new ConsumerBuilder<string, string>(config)
    //     let cons = consumer.Build () 
    //     let _ = cons.Subscribe topic 
        
    //     member this.Assign(position: int64, partition: int) =
    //         let partition = Partition(partition) 
    //         cons.Assign [TopicPartitionOffset(topic, partition, position)]
    //         () 

    //     member this.Consume () =
    //         let result = cons.Consume ()
    //         result
    //     member this.Reset() =
    //         let _ = cons.Close()
    //         let _ = cons.Dispose()
    //         let consumer = new ConsumerBuilder<string, string>(config)
    //         let cons = consumer.Build () 
    //         let _ = cons.Subscribe topic 
    //         ()
    //     // too late to change the name
    //     member this.consume(timeoutMilliseconds: int): Result<ConsumeResult<string, string>, string> =
    //         ResultCE.result {
    //             try
    //                 let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
    //                 let result = cons.Consume cancellationTokenSource.Token
    //                 return result
    //             with 
    //             | _ -> 
    //                 return! "timeout" |> Result.Error
    //         }            
    //     static member Create(bootStrapServer: string, version: string, name: string, groupId: string) =
    //         try
    //             KafkaSubscriber(bootStrapServer, version, name, groupId) |> Ok
    //         with 
    //         | _ as e -> Result.Error (e.Message)
            
    // type GuidDeserializer() =
    //     interface IDeserializer<Guid> with
    //         member _.Deserialize(data: ReadOnlySpan<byte>, isNull: bool, context: SerializationContext) =
    //             if isNull then Guid.Empty
    //             else Guid(data.ToArray())
    
    // type KafkaViewer<'A, 'E when 'E :> Event<'A>> 
    //     (subscriber: KafkaSubscriber, 
    //     sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>,
    //     appId: Guid)
    //     =
    //     let mutable state = 
    //         try
    //             sourceOfTruthStateViewer ()
    //         with
    //         | e  -> 
    //             "state error" |> Result.Error

    //     let _ =
    //         match state with
    //         | Ok ( _, _, Some offset, Some partition ) ->
    //             subscriber.Assign (offset + 1L, partition)
    //         | _ -> 
    //             log.Info "Cannot assign offset and partition because they are None"
    //             ()

    //     member this.State () =
    //         state

    //     member this.Refresh() =
    //         let result = subscriber.consume config.RefreshTimeout
    //         match result with
    //         | Error e ->
    //             log.Error e
    //             Result.Error e 
    //         | Ok msg ->
    //             ResultCE.result {
    //                 let! newMessage = msg.Message.Value |> serializer.Deserialize<BrokerMessage<'F>>
    //                 let eventId = newMessage.EventId
    //                 let! currentStateId, _, _, _ = this.State ()
    //                 if eventId = currentStateId + 1 then
    //                     let msgAppId = newMessage.ApplicationId
    //                     let! newEvent = newMessage.Event |> serializer.Deserialize<'E>
    //                     let! _, currentState, _, _ = this.State ()
    //                     if appId <> msgAppId then
    //                         ()
    //                     else
    //                         let! newState = evolve currentState [newEvent] 
    //                         state <- (eventId, newState, None, None) |> Result.Ok
    //                     return () |> Result.Ok
    //                 else
    //                     let! _ = this.ForceSyncWithSourceOfTruth ()
    //                     let! _, _, offset, partition = this.State ()
    //                     let _ =
    //                         match offset, partition with
    //                         | Some off, Some part ->
    //                             subscriber.Assign(off + 1L, part)
    //                         | _ -> 
    //                             log.Error "Cannot assign offset and partition"
    //                             ()
    //                     return () |> Result.Ok
    //             }

    //     member this.RefreshLoop() =
    //         let mutable result = this.Refresh ()
    //         while ( result |> Result.toOption ).IsSome do
    //             result <- this.Refresh ()
    //             ()
    //         ()
            
    //     member this.ForceSyncWithSourceOfTruth() = 
    //         ResultCE.result {
    //             let! newState = sourceOfTruthStateViewer ()
    //             state <- newState |> Result.Ok
    //             let! _, _, offset, partition = this.State ()
    //             match offset, partition with
    //             | Some off, Some part ->
    //                 subscriber.Assign ( off + 1L, part )
    //             | _ ->
    //                 log.Error "Cannot assign offset and partition"
    //                 ()
    //             return ()
    //         }

    // let inline mkKafkaViewer<'A, 'E
    //     when 'A: (static member Zero: 'A)
    //     and 'A: (static member StorageName: string)
    //     and 'A: (static member Version: string)
    //     and 'A: (static member Lock: obj)
    //     and 'A: (member Serialize: string)
    //     and 'A: (static member Deserialize: Json -> Result<'A, string>)
    //     and 'A: (static member SnapshotsInterval : int)
    //     and 'E :> Event<'A>
    //     and 'E: (static member Deserialize: Json -> Result<'E, string>)
    //     and 'E: (member Serialize: string)
    //     >
    //     (subscriber: KafkaSubscriber) 
    //     (sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>) 
    //     (applicationId: Guid) 
    //     =
    //     KafkaViewer<'A, 'E>(subscriber, sourceOfTruthStateViewer, applicationId)


