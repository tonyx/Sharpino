namespace Sharpino

open System
open Sharpino.Definitions

module EventBroker =
  
    type Message<'A> =
        | InitialSnapshot of 'A
        | Delete
        | Events of EventId * List<Event<'A>>
     
    type AggregateMessage<'A> =
        {
            AggregateId: AggregateId
            Message: Message<'A>
        }
    
    type AggregateMessageSender<'A> =
        AggregateMessage<'A> -> Result<unit, string>
        