namespace Sharpino.Sample
open System

open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Shared.Entities
open Sharpino.Utils
open Sharpino.Definitions
open Sharpino.Repositories

open FSharpPlus
open FsToolkit.ErrorHandling

module TodosContext =

    [<CurrentVersion>]
    type TodosContext =
        {
            todos: Todos
            categories: Categories
        }
        static member Zero =
            {
                todos = Todos.Zero
                categories = Categories.Zero
            }
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
            result
                {
                    return
                        this
                }

        member this.AddTodo (t: Todo) =
            let checkCategoryExists (c: Guid ) =
                this.categories.GetCategories().Exists (fun x -> x.Id = c)
                |> boolToResult (sprintf "A category with id '%A' does not exist" c)

            result
                {
                    let! categoriesMustExist = t.CategoryIds |> catchErrors checkCategoryExists
                    let! todos = this.todos.AddTodo t
                    return 
                        {
                            this with
                                todos = todos
                        }
                }
        member this.RemoveTodo (id: Guid) =
            result
                {
                    let todos = this.todos.RemoveTodo id
                    let! todos = todos
                    return
                        {
                            this with
                                todos = todos
                        }
                }
        member this.GetTodos() = this.todos.GetTodos()
        member this.AddCategory (c: Category) =
            result
                {
                    let! categories = this.categories.AddCategory c
                    return
                        {
                            this with
                                categories = categories
                        }
                }
        member this.RemoveCategory (id: Guid) = 
            let removeReferenceOfCategoryToTodos (id: Guid) =
                this.todos.todos.GetAll()
                |>>
                (fun x -> 
                    { x with 
                        CategoryIds = 
                            x.CategoryIds 
                            |> List.filter (fun y -> y <> id)}
                )
            
            result
                {
                    let! categories = this.categories.RemoveCategory id
                    return
                        {
                            this with
                                categories = categories
                                todos = 
                                    (removeReferenceOfCategoryToTodos id) |> Todos.FromList
                        }
                }   
        member this.RemoveTagReference (id: Guid) =
            let removeReferenceOfTagToAllTodos (id: Guid) =
                this.todos.todos.GetAll()
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
                        {
                            this with
                                todos = 
                                    (removeReferenceOfTagToAllTodos id) |> Todos.FromList


                        }
                }
        member this.GetCategories() = this.categories.GetCategories().GetAll()

        // assume this should me moved but atm doesn't work in a separate module


// what follows is the same code as above, but with the new version of the context
    [<UpgradedVersion>]

    type TodosContextUpgraded =
        {
            todos: Todos
        }
        static member Zero =
            {
                todos = Todos.Zero
            }
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
            result
                {
                    return
                        this
                }
        member this.AddTodo (t: Todo) =
            result
                {
                    let! todos = this.todos.AddTodo t
                    return
                        {
                            this with
                                todos = todos
                        }
                }
        member this.RemoveTodo (id: Guid) =
            result
                {
                    let! todos = this.todos.RemoveTodo id
                    return
                        {
                            this with
                                todos = todos
                        }
                }

        member this.AddTodos (ts: List<Todo>) =
            result
                {
                    let! todos = this.todos.AddTodos ts
                    return
                        {
                            this with
                                todos = todos
                        }
                }

        member this.GetTodos() = this.todos.GetTodos()

        member this.RemoveCategoryReference (id: Guid) =
            let removeReferenceOfCategoryToTodos (id: Guid) =
                this.todos.todos.GetAll()
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
                        {
                            this with
                                todos = 
                                    (removeReferenceOfCategoryToTodos id) |> Todos.FromList
                        }
                }

        member this.RemoveTagReference (id: Guid) =
            let removeReferenceOfTagToAllTodos (id: Guid) =
                this.todos.todos.GetAll() 
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
                        {
                            this with
                                todos = 
                                    (removeReferenceOfTagToAllTodos id) |> Todos.FromList
                        }
                }
        member this.Serialize(serializer: ISerializer) =
            this |> serializer.Serialize

        static member Deserialize (serializer: ISerializer, json: Json)=
            serializer.Deserialize<TodosContextUpgraded> json
