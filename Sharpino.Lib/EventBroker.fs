namespace Sharpino

open System
open System.Threading.Tasks
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.Core
open FsToolkit.ErrorHandling

type RawMessage = string
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
    
    type MessageSender =
        RawMessage -> ValueTask
        