
namespace seatsLockWithSharpino

open seatsLockWithSharpino.Row1Context
open seatsLockWithSharpino.RowAggregate
open seatsLockWithSharpino.Seats
open seatsLockWithSharpino
open seatsLockWithSharpino.Row
open seatsLockWithSharpino.Row1
open seatsLockWithSharpino.Row2
open FsToolkit.ErrorHandling
open Expecto
open Sharpino
open Sharpino.Definitions
open Sharpino.MemoryStorage
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Core
open Sharpino
open System
open Stadium
open Sharpino.Utils

module StadiumContext =
    type StadiumContext = 
        { 
            stadium: Stadium
        }
        static member Zero = 
            {
                stadium = Stadium.Zero
            }
        static member StorageName =
            "_stadium"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15
        static member Lock =
            new Object()
        static member Deserialize (serializer: ISerializer, json: Json): Result<StadiumContext, string>  =
            serializer.Deserialize<StadiumContext> json
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

        member this.AddRow (r: RefactoredRow) =
            result {
                let! stadium = this.stadium.AddRow r
                return
                    {
                        this with
                            stadium = stadium
                    }
            } 
        member this.GetRow id =
            this.stadium.GetRow id
        member this.GetRows () =
            this.stadium.GetRows ()
        
    type StadiumEvent =
        | RowAdded of RefactoredRow
            interface Event<StadiumContext> with
                member this.Process (x: StadiumContext) =
                    match this with
                    | RowAdded row ->
                        x.AddRow row
        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<StadiumEvent> json
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

    type StadiumCommand =
        | AddRow of RefactoredRow
            interface Command<StadiumContext, StadiumEvent> with
                member this.Execute (x: StadiumContext) =
                    match this with
                    | AddRow (row: RefactoredRow) ->
                        x.AddRow row
                        |> Result.map (fun _ -> [StadiumEvent.RowAdded row])
                member this.Undoer = None 

