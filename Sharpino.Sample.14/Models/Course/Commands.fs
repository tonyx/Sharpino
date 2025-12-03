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
        | EnrollStudent of Guid
        | UnenrollStudent of Guid
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
                match this with
                | EnrollStudent id ->
                    Some
                        (
                            fun (course: Course) (viewer: AggregateViewer<Course>) ->
                                result {
                                    return 
                                        fun () ->
                                            result {
                                                let! (_, state) = viewer course.Id
                                                let result =
                                                    state.UnenrollStudent id
                                                    |> Result.map (fun s -> s, [ StudentUnenrolled id])
                                                return! result
                                            }
                                }    
                        )
                | UnenrollStudent id ->
                    Some
                        (
                            fun (course: Course) (viewer: AggregateViewer<Course>) ->
                                result {
                                    return 
                                        fun () ->
                                            result {
                                                let! (_, state) = viewer course.Id
                                                let result =
                                                    state.EnrollStudent id 
                                                    |> Result.map (fun s -> s, [StudentEnrolled id])
                                                return! result
                                            }
                                } 
                        )
                
                    