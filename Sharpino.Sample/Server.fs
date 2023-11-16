namespace Sharpino.Sample
open Sharpino.EventSourcing.Sample.AppVersions
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open System

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

module Server =
    let app = currentMemoryApp

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


    let todosApi: ITodosApi =
        {
            AddTodo =
                fun (t: Todo) ->
                    async {
                            return app.addTodo t
                    }
            GetAllTodos = 
                fun () ->
                    async {
                        return app.getAllTodos ()
                    }

            Add2Todos = 
                fun (t1, t2) ->
                    async {
                        return app.add2Todos (t1, t2)
                    }
            RemoveTodo = 
                fun (id: Guid) ->
                    async {
                        return app.removeTodo id
                    }
            GetAllCategories = 
                fun () ->
                    async {
                        return app.getAllCategories ()
                    }
            AddCategory = 
                fun (c: Category) ->
                    async {
                        return app.addCategory c
                    }
            RemoveCategory = 
                fun (id: Guid) ->
                    async {
                        return app.removeCategory id
                    }
            AddTag = 
                fun (t: Tag) ->
                    async {
                        return app.addTag t
                    }
            RemoveTag = 
                fun (id: Guid) ->
                    async {
                        return app.removeTag id
                    }
            GetAllTags = 
                fun () ->
                    async {
                        return app.getAllTags ()
                    }
        }

    let webApp =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue todosApi
        |> Remoting.buildHttpHandler

    let appl =
        application {
            use_router webApp
            memory_cache
            use_static "public"
            use_gzip
        }

    [<EntryPoint>]
    let main _ =
        run appl
        0
// Try http://localhost:5000/api/ITodosApi/GetAllTodos


        