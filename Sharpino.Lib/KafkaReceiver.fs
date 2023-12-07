
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

        member this.consumeWithTimeOut(timeoutMilliseconds: int): Result<ConsumeResult<Null, string>, string> =
            ResultCE.result {
                try
                    printf "entered in consumewithtimeout"
                    let cancellationTokenSource = new System.Threading.CancellationTokenSource(timeoutMilliseconds)
                    let result = cons.Consume(cancellationTokenSource.Token)
                    return result
                with 
                | _ -> 
                    printf "Timeout! "
                    return! "timeout" |> Result.Error
            }

        member this.ConsumeTillEnd() =
            printf "entered in consumetillend\n"
            let rec consumeTillEndWithTimeOut (acc: List<ConsumeResult<Null, string>>) =
                // printf "entered in consumetillendwithtimeout 1.\n"
                let result = this.Consume() |> Ok
                // printf "entered in consumetillendwithtimeout 2.\n"
                match result with
                | Ok result -> 
                    consumeTillEndWithTimeOut (result::acc)
                | Error err -> 
                    acc
            consumeTillEndWithTimeOut []

        member this.ConsumeTillEndWithTimeOut(timeout: int) =
            let rec consumeTillEndWithTimeOut (acc: List<ConsumeResult<Null, string>>) =
                printf "ola\n"
                let result = this.consumeWithTimeOut(timeout)
                match result with
                | Ok result -> 
                    consumeTillEndWithTimeOut (result::acc)
                | Error err -> 
                    acc
            consumeTillEndWithTimeOut []

    type EventBrokerState<'A, 'E when 'E:> Event<'A> >
        (version: string, storageName: string, bootstrapServer: string, clientId: string, zero: 'A, applId: Guid) =
        let mutable state:'A = zero
        let mutable applicationId: Guid = applId
        let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
        let subscriber = new KafkaSubscriber(bootstrapServer, version, storageName, clientId)


        member this.UpdateState(appId: Guid, timeout: int) =  
            let newState =
                ResultCE.result {
                    let receivedMany = subscriber.ConsumeTillEndWithTimeOut timeout
                    let! deserializedMany = receivedMany |> catchErrors (fun r -> r.Message.Value |> serializer.Deserialize<BrokerMessage>)
                    let filtered = deserializedMany |> List.filter (fun r -> r.ApplicationId = appId)
                    let! deserializedMany' = filtered |> catchErrors (fun r -> r.Event |> serializer.Deserialize<'E>)
                    let! state' =  evolve<'A,'E> state deserializedMany'
                    return state'
                }
            // todo verify that you can filter against something to get only the latest events (see application id)
            match newState with
            | Ok state' -> 
                state <- state'
                state' 
            | Error err ->
                state 
        member this.GetState() =
            state

        member this.SetAppId(appId: Guid) =
            applicationId <- appId

    let inline mkEventBrokerStateKeeper<'A , 'E
        when 'A: (static member Zero: 'A)
        and 'A: (static member StorageName: string)
        and 'A: (static member Version: string)
        and 'A: (static member Deserialize: ISerializer -> Json -> Result<'A, string>)
        and 'E: (static member Deserialize: ISerializer -> Json -> Result<'E, string>)
        and 'E:> Event<'A>
        > 
        (bootstrapServer: string) (clientId: string) (applicationId: Guid)=
        EventBrokerState<'A, 'E>('A.Version, 'A.StorageName, bootstrapServer, clientId, 'A.Zero, applicationId)

