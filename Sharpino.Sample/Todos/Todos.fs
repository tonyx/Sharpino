namespace Sharpino.Sample.Entities
open FSharpPlus
open System
open Sharpino.Utils
open Sharpino.Repositories
open FsToolkit.ErrorHandling
open Sharpino.Sample.Shared.Entities

module Todos =

    type Todos =
        {
            todos: IRepository<Todo>
        }
        with
            static member Zero =
                {
                    todos = ListRepository<Todo>.Zero
                }
            static member FromList (xs: List<Todo>) =
                {
                    todos = ListRepository<Todo>.Create xs
                }

            member this.AddTodo (t: Todo) =
                result {
                    // this predicate is skipped for now, but check it later 'cause it's suspicious
                    // let! added = this.todos.AddWithPredicate (t, (fun x -> x.Description = t.Description), sprintf "An item with id '%A' already exists" t.Id)
                    let! added = this.todos.Add (t, sprintf "An item with id '%A' already exists" t.Id)
                    return
                        {
                            this with
                                todos = added
                        }
                }
            member this.AddTodos (ts: List<Todo>) =
                result {
                    let! added = 
                        this.todos.AddManyWithPredicate
                            (   
                                ts, 
                                (fun (t: Todo) -> sprintf  "a todo with id %A or description %A already exists" t.Id t.Description),
                                (fun (x: Todo, t: Todo) -> x.Description = t.Description)
                            )
                    return 
                        {
                            this with
                                todos = added
                        }
                }
                
            member this.RemoveTodo (id: Guid) =
                ResultCE.result
                    {
                        let! removed = 
                            sprintf "A todo with id '%A' does not exist" id 
                            |> this.todos.Remove id
                        return {
                            this with
                                todos = removed
                        }
                    }
            member this.GetTodos() = this.todos.GetAll()
