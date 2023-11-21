namespace Sharpino.Sample
open Sharpino.EventSourcing.Sample.AppVersions
open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Shared.Service
open Sharpino.Sample.Shared
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open System
open log4net

module Server =
    // let app = currentMemoryApp // currentPostgresApp
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    // log4net.Config.BasicConfigurator.Configure() |> ignore
    log.Debug "starting server"
    // let app = currentPostgresApp
    let app = currentMemoryApp

    let todosApi: ITodosApi =
        {
            AddTodo =
                fun (t: Todo) ->
                    log.Debug (sprintf "adding todo %A" t)
                    async {
                            return app.addTodo t
                    }
            GetAllTodos = 
                fun () ->
                    log.Debug "getting all todos"
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
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message)
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


        