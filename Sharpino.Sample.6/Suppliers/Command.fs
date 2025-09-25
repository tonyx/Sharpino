namespace Tonyx.Sharpino.Pub
open Tonyx.Sharpino.Pub.Supplier
open FSharpPlus
open FsToolkit.ErrorHandling
open Sharpino.Definitions
open Sharpino.Utils
open Sharpino.Core
open System

type SupplierCommands =
    | ChangePhone of string
    | ChangeEmail of string
        interface AggregateCommand<Supplier, SupplierEvents> with
            member this.Execute (x: Supplier) =
                match this with
                    | ChangePhone phone ->
                        x.ChangePhone phone
                        |> Result.map (fun s -> (s, [PhoneChanged phone]))
                    | ChangeEmail email ->
                        x.ChangeEmail email
                        |> Result.map (fun s -> (s, [EmailChanged email]))
                
            member this.Undoer = None         
