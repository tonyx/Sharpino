
namespace Sharpino
open Sharpino.Storage
open Sharpino.Commons
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
    // let picklerSerializer = Commons.jsonPSerializer // :> Commons.Serialization<string>
    let binPicklerSerializer = Commons.binarySerializer // :> Commons.Serialization<string>
    // I decided to binarize the objects and then encode them to base64
    let jsonPicklerSerializer = Commons.jsonPSerializer // :> Commons.Serialization<string>

    type BrokerEvent =
        | StrEvent of string
        | BinaryEvent of byte[]

    type BrokerMessageRef = {
        // ApplicationId: Guid
        EventId: int
        BrokerEvent: BrokerEvent
    }

    type BrokerAggregateMessageRef = {
        // ApplicationId: Guid
        AggregateId: Guid
        EventId: int
        BrokerEvent: BrokerEvent
    }

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

    let getKafkaBroker (bootStrapServer: string) =
        let log = Serilog.LoggerConfiguration().CreateLogger()
        let batching = Batching.Linger (System.TimeSpan.FromMilliseconds 10.)
        let producerConfig = KafkaProducerConfig.Create("MyClientId", bootStrapServer, Acks.All, batching)
        try 
            let notifier: IEventBroker<_> =
                {
                    notify = 
                        (fun version name events -> 
                            let topic = name + "-" + version |> String.replace "_" ""
                            let producer = KafkaProducer.Create(log, producerConfig, topic)
                            let key = topic
                            let deliveryResults =
                                events 
                                |> List.map 
                                    (fun (id, x) -> 
                                        let brokerMessageRef = {
                                            EventId = id
                                            BrokerEvent = StrEvent x
                                        }
                                        // decide which format will be used (probably binary and then text encoded)
                                        let binPicled = binPicklerSerializer.Serialize x
                                        // let jsonPickled = jsonPicklerSerializer.Serialize x
                                        let encoded = Convert.ToBase64String binPicled
                                        // producer.ProduceAsync(key, x) |> Async.RunSynchronously // |> ignore
                                        producer.ProduceAsync(key, encoded) |> Async.RunSynchronously // |> ignore
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
                                    (fun (id, x) -> 
                                        let brokerAggregateMessageRef = {
                                            AggregateId = aggregateId
                                            EventId = id
                                            BrokerEvent = StrEvent x
                                        }
                                        // ok go for binary and then text encoded
                                        let binPickled = binPicklerSerializer.Serialize brokerAggregateMessageRef 
                                        let encoded = Convert.ToBase64String binPickled
                                        producer.ProduceAsync (key, encoded) |> Async.RunSynchronously // |> ignore
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
        async {
            return
                if (eventBroker.notify.IsSome) then
                    eventBroker.notify.Value version name idAndEvents
                else
                    log.Info "no sending to any broker"
                    [] 
        }
        |> Async.StartAsTask

    
    let tryPublishAggregateEvent eventBroker aggregateId version name idAndEvents =
        async {
            return
                match eventBroker.notifyAggregate with
                | Some notify ->
                    notify version name aggregateId idAndEvents
                | None ->
                    []
        }
        |> Async.StartAsTask
