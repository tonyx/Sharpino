namespace Sharpino.Sample.Shared
open System

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

module Entities =
    type Category =
        {
            Id: Guid
            Name: string
        }

    type Color =
        | Red
        | Green
        | Blue

    type Tag =
        {
            Id: Guid
            Name: string
            Color: Color
        }

    type Todo =
        {
            Id: Guid
            CategoryIds : List<Guid>
            TagIds: List<Guid>
            Description: string
        }
        with
        static member Create name =
            {
                Id = Guid.NewGuid()
                CategoryIds = []
                TagIds = []
                Description = name
            }

module Service =
    open Entities
    type ITodosApi =
        {
            AddTodo: Todo -> Async<Result<unit, string>>
            GetAllTodos: unit -> Async<Result<List<Todo>, string>>
            Add2Todos: Todo * Todo -> Async<Result<unit, string>>
            RemoveTodo: Guid -> Async<Result<unit, string>>
            GetAllCategories: unit -> Async<Result<List<Category>, string>>
            AddCategory: Category -> Async<Result<unit, string>>
            RemoveCategory: Guid -> Async<Result<unit, string>>
            AddTag: Tag -> Async<Result<unit, string>>
            RemoveTag: Guid -> Async<Result<unit, string>>
            GetAllTags: unit -> Async<Result<List<Tag>, string>>
        }
