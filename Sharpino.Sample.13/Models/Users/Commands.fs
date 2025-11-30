namespace Sharpino.Sample._13

open Sharpino.Core
open Sharpino.Sample._13.User
open Sharpino.Sample._13.Events

module Commands =
    type UserCommand =
        | AddPreference of Preference
        | RemovePreference of Preference
        interface Command<User, UserEvent> with
            member this.Execute (x: User) =
                match this with
                | AddPreference p ->
                    x.AddPreference p
                    |> Result.map (fun s -> (s, [UserEvent.PreferenceAdded p]))
                | RemovePreference p ->
                    x.RemovePreference p
                    |> Result.map (fun s -> (s, [UserEvent.PreferenceRemoved p]))
            member this.Undoer = None
