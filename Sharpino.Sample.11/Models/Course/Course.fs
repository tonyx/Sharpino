namespace Sharpino.Sample._11

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling
open System.Text.Json
open Sharpino.Sample._11.Definitions

module  Course =

    let maximumNumberOfTeachers = 3
    type Course =
        {
            Name: string
            Id: Guid
            Students: List<Guid>
            MaxNumberOfStudents: int
        }
        
        with
            static member MkCourse (name: string, maxNumberOfStudents: int) =
                { Id = Guid.NewGuid(); Name = name; Students = List.empty; MaxNumberOfStudents = maxNumberOfStudents }
                    
            member this.EnrollStudent (studentId: Guid) =
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
                
            member this.UnenrollStudent (studentId: Guid) =
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
                try
                    JsonSerializer.Deserialize<Course> (x, jsonOptions) |> Ok
                with    
                | ex  ->
                    Error (ex.Message)

            member this.Serialize =
                JsonSerializer.Serialize(this, jsonOptions)
            
            interface Aggregate<string> with
                member this.Id =
                    this.Id
                member this.Serialize =
                    this.Serialize
   