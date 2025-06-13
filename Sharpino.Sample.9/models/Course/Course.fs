namespace Sharpino.Sample._9

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling

module Course = 
    type Course =
        {
            Name: string
            Id: Guid
            Students: List<Guid>
        }
        
        with
            static member MkCourse (id: Guid, name: string) =
                { Id = id; Name = name; Students = List.empty}
                    
            member this.AddStudent (studentId: Guid) =
                {
                    this
                        with
                            Students = this.Students @ [studentId]
                }
                |> Ok        
                
            member this.RemoveStudent (studentId: Guid) =
                result
                    {
                        do! 
                            this.Students
                            |> List.exists (fun x -> x = studentId)
                            |> Result.ofBool "there is no such student"
                        return
                            {
                                this
                                    with
                                        Students =
                                            this.Students |> List.filter (fun x -> x <> studentId)
                            }
                    }
            
            static member Version = "_01"
            static member StorageName = "_course"
            static member SnapshotsInterval = 15
            static member Deserialize(x: string) =
                jsonPSerializer.Deserialize<Course> x
            member this.Serialize =
                this
                |> jsonPSerializer.Serialize
            
            interface Aggregate<string> with
                member this.Id =
                    this.Id
                member this.Serialize =
                    this.Serialize
    