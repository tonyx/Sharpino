namespace Sharpino.Sample._14
open System
open System.Text.Json.Serialization

module Definitions =
    type Observer<'A,'B>  =
        abstract member Update: 'A -> 'B
    type Observable =
        abstract member Attach: Observer<'A,'B> -> unit
        abstract member Detach: Observer<'A,'B> -> unit
        abstract member Notify: 'A -> unit
    
    // type Refreshable<'A> =
    //     abstract member Refresh: unit -> Result<'A, string>
        
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
