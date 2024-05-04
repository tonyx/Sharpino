
namespace Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Stadium
open Tonyx.SeatsBooking.Commons
open Sharpino.Definitions

open Sharpino.Core
open System
open Sharpino.Utils

module StadiumEvents =
    type StadiumEvent =
        | RowReferenceAdded of Guid
        | RowReferenceRemoved of Guid
            interface Event<Stadium> with
                member this.Process (x: Stadium) =
                    match this with
                    | RowReferenceAdded id ->
                        x.AddRowReference id
                    | RowReferenceRemoved id ->
                        x.RemoveRowReference id

        static member Deserialize  json =
            serializer.Deserialize<StadiumEvent> json
        member this.Serialize =
            this
            |> serializer.Serialize
