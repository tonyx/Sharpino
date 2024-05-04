namespace Tonyx.SeatsBooking

open Tonyx.SeatsBooking.Commons
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open System

module Stadium =
    type Stadium (rowReferences: List<DateTime * Guid>) =
        let stateId = Guid.NewGuid()
        member this.rowReferences = rowReferences
        static member Zero =
            Stadium []
        static member Deserialize  json: Result<Stadium, string>  =
            serializer.Deserialize<Stadium> json
        member this.Serialize =
            this
            |> serializer.Serialize

        member this.AddRowReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.rowReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> boolToResult (sprintf "A row with id '%A' already exists" id)
                return
                    Stadium ((System.DateTime.Now, id) :: this.rowReferences)
            }

        member this.RemoveRowReference (id: Guid) =
            result {
                let! chckExists =
                    this.rowReferences
                    |>> snd
                    |> List.contains id
                    |> boolToResult (sprintf "A row with id '%A' does not exist" id)
                return
                    Stadium (this.rowReferences |> List.filter (fun (_, x) -> x <> id))
            }

        member this.GetRowReferences () =
            this.rowReferences |> List.map snd
        static member StorageName =
            "_stadium"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
        member this.StateId = stateId
