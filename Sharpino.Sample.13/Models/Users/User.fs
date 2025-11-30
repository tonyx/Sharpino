namespace Sharpino.Sample._13
open System
open System.Text.Json
open Sharpino.Commons
open Sharpino
open Sharpino.Core
open Sharpino.Sample._13.Commons

module User =
    type Preference =
        | Games
        | Movies
        | Books

    type User = {
        Id: Guid
        NickName: string
        Preferences: List<Preference>
    }
    with 
        static member MkUser (nickName: string) = {
            Id = Guid.NewGuid()
            NickName = nickName
            Preferences = []
        }
        member this.AddPreference (preference: Preference) = 
            result
                {
                    return
                        {
                            this with
                                Preferences = this.Preferences @ [preference]
                        }
                }
        member this.RemovePreference (preference: Preference) =
            result
                {
                    return
                        {
                            this with
                                Preferences = this.Preferences |> List.filter (fun p -> p <> preference)
                        }
                }
        
        static member Version = "_01"
        static member StorageName = "_User"
        static member SnapshotsInterval = 15
        
        static member Deserialize (x: string): Result<User, string> =
            try
                JsonSerializer.Deserialize<User>(x, jsonOptions) |> Ok
            with
            | ex ->
                Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize(this, jsonOptions)
        
        interface Aggregate<string> with
            member this.Id =
                this.Id
            member this.Serialize =
                this.Serialize