namespace ItemManager

open System
open Sharpino
open Sharpino.Cache
open Sharpino.Core
open Sharpino.Sample._9
open Sharpino.Sample._9.Reservation
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.Course
open Sharpino.Sample._9.Item
open Sharpino.Sample._9.Student
open Sharpino.Sample._9.Teacher
open Sharpino.Storage
open DotNetEnv

module Common =

    Env.Load() |> ignore
    let password = Environment.GetEnvironmentVariable("password")

    let connection =
        "Server=127.0.0.1;"+
        "Database=sharpino_item;" +
        "Include Error Detail=True;"+
        "User Id=safe;"+
        $"Password={password}";

    let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection
    let memEventStore = MemoryStorage.MemoryStorage()

    let setUp (eventStore: IEventStore<string>) =
        eventStore.Reset Item.Version Item.StorageName
        eventStore.ResetAggregateStream Item.Version Item.StorageName
        eventStore.Reset Course.Version Course.StorageName
        eventStore.ResetAggregateStream Course.Version Course.StorageName
        eventStore.Reset Student.Version Student.StorageName
        eventStore.ResetAggregateStream Student.Version Student.StorageName
        eventStore.Reset Balance.Version Balance.StorageName
        eventStore.ResetAggregateStream Balance.Version Balance.StorageName
        eventStore.Reset Teacher.Version Teacher.StorageName
        eventStore.ResetAggregateStream Teacher.Version Teacher.StorageName
        eventStore.Reset Reservation.Version Reservation.StorageName
        AggregateCache<Item, string>.Instance.Clear()
        AggregateCache<Reservation, string>.Instance.Clear()
        AggregateCache<Balance, string>.Instance.Clear()
        AggregateCache<Course, string>.Instance.Clear()
        AggregateCache<Student, string>.Instance.Clear()
        AggregateCache<Teacher, string>.Instance.Clear()

    let inline getHistoryAggregateStorageFreshStateViewer<'A, 'E, 'F
        when 'A :> Aggregate<'F> 
        and 'A : (static member Deserialize: 'F -> Result<'A, string>) 
        and 'A : (static member StorageName: string) 
        and 'A : (static member Version: string) 
        and 'E :> Event<'A>
        and 'E: (static member Deserialize: 'F -> Result<'E, string>)
        >
        (eventStore: IEventStore<'F>) 
        =
            fun (id: Guid) -> StateView.getHistoryAggregateFreshState<'A, 'E, 'F> id eventStore
