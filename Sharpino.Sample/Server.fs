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


    // those are the three options for the app to be used in the server

    // let app = currentPostgresApp
    // let app = currentMemoryApp
    // let app = eventBrokerStateBasedApp
    // let app = currentVersionPgWithKafkaApp 
    let app = eventBrokerStateBasedApp 

    let todosApi: ITodosApi =
        {
            AddTodo =
                fun (t: Todo) ->
                    log.Debug (sprintf "adding todo %A" t)
                    async {
                        return app.addTodo t |> Result.map (fun _ -> ())
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
                        let added = app.add2Todos (t1, t2)
                        match added with
                        | Ok _ -> return Ok ()
                        | Error x ->  return Error x
                    }
            RemoveTodo = 
                fun (id: Guid) ->
                    async {
                        let removed = app.removeTodo id
                        match removed with
                        | Ok _ -> return Ok ()
                        | Error x ->  return Error x
                    }
            GetAllCategories = 
                fun () ->
                    async {
                        return app.getAllCategories ()
                    }
            AddCategory = 
                fun (c: Category) ->
                    async {
                        let added = app.addCategory c
                        match added with
                        | Ok _ -> return Ok ()
                        | Error x ->  return Error x
                    }
            RemoveCategory = 
                fun (id: Guid) ->
                    async {
                        let removed =  app.removeCategory id
                        match removed with
                        | Ok _ -> return Ok ()
                        | Error x -> return Error x
                    }
            AddTag = 
                fun (t: Tag) ->
                    async {
                        let added = app.addTag t
                        match added with
                        | Ok _ -> return Ok ()
                        | Error x ->  return Error x
                    }
            RemoveTag = 
                fun (id: Guid) ->
                    async {
                        let removed = app.removeTag id
                        match removed with
                        | Ok _ -> return Ok ()
                        | Error x ->  return Error x
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


        