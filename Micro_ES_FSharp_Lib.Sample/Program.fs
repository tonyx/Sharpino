namespace Tonyx.EventSourcing.Sample

open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel
open Tonyx.EventSourcing.Sample.Shared

open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open System

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

module EntryPoint =
    let coreapplication = AppVersions.applicationMemoryStorage

    let restTodoApi =
        {
            getAllTodos = 
                fun () ->
                    async 
                        {
                            return
                                match (coreapplication.getAllTodos()) with
                                | Ok l -> l
                                | Error x -> failwith x
                        }
            addTodo =
                fun todo ->
                    async 
                        {
                            return
                                match (coreapplication.addTodo todo) with
                                | Ok _ -> todo
                                | Error x -> failwith x
                        }
            removeTodo =
                fun guid ->
                    async 
                        {
                            return
                                match (coreapplication.removeTodo guid) with
                                | Ok _ -> guid
                                | Error x -> failwith x
                        }
            getAllCategories = 
                fun () ->
                    async
                        {
                            return
                                match (coreapplication.getAllCategories()) with
                                | Ok l -> l
                                | Error x -> failwith x
                        }
            addCategory =       
                fun category ->
                    async {
                        return
                            match (coreapplication.addCategory category) with
                            | Ok _ -> category
                            | Error x -> failwith x
                    }
            removeCategory =
                fun guid ->
                    async {
                        return
                            match (coreapplication.removeCategory guid) with
                            | Ok _ -> guid
                            | Error x -> failwith x
                    }
            addTag =    
                fun tag ->
                    async {
                        return
                            match (coreapplication.addTag tag) with
                            | Ok _ -> tag
                            | Error x -> failwith x
                    }
            removeTag =
                fun guid ->
                    async {
                        return
                            match (coreapplication.removeTag guid) with
                            | Ok _ -> guid
                            | Error x -> failwith x
                    }
            getAllTags =
                fun () ->
                    async
                        {
                            return
                                match (coreapplication.getAllTags()) with
                                | Ok l -> l
                                | Error x -> failwith x
                        }
        }

    let webApp =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue restTodoApi
        |> Remoting.buildHttpHandler

    let app =  
        application {
            use_router webApp
            memory_cache
            use_static "public"
            use_gzip
        }

    [<EntryPoint>]
    let main _ =
        run app
        0