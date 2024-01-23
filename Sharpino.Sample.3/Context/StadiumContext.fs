
namespace seatsLockWithSharpino

open seatsLockWithSharpino.Row1Context
open seatsLockWithSharpino.RefactoredRow
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

        member this.AddRow (r: SeatsRow) =
            result {
                let! stadium = this.stadium.AddRow r
                return
                    {
                        this with
                            stadium = stadium
                    }
            } 

        member this.AddRowReference (id: Guid) =
            result {
                let! stadium = this.stadium.AddRowReference id
                return
                    {
                        this with
                            stadium = stadium
                    }
            }

        member this.RemoveRowReference (id: Guid) =
            result {
                let! stadium = this.stadium.RemoveRowReference id
                return
                    {
                        this with
                            stadium = stadium
                    }
            }
        member this.GetRowReferences () =
            this.stadium.rowsReferences

        member this.GetRow id =
            this.stadium.GetRow id
        member this.GetRows () =
            this.stadium.GetRows ()
        
    type StadiumEvent =
        | RowAdded of SeatsRow
        | RowReferenceAdded of Guid
        | RowReferenceRemoved of Guid
            interface Event<StadiumContext> with
                member this.Process (x: StadiumContext) =
                    match this with
                    | RowAdded row ->
                        x.AddRow row
                    | RowReferenceAdded id ->
                        x.AddRowReference id
                    | RowReferenceRemoved id ->
                        x.RemoveRowReference id

        static member Deserialize (serializer: ISerializer, json: Json) =
            serializer.Deserialize<StadiumEvent> json
        member this.Serialize (serializer: ISerializer) =
            this
            |> serializer.Serialize

    type StadiumCommand =
        | AddRow of SeatsRow
        | AddRowReference of Guid
        | RemoveRowReference of Guid
            interface Command<StadiumContext, StadiumEvent> with
                member this.Execute (x: StadiumContext) =
                    match this with
                    | AddRow (row: SeatsRow) ->
                        x.AddRow row
                        |> Result.map (fun _ -> [StadiumEvent.RowAdded row])
                    | AddRowReference id ->
                        x.AddRowReference id
                        |> Result.map (fun _ -> [StadiumEvent.RowReferenceAdded id])
                    | RemoveRowReference id ->
                        x.RemoveRowReference id
                        |> Result.map (fun _ -> [StadiumEvent.RowReferenceRemoved id])
                member this.Undoer = None 

