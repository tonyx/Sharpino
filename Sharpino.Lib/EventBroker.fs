namespace Sharpino

open System
open System.Threading.Tasks
open Sharpino.Commons
open Sharpino.Definitions
open FsToolkit.ErrorHandling

module EventBroker =
 
    type StreamName = string 
    type Message<'A> =
        | InitialSnapshot of 'A
        | Delete
        | Events of EventId * List<Event<'A>>
     
    type AggregateMessage<'A> =
        {
            AggregateId: AggregateId
            Message: Message<'A>
        }
        with
            member this.Serialize =
                this
                |> jsonPSerializer.Serialize 
    
    type AggregateMessageSender =
        StreamName -> TaskResult<Json -> Task<ValueTask>, string>
        