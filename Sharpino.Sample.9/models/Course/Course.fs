namespace Sharpino.Sample._9

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling

module rec Course =
    type Course =
        {
            Name: string
            Id: Guid
            Students: List<Guid>
            Teacher: Option<Guid>
            MaxNumberOfStudents: int
        }
        
        with
            static member MkCourse (name: string, maxNumberOfStudents: int) =
                { Id = Guid.NewGuid(); Name = name; Students = List.empty; Teacher = None; MaxNumberOfStudents = maxNumberOfStudents }
                    
            member this.AddStudent (studentId: Guid) =
                result
                    {
                        do! 
                            (this.Students.Length < this.MaxNumberOfStudents)
                            |> Result.ofBool "course is full"
                        
                        return    
                            {     
                                this
                                    with
                                        Students = this.Students @ [studentId]
                            }
                    }
            member this.SetTeacher (teacherId: Guid) =
                result
                    {
                        return
                            {
                                this
                                    with
                                        Teacher = Some teacherId
                            }
                    } 
                
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
            static member Deserialize(x: string): Result<Course, string> =
                let firstTry = jsonPSerializer.Deserialize<Course> x
                match firstTry with
                | Ok x -> x |> Ok
                | Error e ->
                    let secondTry = jsonPSerializer.Deserialize<Curse001> x
                    match secondTry with
                    | Ok x -> x.Upcast() |> Ok
                    | Error e1 ->
                        Error (e + " " + e1)
                    
            member this.Serialize =
                this
                |> jsonPSerializer.Serialize
            
            interface Aggregate<string> with
                member this.Id =
                    this.Id
                member this.Serialize =
                    this.Serialize
   
    type Curse001 =
        {
            Id: Guid
            Name: string
            Students: List<Guid>
            MaxNumberOfStudents: int
        }
        with
            static member Deserialize x =
                jsonPSerializer.Deserialize<Curse001> x
            member this.Upcast(): Course =
                {   Name = this.Name
                    Id = this.Id
                    Students = this.Students
                    MaxNumberOfStudents = this.MaxNumberOfStudents
                    Teacher = None
                }