namespace Sharpino.Sample._14

open Sharpino.Sample._14.Student
open Sharpino.Commons
open Sharpino.Core

open Sharpino.Sample._14.Definitions
open System.Text.Json

module StudentEvents =
    type StudentEvents =
        | EnrolledCourse of CourseId
        | UnenrolledCourse of CourseId
        | Renamed of string
        interface Event<Student> with
            member this.Process (student: Student) =
                match this with
                | EnrolledCourse id -> student.EnrollCourse id
                | UnenrolledCourse id -> student.UnenrollCourse id
                | Renamed name -> student.Rename name
       
        static member Deserialize (x: string) =
            try
                JsonSerializer.Deserialize<StudentEvents> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
                
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
