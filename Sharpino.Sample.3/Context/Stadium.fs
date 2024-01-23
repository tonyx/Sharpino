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
open Sharpino.MemoryStorage
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Core
open Sharpino
open System

module Stadium =
    open Sharpino.Repositories
    open Sharpino.Utils
    type Stadium = 
        {
            rows: IRepository<SeatsRow>
            rowsReferences: List<Guid>
        }
        with
            static member Zero =
                {
                    rows = ListRepository<SeatsRow>.Zero
                    rowsReferences = []
                }
            // static member FromList (xs: List<RefactoredRow>) =
            //     {
            //         rows = ListRepository<RefactoredRow>.Create xs
            //     }
            member this.AddRow (r: SeatsRow) =
                result {
                    let! added = this.rows.Add (r, sprintf "A row with id '%A' already exists" r.Id)
                    return
                        {
                            this with
                                rows = added
                        }
                }
            member this.AddRowReference (id: Guid) =
                result {
                    let! notAlreadyExists =
                        this.rowsReferences 
                        |> List.contains id 
                        |> not
                        |> boolToResult (sprintf "A row with id '%A' already exists" id)

                    return {
                        this with
                            rowsReferences = id::this.rowsReferences
                    }
                } 
            member this.RemoveRowReference (id: Guid) =
                result {
                    let! chckExists =
                        this.rowsReferences 
                        |> List.contains id 
                        |> boolToResult (sprintf "A row with id '%A' does not exist" id)
                    return {
                        this with
                            rowsReferences = this.rowsReferences |> List.filter (fun x -> x <> id)
                    }
                }
            member this.RemoveRow  (id: Guid) =
                result {
                    let! removed = 
                        sprintf "A row with id '%A' does not exist" id 
                        |> this.rows.Remove id
                    return {
                        this with
                            rows = removed
                    }
                } 
            member this.GetRow (id: Guid) =
                result {
                    let! row' = 
                        this.rows.Get id
                        |> Result.ofOption (sprintf "A row with id '%A' does not exist" id)
                    return row'
                }

            member this.GetRows () =
                this.rows.GetAll()
