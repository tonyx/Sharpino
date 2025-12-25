namespace Sharpino.Sample._15

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization

open Sharpino.Sample._15
open Sharpino.Sample._15
open Sharpino.Sample._15.Course
open Sharpino.Sample._15.Course
open Sharpino.Sample._15.CourseEvents

module CourseCommands =
    type CourseCommands =
        | Rename of string
        interface AggregateCommand<Course,CourseEvents> with
            member this.Execute (course: Course) =
                match this with
                | Rename newName ->
                    course.Rename newName
                    |> Result.map (fun s -> (s, [Renamed newName]))
           
            member this.Undoer = None
            