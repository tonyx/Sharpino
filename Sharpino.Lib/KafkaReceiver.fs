
namespace Sharpino

open Sharpino.Core
open System
open log4net
open FSharpPlus.Operators

// to be removed/rewritten

// todo: this part will be revised or removed or replaced
// leaving code under comment just for me, just for now 
module KafkaReceiver =
    let getStrAggregateMessage message =
        // unimplemented
        ()

    let getFromMessage<'E> value =
        // unimplemented
        ()

    let logger = LogManager.GetLogger (System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)

    // beware of the string generic type in the aggregate. should work also with other types
    type ConsumerX<'A, 'E when 'E :> Event<'A> and 'A:> Aggregate<string>> 
        (topic: string, clientId: string, bootStrapServers: string, groupId: string, timeOut: int, fallBackViewer: AggregateViewer<'A>) =

        member this.GetEventsByAggregate aggregateId =
            // unimplemented
            ()
        member this.GetEventsByAggregateNew aggregateId =
            // unimplemented
            ()

        member this.GetMessages =
            // unimplemented
            ()

        member this.Update() =
            // unimplemented
            ()

        member this.GetState (id: Guid) = // : Result<EventId * 'A, string> =
            // unimplemented
            ()

    let getStrContextMessage message =
        // unimplemented
        ()

    let getContextEventFromMessage<'E> value =
        // unimplemented
        ()

    type ConsumerY<'A, 'E when 'E :> Event<'A> >
        (topic: string, clientId: string, bootStrapServers: string, groupId: string, timeOut: int, start: 'A, fallbackViewer: StateViewer<'A>) =
            // let mutable currentState: 'A = start
            // note that "start" must correspond to the same as fallbackViewer appied
            // let mutable gMessages = []

            // member this.GMessages = gMessages

            member this.GetEvents () =
                // unimplemented
                ()

            member this.GetMessages =
                // unimplemented
                ()

            member this.Consuming () =
                // unimplemented
                ()

            member this.Update () =
                // unimplemented
                ()
                    
            member this.GetState () =
                // unimplemented
                ()





                    



