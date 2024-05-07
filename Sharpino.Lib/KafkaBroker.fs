
namespace Sharpino
open Sharpino.Storage
open FsKafka
open Confluent.Kafka
open System.Net
open System
open FsToolkit.ErrorHandling
open FsToolkit.ErrorHandling.ResultCE
open Npgsql
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Storage
open log4net
open log4net.Config
open FsToolkit.ErrorHandling.ResultCE
open FSharp.Core

module KafkaBroker =

    let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    type BrokerMessage<'F> = {
        ApplicationId: Guid
        EventId: int
        Event: 'F
    }
    
    type BrokerAggregateMessage<'F> = {
        ApplicationId: Guid
        AggregateId: Guid
        EventId: int
        Event: 'F
    }

    let log = LogManager.GetLogger(Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // uncomment following for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    let getKafkaBroker<'F> (bootStrapServer: string, eventStore: IEventStore<string>) =
        let log = Serilog.LoggerConfiguration().CreateLogger()
        let batching = Batching.Linger (System.TimeSpan.FromMilliseconds 10.)
        let producerConfig = KafkaProducerConfig.Create("MyClientId", bootStrapServer, Acks.All, batching)
        printf "getting kafka broker 1000\n"

        try 
            let notifier: IEventBroker<string> =
                {
                    notify = 
                        (fun version name events -> 
                            let topic = name + "-" + version |> String.replace "_" ""
                            let producer = KafkaProducer.Create(log, producerConfig, topic)
                            let key = topic // Guid.NewGuid().ToString()
                            let deliveryResults =
                                events 
                                |> List.map 
                                    (fun (_, x) -> 
                                        producer.ProduceAsync(key, x) |> Async.RunSynchronously // |> ignore
                                    )
                            deliveryResults
                        )
                        |> Some
                    notifyAggregate =
                        (fun version name aggregateId events -> 
                            let topic = name + "-" + version |> String.replace "_" ""
                            let producer = KafkaProducer.Create(log, producerConfig, topic)
                            let key = aggregateId.ToString() 
                            let deliveryResults =
                                events 
                                |> List.map 
                                    (fun (_, x) -> 
                                        producer.ProduceAsync(key, x) |> Async.RunSynchronously // |> ignore
                                    )
                            deliveryResults
                        )
                        |> Some
                }
            notifier
        with
        | _ as e -> 
            printf "error getting kafka broker %A\n" e
            log.Error e.Message
            {
                notify = None
                notifyAggregate = None
            }

    let tryPublish eventBroker version name idAndEvents =

        if (eventBroker.notify.IsSome) then
            eventBroker.notify.Value version name idAndEvents
            
        else
            printf "not publishing X \n" // |> ignore
            log.Info "no sending to any broker"
            [] //|> Ok
    
    let tryPublishAggregateEvent eventBroker aggregateId version name idAndEvents =
        async {
            return
                match eventBroker.notifyAggregate with
                | Some notify ->
                    notify version name aggregateId idAndEvents
                | None ->
                    log.Info "no sending to any broker"
                    []
                    // [] |> Ok
        }
        // |> Async.RunSynchronously
        |> Async.StartAsTask
