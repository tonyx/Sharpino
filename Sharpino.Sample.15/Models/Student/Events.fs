namespace Sharpino.Sample._15

open Sharpino.Sample._15
open Sharpino.Sample._15.Student
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._15.Commons.Definitions

module StudentEvents =
    type StudentEvents =
        | Renamed of string
        interface Event<Student> with
            member this.Process (student: Student) =
                match this with
                | Renamed newName -> student.Rename newName
        
        static member Deserialize (x: string): Result<StudentEvents, string> =
            try
                JsonSerializer.Deserialize<StudentEvents> (x, jsonOptions) |> Ok
            with
                | ex -> Error ex.Message
        
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
