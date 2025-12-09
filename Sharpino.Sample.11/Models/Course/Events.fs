namespace Sharpino.Sample._11
open Sharpino.Sample._11.Course
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._11.Definitions

module CourseEvents =
    type CourseEvents =
        | StudentEnrolled of StudentId
        | StudentUnenrolled of StudentId
        interface Event<Course> with
            member this.Process (course: Course) =
                match this with
                | StudentEnrolled id -> course.EnrollStudent id
                | StudentUnenrolled id -> course.UnenrollStudent id
       
        static member Deserialize (x: string): Result<CourseEvents, string> =
            try
                JsonSerializer.Deserialize<CourseEvents> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
