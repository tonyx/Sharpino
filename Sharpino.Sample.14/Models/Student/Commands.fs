namespace Sharpino.Sample._11

open Sharpino.Sample._11.Student
open Sharpino.Sample._11.StudentEvents

open System
open Sharpino.Core
open FsToolkit.ErrorHandling

module StudentCommands =
    type StudentCommands =
        | Enroll of Guid
        | Unenroll of Guid
        interface AggregateCommand<Student, StudentEvents> with
            member this.Execute (student: Student) =
                match this with
                | Enroll id ->
                    student.EnrollCourse id
                    |> Result.map (fun i -> (i, [ EnrolledCourse id]))
                | Unenroll id ->
                    student.UnenrollCourse id
                    |> Result.map (fun i -> (i, [ UnenrolledCourse id]))
            member this.Undoer =
                match this with
                | Enroll id -> 
                    Some 
                        (
                            fun (student: Student) (viewer: AggregateViewer<Student>)-> 
                                result {
                                    return 
                                        fun () ->
                                            result {
                                                let! (_, state) = viewer student.Id
                                                let result =
                                                    state.UnenrollCourse id
                                                    |> Result.map (fun s -> s, [ UnenrolledCourse id])
                                                return! result
                                            }
                                }    
                        )
                | Unenroll id -> 
                    Some 
                        (
                            fun (student: Student) (viewer: AggregateViewer<Student>)-> 
                                result {
                                    return 
                                        fun () ->
                                            result {
                                                let! (_, state) = viewer student.Id
                                                let result =
                                                    state.EnrollCourse id
                                                    |> Result.map (fun s -> s, [ EnrolledCourse id])
                                                return! result
                                            }
                                }    
                        )
