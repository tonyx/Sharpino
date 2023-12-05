
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Storage
open Sharpino.Definitions
open log4net
open log4net.Config
open Confluent.Kafka
open Sharpino.Core
open Sharpino.Storage
open Sharpino.Utils
open Sharpino.Definitions
// open Sharpino.StateView
open Sharpino.KafkaBroker
open System

module KafkaReceiver =
    // this is a basic version of consumer, it does not handle errors, and it does not commit offsets
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    type KafkaSubscriber(bootStrapServer: string, version: string, name: string, groupId: string) =
        let topic = name + "-" + version |> String.replace "_" ""
        let config = ConsumerConfig() // "sharpino", brokers)
        let _ = config.GroupId <- groupId
        let _ = config.BootstrapServers <- bootStrapServer
        let _ = config.AutoOffsetReset <- AutoOffsetReset.Earliest
        let consumer = new ConsumerBuilder<Null, string>(config)
        let cons = consumer.Build () 
        let _ = cons.Subscribe(topic)

        member this.Consume () =
            let result = cons.Consume()
            result

        member this.consumeWithTimeOut(): Result<ConsumeResult<Null, string>, string> =
            ResultCE.result {
                try
                    printf "entered in consumewithtimeout"
                    let timeoutMilliseconds = 10000; 
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)

                    let result = cons.Consume(cancellationTokenSource.Token)
                    return result
                with 
                | _ -> 
                    printf "Timeout! "
                    return! "timeout" |> Result.Error
            }

    type EventBrokerState<'A, 'E when 'E:> Event<'A> >
        (version: string, storageName: string, bootstrapServer: string, clientId: string, zero: 'A) =
        let mutable state:'A = zero
        let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
        let subscriber = new KafkaSubscriber(bootstrapServer, version, storageName, clientId)

        // todo verify that you can filter against something to get only the latest events (see application id)
        // use application id (see listenforevent in multitests)
        member this.GetState() =  
            let newState =
                ResultCE.result {
                    let! received = subscriber.consumeWithTimeOut()
                    let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage>
                    let! deserialized' = deserialized.Event |> serializer.Deserialize<'E>
                    let! state' =  evolveUNforgivingErrors<'A,'E> state [deserialized']
                    return state'
                }

            // todo verify that you can filter against something to get only the latest events (see application id)
            // match newState with
            // | Ok state' -> 
            //     state <- state'
            //     state' 
            // | Error err ->
            //     state 

            state 
        
            // state
            // try
            //     ResultCE.result {
            //         let! received = subscriber.consumeWithTimeOut()
            //         let! deserialized = received.Message.Value |> serializer.Deserialize<BrokerMessage>
            //         let! deserialized' = deserialized.Event |> serializer.Deserialize<'A>
            //         let! state' = state |> evolve [deserialized']
            //         // let! state'' = state' |> Result.mapError (fun err -> err + " " + deserialized.EventId.ToString())
            //         // state <- state'
            //         return state'
            //         // return deserialized
            //     }
            // with
            // | _ -> state |> Ok


    let inline mkEventBrokerStateKeeper<'A , 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E:> Event<'A>
        > 
        (bootstrapServer: string) (clientId: string) =
        EventBrokerState<'A, 'E>('A.Version, 'A.StorageName, bootstrapServer, clientId, 'A.Zero)

