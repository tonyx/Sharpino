namespace Sharpino.Sample._15

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open FsToolkit.ErrorHandling
open System.Text.Json
open Sharpino.Sample._15.Commons.Definitions

module Enrollment =
    let enrollmentId = EnrollmentId (Guid.Parse("5b0bf9f7-2fe0-485e-88b5-94c978f4b399"))
    type Enrollment =
        { 
          CourseId: CourseId
          StudentId: StudentId
          EnrollmentDate: DateTime
        }
    type Enrollments =
        { EnrollmentId: EnrollmentId
          Enrollments: List<Enrollment>}
    with    
        member this.AddEnrollment enrollment =
            result
                {
                    return
                        {
                            this with
                                Enrollments = this.Enrollments @ [enrollment] 
                        }
                }
        
        member this.Id = this.EnrollmentId.Id
        static member Version = "_01"
        static member StorageName = "_Enrollments"
        static member SnapshotsInterval = 15
        
        static member Deserialize (x: string): Result<Enrollments, string> =
            try
                let enrollments = JsonSerializer.Deserialize<Enrollments> (x, jsonOptions)
                Ok enrollments
            with
                | ex -> Error ex.Message
            
        member this.Serialize =
            JsonSerializer.Serialize (this, jsonOptions)
        
        
            
            
     
        