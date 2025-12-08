module Sharpino.Lib.Test.Models.ContextObject.SampleContext

open System.Linq
open Sharpino.Commons

type Tag =
    Music | Books| Movies | TVSeries | BoardGames | VideoGames | EventSourcing

type SampleContext =
    {
        Name: string
        Tags: List<Tag>
    }
    with
        member this.Rename (name: string) =
                { this with Name = name } |> Ok
        
        member this.AddTag (tag: Tag) =
            if (this.Tags.Contains tag) then
                this |> Ok
            else
                { this with Tags = this.Tags @ [tag] } |> Ok
        
        member this.RemoveTag (tag: Tag) =
            if (this.Tags.Contains tag) then
                { this with Tags = this.Tags |> List.filter (fun x -> x <> tag) } |> Ok
            else
                this |> Ok
        
        member this.Serialize =
            jsonPSerializer.Serialize this
        
        static member Deserialize x =
            jsonPSerializer.Deserialize<SampleContext> x
        
        static member StorageName = "_sampleContext"
        static member Version = "_01"
        static member SnapshotsInterval = 15
            