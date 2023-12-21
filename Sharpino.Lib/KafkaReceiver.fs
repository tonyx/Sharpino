
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
        
        // member this.Assign(position: int64, partition: Partition) =
        //     cons.Assign([new TopicPartitionOffset(topic, partition, position)])
        //     ()
            
        // at the moment I rely on this to make the subscriber start from the right position
        member this.Assign2(position: int64, partition: int) =
            let partition = new Partition(partition) 
            cons.Assign ([new TopicPartitionOffset(topic, partition, position)])
            () 

        member this.Consume () =
            let result = cons.Consume()
            result
            
        member this.consumeWithTimeOut(timeoutMilliseconds: int): Result<ConsumeResult<Null, string>, string> =
            ResultCE.result {
                try
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
                    let result = cons.Consume(cancellationTokenSource.Token)
                    return result
                with 
                | _ -> 
                    // printf "Timeout! "
                    return! "timeout" |> Result.Error
            }            
        static member Create(bootStrapServer: string, version: string, name: string, groupId: string) =
            try
                KafkaSubscriber(bootStrapServer, version, name, groupId) |> Ok
            with 
            | _ as e -> Result.Error (e.Message)

    type KafkaViewer<'A, 'E when 'E :> Event<'A>> 
        (subscriber: KafkaSubscriber, 
        sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<int64>, string>,
        appId: Guid)
        =

        let mutable state = 
            try
                sourceOfTruthStateViewer() |> Result.get
            with
            | _ -> 
                log.Error "cannot get the state from the source of truth\n"
                failwith "error" 

        // let _ =
        //     let (eventId, _, offSet) = state
        //     match offSet with
        //     | Some offset -> 
        //         printf "XXXX. assigning"
        //         subscriber.Assign2(eventId, offset |> int)
        //     | _ -> 
        //         printf "XXXX. not assigning"
        //         ()


        member this.State = state

        member this.Refresh() =
            let result = subscriber.consumeWithTimeOut(100)
            match result with
            | Error e -> 
                Result.Error e 
            | Ok msg ->
                ResultCE.result {
                    let! newMessage = msg.Message.Value |> serializer.Deserialize<BrokerMessage> // |> Result.get
                    let msgAppId = newMessage.ApplicationId
                    let! newEvent = newMessage.Event |> serializer.Deserialize<'E> // |> Result.get
                    let eventId = newMessage.EventId 
                    let (_, currentState, _) = this.State
                    if appId <> msgAppId then
                        ()
                    else
                        let! newState = evolve currentState [newEvent] 
                        state <- (eventId, newState, None)
                    return () |> Result.Ok
                }

        member this.RefreshLoop() =
            let mutable refreshed = 
                this.Refresh() |> resultToBool
            while refreshed do
                refreshed <-  
                    this.Refresh() |> resultToBool

        member this.ForceSyncWithEventStore() = 
            ResultCE.result {
                let! newState = sourceOfTruthStateViewer()
                state <- newState 
                return ()
            }

        // interface IHostedService with
        //     member this.StartAsync(cancellationToken: Threading.CancellationToken): Task = 
        //         Task.Run(fun () ->
        //             printf "first entry\n"
        //             // let rec loop () =
        //             let mutable refreshed = 
        //                 match this.RefreshByApplicationId() with
        //                 | Ok _ -> true
        //                 | Error _ -> false
        //             while refreshed do
        //                 // printf "XXXX. Entered in task\n"
        //                 let refreshed = 
        //                     match this.RefreshByApplicationId() with
        //                     | Ok _ -> true
        //                     | Error _ -> false
        //                 System.Threading.Thread.Sleep(1000)
        //                 // printf "XXXX. exiting from loop\n"
        //                 // printf "STATE: %A\n" this.State |> ignore
        //             // loop()
                    
        //         )

        //     member this.StopAsync(cancellationToken: Threading.CancellationToken): Task = 
        //         failwith "Not Implemented" 

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
        (sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<int64>, string>) 
        (applicationId: Guid) 
        =
        KafkaViewer<'A, 'E>(subscriber, sourceOfTruthStateViewer, applicationId)


