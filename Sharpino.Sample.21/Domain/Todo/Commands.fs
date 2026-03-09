
namespace Sharpino.Template.Models
open Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

type TodoCommands =
    | Activate of DateTime
    | Complete of DateTime
    | Rename of string
    interface AggregateCommand<Todo, TodoEvents> with
        member this.Execute todo =
            match this with
            | Activate dateTime -> 
                todo.Activate dateTime
                |> Result.map (fun s -> (s, [Activated  dateTime]))

            | Complete dateTime -> 
                todo.Complete dateTime
                |> Result.map (fun s -> (s, [Completed  dateTime]))

            | Rename newName ->
                todo.Rename newName
                |> Result.map (fun s ->(s, [Renamed newName]))
        member this.Undoer =
            None
