
namespace Sharpino.Template.Models

open Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

type UserCommands =
    | Rename of string
    | AssignTodo of TodoId
    | DetachTodo of TodoId
    interface AggregateCommand<User, UserEvents> with
        member this.Execute user =
            match this with
            | Rename newName ->
                user.Rename newName
                |> Result.map (fun s -> (s, [Renamed newName]))
            | AssignTodo todoId ->
                user.AssignTodo todoId
                |> Result.map (fun s -> (s, [TodoAssigned todoId]))
            | DetachTodo todoId ->
                user.DetatchTodo todoId
                |> Result.map (fun s -> (s, [TodoDetatched todoId]))
        member this.Undoer =
            None
