namespace Sharpino.Sample.Entities
open FSharpPlus
open System
open Sharpino.Utils
open FsToolkit.ErrorHandling

module Todos =
    type Todo =
        {
            Id: Guid
            CategoryIds : List<Guid>
            TagIds: List<Guid>
            Description: string
        }
    type Todos =
        {
            todos: List<Todo>
        }
        with
            static member Zero =
                {
                    todos = []
                }

            member this.AddTodo (t: Todo) =
                result {
                    let! description_must_not_exist_already =
                        this.todos
                        |> List.exists (fun x -> x.Description = t.Description)
                        |> not
                        |> boolToResult (sprintf "A todo with the description %A already exists" t.Description)
                    return
                        {
                            this with
                                todos = t::this.todos
                        }
                }
            member this.AddTodos (ts: List<Todo>) =
                let checkNotExists t =
                    this.todos
                    |> List.exists (fun x -> x.Description = t.Description || x.Id = t.Id)
                    |> not
                    |> boolToResult (sprintf "A todo with the description %A already exists, or having the same id" t.Description)

                result {
                    let! mustNotExist =
                        ts |> catchErrors checkNotExists
                    return
                        {
                            this with
                                todos = ts @ this.todos
                        }
                }
            member this.RemoveTodo (id: Guid) =
                result {
                    let! id_must_exist =
                        this.todos
                        |> List.exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A Todo with id '%A' does not exist" id)
                    return
                        {
                            this with
                                todos = this.todos |> List.filter (fun x -> x.Id <> id)
                        }
                }
            member this.GetTodos() = this.todos
