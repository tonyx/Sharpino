namespace Sharpino.Sample._11

open Sharpino.Sample._11.Student2
open Sharpino.Commons
open Sharpino.Core

open Sharpino.Sample._11.Definitions

open System
open System.Text.Json
open System.Text.Json.Serialization
module StudentEvents2 =
    type StudentEvents2 =
        | EnrolledCourse of Guid
        | UnenrolledCourse of Guid
        | AnnotationAdded of string
        interface Event<Student2> with
            member this.Process (student: Student2) =
                match this with
                | EnrolledCourse id -> student.EnrollCourse id
                | UnenrolledCourse id -> student.UnenrollCourse id
                | AnnotationAdded note -> student.AddAnnotations note
       
        static member Deserialize (x: string) =
            try
                JsonSerializer.Deserialize<StudentEvents2> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
                
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
