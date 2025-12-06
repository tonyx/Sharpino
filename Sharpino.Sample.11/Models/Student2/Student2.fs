namespace Sharpino.Sample._11
open System
open Sharpino.Commons
open Sharpino
open Sharpino.Core
open System.Text.Json
open System.Text.Json.Serialization
open Sharpino.Sample._11.Definitions

module Student2 =
    type Student2 = {
        Id: Guid
        Name: string
        Annotations: List<string>
        Courses: List<Guid>
        MaxNumberOfCourses: int
    }
    with
        static member MkStudent2 (name: string, maxNumberOfCourses: int) =
            { Id = Guid.NewGuid(); Name = name; Annotations = []; Courses = List.empty; MaxNumberOfCourses = maxNumberOfCourses }
            
        member this.EnrollCourse (courseId: Guid) =
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
            
        member this.UnenrollCourse (courseId: Guid) =
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
        member this.AddAnnotations (note: string) =
            result
                {
                    return
                        {
                            this
                                with
                                    Annotations = this.Annotations @ [note]
                        }
                }
            
        static member Version = "_01"
        static member StorageName = "_student2"
        static member SnapshotsInterval = 100 
        
        static member Deserialize(x: string) =
            try
                JsonSerializer.Deserialize<Student2> (x, jsonOptions) |> Ok
            with
            | ex ->
                Error (ex.Message)
                
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
            
        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize
