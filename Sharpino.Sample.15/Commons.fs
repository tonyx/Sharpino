namespace Sharpino.Sample._15.Commons

open System
open System.Text.Json.Serialization

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
   
    type EnrollmentId =
        EnrollmentId of Guid
        with
            static member New = EnrollmentId (Guid.NewGuid())
            member this.Id =
                this |> fun (EnrollmentId id) -> id
     
    let jsonOptions =
        JsonFSharpOptions.Default()
            .ToJsonSerializerOptions()
