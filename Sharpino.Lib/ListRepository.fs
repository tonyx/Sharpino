namespace Sharpino
open Sharpino.Lib.Core
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Lib.Core.Commons
open System
open FsToolkit.ErrorHandling

module Repositories =    

    type Repository2<'A when 'A: equality and 'A :> Entity> =
        {
            Items: List<'A>
        }
        with 
            static member Create (items: List<'A>) =
                {
                    Items = items
                }
            static member Zero = { Items = [] :> List<'A>}

                member this.Add (x: 'A, msg: string) = 
                    ResultCE.result {
                        let! notAlreadyExists = 
                            (this.Items |> List.tryFind (fun y -> y.Id = x.Id)).IsNone
                            |> boolToResult msg
                        return { this with Items = x::this.Items }
                    }
                member this.AddWithPredicate (x: 'A, p: 'A -> bool, msg: string) =
                    ResultCE.result {
                        let! notAlreadyExists = 
                            (this.Items |> List.tryFind (fun y -> y.Id = x.Id || p(y))).IsNone
                            |> boolToResult msg
                        return { this with Items = x::this.Items }
                    }

                member this.AddMany (xs: List<'A>, msg: 'A -> string) =
                    let notExists (t: 'A) =
                        this.Items |> List.exists (fun x -> x.Id = t.Id)
                        |> not
                        |> boolToResult (msg t) // (sprintf "an item with id %A already exists" t.Id)

                    ResultCE.result {
                        let! doesNotExist =
                            xs |> catchErrors notExists
                        return {
                            this    
                                with Items = xs @ this.Items
                        }
                    }
                member this.AddManyWithPredicate (xs: List<'A>, msg: 'A -> string, p: 'A * 'A -> bool) =
                    let notExists (t: 'A) =
                        this.Items |> List.exists (fun x -> x.Id = t.Id || p(x, t))
                        |> not
                        |> boolToResult (msg t)

                    ResultCE.result {
                        let! doesNotExist =
                            xs |> catchErrors notExists
                        return 
                            {
                                this    
                                    with Items = xs @ this.Items
                            }
                    }
                member this.Remove (id: Guid) (errorMsg: string) =
                    ResultCE.result {
                        let exists = this.Items |> List.exists (fun x -> x.Id = id)
                        if exists then
                            return { this with Items = this.Items |> List.filter (fun x -> x.Id <> id) }
                        else
                            return! Error errorMsg
                    }
                member this.Find (f: 'A -> bool) =
                    this.Items |> List.tryFind f
                member this.Get id =
                    this.Items |> List.tryFind (fun x -> x.Id = id)
                member this.Exists (f: 'A -> bool) =
                    this.Items |> List.exists f
                member this.IsEmpty () =
                    this.Items |> List.isEmpty
                member this.GetAll () =
                    this.Items
