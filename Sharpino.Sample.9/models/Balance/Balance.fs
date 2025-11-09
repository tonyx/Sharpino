namespace Sharpino.Sample._9

open System
open Sharpino
open Sharpino.Commons
open Sharpino.Core

module Balance =
    let courseCreationFee = 100.0M
    let courseDeletionFee = 50.0M

    type Balance = {
        Id: Guid
        Amount: decimal
        OpenCourses: List<Guid>
    }
    with
        static member MkBalance (amount: decimal) = {
            Id = Guid.NewGuid()
            Amount = amount
            OpenCourses = []
        }
        member this.CourseCreationFees = courseCreationFee
        member this.CourseDeletionFees = courseDeletionFee
        member
            this.AddAmount (amount: decimal) =
                result {
                    return { this with Amount = this.Amount + amount }
                }
                
        member this.PayCourseCreationFee (courseId: Guid) =
            result {
                let! sufficientFunds =
                    this.Amount >= courseCreationFee
                    |> Result.ofBool "Not enough funds to cancel the course"
                
                let result =    
                    {
                        this
                            with
                                OpenCourses = this.OpenCourses @ [courseId]
                                Amount = this.Amount - courseCreationFee
                    }
                return result     
            }
            
        member this.PayCourseCancellationFee (courseId: Guid) =
            result {
                let! sufficientFunds =
                    this.Amount >= courseDeletionFee
                    |> Result.ofBool "Not enough funds to delete course"
                let result =    
                    {
                        this
                            with
                                OpenCourses =
                                    this.OpenCourses |> List.filter (fun x -> x <> courseId)
                                Amount = this.Amount - courseDeletionFee
                    }
                return result     
            }
        
        static member Version = "_01"
        static member StorageName = "_balance"
        static member SnapshotsInterval = 15
        
        static member Deserialize (json: string) =
            jsonPSerializer.Deserialize<Balance> json
        member this.Serialize =
            jsonPSerializer.Serialize this
        
        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize  =
                this.Serialize    
        
            