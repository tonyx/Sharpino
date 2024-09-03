namespace Sharpino
open Sharpino.Lib.Core
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Lib.Core.Commons
open Sharpino
open System
open FsToolkit.ErrorHandling


// usually for each aggregate we mantain its state in memory and use lists
// but we could use a repository like this to be able to persist the state 
module Repositories =    
    type JsonSerializableEntity =
        abstract member Id: Guid
        abstract member Serialize: string
        // abstract member RepositoryName : string
        
    type IRepository<'A when 'A: equality and 'A :> JsonSerializableEntity> =
        abstract member Add<'A> : 'A * string -> Result<IRepository<'A>, string>
        abstract member AddWithPredicate<'A>: 'A * ('A -> bool) * string -> Result<IRepository<'A>, string>
        abstract member AddMany<'A>: List<'A> * ('A -> string) -> Result<IRepository<'A>, string>
        abstract member AddManyWithPredicate<'A>: List<'A> * ('A -> string) * ('A * 'A -> bool) -> Result<IRepository<'A>, string>
        abstract member Remove: Guid -> string -> Result<IRepository<'A>, string>
        abstract member Find<'A>: ('A -> bool) -> Result<Option<'A>, string>
        abstract member Get: Guid -> Result<Option<'A>, string>
        abstract member Exists<'A>: ('A -> bool) -> Result<bool, string>
        abstract member IsEmpty: unit -> Result<bool, string>
        abstract member GetAll<'A>: unit -> Result<List<'A>, string>

    
        
    // type ListRepository<'A when 'A: equality and 'A:> Entity and 'A: (member Serialize: string)>  =
    type ListRepository<'A when 'A: equality and 'A:> JsonSerializableEntity>  =
        {
            Items: List<'A>
        }
        with 
            static member Create (items: List<'A>) =
                {
                    Items = items
                }
            static member Zero = { Items = [] :> List<'A>}

            interface IRepository<'A> with
                member this.Add (x: 'A, msg: string) = 
                    ResultCE.result {
                        let! notAlreadyExists = 
                            (this.Items |> List.tryFind (fun y -> y.Id = x.Id)).IsNone
                            |> Result.ofBool msg
                        return { this with Items = x::this.Items }
                    }
                member this.AddWithPredicate (x: 'A, p: 'A -> bool, msg: string) =
                    ResultCE.result {
                        let! notAlreadyExists = 
                            (this.Items |> List.tryFind (fun y -> y.Id = x.Id || p(y))).IsNone
                            |> Result.ofBool msg
                        return { this with Items = x::this.Items }
                    }

                member this.AddMany (xs: List<'A>, msg: 'A -> string) =
                    let notExists (t: 'A) =
                        this.Items |> List.exists (fun x -> x.Id = t.Id)
                        |> not
                        |> Result.ofBool (msg t) 

                    ResultCE.result {
                        let! doesNotExist =
                            xs |> List.traverseResultM notExists
                        return {
                            this    
                                with Items = xs @ this.Items
                        }
                    }
                member this.AddManyWithPredicate (xs: List<'A>, msg: 'A -> string, p: 'A * 'A -> bool) =
                    let notExists (t: 'A) =
                        this.Items |> List.exists (fun x -> x.Id = t.Id || p(x, t))
                        |> not
                        |> Result.ofBool (msg t)

                    ResultCE.result {
                        let! doesNotExist =
                            xs |> List.traverseResultM notExists
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
                    this.Items |> List.tryFind f |> Ok
                member this.Get id =
                    this.Items |> List.tryFind (fun x -> x.Id = id) |> Ok
                member this.Exists (f: 'A -> bool) =
                    this.Items |> List.exists f |> Ok
                member this.IsEmpty () =
                    this.Items |> List.isEmpty |> Ok
                member this.GetAll () =
                    this.Items |> Ok
