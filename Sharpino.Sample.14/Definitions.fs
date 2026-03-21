namespace Sharpino.Sample._14

open System
open System.Threading
open System.Text.Json.Serialization
open FsToolkit.ErrorHandling
open Sharpino.Definitions

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

    type AggregateViewerAsync<'A> = Guid -> Option<CancellationToken> ->  TaskResult<EventId * 'A,string>
