namespace Sharpino.Sample._15

open Sharpino.Sample._15
open Sharpino.Sample._15.Course
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._15.Commons.Definitions

module CourseEvents =
    type CourseEvents =
        | Renamed of string
        interface Event<Course> with
            member this.Process (course: Course) =
                match this with
                | Renamed newName -> course.Rename newName
        
        static member Deserialize (x: string): Result<CourseEvents, string> =
            try
                JsonSerializer.Deserialize<CourseEvents> (x, jsonOptions) |> Ok
            with
                | ex -> Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)




