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
open log4net.Config
open Conf


module PgRepository =

    // serialize are already present ... ????
    type Serializable<'F> =
        abstract member Deserialize<'A> : 'F -> Result<'A, string>
        abstract member Serialize<'A> : 'A -> 'F
        
    let config = Conf.config ()
    let evenStoreTimeout = config.EventStoreTimeout    

    type PgRepository<'A when 'A: equality and 'A:> JsonSerializableEntity> (connection: string, repositoryName: string) =
        let log = LogManager.GetLogger(repositoryName)
        let streamName = repositoryName + "_repository"
        interface IRepository<'A> with
            member this.Add (x: 'A, msg: string) =
                printf "XXX adding %A" x
                // let addCommand = sprintf "INSERT INTO %s (id, data) VALUES ('%s', '%s')" repositoryName (x.Id.ToString()) x.Serialize
                let addCommand = sprintf "INSERT INTO %s (id, data) VALUES ('%s', '%s')" streamName (x.Id.ToString()) x.Serialize
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
                                printf "XXX error %A" ex
                                log.Error ex.Message
                                Result.Error msg
                    }, timeout = 1000)
                        
            member this.AddMany(items:List<'A>, msg: 'A -> string) =
                log.Debug "add many"
                // let addCommand  = sprintf "INSERT INTO %s (id, data) VALUES (@id, @item);" repositoryName
                let addCommand  = sprintf "INSERT INTO %s (id, data) VALUES (@id, @item);" streamName
                let conn = new NpgsqlConnection(connection)
                conn.Open()
                let transaction = conn.BeginTransaction()
                Async.RunSynchronously
                    ( async {
                         let result =
                            try
                                items
                                |>>
                                     fun item ->
                                         let addCommand' = new NpgsqlCommand(addCommand, conn)
                                         addCommand'.Parameters.AddWithValue("id", item.Id.ToString())
                                         addCommand'.Parameters.AddWithValue("item", item.Serialize)
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
                
            member this.AddManyWithPredicate(x, msg, p) = 
                log.Debug "add many with preicate"
                (this:> IRepository<'A>).AddMany(x, msg)
            
            member this.AddWithPredicate (x, p, msg) = 
                log.Debug "add with predicate"
                (this:> IRepository<'A>).Add(x, msg)
            
            member this.Exists(var0) = failwith "todo"
            member this.Find(var0) = failwith "todo"
            member this.Get(var0) = failwith "todo"
            member this.GetAll() = failwith "todo"
            member this.IsEmpty() = failwith "todo"
            member this.Remove var0 var1 = failwith "todo"
            
