namespace Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Stadium
open Sharpino.Definitions
open Sharpino.Core
open Sharpino.Utils
open Sharpino.Commons
open System

module StadiumEvents =
    type StadiumEvent =
        | RowReferenceAdded of Guid
        | RowReferenceRemoved of Guid
        | StadiumNoOp
            interface Event<Stadium> with
                member this.Process (x: Stadium) =
                    match this with
                    | RowReferenceAdded id ->
                        x.AddRowReference id
                    | RowReferenceRemoved id ->
                        x.RemoveRowReference id
                    | StadiumNoOp -> Ok x

        static member Deserialize  (json: Json) =
            jsonPSerializer.Deserialize<StadiumEvent> json
        member this.Serialize  =
            this
            |> jsonPSerializer.Serialize
