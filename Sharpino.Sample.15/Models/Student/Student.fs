namespace Sharpino.Sample._15

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling
open System.Text.Json
open Sharpino.Sample._15.Commons.Definitions

module Student =
    type Student = 
        { Id: StudentId
          Name: string
          MaxCourses: int }
    
    with
        static member MkStudent name maxCourses =
            { Id = StudentId.New
              Name = name
              MaxCourses = maxCourses }
    
        member this.Rename newName =
            { this with Name = newName } |> Ok
        
        //////
        static member Version = "_01"
        static member StorageName = "_Student"
        static member SnapshotsInterval = 15
        
        static member Deserialize (x: string): Result<Student, string> =
            try
                let student = JsonSerializer.Deserialize<Student> (x, jsonOptions)
                Ok student
            with
                | ex -> Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
        
        interface Aggregate<string> with
            member this.Id = this.Id.Id
            member this.Serialize = this.Serialize
            
