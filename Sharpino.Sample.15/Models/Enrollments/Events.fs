namespace Sharpino.Sample._15

open Sharpino.Sample._15
open Sharpino.Sample._15.Enrollment
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._15.Commons.Definitions

module EnrollmentEvents =
    type EnrollmentEvents =
        | EnrollmentAdded of Enrollment

        interface Event<Enrollments> with
            member this.Process (enrollments: Enrollments) =
                match this with
                | EnrollmentAdded item -> enrollments.AddEnrollment item
        
        static member Deserialize (x: string): Result<EnrollmentEvents, string> =
            try
                JsonSerializer.Deserialize<EnrollmentEvents> (x, jsonOptions) |> Ok
            with
                | ex -> Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
