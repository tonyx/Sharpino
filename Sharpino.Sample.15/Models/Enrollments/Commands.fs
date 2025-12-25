namespace Sharpino.Sample._15

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization

open Sharpino.Sample._15
open Sharpino.Sample._15.Enrollment
open Sharpino.Sample._15.EnrollmentEvents

module EnrollmentCommands =
    type EnrollmentCommands =
        | AddEnrollment of EnrollmentItem
        interface AggregateCommand<Enrollments,EnrollmentEvents> with
            member this.Execute (enrollments: Enrollments) =
                match this with
                | AddEnrollment item ->
                    enrollments.AddEnrollment item
                    |> Result.map (fun s -> (s, [EnrollmentAdded item]))
           
            member this.Undoer = None
            
