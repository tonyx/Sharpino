namespace Sharpino

open FsToolkit.ErrorHandling
open Sharpino.EventBroker
open System.Text
open RabbitMQ.Client

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
            
        
    let mkSimpleChannel(factory: ConnectionFactory, streamName: string) =
        taskResult
            {
                let! connection = factory.CreateConnectionAsync()
                let! channel = connection.CreateChannelAsync()
                let! qDec =
                    queueDeclare channel streamName
                return channel 
            }
    
    let mkAggregateMessageSender<'A> (host: string) (streamName: string) =
        let factory = mkfactory(host)
        let channel = mkSimpleChannel(factory, streamName).Result
        let aggregateMessageSender =
            fun (message: AggregateMessage<'A>) ->
                let body = Encoding.UTF8.GetBytes (message.Message)
                let properties = channel.CreateBasicProperties()
        ()