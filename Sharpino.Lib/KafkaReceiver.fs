
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Storage
open Sharpino.Definitions
open log4net
open log4net.Config
open Confluent.Kafka
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.CommandHandler
open Sharpino.KafkaBroker
open System
open Farmer
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
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
    let resultToBool = function
        | Ok _ -> true
        | Error _ -> false

    type KafkaSubscriber(bootStrapServer: string, version: string, name: string, groupId: string) =
        let topic = name + "-" + version |> String.replace "_" ""

        let config = ConsumerConfig()
        let _ = config.GroupId <- groupId
        let _ = config.BootstrapServers <- bootStrapServer
        let _ = config.AutoOffsetReset <- AutoOffsetReset.Earliest
        let _ = config.EnableAutoCommit <- false

        let consumer = new ConsumerBuilder<Null, string>(config)
        let cons = consumer.Build () 
        let _ = cons.Subscribe(topic)
        
        member this.Assign2(position: int64, partition: int) =
            // ()
            let partition = new Partition(partition) 
            cons.Assign ([new TopicPartitionOffset(topic, partition, position)])
            () 

        member this.Consume () =
            let result = cons.Consume()
            result
            
        member this.consume(timeoutMilliseconds: int): Result<ConsumeResult<Null, string>, string> =
            ResultCE.result {
                try
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
                    let result = cons.Consume(cancellationTokenSource.Token)
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

    type KafkaViewer<'A, 'E when 'E :> Event<'A>> 
        (subscriber: KafkaSubscriber, 
        sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<KafkaOffset> * Option<KafkaPartitionId>, string>,
        appId: Guid)
        =
        let mutable state = 
            try
                sourceOfTruthStateViewer() |> Result.get
            with
            | _ -> 
                log.Error "cannot get the state from the source of truth\n"
                failwith "error" 


        let (_, _, offset, partition) = state

        let _ =
            match offset, partition with
            | Some off, Some part ->
                printf "XXX. assigning partition and offset\n"
                // subscriber.Assign2(off, part)
                // should start from next message so: offset + 1
                subscriber.Assign2(off + 1L, part)
            | _ -> ()

        member this.State () = 
            state
        // member this.State = state




        member this.Refresh() =
            printf "refreshing\n"
            let result = subscriber.consume(config.RefreshTimeout)
            match result with
            | Error e -> 
                log.Error e
                printf "error in consuming from subscriber %A\n" e
                Result.Error e 
            | Ok msg ->
                printf "XXX. message consumed\n"
                ResultCE.result {
                    let! newMessage = msg.Message.Value |> serializer.Deserialize<BrokerMessage>
                    let eventId = newMessage.EventId
                    let currentStateid, _, _, _ = this.State ()
                    if eventId = currentStateid + 1 then
                        let msgAppId = newMessage.ApplicationId
                        let! newEvent = newMessage.Event |> serializer.Deserialize<'E>
                        let (_, currentState, _, _) = this.State ()
                        printf "XXX. appId: %A\n" (appId.ToString())
                        printf "XXX. msgAppId: %A\n" (msgAppId.ToString())
                        if appId <> msgAppId then
                            ()
                        else
                            let! newState = evolve currentState [newEvent] 
                            state <- (eventId, newState, None, None)
                        return () |> Result.Ok
                    else
                        let! _ = this.ForceSyncWithSourceOfTruth()
                        let _ =
                            let _, _, offset, partition = this.State ()
                            match offset, partition with
                            | Some off, Some part ->
                                subscriber.Assign2(off + 1L, part)
                            | _ -> 
                                log.Error "Cannot assign offset and partition"
                                ()
                        return () |> Result.Ok
                }

        member this.RefreshLoop() =
            printf "refresh loop\n"
            let mutable refreshed = 
                this.Refresh() |> resultToBool
            while refreshed do
                refreshed <-  
                    this.Refresh() |> resultToBool

        member this.ForceSyncWithSourceOfTruth() = 
            ResultCE.result {
                let! newState = sourceOfTruthStateViewer()
                state <- newState 
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


