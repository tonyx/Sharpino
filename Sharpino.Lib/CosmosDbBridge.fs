
namespace Sharpino

open FsToolkit.ErrorHandling
open FSharp.Data.Sql
open Npgsql.FSharp
// open FSharpPlus
open Sharpino
open Sharpino.Core
open Sharpino.Storage
open Microsoft.Azure.Cosmos
open System

module CosmosDb =
    // let accountEndpoint =  "https://localhost:8081",
    // let authKeyOrResourceToken = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

    type Serializable<'E> =
        {
            id: string
            aggregateName: string
            // version: string
            // aggregateName: string
            event: 'E
        }

    type CosmosDbBridge<'A, 'E when 'E :> Event<'A>>(accountEndPoint: string, authKeyorResourceToken: string) =
            let client = new CosmosClient( accountEndPoint, authKeyorResourceToken )
            let database = client.CreateDatabaseIfNotExistsAsync("es_01") |> Async.AwaitTask |> Async.RunSynchronously
            let db = database.Database
        // interface IStorage with
            member this.AddEvents(version: version) (events: List<'E>) (name: Name): Result<unit,string> = 
                try
                    let streamName = version+name
                    let first = events.Head
                    let container = db.CreateContainerIfNotExistsAsync("events", "/aggregateName") |> Async.AwaitTask |> Async.RunSynchronously
                    let cnt = container.Container

                    // let created =
                    //     events 
                    //     |> cnt.CreateItemStreamAsync<'E>(events, Nullable(new PartitionKey(streamName)))  |> Async.AwaitTask |> Async.RunSynchronously


                    let serializableEvent = {id = Guid.NewGuid().ToString(); aggregateName = streamName; event = first}

                    printf "add event 100\n"
                    let created =
                        // cnt.CreateItemAsync<'E>(events.Head, Nullable(new PartitionKey(streamName)))  |> Async.AwaitTask  |> Async.RunSynchronously
                        cnt.CreateItemAsync<Serializable<'E>>(serializableEvent, Nullable(new PartitionKey(streamName)))  |> Async.AwaitTask  |> Async.RunSynchronously
                        // events 
                        // |> List.map (fun x -> 
                        //     // cnt.CreateItemAsync<'E>(x, Nullable(new PartitionKey("/aggregateName"))) |> Async.AwaitTask |> Async.RunSynchronously)
                        //     cnt.CreateItemAsync<'E>(x, Nullable(new PartitionKey(streamName))) |> Async.AwaitTask |> Async.RunSynchronously)

                    printf "add event 100 %A\n" created

                    () |> Ok
                with
                    e -> Error (sprintf "error %A" e)


            member this.GetEventsAfterId(arg1: version) (arg2: int) (arg3: Name): List<int * 'E> = 
                failwith "Not Implemented"
            member this.MultiAddEvents(arg1: List<List<'E> * version * Name>): Result<unit,string> = 
                failwith "Not Implemented"
            member this.Reset(arg1: version) (arg2: Name): unit = 
                failwith "Not Implemented"
            member this.SetSnapshot(arg1: version) (arg2: int, arg3: 'A) (arg4: Name): Result<unit,string> = 
                failwith "Not Implemented"
            member this.TryGetEvent(arg1: version) (arg2: int) (arg3: Name): Option<StorageEvent> = 
                failwith "Not Implemented"
            member this.TryGetLastEventId(arg1: version) (arg2: Name): Option<int> = 
                failwith "Not Implemented"
            member this.TryGetLastSnapshot(arg1: version) (arg2: Name): Option<int * int * 'A> = 
                failwith "Not Implemented"
            member this.TryGetLastSnapshotEventId(arg1: version) (arg2: Name): Option<int> = 
                failwith "Not Implemented"
            member this.TryGetLastSnapshotId(arg1: version) (arg2: Name): Option<int> = 
                failwith "Not Implemented"
