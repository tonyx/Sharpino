namespace Sharpino.Sample._9
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino

module Item =

    type Item = {
        Id: Guid
        Name: string
        Description: string
        ReferencesCounter: int
    }
    with
        static member MkItem (name: string, description: string) =
            {
                Id = Guid.NewGuid()
                Name = name
                Description = description
                ReferencesCounter = 0
            }
        member
            this.Rename (name: string) =
                {
                    this
                        with Name = name
                }
                |> Ok
        member this.ChangeDescription (description: string) =
            {
                this
                    with Description = description
            }
            |> Ok
        member this.IncrementReferenceCounter (i: int) =
            {
                this
                    with ReferencesCounter = this.ReferencesCounter + i
            }
            |> Ok
        member this.DecrementReferenceCounter (i: int) =
            result
                {
                    do!
                        this.ReferencesCounter + i >= 0
                        |> Result.ofBool "Reference counter must be non negative"
                    return
                        {
                            this
                                with ReferencesCounter = this.ReferencesCounter - i
                        }
                }
            
        static member Version = "_01"
        static member StorageName = "_item"
        static member SnapshotsInterval = 10 
        member this.Serialize =
            this
            |> jsonPSerializer.Serialize
        static member Deserialize (json: string) =
            jsonPSerializer.Deserialize<Item> json    
       
        interface Aggregate<string> with 
            member this.Id =
                this.Id
            member this.Serialize =
                this.Serialize    
