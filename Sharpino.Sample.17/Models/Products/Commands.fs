namespace Sharpino.Template.Models

open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

    
type ProductCommands =
    interface AggregateCommand<Product, ProductEvents> with
        member this.Execute item =
            failwith "not implemented"
        member this.Undoer = None    