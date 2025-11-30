module Sharpino.Sample._13.Tests
open DotNetEnv
open System

open Expecto
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.Sample._13.Models.Events
open Sharpino.Sample._13.Models.Reservation
open Sharpino.Sample._13.Models.ReservationEvents
open Sharpino.Sample._13.Models.User
open Sharpino.Sample._13.UsersRegistrationManager
open Sharpino.Storage

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;"+
    "Database=sharpino_sample13;" +
    "User Id=safe;"+
    $"Password={password}"

let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset User.Version User.StorageName
    pgEventStore.ResetAggregateStream User.Version User.StorageName
    pgEventStore.Reset ReservationForNickNames.Version ReservationForNickNames.StorageName
    pgEventStore.ResetAggregateStream ReservationForNickNames.Version ReservationForNickNames.StorageName
    AggregateCache3.Instance.Clear()

let pgStorageUsersViewer = getAggregateStorageFreshStateViewer<User, UserEvent, string> pgEventStore
let pgStorageReservationsViewer = getAggregateStorageFreshStateViewer<ReservationForNickNames, ReservationEvent, string> pgEventStore

[<Tests>]
let tests =
    testList "samples" [
        testCase "register a single user - Ok" <| fun _ ->
            setUp()
            let registrationManager =
                UsersRegistrationManager
                    (
                        pgEventStore,
                        pgStorageUsersViewer,
                        pgStorageReservationsViewer,
                        MessageSenders.NoSender
                    )
            let user = User.MkUser "test"
            let result = registrationManager.RegisterUser user
            Expect.isOk result "should be ok"
            let retrievedUser = registrationManager.GetUser user.Id
            Expect.isOk retrievedUser "should be ok"

        testCaseAsync "concurrent register same nickname - only one succeeds" <| async {
            setUp()
            let registrationManager =
                UsersRegistrationManager
                    (
                        pgEventStore,
                        pgStorageUsersViewer,
                        pgStorageReservationsViewer,
                        MessageSenders.NoSender
                    )
            let u1 = User.MkUser "test"
            let u2 = User.MkUser "test"

            let t1 = async { return registrationManager.RegisterUser u1 }
            let t2 = async { return registrationManager.RegisterUser u2 }

            let! results = Async.Parallel [| t1; t2 |]
            let oks = results |> Array.filter (function | Ok _ -> true | _ -> false) |> Array.length
            let errs = results |> Array.filter (function | Error _ -> true | _ -> false) |> Array.length

            Expect.equal oks 1 "exactly one registration should succeed"
            Expect.equal errs 1 "exactly one registration should fail"

            match StateView.getAllAggregateStates<User, UserEvent, string> pgEventStore with
            | Ok states ->
                let users = states |> List.map snd
                Expect.equal users.Length 1 "only one user should be stored"
                Expect.equal users.Head.NickName "test" "stored user has expected nickname"
            | Error e -> failtestf "unexpected error reading users: %s" e
        }
        
        testCaseAsync "concurrent register same nicknames 10 times - only one succeeds" <| async {
            setUp()
            let registrationManager =
                UsersRegistrationManager
                    (
                        pgEventStore,
                        pgStorageUsersViewer,
                        pgStorageReservationsViewer,
                        MessageSenders.NoSender
                    )
            let users = [ for i in 1 .. 10 -> User.MkUser "test" ]
            let insertionTaks =
                users |> List.map (fun u -> async { return registrationManager.RegisterUser u }) |> List.toArray
           
            let! results = Async.Parallel insertionTaks
            let oks = results |> Array.filter (function | Ok _ -> true | _ -> false) |> Array.length
            let errs = results |> Array.filter (function | Error _ -> true | _ -> false) |> Array.length
            
            Expect.equal oks 1 "exactly one registration should succeed"
            Expect.equal errs 9 "exactly nine registrations should fail"
            
            match StateView.getAllAggregateStates<User, UserEvent, string> pgEventStore with
            | Ok states ->
                let users = states |> List.map snd
                Expect.equal users.Length 1 "only one user should be stored"
                Expect.equal users.Head.NickName "test" "stored user has expected nickname"
            | Error e -> failtestf "unexpected error reading users: %s" e
        }
        
    ]
    |> testSequenced
    
    
