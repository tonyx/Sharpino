namespace Tonyx.EventSourcing.Sample.Todos.Models
open FSharpPlus
open System
open Tonyx.EventSourcing.Utils

module TodosModel =
    let ceResult = CeResultBuilder()
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
                ceResult {
                    let! description_must_not_exist_already =
                        this.todos
                        |> List.exists (fun x -> x.Description = t.Description)
                        |> not
                        |> boolToResult (sprintf "A todo with the description %A already exists" t.Description)
                    let result =
                        {
                            this with
                                todos = t::this.todos
                        }
                    return result
                }
            member this.RemoveTodo (id: Guid) =
                ceResult {
                    let! id_must_exist =
                        this.todos
                        |> List.exists (fun x -> x.Id = id)
                        |> boolToResult (sprintf "A Todo with id '%A' does not exist" id)
                    let result =
                        {
                            this with
                                todos = this.todos |> List.filter (fun x -> x.Id <> id)
                        }
                    return result
                }
            member this.GetTodos() = this.todos
