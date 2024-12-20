
namespace Sharpino
open System
open Npgsql
open Sharpino
open log4net
open FSharp.Core

// todo: this part will be revised or removed or replaced
// leaving the code under comment just for me, just for now
module KafkaBroker =

    // let serializer = Utils.JsonSerializer(Utils.serSettings) :> Utils.ISerializer
    // let picklerSerializer = Commons.jsonPSerializer // :> Commons.Serialization<string>
    // let binPicklerSerializer = Commons.binarySerializer // :> Commons.Serialization<string>
    // I decided to binarize the objects and then encode them to base64
    // let jsonPicklerSerializer = Commons.jsonPSerializer // :> Commons.Serialization<string>

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

    // let log = LogManager.GetLogger(Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // uncomment following for quick debugging
    // log4net.Config.BasicConfigurator.Configure() |> ignore

    let getKafkaBroker (bootStrapServer: string) =
        failwith "unimplemented"

    let tryPublish eventBroker version name idAndEvents =
        failwith "unimplemented"
    
    let tryPublishAggregateEvent eventBroker aggregateId version name idAndEvents =
        failwith "unimplemented"
        
