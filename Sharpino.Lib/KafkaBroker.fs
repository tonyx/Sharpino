
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
        Event: string
    }

    let log = LogManager.GetLogger(Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // uncomment following for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    // this implementation cannot avoid the annoying messages that producer.Build prints when kafka is not running
    // let getKafkaBroker (bootStrapServer: string, pgConnection: string) =
    let getKafkaBroker (bootStrapServer: string, eventStore: IEventStore) =
        try 
            let config = ProducerConfig()
            config.BootstrapServers <- bootStrapServer
            let producer = ProducerBuilder<Null, string>(config)
            let p = producer.Build()
            let message = Message<Null, string>()

            let notifyMessage (version: string) (name: string)  (msg: int * string) =
                let topic = name + "-" + version |> String.replace "_" ""

                let brokerMessage = {
                    ApplicationId = ApplicationInstance.ApplicationInstance.Instance.GetGuid()
                    EventId = msg |> fst
                    Event = msg |> snd
                }

                try
                    let sent =
                        message.Key <- null
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
                        Error("Not persisted in Kafka")
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
                                    let notified = events |> catchErrors (fun x -> notifyMessage version name x) 
                                    let notified2 =
                                        match notified with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 1 %s" e)
                                            events |> catchErrors (fun x -> notifyMessage version name x)

                                    let notified3 =
                                        match notified2 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 2 %s" e)
                                            events |> catchErrors (fun x -> notifyMessage version name x)

                                    let notified4 =
                                        match notified3 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 3 %s" e)
                                            events |> catchErrors (fun x -> notifyMessage version name x)

                                    let notified5 =
                                        match notified4 with
                                        | Ok x -> Ok x 
                                        | Error e -> 
                                            log.Error (sprintf "retry send n. 4 %s" e)
                                            events |> catchErrors (fun x -> notifyMessage version name x)
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
            }

    let notify (broker: IEventBroker) (version: string) (name: string) (idAndEvents: List<int * string>) =
        match broker.notify with
        | Some notify ->
            notify version name idAndEvents
        | None ->
            log.Info "no broker configured"
            [] |> Ok

    let tryPublish eventBroker version name idAndEvents =
        async {
            return
                notify eventBroker version name idAndEvents
        }
        |> Async.StartAsTask
        |> Async.AwaitTask
        |> Async.RunSynchronously
