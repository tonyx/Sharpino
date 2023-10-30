
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
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    let getKafkaBroker(bootStrapServer: string, pgConnection: string) =
        let config = ProducerConfig()
        config.BootstrapServers <- bootStrapServer
        let producer = ProducerBuilder<Null, string>(config)
        let p = producer.Build()
        let message = Message<Null, string>()

        let notifySingleMessage (version: string) (name: string)  (msg: int * string) =
            let topic = name + "-" + version |> String.replace "_" ""
            try
                let _ =
                    message.Key <- null
                    message.Value <- msg |> snd
                    p.ProduceAsync(topic, message)
                    |> Async.AwaitTask 
                    |> Async.RunSynchronously
                    |> ignore

                let _ =
                    let streamName = version + name
                    let updateQuery = sprintf "UPDATE events%s SET published = true  WHERE id = '%d'" streamName (msg |> fst)
                    let _ =
                        pgConnection 
                        |> Sql.connect
                        |> Sql.query updateQuery
                        |> Sql.executeNonQuery
                    ()
                () |> Ok
            with
                | _ as e -> 
                    log.Error e.Message
                    Error(e.Message.ToString())

        let notifier: IEventBroker =
            {
                notify = 
                    fun version name events ->
                        let topic = name + "-" + version |> String.replace "_" ""
                        result {    
                            let! _ =  events |> catchErrors (fun x -> notifySingleMessage version name x) 
                            return ()
                        }
                    |> Some
            }
        notifier

    let notifyInCase (broker: IEventBroker ) (version: string) (name: string) (idAndEvents: List<int * string>) =
        match broker.notify with
        | Some notify ->
            // notify version name (idAndEvents |>> snd)
            notify version name idAndEvents
        | None ->
            Ok ()
