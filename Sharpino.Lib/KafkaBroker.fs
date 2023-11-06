
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

    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    let getKafkaBroker (bootStrapServer: string, pgConnection: string) =
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
                    let streamName = version + name
                    let updateQuery = sprintf "UPDATE events%s SET published = true WHERE id = '%d'" streamName (msg |> fst)
                    pgConnection 
                    |> Sql.connect
                    |> Sql.query updateQuery
                    |> Sql.executeNonQuery
                    |> ignore
                    |> Ok
                else
                    Error("Not persisted")
            with
                | _ as e -> 
                    log.Error e.Message
                    Error(e.Message.ToString())

        let notifier: IEventBroker =
            {
                notify = 
                    fun version name events ->
                        result {    
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

                            let! result = notified5 |> Result.map (fun _ -> Ok())
                            return! result
                        }
                    |> Some
            }
        notifier

    let notify (broker: IEventBroker) (version: string) (name: string) (idAndEvents: List<int * string>) =
        match broker.notify with
        | Some notify ->
            notify version name idAndEvents
        | None ->
            log.Info "No broker configured"
            Ok ()

