
namespace Sharpino
open Sharpino.Storage
open Confluent.Kafka
open System.Net
open System
open FsToolkit.ErrorHandling
open FsToolkit.ErrorHandling.ResultCE
open Npgsql
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Storage
open log4net
open log4net.Config
open FsToolkit.ErrorHandling.ResultCE
open FSharp.Core

module KafkaBroker =

    let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    type BrokerMessage = {
        ApplicationId: Guid
        EventId: int
        Event: Json
    }
    
    type BrokerAggregateMessage = {
        ApplicationId: Guid
        AggregateId: Guid
        EventId: int
        Event: Json
    }

    let log = LogManager.GetLogger(Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // uncomment following for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    let getKafkaBroker (bootStrapServer: string, eventStore: IEventStore<string>) =
        try 
            let config = ProducerConfig()
            config.BootstrapServers <- bootStrapServer
            let producer = ProducerBuilder<string, string>(config)
            let p = producer.Build()

            let aggregateProducer = ProducerBuilder<Null, string>(config)
            let aggregateP = aggregateProducer.Build()


            let message = Message<string, string>()
            let aggregatemessage = Message<Null, string>()

            let notifyAggregateMessage  (version: string) (name: string) (aggregateId: Guid) (msg: int * Json) =
                let topic = name + "-" + version |> String.replace "_" ""

                let brokerMessage = {
                    AggregateId = aggregateId
                    ApplicationId = ApplicationInstance.ApplicationInstance.Instance.GetGuid()
                    EventId = msg |> fst
                    Event = msg |> snd
                }

                try
                    let sent =
                        aggregatemessage.Key <- null
                        aggregatemessage.Value <- brokerMessage |> serializer.Serialize
                        aggregateP.ProduceAsync(topic, aggregatemessage)
                        |> Async.AwaitTask 
                        |> Async.RunSynchronously

                    if sent.Status = PersistenceStatus.Persisted then
                        let offset = sent.Offset.Value
                        let partition = sent.Partition.Value
                        let isPublished = eventStore.SetPublished version name (msg |> fst) offset partition
                        match isPublished with
                        | Ok _  -> sent |> Ok
                        | Error e -> Error e
                    else        
                        Error(sprintf "Message %A Not persisted in Kafka topic. Sent: %A" message sent)
                with
                    | _ as e -> 
                        log.Error e.Message
                        Error(e.Message.ToString())
            
            let notifyMessage (version: string) (name: string)  (msg: int * Json) =
                let topic = name + "-" + version |> String.replace "_" ""

                let brokerMessage = {
                    ApplicationId = ApplicationInstance.ApplicationInstance.Instance.GetGuid()
                    EventId = msg |> fst
                    Event = msg |> snd
                }

                try
                    let sent =
                        message.Key <- topic
                        message.Value <- brokerMessage |> serializer.Serialize
                        p.ProduceAsync(topic, message)
                        |> Async.AwaitTask 
                        |> Async.RunSynchronously

                    if sent.Status = PersistenceStatus.Persisted then
                        let offset = sent.Offset.Value
                        let partition = sent.Partition.Value
                        let isPublished = eventStore.SetPublished version name (msg |> fst) offset partition
                        match isPublished with
                        | Ok _  -> sent |> Ok
                        | Error e -> Error e
                    else        
                        Error(sprintf "Message %A Not persisted in Kafka topic. Sent: %A" message sent)
                with
                    | _ as e -> 
                        log.Error e.Message
                        Error(e.Message.ToString())

            let notifier: IEventBroker =
                {
                    notify = 
                        fun version name events ->
                            result {    
                                try 
                                    let notified = events |> List.traverseResultM (fun x -> notifyMessage version name x) 
                                    let notified2 =
                                        match notified with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 1 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyMessage version name x)

                                    let notified3 =
                                        match notified2 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 2 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyMessage version name x)

                                    let notified4 =
                                        match notified3 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 3 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyMessage version name x)

                                    let notified5 =
                                        match notified4 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 4 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyMessage version name x)
                                    return! notified5
                                with
                                | _ as e -> 
                                    log.Error e.Message
                                    return! Result.Error (e.Message.ToString())
                            }
                        |> Some
                    notifyAggregate =
                        fun version name aggregateId events ->
                            result {    
                                try 
                                    let notified = events |> List.traverseResultM (fun x -> notifyAggregateMessage version name aggregateId x) 
                                    let notified2 =
                                        match notified with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 1 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyAggregateMessage version name aggregateId x)

                                    let notified3 =
                                        match notified2 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 2 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyAggregateMessage version name aggregateId x)

                                    let notified4 =
                                        match notified3 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 3 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyAggregateMessage version name aggregateId x)

                                    let notified5 =
                                        match notified4 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 4 %s" e)
                                            events |> List.traverseResultM (fun x -> notifyAggregateMessage version name aggregateId x)
                                    return! notified5
                                with
                                | _ as e -> 
                                    log.Error e.Message
                                    return! Result.Error (e.Message.ToString())
                            }
                        |> Some
                }
            notifier
        with
        | _ as e -> 
            log.Error e.Message
            {
                notify = None
                notifyAggregate = None
            }

    let tryPublish eventBroker version name idAndEvents =
        async {
            return
                match eventBroker.notify with
                | Some notify ->
                    notify version name idAndEvents
                | None ->
                    log.Info "no sending to any broker"
                    [] |> Ok
        }
        |> Async.StartAsTask
    
    let tryPublishAggregateEvent eventBroker aggregateId version name idAndEvents =
        async {
            return
                match eventBroker.notifyAggregate with
                | Some notify ->
                    notify version name aggregateId idAndEvents
                | None ->
                    log.Info "no sending to any broker"
                    [] |> Ok
        }
        |> Async.StartAsTask
