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
    type EnrollmentItem =
        { 
          CourseId: CourseId
          StudentId: StudentId
          EnrollmentDate: DateTime
        }
    type Enrollments =
        { Id: EnrollmentId
          Enrollments: List<EnrollmentItem>}
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
        
        interface Aggregate<string> with
            member this.Id = this.Id.Id
            member this.Serialize = this.Serialize    
        
            
            
     
        