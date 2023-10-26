
namespace Sharpino
open Sharpino.Storage
open Confluent.Kafka
open System.Net
open System
open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Storage
open log4net
open log4net.Config
open FSharp.Core

module KafkaBroker =


    let getKafkaBroker(bootStrapServer: string) =
        let config = ProducerConfig()
        config.BootstrapServers <- bootStrapServer
        let producer = ProducerBuilder<Null, string>(config)
        let p = producer.Build()


        let notifySingleMessage (topic: string) (msg: string) =
            try
                let message = Message<Null, string>()
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
                        try
                            let topic = name + "-" + version |> String.replace "_" ""
                            let message = Message<Null, string>()
                            message.Key <- null
                            message.Value <- events |> String.concat "\n" // TODO: check if this is the right way to do it
                            p.ProduceAsync(topic, message)
                            |> Async.AwaitTask 
                            |> Async.RunSynchronously
                            |> ignore
                            |> Ok
                        with 
                            | _ as e -> 
                                Error(e.Message.ToString())
                    |> Some
            }
        notifier

