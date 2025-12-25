namespace Sharpino.Sample._15

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling
open System.Text.Json
open Sharpino.Sample._15.Commons.Definitions

module Course =
    type Course =
        { Id: CourseId
          Name: string
          MaxStudents: int }
    
    with
        static member MkCourse name maxStudents =
            { Id = CourseId.New
              Name = name
              MaxStudents = maxStudents }
    
        member this.Rename newName =
            { this with Name = newName } |> Ok
        
        //////
        static member Version = "_01"
        static member StorageName = "_Course"
        static member SnapshotsInterval = 15
        
        static member Deserialize (x: string): Result<Course, string> =
            try
                let course = JsonSerializer.Deserialize<Course> (x, jsonOptions)
                Ok course
            with
                | ex -> Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
        
        interface Aggregate<string> with
            member this.Id = this.Id.Id
            member this.Serialize = this.Serialize
            