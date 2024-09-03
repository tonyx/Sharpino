namespace Sharpino

open FSharp.Core
open FSharpPlus

open Sharpino.Conf
open Sharpino.Core
open Sharpino.Lib.Core.Commons
open Sharpino.Storage
open Sharpino.Repositories
open Sharpino.Utils
open Sharpino.Definitions
open System.Runtime.CompilerServices

open Npgsql.FSharp
open Npgsql
open FsToolkit.ErrorHandling
open log4net
open Sharpino.Commons
open log4net.Config
open Conf


module PgRepository =
    
        
    let config = Conf.config ()
    let evenStoreTimeout = config.EventStoreTimeout    

    type PgRepository<'A when 'A: equality and 'A:> JsonSerializableEntity> (connection: string, repositoryName: string) =
        let log = LogManager.GetLogger(repositoryName)
        let streamName = repositoryName + "_repository"
        
        // caution!!
        member this.Reset () =
            let resetCommand = sprintf "DELETE FROM %s" streamName
            Async.RunSynchronously
                ( async {
                     return
                        try
                            connection
                            |> Sql.connect
                            |> Sql.query resetCommand
                            |> Sql.executeNonQuery
                            |> ignore
                            Result.Ok this
                        with
                        | ex ->
                            printf "XXX error %A" ex
                            log.Error ex.Message
                            Result.Error ex.Message
                }, timeout = evenStoreTimeout)
        
        interface IRepository<'A> with
            member this.Add (x: 'A, msg: string) =
                let addCommand = sprintf "INSERT INTO %s (id, data) VALUES ('%s', '%s')" streamName (x.Id.ToString()) x.Serialize
                try
                    Async.RunSynchronously
                        ( async {
                             return
                                try
                                    connection
                                    |> Sql.connect
                                    |> Sql.query addCommand
                                    |> Sql.executeNonQuery
                                    |> ignore
                                    Result.Ok this
                                with
                                | ex ->
                                    log.Error ex.Message
                                    Result.Error msg
                        }, timeout = evenStoreTimeout)
                with        
                | _ as ex -> 
                    log.Error ex.Message
                    Result.Error ex.Message
                        
            member this.AddMany(items:List<'A>, msg: 'A -> string) =
                log.Debug "add many"
                let addCommand  = sprintf "INSERT INTO %s (id, data) VALUES (@id, @item);" streamName
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction()
                try
                    Async.RunSynchronously
                        ( async {
                             let result =
                                try
                                    items
                                    |>>
                                         fun item ->
                                             let addCommand' = new NpgsqlCommand(addCommand, conn)
                                             addCommand'.Parameters.AddWithValue("id", item.Id.ToString())
                                             addCommand'.Parameters.AddWithValue("data", item.Serialize)
                                    |> ignore         
                                    transaction.Commit()
                                    Result.Ok (this:>IRepository<'A>)
                                with
                                | _ as ex ->
                                    transaction.Rollback()
                                    log.Error ex.Message
                                    ex.Message |> Result.Error
                             try
                                 return result
                             finally
                                    conn.Close()
                        }, timeout = evenStoreTimeout)
                with        
                | _ as ex -> 
                    log.Error ex.Message
                    Result.Error ex.Message
                
            member this.AddManyWithPredicate(x, msg, p) = 
                log.Debug "add many with preicate"
                (this:> IRepository<'A>).AddMany(x, msg)
            
            member this.AddWithPredicate (x, p, msg) = 
                result {
                    let! exists = 
                        (this:> IRepository<'A>).Exists p
                    if exists then
                        return! Result.Error msg
                    else
                        return! (this:> IRepository<'A>).Add(x, msg)
                }
            
            member this.Exists (p: 'A -> bool) =
                log.Debug "exists"
                try
                    Async.RunSynchronously(
                        async {
                            return
                                result
                                    {
                                        let query = sprintf "SELECT data FROM %s" streamName
                                        let! found =
                                            connection
                                            |> Sql.connect
                                            |> Sql.query query
                                            |> Sql.execute (fun read -> 
                                                (read.string "data"))
                                            |> List.traverseResultM (fun x ->
                                                jsonSerializer.Deserialize<'A> ((string)x))
                                        let filtered = found |> List.filter p
                                        return not (List.isEmpty filtered)
                                    }
                        }, timeout = evenStoreTimeout)
                with
                | _ as ex -> 
                    log.Error ex.Message
                    Result.Error ex.Message

            // to be optimized as there is no json query
            member this.Find (p: 'A -> bool) = 
                log.Debug "find"
                result
                    {
                        let query = sprintf "SELECT data FROM %s" streamName
                        let! found =
                            connection
                            |> Sql.connect
                            |> Sql.query query
                            |> Sql.execute (fun read -> 
                                 (read.string "data"))
                            |> List.traverseResultM (fun x ->
                                jsonSerializer.Deserialize<'A> ((string)x))
                        let filtered = found |> List.filter p
                        let optFiltered = filtered |> List.tryHead
                        return optFiltered
                    }
                
                
            member this.Get(counterId) = 
                log.Debug "get"
                let query = sprintf "SELECT data FROM %s WHERE id = @id" streamName

                try
                    Async.RunSynchronously (
                        async {
                            return
                                result {
                                    let! found =
                                        connection
                                        |> Sql.connect
                                        |> Sql.query query
                                        |> Sql.parameters ["id", Sql.uuid counterId]
                                        |> Sql.execute (fun read -> 
                                            (read.string "data"))
                                        |> List.traverseResultM (fun x ->
                                            jsonSerializer.Deserialize<'A> ((string)x))
                                    let firstFound = found |> List.tryHead
                                    return firstFound
                                }
                        }, timeout = evenStoreTimeout)
                with
                | _ as ex -> 
                    log.Error ex.Message
                    Result.Error ex.Message

            member this.GetAll() =
                log.Debug "get all"
                let command = sprintf "SELECT data FROM %s" streamName
                try
                    Async.RunSynchronously (
                        async  {
                            return
                                connection
                                |> Sql.connect
                                |> Sql.query command
                                |> Sql.execute (fun read -> 
                                     (read.string "data"))
                                |> List.traverseResultM (fun x ->
                                    jsonSerializer.Deserialize<'A> ((string)x))    
                        }, timeout = evenStoreTimeout)
                with        
                | _ as ex -> 
                    log.Error ex.Message
                    Result.Error ex.Message
            
            member this.IsEmpty() = 
                result {
                    let! all = (this :> IRepository<'A>).GetAll()
                    return List.isEmpty all
                }

            member this.Remove(id: System.Guid) (msg: string) =
                log.Debug "remove"
                let removeCommand = sprintf "DELETE FROM %s WHERE id = @id" streamName
                Async.RunSynchronously
                    ( async {
                         return
                            try
                                connection
                                |> Sql.connect
                                |> Sql.query removeCommand
                                |> Sql.parameters ["id", Sql.uuid id]
                                |> Sql.executeNonQuery
                                |> ignore
                                Result.Ok this
                            with
                            | ex ->
                                log.Error ex.Message
                                Result.Error msg
                    }, timeout = evenStoreTimeout)

            
