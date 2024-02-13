
namespace Tonyx.SeatsBooking
open Tonyx.SeatsBooking.Stadium
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

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<StadiumEvent> json
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize
