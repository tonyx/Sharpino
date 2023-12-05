
namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Storage
open log4net
open log4net.Config
open Confluent.Kafka
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



    // type EventBrokerState()
    //     let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    //     let subscriber = new KafkaSubscriber(bootstrapServer, context.Version, context.StorageName, "sharpinoTestClient")
    //     member this.GetState(): 'A = context

