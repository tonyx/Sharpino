namespace Sharpino.Sample._11

open Sharpino.Sample._11.Student
open Sharpino.Commons
open Sharpino.Core

open Sharpino.Sample._11.Definitions

open System
open System.Text.Json
open System.Text.Json.Serialization

module StudentEvents =
    type StudentEvents =
        | CourseAdded of Guid
        | CourseRemoved of Guid
        interface Event<Student> with
            member this.Process (student: Student) =
                match this with
                | CourseAdded id -> student.AddCourse id
                | CourseRemoved id -> student.RemoveCourse id
       
        static member Deserialize (x: string) =
            try
                JsonSerializer.Deserialize<StudentEvents> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
                
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
