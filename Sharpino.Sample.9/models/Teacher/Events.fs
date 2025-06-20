
namespace Sharpino.Sample._9
open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino.Sample._9.Teacher

module TeacherEvents =
    type TeacherEvents =
        | CourseAdded of Guid
        | CourseRemoved of Guid
        interface Event<Teacher> with
            member this.Process (x: Teacher.Teacher) = 
                match this with
                | CourseAdded id ->
                    x.AddCourse id
                | CourseRemoved id ->
                    x.RemoveCourse id
        static member Deserialize x =
            jsonPSerializer.Deserialize<TeacherEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this
