
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
        
        member this.Assign(position: int64, partition: Partition) =
            cons.Assign([new TopicPartitionOffset(topic, partition, position)])
            ()
            
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
                    printf "entered in consumewithtimeout\n"
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
                    let result = cons.Consume(cancellationTokenSource.Token)
                    printf "exited in consumewithtimeout\n"
                    return result
                with 
                | _ -> 
                    printf "Timeout! "
                    return! "timeout" |> Result.Error
            }            
        static member Create(bootStrapServer: string, version: string, name: string, groupId: string) =
            try
                KafkaSubscriber(bootStrapServer, version, name, groupId) |> Ok
            with 
            | _ as e -> Result.Error (e.Message)

    type KafkaViewer<'A, 'E when 'E :> Event<'A>> (subscriber: KafkaSubscriber, currentState: EventId*'A, eventStore: IEventStore, sourceOfTruthStateViewer: unit -> Result<EventId * 'A * Option<int64>, string>) =
        let mutable state = sourceOfTruthStateViewer() |> Result.get
        member this.State = state
        member this.Refresh() =
            printf "entered in refresh\n"
            let result = subscriber.consumeWithTimeOut(100)
            match result with
            | Error e -> 
                printf "ErrorX %A\n" e
                Result.Error e // "error"
            | Ok msg ->
                printf "Message %A\n" msg
                let newMessage = msg.Message.Value |> serializer.Deserialize<BrokerMessage> |> Result.get
                let newEvent = newMessage.Event |> serializer.Deserialize<'E> |> Result.get
                printf "XXX. this is the event %A\n" newEvent
                let eventId = newMessage.EventId 
                let (_, currentState, _) = this.State
                let newState = evolve currentState [newEvent] |> Result.get
                state <- (eventId, newState, None)
                () |> Result.Ok
            // ()
        // member this.RefreshEveryNSeconds(n: int) =
        //     let rec loop() =
        //         this.Refresh()
        //         System.Threading.Thread.Sleep(n * 1000)
        //         loop()
        //     loop()
        
        member this.ForceSyncWithEventStore() = 
            ResultCE.result {
                let! newState = sourceOfTruthStateViewer()
                state <- newState 
                return ()
            }
        interface IHostedService with
            member this.StartAsync(cancellationToken: Threading.CancellationToken): Task = 
                // Task.Run(fun () ->
                //     printf "XXXX. Entered in task\n"
                //     this.Refresh()
                // )
                Task.Run(fun () ->
                    printf "first entry\n"
                    // let rec loop () =
                    let mutable refreshed = 
                        match this.Refresh() with
                        | Ok _ -> true
                        | Error _ -> false
                    while refreshed do
                        printf "XXXX. Entered in task\n"
                        let refreshed = 
                            match this.Refresh() with
                            | Ok _ -> true
                            | Error _ -> false
                        System.Threading.Thread.Sleep(1000)
                        printf "XXXX. exiting from loop\n"
                        printf "STATE: %A\n" this.State |> ignore
                    // loop()
                    
                )

            member this.StopAsync(cancellationToken: Threading.CancellationToken): Task = 
                failwith "Not Implemented" 

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
        (eventStore: IEventStore) =
        let sourceOfTruthStateViewer = CommandHandler.getStorageFreshStateViewer<'A, 'E> eventStore
        KafkaViewer<'A, 'E>(subscriber, (0, 'A.Zero), eventStore, sourceOfTruthStateViewer)
