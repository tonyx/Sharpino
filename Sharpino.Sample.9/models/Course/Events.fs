namespace Sharpino.Sample._9
open Sharpino.Sample._9.Course
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino

module CourseEvents =
    type CourseEvents =
        | StudentAdded of Guid
        | StudentRemoved of Guid
        | TeacherAdded of Guid
        interface Event<Course> with
            member this.Process (course: Course) =
                match this with
                | StudentAdded id -> course.AddStudent id
                | StudentRemoved id -> course.RemoveStudent id
                | TeacherAdded id -> course.AddTeacher id
       
        static member Deserialize x =
            jsonPSerializer.Deserialize<CourseEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this     
