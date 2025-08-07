namespace ShoppingCart

open System
open System.Collections.Concurrent
open System.Text
open Microsoft.Extensions.Hosting
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.EventBroker
open Sharpino.Core
open ShoppingCart.CartEvents

module CartConsumer =
    type CartConsumer(sp: IServiceProvider) =
        inherit BackgroundService()
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
            channel.QueueDeclareAsync ("_01_cart", false, false, false, null)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let statePerAggregate =
            ConcurrentDictionary<AggregateId, EventId * Cart.Cart>()
        
        member this.GetAggregateState (id: AggregateId) =
            if (statePerAggregate.ContainsKey id) then
                statePerAggregate.[id]
                |> Result.Ok
            else
                Result.Error "No state"
                
        override this.ExecuteAsync (stoppingToken) =
            let consumer =  AsyncEventingBasicConsumer channel
            consumer.add_ReceivedAsync
                (fun _ ea ->
                    task {
                        let body = ea.Body.ToArray()
                        let message = Encoding.UTF8.GetString(body)
                        let deserializedMessage = jsonPSerializer.Deserialize<AggregateMessage<Cart.Cart, CartEvents>> message
                        match deserializedMessage with
                        | Ok message ->
                            let aggregateId = message.AggregateId
                            match message with
                            | { Message = InitialSnapshot good } ->
                                statePerAggregate.[aggregateId] <- (0, good)
                                ()
                            | { Message = Message.Events { InitEventId = eventId; EndEventId = endEventId; Events = events  } }  ->
                                printf " [Q] Received %A\n" message
                                if (statePerAggregate.ContainsKey aggregateId && statePerAggregate.[aggregateId] |> fst = eventId || statePerAggregate.[aggregateId] |> fst = 0) then
                                    let currentState = statePerAggregate.[aggregateId] |> snd
                                    let newState = evolve currentState events
                                    if newState.IsOk then
                                        statePerAggregate.[aggregateId] <- (endEventId, newState.OkValue)
                                    else () // todo: error
                                else
                                    () // todo: error
                                ()
                            | { Message = Message.Delete } ->
                                if (statePerAggregate.ContainsKey aggregateId) then
                                    statePerAggregate.TryRemove aggregateId  |> ignore
                                ()     
                        printfn " [Y Y] Received %A\n" deserializedMessage
                        return ()
                   })
            channel.BasicConsumeAsync(queueDeclare.QueueName, true, consumer)    
