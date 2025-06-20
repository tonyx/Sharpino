namespace Sharpino.Sample._9

open FSharpPlus
open FsToolkit.ErrorHandling
open System

open Sharpino.Core
open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherEvents

module TeacherCommands =
    type TeacherCommands =
        | AddCourse of Guid
        | RemoveCourse of Guid
        interface AggregateCommand<Teacher, TeacherEvents> with
            member this.Execute (x: Teacher) = 
                match this with
                | AddCourse id ->
                    x.AddCourse id
                    |> Result.map (fun i -> (i, [CourseAdded id]))
                | RemoveCourse id ->
                    x.RemoveCourse id
                    |> Result.map (fun i -> (i, [CourseRemoved id]))
            member this.Undoer =
                None
