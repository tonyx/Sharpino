module Sharpino.Lib.Test.Models.SampleObject.SampleObject

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.Utils
open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open Sharpino.Storage
open Sharpino.Commons
open Sharpino.Lib.Core.Commons
open System
open FSharp.Quotations
open MBrace.FsPickler.Json
open Newtonsoft.Json


type SampleObject =
    {
        Id: Guid
        Name: string
    }
    
    static member MkSampleObject (id: Guid, name: string) =
        { Id = id; Name = name }
    
    member this.Rename (name:  string) =
        result {
            do! 
                this.Name <> name
                |> Result.ofBool "Name already exists"
            return { this with Name = name }
        } 
    
    member this.Serialize =
        jsonPSerializer.Serialize this
    
    static member Deserialize x=
        jsonPSerializer.Deserialize<SampleObject> x  
   
    static member StorageName = "_sampleObject"
    static member Version = "_01"
    static member SnapshotsInterval = 15
    interface Aggregate<string> with
        member this.Id = this.Id
        member this.Serialize =
            this.Serialize
    