namespace Sharpino.Sample._9
open Sharpino.Sample._9.Course
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino

module CourseEvents =
    type CourseEvents =
        | CourseAdded of Guid
        | CourseRemoved of Guid
        interface Event<Course> with
            member this.Process (course: Course) =
                match this with
                | CourseAdded id -> course.AddStudent id
                | CourseRemoved id -> course.RemoveStudent id
       
        static member Deserialize x =
            jsonPSerializer.Deserialize<CourseEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this     
