namespace Sharpino.Sample.Entities
open FSharpPlus
open System
open Sharpino.Utils
open Sharpino.Core
open FsToolkit.ErrorHandling

module Todos =

    type Todo =
        {
            Id: Guid
            CategoryIds : List<Guid>
            TagIds: List<Guid>
            Description: string
        }
        interface Entity with
            member this.Id = this.Id
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
                    let! idAndDescriptionsNotAlreadyExists =
                        this.todos 
                        |> List.forall
                            ( fun 
                                x -> 
                                    x.Description <> t.Description 
                                    || x.Id <> t.Id 
                            )
                        |> boolToResult (sprintf "A todo with the description %A already exists" t.Description)

                    return
                        {
                            this with
                                todos = t::this.todos
                        }
                }
            member this.AddTodos (ts: List<Todo>) =
                let descriptionNotAlreadyExists t =
                    this.todos
                    |> List.exists (fun x -> x.Description = t.Description || x.Id = t.Id)
                    |> not
                    |> boolToResult (sprintf "A todo with the description %A already exists, or having the same id" t.Description)

                let idNotAlreadyExists t =
                    this.todos
                    |> List.exists (fun x -> x.Id = t.Id)
                    |> not
                    |> boolToResult (sprintf "A todo with the id %A already exists" t.Id)

                result {
                    let! descMustNotExist =
                        ts |> catchErrors descriptionNotAlreadyExists
                    let! idMustNotExist =
                        ts |> catchErrors idNotAlreadyExists

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
