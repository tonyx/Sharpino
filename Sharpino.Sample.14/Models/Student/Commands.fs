namespace Sharpino.Sample._14

open Sharpino.Sample._14.Definitions
open Sharpino.Sample._14.Student
open Sharpino.Sample._14.StudentEvents

open System
open Sharpino.Core
open FsToolkit.ErrorHandling

module StudentCommands =
    type StudentCommands =
        | Enroll of CourseId
        | Unenroll of CourseId
        | Rename of string
        interface AggregateCommand<Student, StudentEvents> with
            member this.Execute (student: Student) =
                match this with
                | Enroll courseId ->
                    student.EnrollCourse courseId
                    |> Result.map (fun s -> ( s, [ EnrolledCourse courseId ]))
                | Unenroll courseId ->
                    student.UnenrollCourse courseId
                    |> Result.map (fun s -> ( s, [ UnenrolledCourse courseId ]))
                | Rename name ->
                    student.Rename name
                    |> Result.map (fun s -> ( s, [ Renamed name ]))    
            member this.Undoer =
                match this with
                | Enroll courseId -> 
                    (
                        fun (student: Student) (viewer: AggregateViewer<Student>)-> 
                            result {
                                return 
                                    fun () ->
                                        result {
                                            let! _, state = viewer student.Id.Id
                                            let result =
                                                state.UnenrollCourse courseId
                                                |> Result.map (fun s -> s, [ UnenrolledCourse courseId])
                                            return! result
                                        }
                            }    
                    )
                    |> Some
                    
                | Unenroll courseId -> 
                    (
                        fun (student: Student) (viewer: AggregateViewer<Student>)-> 
                            result {
                                return 
                                    fun () ->
                                        result {
                                            let! _, state = viewer student.Id.Id
                                            let result =
                                                state.EnrollCourse courseId
                                                |> Result.map (fun s -> s, [ EnrolledCourse courseId])
                                            return! result
                                        }
                            }    
                    )
                    |> Some
