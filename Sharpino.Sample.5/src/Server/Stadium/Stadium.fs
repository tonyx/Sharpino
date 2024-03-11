namespace Tonyx.SeatsBooking

open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino
open System

module Stadium =
    type Stadium (rowReferences: List<DateTime * Guid>) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.rowReferences = rowReferences
        static member Zero =
            Stadium []
        static member StorageName =
            "_stadium"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
        static member Deserialize (serializer: ISerializer, json: Json): Result<Stadium, string>  =
            serializer.Deserialize<Stadium> json
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

        member this.AddRowReference (id: Guid) =
            result {
                let! notAlreadyExists =
                    this.rowReferences
                    |>> snd
                    |> List.contains id
                    |> not
                    |> Result.ofBool (sprintf "A row with id '%A' already exists" id)
                return Stadium ((System.DateTime.Now, id) :: this.rowReferences)
            }

        member this.RemoveRowReference (id: Guid) =
            result {
                let! chckExists =
                    this.rowReferences
                    |>> snd
                    |> List.contains id
                    |> Result.ofBool (sprintf "A row with id '%A' does not exist" id)
                return Stadium (this.rowReferences |> List.filter (fun (_, x) -> x <> id))
            }

        member this.GetRowReferences () =
            this.rowReferences |> List.map snd