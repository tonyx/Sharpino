namespace Sharpino.Sample.Entities
open FSharpPlus
open System
open Sharpino.Utils
open Sharpino.Repositories
open FsToolkit.ErrorHandling
open Sharpino.Sample.Shared.Entities

module Todos =

    type Todos (todos: IRepository<Todo>) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.Todos = todos
        with
            static member Zero =
                Todos (todos = ListRepository<Todo>.Zero)
            static member FromList (xs: List<Todo>) =
                Todos (todos = ListRepository<Todo>.Create xs)

            member this.AddTodo (t: Todo) =
                result {
                    let! added = this.Todos.AddWithPredicate (t, (fun x -> x.Description = t.Description), sprintf "An item with id '%A' already exists" t.Id)
                    return
                        Todos (todos = added)
                }
            member this.AddTodos (ts: List<Todo>) =
                result {
                    let! added = 
                        this.Todos.AddManyWithPredicate
                            (   
                                ts, 
                                (fun (t: Todo) -> sprintf  "a todo with id %A or description %A already exists" t.Id t.Description),
                                (fun (x: Todo, t: Todo) -> x.Description = t.Description)
                            )
                    return 
                        Todos (todos = added)
                }
                
            member this.RemoveTodo (id: Guid) =
                ResultCE.result
                    {
                        let! removed = 
                            sprintf "A todo with id '%A' does not exist" id 
                            |> this.Todos.Remove id
                        return 
                            Todos (todos = removed)
                    }
            member this.GetTodos() = this.Todos.GetAll()
