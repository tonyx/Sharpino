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
            todos: Repository<Todo>
        }
        with
            static member Zero =
                {
                    todos = Repository<Todo>.Zero
                }

            member this.AddTodo (t: Todo) =
                result {
                    let! notExists = 
                        this.todos.Exists (fun x -> 
                                x.Description = t.Description 
                                || x.Id = t.Id 
                        )
                        |> not
                        |> boolToResult (sprintf "A todo with the description %A already exists, or having the same id" t.Description)
                    return
                        {
                            this with
                                todos = this.todos.Add t
                        }
                }
            member this.AddTodos (ts: List<Todo>) =
                let descriptionOrIdNotAlreadyExists t =
                    this.todos.Exists (fun x -> x.Description = t.Description || x.Id = t.Id)
                    |> not
                    |> boolToResult (sprintf "A todo with the description %A already exists, or having the same id" t.Description)

                result {
                    let! descMustNotExist =
                        ts |> catchErrors descriptionOrIdNotAlreadyExists

                    return
                        {
                            this with
                                todos = this.todos.AddMany ts
                        }
                }
            member this.RemoveTodo (id: Guid) =
                result {
                    let! newTodos = this.todos.Remove id
                    return 
                        {
                            this with
                                todos = newTodos
                        }

                }
            member this.GetTodos() = this.todos.GetAll()
