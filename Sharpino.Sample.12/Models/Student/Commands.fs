namespace Sharpino.Sample._11

open Sharpino.Sample._11.Student
open Sharpino.Sample._11.StudentEvents

open System
open Sharpino.Core
module StudentCommands =
    type StudentCommands =
        | AddCourse of Guid
        | RemoveCourse of Guid
        interface AggregateCommand<Student, StudentEvents> with
            member this.Execute (student: Student) =
                match this with
                | AddCourse id ->
                    student.AddCourse id
                    |> Result.map (fun i -> (i, [CourseAdded id]))
                | RemoveCourse id ->
                    student.RemoveCourse id
                    |> Result.map (fun i -> (i, [CourseRemoved id]))
            member this.Undoer =
                None        
