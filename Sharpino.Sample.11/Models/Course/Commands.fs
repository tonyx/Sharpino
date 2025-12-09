namespace Sharpino.Sample._11

open Sharpino.Sample._11.Course
open Sharpino.Sample._11.CourseEvents
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._11.Definitions

module CourseCommands =
    type CourseCommands =
        | Enroll of StudentId
        | Unenroll of StudentId
        interface AggregateCommand<Course, CourseEvents> with
            member this.Execute (course: Course) =
                match this with
                | Enroll id ->
                    course.Enroll id
                    |> Result.map (fun i -> (i, [ Enrolled id]))
                | Unenroll id ->
                    course.Unenroll id
                    |> Result.map (fun i -> (i, [ Unenrolled id]))
            member this.Undoer =
                None
                    