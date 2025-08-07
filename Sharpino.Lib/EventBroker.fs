namespace Sharpino

open System
open System.Threading.Tasks
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.Core
open FsToolkit.ErrorHandling

module EventBroker =
    type EventsMessage<'E> =
        {
            InitEventId: EventId
            EndEventId: EventId
            Events: List<'E>
        }
 
    type StreamName = string 
    type Message<'A, 'E> =
        | InitialSnapshot of 'A
        | Delete
        // | Events of EventId * List<'E>
        | Events of EventsMessage<'E>
     
    type AggregateMessage<'A, 'E> =
        {
            AggregateId: AggregateId
            Message: Message<'A, 'E>
        }
        with
            member this.Serialize =
                this
                |> jsonPSerializer.Serialize 
    
    type AggregateMessageSender =
        string -> ValueTask
        