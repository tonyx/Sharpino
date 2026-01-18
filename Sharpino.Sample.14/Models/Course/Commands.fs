namespace Sharpino.Sample._14

open Sharpino.Sample._14.Course
open Sharpino.Sample._14.CourseEvents
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._14.Definitions

module CourseCommands =
    type CourseCommands =
        | EnrollStudent of StudentId
        | UnenrollStudent of StudentId
        | Rename of string
        interface AggregateCommand<Course, CourseEvents> with
            member this.Execute (course: Course) =
                match this with
                | EnrollStudent studentId ->
                    course.EnrollStudent studentId
                    |> Result.map (fun s -> (s, [ StudentEnrolled studentId]))
                | UnenrollStudent studentId ->
                    course.UnenrollStudent studentId
                    |> Result.map (fun s -> (s, [ StudentUnenrolled studentId]))
                | Rename name ->
                    course.Rename name
                    |> Result.map (fun s -> (s, [ Renamed name]))    
                    
            member this.Undoer =
                match this with
                | EnrollStudent studentId ->
                    (
                        fun (course: Course) (viewer: AggregateViewer<Course>) ->
                            result {
                                return 
                                    fun () ->
                                        result {
                                            let! _, state = viewer course.CourseId.Id
                                            let result =
                                                state.UnenrollStudent studentId
                                                |> Result.map (fun s -> s, [ StudentUnenrolled studentId])
                                            return! result
                                        }
                            }    
                    )
                    |> Some
                | UnenrollStudent studentId ->
                    (
                        fun (course: Course) (viewer: AggregateViewer<Course>) ->
                            result {
                                return 
                                    fun () ->
                                        result {
                                            let! _, state = viewer course.CourseId.Id
                                            let result =
                                                state.EnrollStudent studentId 
                                                |> Result.map (fun s -> s, [StudentEnrolled studentId])
                                            return! result
                                        }
                            } 
                    )
                    |> Some
                | Rename _ ->
                    None
                    
                    
                
                    