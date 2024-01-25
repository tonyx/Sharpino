
namespace seatsLockWithSharpino

open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open System

module Stadium =
    type Stadium = 
        {
            rowReferences: List<Guid>
        }
        static member Zero = 
            {
                rowReferences = []
            }
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
                    |> List.contains id 
                    |> not
                    |> boolToResult (sprintf "A row with id '%A' already exists" id)

                return {
                    this with
                        rowReferences = id::this.rowReferences
                }
            } 

        member this.RemoveRowReference (id: Guid) =
            result {
                let! chckExists =
                    this.rowReferences 
                    |> List.contains id 
                    |> boolToResult (sprintf "A row with id '%A' does not exist" id)
                return {
                    this with
                        rowReferences = this.rowReferences |> List.filter (fun x -> x <> id)
                }
            }
            
        member this.GetRowReferences () =
            this.rowReferences
        