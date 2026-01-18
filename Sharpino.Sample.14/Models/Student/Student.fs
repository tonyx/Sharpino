namespace Sharpino.Sample._14

open System
open Sharpino.Commons
open Sharpino
open Sharpino.Core
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._14.Definitions

module Student =
    type Student = {
        StudentId: StudentId
        Name: string
        Courses: List<CourseId>
        MaxNumberOfCourses: int
    }
    with
        static member MkStudent (name: string, maxNumberOfCourses: int) =
            { StudentId = StudentId.New; Name = name; Courses = List.empty; MaxNumberOfCourses = maxNumberOfCourses }
   
        member this.EnrollCourse (courseId: CourseId) =
            result
                {
                    do! 
                        this.Courses
                        |> List.length < this.MaxNumberOfCourses
                        |> Result.ofBool "Maximum number of courses reached"
                    return    
                        {
                            this
                                with
                                    Courses = this.Courses @ [courseId]
                        }
                }
            
        member this.UnenrollCourse (courseId: CourseId) =
            result
                {
                    let! courseExists =
                        this.Courses
                        |> List.exists (fun x -> x = courseId)
                        |> Result.ofBool "Course does not exist"
                    return
                        {
                            this
                                with
                                    Courses =
                                        this.Courses |> List.filter (fun x -> x <> courseId)
                                
                        }
                }
                
        member this.Rename (name: string) =
            result
                {
                    let! _ = 
                        (this.Name <> name && name <> "")
                        |> Result.ofBool "Name cannot be empty or equal to the existing one"
                    return
                        {
                            this
                                with
                                    Name = name
                        }
                }
            
        static member Version = "_01"
        static member StorageName = "_person"
        static member SnapshotsInterval = 15
        member this.Id = this.StudentId.Id
         
        static member Deserialize(x: string) =
            try
                JsonSerializer.Deserialize<Student> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
            
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
            
        

