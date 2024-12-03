module Sharpino.Sample.Saga.Context.SeatBookings
open Sharpino
open Sharpino.Commons

open Sharpino.Core
open FSharpPlus
open FSharpPlus.Operators
open FsToolkit.ErrorHandling

open System

type Theater = {
    Bookings: List<Guid>
    Rows: List<Guid>
    Vouchers: List<Guid>
}
with
    member this.AddRowReference (rowId: Guid) =
        result {
            do! 
                this.Rows
                |> List.exists (fun x -> x = rowId)
                |> not
                |> Result.ofBool "row already exists"
            return { this with Rows = rowId :: this.Rows } 
        }
    member this.AddBookingReference (bookingId: Guid) =
        result {
            do! 
                this.Bookings
                |> List.exists (fun x -> x = bookingId)
                |> not
                |> Result.ofBool "booking already exists"
            return { this with Bookings = bookingId :: this.Bookings } 
        }
        
    member this.RemoveBookingReference (bookingId: Guid) =
        result {
            do! 
                this.Bookings
                |> List.exists (fun x -> x = bookingId)
                |> Result.ofBool "booking not found"
            return  { this with Bookings = this.Bookings |> List.filter (fun x -> x <> bookingId) } 
        }
    member this.RemoveRowReference (rowId: Guid) =
        result {
            do! 
                this.Rows
                |> List.exists (fun x -> x = rowId)
                |> Result.ofBool "row not found"
            return  
                { this with Rows = this.Rows |> List.filter (fun x -> x <> rowId) }
        }
        
    member this.AddVoucherReference (voucherId: Guid) =
        result {
            do! 
                this.Vouchers
                |> List.exists (fun x -> x = voucherId)
                |> not
                |> Result.ofBool "voucher already exists"
            return { this with Vouchers = voucherId :: this.Vouchers } 
        }
    member this.RemoveVoucherReference (voucherId: Guid) =
        result {
            do! 
                this.Vouchers
                |> List.exists (fun x -> x = voucherId)
                |> Result.ofBool "voucher not found"
            return { this with Vouchers = this.Vouchers |> List.filter (fun x -> x <> voucherId) }
        }
    
    static member Zero = { Bookings = []; Rows = []; Vouchers = [] }
  
    static member StorageName = "_theater"
    static member Version = "_01"
    static member SnapshotsInterval = 15
    static member Deserialize(x: string) =
        jsonPSerializer.Deserialize<Theater> x
   
    member this.Serialize =
        jsonPSerializer.Serialize this
        
