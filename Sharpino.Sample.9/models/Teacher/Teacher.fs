namespace Sharpino.Sample._9
open System
open Sharpino.Commons
open Sharpino
open Sharpino.Core

module Teacher =
    type Teacher = {
        Id: Guid
        Name: string
        Courses: List<Guid>
    }
    with
        static member MkTeacher (name: string) =
            { Id = Guid.NewGuid(); Name = name; Courses = List.empty }

        member this.AddCourse (courseId: Guid) =
            {
                this
                    with
                        Courses = this.Courses @ [courseId]
            }
            |> Ok
            
        member this.RemoveCourse (courseId: Guid) =
            {
                this
                    with
                        Courses = this.Courses |> List.filter (fun x -> x <> courseId)
            }
            |> Ok
            
        static member Version = "_01"
        static member StorageName = "_teacher"
        static member SnapshotsInterval = 15
        
        static member Deserialize (json: string) =
            json
            |> jsonPSerializer.Deserialize<Teacher>
       
        member this.Serialize =
            this
            |> jsonPSerializer.Serialize
       
        interface Aggregate<string> with
            member this.Id =
                this.Id
            member this.Serialize =
                this.Serialize 
