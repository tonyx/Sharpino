namespace Sharpino.Sample._14

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling
open System.Text.Json
open Sharpino.Sample._14.Definitions

module Course =

    let maximumNumberOfTeachers = 3
    type Course =
        {
            Name: string
            CourseId: CourseId
            Students: List<StudentId>
            MaxNumberOfStudents: int
        }
        
        with
            static member MkCourse (name: string, maxNumberOfStudents: int) =
                { CourseId = CourseId.New ; Name = name; Students = List.empty; MaxNumberOfStudents = maxNumberOfStudents }
                    
            member this.EnrollStudent (studentId: StudentId) =
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
                
            member this.UnenrollStudent (studentId: StudentId) =
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
            
            member this.Rename (name: string) =
                result {
                    do! 
                        (name <> this.Name)
                        |> Result.ofBool "course name is the same"
                    return { this with Name = name }
                }
            
            static member Version = "_01"
            static member StorageName = "_course"
            static member SnapshotsInterval = 15
            member this.Id = this.CourseId.Id

            static member Deserialize(x: string): Result<Course, string> =
                try
                    JsonSerializer.Deserialize<Course> (x, jsonOptions) |> Ok
                with    
                | ex  ->
                    Error (ex.Message)

            member this.Serialize =
                JsonSerializer.Serialize(this, jsonOptions)
            
   