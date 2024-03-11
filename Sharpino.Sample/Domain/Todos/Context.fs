namespace Sharpino.Sample
open System

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Shared.Entities
open Sharpino
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Repositories

open FSharpPlus
open FsToolkit.ErrorHandling

module TodosContext =

    [<CurrentVersion>]
    type TodosContext(todos: Todos, categories: Categories) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.Todos = todos
        member this.Categories = categories
        static member Zero =
            TodosContext(Todos.Zero, Categories.Zero)
        static member StorageName =
            "_todo"
        static member Version =
            "_01"
        static member SnapshotsInterval =
            15 
        static member Lock =
            new Object()
        static member Deserialize (serializer: ISerializer, json: Json): Result<TodosContext, string>  =
            serializer.Deserialize<TodosContext> json
        member this.Serialize(serializer: ISerializer) =
            this
            |> serializer.Serialize

        member this.Ping(): Result<TodosContext,string> =
            this |> Ok

        member this.AddTodo (t: Todo) =
            let checkCategoryExists (c: Guid ) =
                this.Categories.GetCategories().Exists (fun x -> x.Id = c)
                |> Result.ofBool (sprintf "A category with id '%A' does not exist" c)

            result
                {
                    let! categoriesMustExist = t.CategoryIds |> List.traverseResultM checkCategoryExists
                    let! todos = this.Todos.AddTodo t
                    return 
                        TodosContext (todos, categories)
                }
        member this.RemoveTodo (id: Guid) =
            result
                {
                    let! todos = this.Todos.RemoveTodo id
                    return
                        TodosContext (todos, categories)
                }
        member this.GetTodos() = this.Todos.GetTodos()
        member this.AddCategory (c: Category) =
            result
                {
                    let! categories = this.Categories.AddCategory c
                    return
                        TodosContext (this.Todos, categories)
                }
        member this.RemoveCategory (id: Guid) = 
            let removeReferenceOfCategoryToTodos (id: Guid) =
                this.Todos.Todos.GetAll()
                |>>
                (fun x -> 
                    { x with 
                        CategoryIds = 
                            x.CategoryIds 
                            |> List.filter (fun y -> y <> id)}
                )
            
            result
                {
                    let! categories = this.Categories.RemoveCategory id
                    return
                        TodosContext (removeReferenceOfCategoryToTodos id |> Todos.FromList, categories)
                }   
        member this.RemoveTagReference (id: Guid) =
            let removeReferenceOfTagToAllTodos (id: Guid) =
                this.Todos.Todos.GetAll()
                |>> 
                (fun x -> 
                    { x with 
                        TagIds = 
                            x.TagIds 
                            |> List.filter (fun y -> y <> id)}
                )
            result
                {
                    return
                        TodosContext((removeReferenceOfTagToAllTodos id) |> Todos.FromList, this.Categories)
                }
        member this.GetCategories() = this.Categories.GetCategories().GetAll()

        // assume this should me moved but atm doesn't work in a separate module


// what follows is the same code as above, but with the new version of the context
    [<UpgradedVersion>]

    type TodosContextUpgraded(todos: Todos) =
        let stateId = Guid.NewGuid()
        member this.StateId = stateId
        member this.todos = todos
        static member Zero =
            TodosContextUpgraded(Todos.Zero)
        // storagename _MUST_ be unique for each context and the relative lock object 
        // must be added in syncobjects map in Conf.fs
        static member StorageName =
            "_todo"
        static member Version =
            "_02"
        static member SnapshotsInterval =
            15
        static member Lock = 
            new Object()

        member this.Ping(): Result<TodosContextUpgraded,string> =
            this |> Ok
        member this.AddTodo (t: Todo) =
            result
                {
                    let! todos = this.todos.AddTodo t
                    return
                        TodosContextUpgraded(todos)
                }
        member this.RemoveTodo (id: Guid) =
            result
                {
                    let! todos = this.todos.RemoveTodo id
                    return
                        TodosContextUpgraded(todos)
                }

        member this.AddTodos (ts: List<Todo>) =
            result
                {
                    let! todos = this.todos.AddTodos ts
                    return
                        TodosContextUpgraded(todos)
                }

        member this.GetTodos() = this.todos.GetTodos()

        member this.RemoveCategoryReference (id: Guid) =
            let removeReferenceOfCategoryToTodos (id: Guid) =
                this.todos.Todos.GetAll()
                |>>
                (fun x -> 
                    { x with 
                        CategoryIds = 
                            x.CategoryIds 
                            |> List.filter (fun y -> y <> id)}
                )
            
            result
                {
                    return
                        TodosContextUpgraded((removeReferenceOfCategoryToTodos id) |> Todos.FromList)
                }

        member this.RemoveTagReference (id: Guid) =
            let removeReferenceOfTagToAllTodos (id: Guid) =
                this.todos.Todos.GetAll() 
                |>> 
                (fun x -> 
                    { x with 
                        TagIds = 
                            x.TagIds 
                            |> List.filter (fun y -> y <> id)}
                )
            result
                {
                    return
                        TodosContextUpgraded((removeReferenceOfTagToAllTodos id) |> Todos.FromList)
                }
        member this.Serialize(serializer: ISerializer) =
            this |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json)=
            serializer.Deserialize<TodosContextUpgraded> json
