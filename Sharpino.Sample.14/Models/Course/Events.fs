namespace Sharpino.Sample._14
open Sharpino.Sample._14.Course
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._14.Definitions

module CourseEvents =
    type CourseEvents =
        | StudentEnrolled of StudentId
        | StudentUnenrolled of StudentId
        | Renamed of string
        interface Event<Course> with
            member this.Process (course: Course) =
                match this with
                | StudentEnrolled studentId -> course.EnrollStudent studentId
                | StudentUnenrolled studentId -> course.UnenrollStudent studentId
                | Renamed name -> course.Rename name
       
        static member Deserialize (x: string): Result<CourseEvents, string> =
            try
                JsonSerializer.Deserialize<CourseEvents> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
