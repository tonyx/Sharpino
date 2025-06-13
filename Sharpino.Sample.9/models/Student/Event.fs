namespace Sharpino.Sample._9
open Sharpino.Commons
open Sharpino.Sample._9.Student
open System
open Sharpino.Core

module StudentEvents =
    type StudentEvents =
        | CourseAdded of Guid
        | CourseRemoved of Guid
        interface Event<Student> with
            member this.Process (student: Student) =
                match this with
                | CourseAdded id -> student.AddCourse id
                | CourseRemoved id -> student.RemoveCourse id
       
        static member Deserialize x =
            jsonPSerializer.Deserialize<StudentEvents> x
        member this.Serialize =
            jsonPSerializer.Serialize this         
