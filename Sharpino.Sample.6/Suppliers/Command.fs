namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Dish
open Tonyx.Sharpino.Pub.Supplier
open Tonyx.Sharpino.Pub.SupplierEvents
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

module SupplierCommands =
    type SupplierCommands =
        | ChangePhone of string
        | ChangeEmail of string
            interface AggregateCommand<Supplier, SupplierEvents> with
                member this.Execute (x: Supplier) =
                    match this with
                        | ChangePhone phone ->
                            x.ChangePhone phone
                            |> Result.map (fun _ -> [PhoneChanged phone])
                        | ChangeEmail email ->
                            x.ChangeEmail email
                            |> Result.map (fun _ -> [EmailChanged email])    
                    
                member this.Undoer = None         
    