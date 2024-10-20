
namespace Sharpino
open Sharpino.Storage
open System
open Npgsql
open Sharpino
open log4net
open FSharp.Core

// todo: this part will be revised
// leaving the code under comment just for me, just for now
module KafkaBroker =

    // let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    // let picklerSerializer = Commons.jsonPSerializer // :> Commons.Serialization<string>
    let binPicklerSerializer = Commons.binarySerializer // :> Commons.Serialization<string>
    // I decided to binarize the objects and then encode them to base64
    let jsonPicklerSerializer = Commons.jsonPSerializer // :> Commons.Serialization<string>

    type BrokerEvent =
        | StrEvent of string
        | BinaryEvent of byte[]

    type BrokerMessageRef = {
        EventId: int
        BrokerEvent: BrokerEvent
    }

    type BrokerAggregateMessageRef = {
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
        failwith "unimplemented"
        
        // let log = Serilog.LoggerConfiguration().CreateLogger()
        // let batching = Batching.Linger (System.TimeSpan.FromMilliseconds 10.)
        // let producerConfig = KafkaProducerConfig.Create("MyClientId", bootStrapServer, Acks.All, batching)
        // try 
        //     let notifier: IEventBroker<_> =
        //         {
        //             notify = 
        //                 (fun version name events -> 
        //                     let topic = name + "-" + version |> String.replace "_" ""
        //                     let producer = KafkaProducer.Create(log, producerConfig, topic)
        //                     let key = topic
        //                     let deliveryResults =
        //                         events 
        //                         |> List.map 
        //                             (fun (id, x) -> 
        //                                 let brokerMessageRef = {
        //                                     EventId = id
        //                                     BrokerEvent = StrEvent x
        //                                 }
        //                                 // decide which format will be used (probably binary and then text encoded)
        //                                 let binPicled = binPicklerSerializer.Serialize x
        //                                 // let jsonPickled = jsonPicklerSerializer.Serialize x
        //                                 let encoded = Convert.ToBase64String binPicled
        //                                 // producer.ProduceAsync(key, x) |> Async.RunSynchronously // |> ignore
        //                                 producer.ProduceAsync(key, encoded) |> Async.RunSynchronously // |> ignore
        //                             )
        //                     deliveryResults
        //                 )
        //                 |> Some
        //             notifyAggregate =
        //                 (fun version name aggregateId events ->
        //                     let topic = name + "-" + version |> String.replace "_" ""
        //                     let producer = KafkaProducer.Create(log, producerConfig, topic)
        //                     let key = aggregateId.ToString() 
        //                     let deliveryResults =
        //                         events 
        //                         |> List.map 
        //                             (fun (id, x) -> 
        //                                 let brokerAggregateMessageRef = {
        //                                     AggregateId = aggregateId
        //                                     EventId = id
        //                                     BrokerEvent = StrEvent x
        //                                 }
        //                                 // ok go for binary and then text encoded
        //                                 let binPickled = binPicklerSerializer.Serialize brokerAggregateMessageRef 
        //                                 let encoded = Convert.ToBase64String binPickled
        //                                 // todo: to be fixed
        //                                 // producer.ProduceAsync (key, encoded) |> Async.RunSynchronously // |> ignore
        //                                 let res =
        //                                     Async.RunSynchronously (producer.ProduceAsync (key, encoded), 1000)
        //                                 res
        //                             )
        //                     deliveryResults
        //                 )
        //                 |> Some
        //         }
        //     notifier
        // with
        // | _ as e -> 
        //     printf "error getting kafka broker %A\n" e
        //     log.Error e.Message
        //     {
        //         notify = None
        //         notifyAggregate = None
        //     }

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
                    let result = notify version name aggregateId idAndEvents
                    result
                | None ->
                    []
        }
        |> Async.StartAsTask
