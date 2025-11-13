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
        | StudentAdded of Guid
        | StudentRemoved of Guid
        interface Event<Course> with
            member this.Process (course: Course) =
                match this with
                | StudentAdded id -> course.AddStudent id
                | StudentRemoved id -> course.RemoveStudent id
       
        static member Deserialize (x: byte[]): Result<CourseEvents, string> =
            binarySerializer.Deserialize<CourseEvents> x
        member this.Serialize =
            binarySerializer.Serialize this
