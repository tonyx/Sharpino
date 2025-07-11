namespace Sharpino.Sample._9

open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino

module CourseCommands =
    type CourseCommands =
        | AddStudent of Guid
        | RemoveStudent of Guid
        | AddTeacher of Guid 
        interface AggregateCommand<Course, CourseEvents> with
            member this.Execute (course: Course) =
                match this with
                | AddStudent id ->
                    course.AddStudent id
                    |> Result.map (fun i -> (i, [StudentAdded id]))
                | RemoveStudent id ->
                    course.RemoveStudent id
                    |> Result.map (fun i -> (i, [StudentRemoved id]))
                | AddTeacher id ->    
                    course.AddTeacher id
                    |> Result.map (fun i -> (i, [TeacherAdded id]))     
            member this.Undoer =
                None
                    