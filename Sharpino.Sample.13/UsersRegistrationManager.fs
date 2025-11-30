namespace Sharpino.Sample._13

open System
open FSharpPlus.Operators
open Sharpino
open Sharpino.Core
open Sharpino.CommandHandler
open Sharpino.EventBroker

open Sharpino.Sample._13.Commons
open Sharpino.Sample._13.Models
open Sharpino.Sample._13.Models.ReservationCommands
open Sharpino.Sample._13.User
open Sharpino.Sample._13.Events
open Sharpino.Sample._13.Models.Reservation
open Sharpino.Sample._13.Models.ReservationEvents
open Sharpino.Storage
open FsToolkit.ErrorHandling

module UsersRegistrationManager =
    type UsersRegistrationManager
        (
            eventStore: IEventStore<string>,
            userViewer: AggregateViewer<User>,
            reservationViewer: AggregateViewer<ReservationForNickNames>,
            messageSenders: MessageSenders
        ) =
        let _ =
            let reservation: ReservationForNickNames =
                { Id = reservationGuid; Claims = [] }
            runInit<ReservationForNickNames, ReservationEvent, string>
                eventStore
                messageSenders
                reservation
            |> function
                | Ok _ -> Ok ()
                | Error _ -> Ok ()

        member _.RegisterUser (user: User): Result<unit, string> =
            result
                {
                    let! users =
                        StateView.getAllAggregateStates<User, UserEvent, string> eventStore
                        
                    let! userShuldNotAlreadyExist =
                        ((users |>> snd) |> List.exists (fun u -> u.NickName = user.NickName) |> not)
                        |> Result.ofBool (sprintf "User %s already claimed" user.NickName)
                        
                    let claim = user.Id, user.NickName
                    let addClaim = ReservationCommand.AddClaim claim
                    //
                    let! claimAdded =
                        runAggregateCommand<ReservationForNickNames, ReservationEvent, string>
                            reservationGuid
                            eventStore
                            messageSenders
                            addClaim
                    let claimCommand = ReservationCommands.Claim claim
                    return! 
                        runInitAndAggregateCommand<ReservationForNickNames, ReservationEvent, User, string>
                            reservationGuid
                            eventStore
                            messageSenders
                            user
                            claimCommand
                }


        member _.GetUser (id: Guid) =
            userViewer id |> Result.map snd

        member _.AddReservationAggregate (r: ReservationForNickNames) =
            runInit<ReservationForNickNames, ReservationEvent, string>
                eventStore
                messageSenders
                r

        member _.GetReservationAggregate (id: Guid) =
            reservationViewer id |> Result.map snd
