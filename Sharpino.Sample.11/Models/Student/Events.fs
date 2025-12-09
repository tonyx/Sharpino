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
        | EnrolledCourse of CourseId
        | UnenrolledCourse of CourseId
        interface Event<Student> with
            member this.Process (student: Student) =
                match this with
                | EnrolledCourse id -> student.EnrollCourse id
                | UnenrolledCourse id -> student.UnenrollCourse id
       
        static member Deserialize (x: string) =
            try
                JsonSerializer.Deserialize<StudentEvents> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
                
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
