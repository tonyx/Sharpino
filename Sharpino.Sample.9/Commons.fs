namespace ItemManager

open System
open Sharpino
open Sharpino.Cache
open Sharpino.Core
open Sharpino.RabbitMq
open Sharpino.Sample._9
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.BalanceConsumer
open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseConsumer
open Sharpino.Sample._9.Item
open Sharpino.Sample._9.ItemConsumer
open Sharpino.Sample._9.ReservationConsumer
open Sharpino.Sample._9.Student
open Sharpino.Sample._9.StudentConsumer
open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherConsumer
open Sharpino.Storage
open DotNetEnv

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

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
        eventStore.Reset Course.Version Course.StorageName
        eventStore.ResetAggregateStream Course.Version Course.StorageName
        eventStore.Reset Student.Version Student.StorageName
        eventStore.ResetAggregateStream Student.Version Student.StorageName
        eventStore.Reset Balance.Version Balance.StorageName
        eventStore.ResetAggregateStream Balance.Version Balance.StorageName
        eventStore.Reset Teacher.Version Teacher.StorageName
        eventStore.ResetAggregateStream Teacher.Version Teacher.StorageName
        AggregateCache3.Instance.Clear()

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

    #if RABBITMQ
    let hostBuilder =
        Host.CreateDefaultBuilder()
            .ConfigureServices(fun (services: IServiceCollection) ->
                services.AddSingleton<RabbitMqReceiver>() |> ignore 
                services.AddHostedService<ItemConsumer>() |> ignore
                services.AddHostedService<ReservationConsumer>() |> ignore
                
                services.AddHostedService<BalanceConsumer>() |> ignore
                services.AddHostedService<CourseConsumer>() |> ignore
                services.AddHostedService<StudentConsumer>() |> ignore
                services.AddHostedService<TeacherConsumer>() |> ignore
            )
    let host = hostBuilder.Build()
    let hostTask = host.StartAsync()
    let services = host.Services
    #endif
