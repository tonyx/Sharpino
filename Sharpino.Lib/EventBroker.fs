namespace Sharpino

open System
open System.Threading.Tasks
open Sharpino.Commons
open Sharpino.Definitions
open Sharpino.Core
open FsToolkit.ErrorHandling

type RawMessage = string
type StreamName = string 
module EventBroker =
    type EventsMessage<'E> =
        {
            InitEventId: EventId
            EndEventId: EventId
            Events: List<'E>
        }
 
    type MessageType<'A, 'E> =
        | InitialSnapshot of 'A
        | Delete
        | Events of EventsMessage<'E>
     
    type AggregateMessage<'A, 'E> =
        {
            AggregateId: AggregateId
            Message: MessageType<'A, 'E>
        }
        with
            member this.Serialize =
                this
                |> jsonPSerializer.Serialize
            static member Deserialize x =
                jsonPSerializer.Deserialize<AggregateMessage<'A, 'E>> x         
    
    type MessageSender =
        RawMessage -> ValueTask
    
    type MessageSenders =
        | NoSender
        | MessageSender of (StreamName -> MessageSender)
        