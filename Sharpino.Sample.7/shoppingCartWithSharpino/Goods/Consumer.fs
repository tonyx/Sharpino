namespace ShoppingCart

open System
open System.Text
open Microsoft.Extensions.Hosting
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker

module GoodConsumer =
    type GoodConsumer(sp: IServiceProvider) =
        inherit BackgroundService ()
        let factory = ConnectionFactory (HostName = "localhost")
        let connection =
            factory.CreateConnectionAsync()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let channel =
            connection.CreateChannelAsync ()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        let queueDeclare =
            channel.QueueDeclareAsync ("_01_good", false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously
 
        let mutable state: Option<EventId * Good.Good> = None
        
        member this.State
            with get () = state
            and set value =
                printfn "XXXXX: Setting state to %A" value
                state <- value
        
        member this.StateAsResult =
            this.State 
            |> Option.map Result.Ok
            |> Option.defaultValue (Result.Error "No state")
         
        override this.ExecuteAsync (stoppingToken) =
            let consumer =  AsyncEventingBasicConsumer channel
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        printfn " [x] Received %s\n" message
                        let deserializedMessage = jsonPSerializer.Deserialize<AggregateMessage<Good.Good>> message
                        match deserializedMessage with
                        | Ok message ->
                            printfn " [x] Received %A\n" message
                            match message with
                            | { Message = InitialSnapshot good } ->
                                (this :> GoodConsumer).State <- (0, good) |> Some
                        printfn " [x x] Received %A\n" deserializedMessage
                        return ()
                   })
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    

