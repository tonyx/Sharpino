
namespace Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open FsToolkit.ErrorHandling
open System

    type User = 
        { UserId: UserId
          Name: string 
          Todos: List<TodoId>
          
          }
        static member New name = 
            { 
                UserId = UserId.New
                Name = name 
                Todos = []
            }
        member this.Rename name = 
            { this with Name = name } |> Ok

        member this.AssignTodo (todoId: TodoId) = 
            result
                {
                    do! 
                        this.Todos 
                        |> List.exists (fun t -> t.Value = todoId.Value)
                        |> not
                        |> Result.ofBool "Todo already assigned"
                    return 
                        { 
                            this with Todos = todoId :: this.Todos 
                        }
                }
        member this.DetatchTodo (todoId: TodoId) = 
            result
                {
                    do! 
                        this.Todos 
                        |> List.exists (fun t -> t.Value = todoId.Value)
                        |> Result.ofBool "Todo not assigned"
                    return 
                        { 
                            this with Todos = this.Todos |> List.filter (fun t -> t.Value <> todoId.Value) 
                        }
                }

        member this.Id = this.UserId.Value
        static member SnapshotsInterval = 50
        static member StorageName = "_User"
        static member Version = "_01"
        member this.Serialize = 
            (this, jsonOptions) |> JsonSerializer.Serialize
        static member Deserialize (data: string) =
            try
                let user = JsonSerializer.Deserialize<User> (data, jsonOptions)
                Ok user
            with
                | ex -> Error ex.Message