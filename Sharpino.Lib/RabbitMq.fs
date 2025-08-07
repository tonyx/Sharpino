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
            
        
    let mkSimpleChannel(factory: ConnectionFactory, streamName: string) =
        taskResult
            {
                let! connection = factory.CreateConnectionAsync()
                let! channel = connection.CreateChannelAsync()
                let! qDec =
                    queueDeclare channel streamName
                return channel 
            }
            
    let mkAggregateMessageSenderBack(host: string) (streamName: string)  =
        taskResult
            {
                let factory = mkfactory(host)
                let! channel = mkSimpleChannel(factory, streamName)
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
               
    let mkAggregateMessageSender(host: string) (streamName: string) =
        let factory = mkfactory(host)
        let channel =
            mkSimpleChannel(factory, streamName)
            |> Async.AwaitTask
            |> Async.RunSynchronously
            |> Result.get
        
        let aggregateMessageSender =
            fun (message: string) ->
                printf "XXXX: Sending message to stream %s: %s" streamName message
                let body = Encoding.UTF8.GetBytes message
                channel.BasicPublishAsync(
                    "",
                    streamName,
                    body
                )
        aggregateMessageSender        

    // type RabbitConsumerService (sp: IServiceProvider) =
    //     inherit BackgroundService ()
    //     let factory = ConnectionFactory (HostName = "localhost")
    //     let connection =
    //         factory.CreateConnectionAsync()
    //         |> Async.AwaitTask
    //         |> Async.RunSynchronously
    //     let channel =
    //         connection.CreateChannelAsync ()
    //         |> Async.AwaitTask
    //         |> Async.RunSynchronously
    //     let queueDeclare =
    //         channel.QueueDeclareAsync ("_01_good", false, false, false, null)
    //         |> Async.AwaitTask
    //         |> Async.RunSynchronously
    //    
    //     override _.ExecuteAsync (stoppingToken) =
    //         let consumer =  AsyncEventingBasicConsumer(channel)
    //         consumer.add_ReceivedAsync
    //             (fun _ ea ->
    //                 task {
    //                     let body = ea.Body.ToArray()
    //                     let message = Encoding.UTF8.GetString(body)
    //                     printfn " [x] Received %s" message
    //                     return ()
    //                })
    //         channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
    //             
    // type RabbitConsumerService2 (sp: IServiceProvider) =
    //     inherit BackgroundService ()
    //     let factory = ConnectionFactory(HostName = "localhost")
    //     let connection =
    //         factory.CreateConnectionAsync ()
    //         |> Async.AwaitTask
    //         |> Async.RunSynchronously
    //     let channel =
    //         connection.CreateChannelAsync ()
    //         |> Async.AwaitTask
    //         |> Async.RunSynchronously
    //     let queueDeclare =
    //         channel.QueueDeclareAsync ("_01_good", false, false, false, null)
    //         |> Async.AwaitTask
    //         |> Async.RunSynchronously
    //    
    //     override _.ExecuteAsync(stoppingToken) =
    //         let consumer =  AsyncEventingBasicConsumer(channel)
    //         consumer.add_ReceivedAsync
    //             (fun _ ea ->
    //                 task {
    //                     let body = ea.Body.ToArray()
    //                     let message = Encoding.UTF8.GetString(body)
    //                     printfn " [y] Received %s" message
    //                     return ()
    //                })
    //         channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
    //     
            
            
                