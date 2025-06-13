namespace ItemManager

open System
open Sharpino
open Sharpino.Cache
open Sharpino.Sample._9.Item
open Sharpino.Storage
open DotNetEnv

module Common =

    Env.Load() |> ignore
    let password = Environment.GetEnvironmentVariable("password")

    let connection =
        "Server=127.0.0.1;"+
        "Database=sharpino_item;" +
        "User Id=safe;"+
        $"Password={password}";

    let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection
    let memEventStore = MemoryStorage.MemoryStorage()

    let setUp (eventStore: IEventStore<string>) =
        eventStore.Reset Item.Version Item.StorageName
        eventStore.ResetAggregateStream Item.Version Item.StorageName
        AggregateCache<Item, string>.Instance.Clear()
