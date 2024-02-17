
namespace Sharpino

open FsToolkit.ErrorHandling
open FSharpPlus
open Sharpino
open Sharpino.Storage
open Sharpino.Definitions
open Confluent.Kafka
open Sharpino.Core
open Sharpino.Utils
open Sharpino.KafkaBroker
open System
open log4net
open log4net.Config
module KafkaReceiver =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    let config = 
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            // if appSettings.json is missing
            log.Error (sprintf "appSettings.json file not found using defult!!! %A\n" ex)
            Conf.defaultConf
            
    type KafkaSubscriber(bootStrapServer: string, version: string, name: string, groupId: string) =
        let topic = name + "-" + version |> String.replace "_" ""

        let config = ConsumerConfig()
        let _ = config.GroupId <- groupId
        let _ = config.BootstrapServers <- bootStrapServer
        let _ = config.AutoOffsetReset <- AutoOffsetReset.Earliest
        let _ = config.EnableAutoCommit <- false

        let consumer = new ConsumerBuilder<Null, string>(config)
        let cons = consumer.Build () 
        let _ = cons.Subscribe topic 
        
        member this.Assign(position: int64, partition: int) =
            let partition = Partition(partition) 
            cons.Assign [TopicPartitionOffset(topic, partition, position)]
            () 

        member this.Consume () =
            let result = cons.Consume ()
            result
            
        // too late to change the name
        member this.consume(timeoutMilliseconds: int): Result<ConsumeResult<Null, string>, string> =
            ResultCE.result {
                try
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
                    let result = cons.Consume cancellationTokenSource.Token
                    return result
                with 
                | _ -> 
                    return! "timeout" |> Result.Error
            }            
        static member Create(bootStrapServer: string, version: string, name: string, groupId: string) =
            try
                KafkaSubscriber(bootStrapServer, version, name, groupId) |> Ok
            with 
            | _ as e -> Result.Error (e.Message)
            
    type GuidDeserializer() =
        interface IDeserializer<Guid> with
            member _.Deserialize(data: ReadOnlySpan<byte>, isNull: bool, context: SerializationContext) =
                if isNull then Guid.Empty
                else Guid(data.ToArray())
            
    type KafkaAggregateSubscriber(bootStrapServer: string, version: string, name: string, groupId: string, aggregateId: Guid) =
        let topic = name + "-" + version |> String.replace "_" ""

        let config = ConsumerConfig()
        let _ = config.GroupId <- groupId
        let _ = config.BootstrapServers <- bootStrapServer
        let _ = config.AutoOffsetReset <- AutoOffsetReset.Earliest
        let _ = config.EnableAutoCommit <- false

        let consumer = new ConsumerBuilder<Guid, string>(config)
        let _ =
            consumer.SetKeyDeserializer(GuidDeserializer())
        let cons = consumer.Build ()
        let _ = cons.Subscribe topic 
        
        member this.Assign(position: int64, partition: int) =
            let partition = Partition(partition) 
            cons.Assign [TopicPartitionOffset(topic, partition, position)]
            () 

        member this.Consume () =
            let result = cons.Consume ()
            result
            
        // too late to change the name
        member this.consume(timeoutMilliseconds: int): Result<ConsumeResult<AggregateId, string>, string> =
            ResultCE.result {
                try
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
                    let result = cons.Consume cancellationTokenSource.Token
                    return result
                with 
                | _ -> 
                    return! "timeout" |> Result.Error
            }            
        static member Create(bootStrapServer: string, version: string, name: string, groupId: string, aggregateId: Guid) =
            try
                KafkaAggregateSubscriber(bootStrapServer, version, name, groupId, aggregateId) |> Ok
            with 
            | _ as e -> Result.Error (e.Message)

    // todo: unify with KafkaViewer
    type KafkaAggregateViewer<'A, 'E when 'E :> Event<'A>> 
        (aggregateId: Guid,
        subscriber: KafkaAggregateSubscriber, 
        // subscriber: KafkaSubscriber, 
        sourceOfTruthStateViewer: AggregateId -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>,
        appId: Guid)
        =
        let mutable state = 
            try
                sourceOfTruthStateViewer aggregateId 
            with
            | e  -> 
                "state error" |> Result.Error
        let _ =
            match state with
            | Ok ( _, _, Some offset, Some partition ) ->
                subscriber.Assign ( offset + 1L, partition )
            | _ -> 
                log.Info "Cannot assign offset and partition because they are None"
                ()

        member this.State () = 
            state

        member this.Refresh () =
            let result = subscriber.consume config.RefreshTimeout
            match result with
            | Error e -> 
                log.Error e
                Result.Error e 
            | Ok msg ->
                ResultCE.result {
                    let! newMessage = msg.Message.Value |> serializer.Deserialize<BrokerAggregateMessage>
                    let eventId = newMessage.EventId
                    let! currentStateId, _, _, _ = this.State ()
                    if eventId = currentStateId + 1 then
                        let msgAppId = newMessage.ApplicationId
                        let msgAggregateId = newMessage.AggregateId
                        let! newEvent = newMessage.Event |> serializer.Deserialize<'E>
                        let! (_, currentState, _, _) = this.State ()
                        if appId <> msgAppId || aggregateId <> msgAggregateId then
                            ()
                        else
                            let! newState = evolve currentState [newEvent] 
                            state <- ( eventId, newState, None, None ) |> Result.Ok
                        return () |> Result.Ok
                    else
                        let! _ = this.ForceSyncWithSourceOfTruth()
                        let! _, _, offset, partition = this.State ()
                        let _ =
                            match offset, partition with
                            | Some off, Some part ->
                                subscriber.Assign( off + 1L, part )
                            | _ -> 
                                log.Error "Cannot assign offset and partition"
                                ()
                        return () |> Result.Ok
                }
                
        member this.RefreshLoop () =
            let mutable result = this.Refresh ()
            while (result |> Result.toOption).IsSome do
                result <- this.Refresh()
                ()
            ()

        member this.ForceSyncWithSourceOfTruth() = 
            ResultCE.result {
                let! newState = sourceOfTruthStateViewer aggregateId
                state <- newState |> Result.Ok
                let! _, _, offset, partition = this.State ()
                match offset, partition with
                | Some off, Some part ->
                    subscriber.Assign ( off + 1L, part )
                | _ ->
                    log.Error "Cannot assign offset and partition"
                    ()
                return ()
            }
    
    type KafkaViewer<'A, 'E when 'E :> Event<'A>> 
        (subscriber: KafkaSubscriber, 
        sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>,
        appId: Guid)
        =
        let mutable state = 
            try
                sourceOfTruthStateViewer ()
            with
            | e  -> 
                "state error" |> Result.Error

        let _ =
            match state with
            | Ok ( _, _, Some offset, Some partition ) ->
                subscriber.Assign (offset + 1L, partition)
            | _ -> 
                log.Info "Cannot assign offset and partition because they are None"
                ()

        member this.State () =
            // printf "getting state %A\n" (state |> serializer.Serialize)
            state

        member this.Refresh() =
            let result = subscriber.consume config.RefreshTimeout
            match result with
            | Error e ->
                log.Error e
                Result.Error e 
            | Ok msg ->
                ResultCE.result {
                    let! newMessage = msg.Message.Value |> serializer.Deserialize<BrokerMessage>
                    let eventId = newMessage.EventId
                    let! currentStateId, _, _, _ = this.State ()
                    if eventId = currentStateId + 1 then
                        let msgAppId = newMessage.ApplicationId
                        let! newEvent = newMessage.Event |> serializer.Deserialize<'E>
                        let! _, currentState, _, _ = this.State ()
                        if appId <> msgAppId then
                            ()
                        else
                            let! newState = evolve currentState [newEvent] 
                            state <- (eventId, newState, None, None) |> Result.Ok
                        return () |> Result.Ok
                    else
                        let! _ = this.ForceSyncWithSourceOfTruth ()
                        let! _, _, offset, partition = this.State ()
                        let _ =
                            match offset, partition with
                            | Some off, Some part ->
                                subscriber.Assign(off + 1L, part)
                            | _ -> 
                                log.Error "Cannot assign offset and partition"
                                ()
                        return () |> Result.Ok
                }

        member this.RefreshLoop() =
            let mutable result = this.Refresh ()
            while ( result |> Result.toOption ).IsSome do
                result <- this.Refresh ()
                ()
            ()
        member this.ForceSyncWithSourceOfTruth() = 
            ResultCE.result {
                let! newState = sourceOfTruthStateViewer ()
                state <- newState |> Result.Ok
                let! _, _, offset, partition = this.State ()
                match offset, partition with
                | Some off, Some part ->
                    subscriber.Assign ( off + 1L, part )
                | _ ->
                    log.Error "Cannot assign offset and partition"
                    ()
                return ()
            }

    let inline mkKafkaViewer<'A, 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Lock: obj)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'A: (static member SnapshotsInterval : int)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (subscriber: KafkaSubscriber) 
        (sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>) 
        (applicationId: Guid) 
        =
        KafkaViewer<'A, 'E>(subscriber, sourceOfTruthStateViewer, applicationId)

    let inline mkKafkaAggregateViewer<'A, 'E
        when 'A:> Aggregate
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (member Serialize: ISerializer -> string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E: (member Serialize: ISerializer -> string)
        >
        (aggregateId: Guid)
        // (subscriber: KafkaSubscriber) 
        (subscriber: KafkaAggregateSubscriber) 
        (sourceOfTruthStateViewer: Guid -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>) 
        (applicationId: Guid) 
        =
            KafkaAggregateViewer<'A, 'E>(aggregateId, subscriber, sourceOfTruthStateViewer, applicationId)

