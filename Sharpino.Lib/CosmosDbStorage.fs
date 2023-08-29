

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

// experimental work in progress spike. should not stay in the main (remove soon)
module CosmosDbStorage =
    // let accountEndpoint =  "https://localhost:8081",
    // let authKeyOrResourceToken = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

    type Serializable<'E> =
        {
            // positionId: int
            id: string
            aggregateName: string
            // version: string
            // aggregateName: string
            event: 'E
        }

    // todo: remove. substitute with CosmosDbLightStorage
    type ComsmosDbStorage (accountEndPoing: string, authKeyorResourceToken: string) =
        // new(accountEndPoing: string, authKeyorResourceToken: string) = ComsmosDbStorage(accountEndPoing, authKeyorResourceToken
        // new () = ComsmosDbStorage("https://localhost:8081", "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
        let client = new CosmosClient( accountEndPoing, authKeyorResourceToken )
        let database = client.CreateDatabaseIfNotExistsAsync("es_01") |> Async.AwaitTask |> Async.RunSynchronously
        let db = database.Database

        let mksSerializableEvent (version: version) (name: Name)  (event: 'E) =
            {id = Guid.NewGuid().ToString(); aggregateName = version+name; event = event}

        interface IStorageRefactor with
            member this.AddEvents(version: version) (events: List<'E>) (name: Name) = 
                let initIntIndex = 
                    match (this :> IStorageRefactor).TryGetLastEventId version name with
                    | Some x -> x + 1
                    | None -> 0

                let indexes = [initIntIndex .. events.Length-1]
                let eventsWithIndex = List.zip events indexes

                try
                    let streamName = version+name
                    let container = db.CreateContainerIfNotExistsAsync("events", "/aggregateName") |> Async.AwaitTask |> Async.RunSynchronously
                    let cnt = container.Container

                    printf "add event 100\n"
                    let created =
                        eventsWithIndex 
                        |> List.map (fun x -> 
                            cnt.CreateItemAsync<Serializable<'E>>( ((x |> fst) |> mksSerializableEvent version name ) , Nullable(new PartitionKey(streamName))) |> Async.AwaitTask |> Async.RunSynchronously)

                    printf "add event 100 %A\n" created

                    () |> Ok
                with
                    e -> Error (sprintf "error %A" e)

            member this.GetEventsAfterId(version: version) (id: int) (name: Name): List<int * 'E> = 
                let stramName = version+name
                failwith "Not Implemented"
            member this.MultiAddEvents(arg1: List<List<obj> * version * Name>): Result<unit,string> = 
                failwith "Not Implemented"
            member this.Reset(arg1: version) (arg2: Name): unit = 
                failwith "Not Implemented"
            member this.SetSnapshot(arg1: version) (arg2: int, arg3: 'A) (arg4: Name): Result<unit,string> = 
                failwith "Not Implemented"
            member this.TryGetEvent(arg1: version) (arg2: int) (arg3: Name): Option<StorageEventRef<obj>> = 
                failwith "Not Implemented"


                // failwith "Not Implemented"

            member this.TryGetLastSnapshot(arg1: version) (arg2: Name): Option<int * int * 'A> = 
                None
            member this.TryGetLastSnapshotEventId(arg1: version) (arg2: Name): Option<int> = 
                failwith "Not Implemented"
            member this.TryGetLastSnapshotId(version: version) (name: Name): Option<int> = 
                failwith "Not Implemented"


            member this.TryGetLastEventId(version: version) (name: Name): Option<int> =
                let streamName = version+name

                let queryText = sprintf "SELECT MAX(c._ts) FROM c WHERE c.aggregateName = '%s'" streamName
                let container = db.CreateContainerIfNotExistsAsync("events", "/aggregateName") |> Async.AwaitTask |> Async.RunSynchronously
                let cnt = container.Container

                let queryResultSetIterator = 
                    cnt.GetItemQueryIterator<Serializable<obj>>(
                        queryText, 
                        null)
                let _ =
                    while queryResultSetIterator.HasMoreResults do
                        let res = queryResultSetIterator.ReadNextAsync() |> Async.AwaitTask |> Async.RunSynchronously
                        printf "XXXXXX %A \n" res
                        ()
                        // let currentResultSet = queryResultSetIterator.ReadNextAsync() |> Async.AwaitTask |> Async.RunSynchronously
                //         let results = currentResultSet.GetEnumerator().Current
                //         printf "results %A\n" results
                        // match results with
                        // | [] -> None
                        // | x::xs -> Some x 

                None
        // new () = ComsmosDbStorage("https://localhost:8081", "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
                // None



    type CosmosDbLightStorage (accountEndPoing: string, authKeyorResourceToken: string) =

        let client = new CosmosClient( accountEndPoing, authKeyorResourceToken )
        let database = client.CreateDatabaseIfNotExistsAsync("es_01") |> Async.AwaitTask |> Async.RunSynchronously
        let db = database.Database

        let mksSerializableEvent (version: version) (name: Name)  (event: 'E) =
            {id = Guid.NewGuid().ToString(); aggregateName = version+name; event = event}

        interface ILightStorageRefactor with
            member this.AddEvents(version: version) (events: List<'E>) (name: Name) = 
                let initIntIndex = 0

                let indexes = [initIntIndex .. events.Length-1]
                let eventsWithIndex = List.zip events indexes

                try
                    let streamName = version+name
                    let container = db.CreateContainerIfNotExistsAsync("events", "/aggregateName") |> Async.AwaitTask |> Async.RunSynchronously
                    let cnt = container.Container

                    printf "add event 100\n"
                    let created =
                        eventsWithIndex 
                        |> List.map (fun x -> 
                            cnt.CreateItemAsync<Serializable<'E>>( ((x |> fst) |> mksSerializableEvent version name ) , Nullable(new PartitionKey(streamName))) |> Async.AwaitTask |> Async.RunSynchronously)

                    printf "add event 100 %A\n" created

                    () |> Ok
                with
                    e -> Error (sprintf "error %A" e)
