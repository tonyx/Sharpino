namespace Sharpino

open System
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Hosting
open Sharpino.EventBroker
open System.Text
open RabbitMQ.Client
open RabbitMQ.Client.Events
open System.Net

module RabbitMq =
    let mkfactory(host: string) =
        let factory = new ConnectionFactory()
        factory.HostName <- host
        factory
   
    let queueDeclare (channel: IChannel) (queueName: string) =
        try
            let queueDeclare =
                channel.QueueDeclareAsync(queueName, false, false, false, null)
                |> Async.AwaitTask
                |> Async.RunSynchronously
            Ok ()
        with
        | _ as ex ->
            ex |> Error
            
        
    let mkSimpleChannel(factory: ConnectionFactory, streamName: string): TaskResult<IChannel, exn> =
        taskResult
            {
                let! connection = factory.CreateConnectionAsync()
                let! channel = connection.CreateChannelAsync()
                let! qDec =
                    queueDeclare channel streamName
                return channel
            }

    let mkMessageSender(host: string) (streamName: string) =
        result {
            let factory = mkfactory(host)
            let channel =
                mkSimpleChannel(factory, streamName)
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> Result.get
            
            let aggregateMessageSender =
                fun (message: string) ->
                    let body = Encoding.UTF8.GetBytes message
                    channel.BasicPublishAsync(
                        "",
                        streamName,
                        body
                    )
            return aggregateMessageSender
       }
                