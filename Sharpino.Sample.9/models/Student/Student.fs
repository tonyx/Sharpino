namespace Sharpino.Sample._9

open System
open Sharpino.Commons
open Sharpino
open Sharpino.Core

module Student =
    let maximumCourses = 5
    type Student = {
        Id: Guid
        Name: string
        Courses: List<Guid>
    }
    with
        static member MkStudent (name: string) =
            { Id = Guid.NewGuid(); Name = name; Courses = List.empty }
   
        member this.AddCourse (courseId: Guid) =
            result
                {
                    do! 
                        this.Courses
                        |> List.length <= maximumCourses
                        |> Result.ofBool "Maximum number of courses reached"
                    return
                        {
                            this
                                with
                                    Courses = this.Courses @ [courseId]
                        }
                }
            
        member this.RemoveCourse (courseId: Guid) =
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
        

