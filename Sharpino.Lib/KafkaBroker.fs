
namespace Sharpino
open Sharpino.Storage
open Confluent.Kafka
open System.Net
open System
open FsToolkit.ErrorHandling
open FsToolkit.ErrorHandling.ResultCE
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

    let getKafkaBroker(bootStrapServer: string) =
        let config = ProducerConfig()
        config.BootstrapServers <- bootStrapServer
        let producer = ProducerBuilder<Null, string>(config)
        let p = producer.Build()
        let message = Message<Null, string>()

        let notifySingleMessage (topic: string) (msg: string) =
            try
                message.Key <- null
                message.Value <- msg
                p.ProduceAsync(topic, message)
                |> Async.AwaitTask 
                |> Async.RunSynchronously
                |> ignore
                |> Ok
            with
                | _ as e -> 
                    Error(e.Message.ToString())

        let notifier: IEventBroker =
            {
                notify = 
                    fun version name events ->
                        let topic = name + "-" + version |> String.replace "_" ""
                        result {    
                            let! _ =  events |> catchErrors (fun x -> notifySingleMessage topic x) 
                            return ()
                        }
                        // b
                    |> Some
            }
        notifier


    let notifyInCase (broker: IEventBroker ) (version: string) (name: string) (events: List<string>) =
        match broker.notify with
        | Some notify ->
            notify version name events
        | None ->
            Ok ()
