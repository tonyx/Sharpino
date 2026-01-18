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
        { StudentId: StudentId
          Name: string
          MaxCourses: int }
    
    with
        static member MkStudent name maxCourses =
            { StudentId = StudentId.New
              Name = name
              MaxCourses = maxCourses }
    
        member this.Rename newName =
            { this with Name = newName } |> Ok
        
        member this.Id = this.StudentId.Id
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
        
            
