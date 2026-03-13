
namespace Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open Sharpino.Cache
open Sharpino
open System.Text.Json
open FsToolkit.ErrorHandling
open System

module Details = 

    type UserDetails = 
        { 
            User: User
            Todos: List<Todo>
            Refresher: unit -> Result<(User * List<Todo>), string>
        }
        member this.Refresh () =
            result {
                let! user, todos = this.Refresher ()
                return { this with User = user; Todos = todos }
            }
        interface Refreshable<UserDetails> with
            member this.Refresh () =
                this.Refresh ()
    
    type TodoDetails = 
        { 
            Todo: Todo
            User: User
            Refresher: unit -> Result<(Todo * User), string>
        }
        member this.Refresh () =
            result {
                let! todo, user = this.Refresher ()
                return { this with Todo = todo; User = user }
            }
        interface Refreshable<TodoDetails> with
            member this.Refresh () =
                this.Refresh ()

