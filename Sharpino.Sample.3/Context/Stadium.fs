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
open Sharpino.MemoryStorage
open Sharpino.Storage
open Sharpino.Cache
open Sharpino.Core
open Sharpino
open System

module Stadium =
    open Sharpino.Repositories
    type Stadium = 
        {
            rows: IRepository<RefactoredRow>
        }
        with
            static member Zero =
                {
                    rows = ListRepository<RefactoredRow>.Zero
                }
            static member FromList (xs: List<RefactoredRow>) =
                {
                    rows = ListRepository<RefactoredRow>.Create xs
                }
            member this.AddRow (r: RefactoredRow) =
                result {
                    let! added = this.rows.Add (r, sprintf "A row with id '%A' already exists" r.Id)
                    return
                        {
                            this with
                                rows = added
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
