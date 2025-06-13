namespace Sharpino.Sample._9

open System
open Sharpino.Commons
open Sharpino.Core

module Student =
    type Student = {
        Id: Guid
        Name: string
        Courses: List<Guid>
    }
    with
        static member MkStudent (id: Guid, name: string) =
            { Id = id; Name = name; Courses = List.empty }
   
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
                        Courses = this.Courses @ [courseId]
                    
            }
            |> Ok     
            
        static member Version = "_01"
        static member StorageName = "_student"
        static member SnapshotsInterval = 15
         
        static member Deserialize(x: string) =
            jsonPSerializer.Deserialize<Student> x
            
        member this.Serialize =
            this
            |> jsonPSerializer.Serialize
            
        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize
        

