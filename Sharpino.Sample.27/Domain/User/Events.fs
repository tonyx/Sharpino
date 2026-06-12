
namespace Sharpino.Template.Models

open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

type UserEvents =
    | Renamed of string
    | TodoAssigned of TodoId
    | TodoDetatched of TodoId
    
    interface Event<User> with
        member this.Process user =
            match this with
            | Renamed newName -> user.Rename newName
            | TodoAssigned todoId -> user.AssignTodo todoId
            | TodoDetatched todoId -> user.DetatchTodo todoId

    static member Deserialize (x: string): Result<UserEvents, string> =
        try
            JsonSerializer.Deserialize<UserEvents> (x, jsonOptions) |> Ok
        with
            | ex -> Error ex.Message
        
    member this.Serialize =
        JsonSerializer.Serialize (this, jsonOptions)
