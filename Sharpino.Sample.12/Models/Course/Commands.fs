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
        | AddStudent of Guid
        | RemoveStudent of Guid
        interface AggregateCommand<Course, CourseEvents> with
            member this.Execute (course: Course) =
                match this with
                | AddStudent id ->
                    course.AddStudent id
                    |> Result.map (fun i -> (i, [StudentAdded id]))
                | RemoveStudent id ->
                    course.RemoveStudent id
                    |> Result.map (fun i -> (i, [StudentRemoved id]))
            member this.Undoer =
                None
                    