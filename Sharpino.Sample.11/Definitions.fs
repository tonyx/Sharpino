namespace Sharpino.Sample._11
open System.Text.Json
open System.Text.Json.Serialization
open System

module Definitions =
    type CourseId =
        CourseId of Guid
        with
            static member New = CourseId (Guid.NewGuid())
            member this.Id =
                this |> fun (CourseId id) -> id
        
    type StudentId =
        StudentId of Guid
        with
            static member New = StudentId (Guid.NewGuid())
            member this.Id =
                this |> fun (StudentId id) -> id
    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()
