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
        | EnrollStudent of StudentId
        | UnenrollStudent of StudentId
        interface AggregateCommand<Course, CourseEvents> with
            member this.Execute (course: Course) =
                match this with
                | EnrollStudent id ->
                    course.EnrollStudent id
                    |> Result.map (fun i -> (i, [ StudentEnrolled id]))
                | UnenrollStudent id ->
                    course.UnenrollStudent id
                    |> Result.map (fun i -> (i, [ StudentUnenrolled id]))
            member this.Undoer =
                None
                    