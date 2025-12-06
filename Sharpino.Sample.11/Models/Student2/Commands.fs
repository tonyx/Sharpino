namespace Sharpino.Sample._11
open Sharpino.Sample._11.Student2
open Sharpino.Sample._11.StudentEvents2
open System
open Sharpino.Core
module StudentCommands2 =
    type StudentCommands =
        | Enroll of Guid
        | Unenroll of Guid
        | AddAnnotation of string
        interface AggregateCommand<Student2, StudentEvents2> with
            member this.Execute (student: Student2) =
                match this with
                | Enroll id ->
                    student.EnrollCourse id
                    |> Result.map (fun i -> (i, [ EnrolledCourse id]))
                | Unenroll id ->
                    student.UnenrollCourse id
                    |> Result.map (fun i -> (i, [ UnenrolledCourse id]))
                | AddAnnotation note ->
                    student.AddAnnotations note
                    |> Result.map (fun i -> (i, [ AnnotationAdded note]))    
            member this.Undoer =
                None        
