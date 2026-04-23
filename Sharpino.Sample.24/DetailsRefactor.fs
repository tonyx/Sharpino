
namespace Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open Sharpino.Cache
open Sharpino
open System.Text.Json
open FsToolkit.ErrorHandling
open System
open System.Threading
open System.Threading.Tasks

module DetailsRefactor = 

    type UserDetailsRef = 
        { 
            User: User
            Todos: List<Todo>
        }

    type RefreshableUserDetailsRef = 
        { 
            UserDetailsRef: UserDetailsRef
            Refresher: Option<CancellationToken>  -> TaskResult<UserDetailsRef, string>
        }
        member this.RefreshAsync ct = 
            taskResult {
                let! userDetailsRef = this.Refresher ct
                return 
                    { 
                        this with 
                            UserDetailsRef = userDetailsRef 
                    }
            }
        interface RefreshableAsync<RefreshableUserDetailsRef> with
            member this.RefreshAsync ct = 
                this.RefreshAsync ct
                
    type TodoDetailsRef =
        {
            Todo: Todo
            User: User
        }

    type RefreshableTodoDetailsRef = 
        { 
            TodoDetailsRef: TodoDetailsRef
            Refresher: Option<CancellationToken>  -> TaskResult<TodoDetailsRef, string>
        }
        member this.RefreshAsync ct = 
            taskResult {
                let! todoDetailsRef = this.Refresher ct
                return 
                    { 
                        this with 
                            TodoDetailsRef = todoDetailsRef 
                    }
            }
        interface RefreshableAsync<RefreshableTodoDetailsRef> with
            member this.RefreshAsync ct = 
                this.RefreshAsync ct

        


