
namespace Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

type TodoEvents =
    | Activated of DateTime
    | Completed of DateTime
    interface Event<Todo> with
        member this.Process todo =
            match this with
            | Activated  dateTime -> todo.Activate dateTime
            | Completed  dateTime -> todo.Complete dateTime

    static member Deserialize (x: string): Result<TodoEvents, string> =
        try
            JsonSerializer.Deserialize<TodoEvents> (x, jsonOptions) |> Ok
        with
            | ex -> Error ex.Message
        
    member this.Serialize =
        JsonSerializer.Serialize (this, jsonOptions)




