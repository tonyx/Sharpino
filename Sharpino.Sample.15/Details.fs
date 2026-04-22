namespace Sharpino.Sample._15

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open DotNetEnv
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Storage
open Sharpino.Sample._15.Course
open Sharpino.Sample._15.CourseEvents
open Sharpino.Sample._15.Student
open Sharpino.Sample._15.StudentEvents
open Sharpino.Sample._15.Enrollment
open Sharpino.Sample._15.EnrollmentEvents

module Details =
    type StudentDetails =
        {
            Student: Student
            EnrolledInCourses: Course[]
        }
    type CourseDetails =
        {
            Course: Course
            EnrolledStudents: Student[]
        }
    
    type RefreshableStudentDetails =
        {
            StudentDetails: StudentDetails
            Refresher: unit -> Result<StudentDetails, string>
        }
        member this.Refresh () =
            result {
                let! studentDetails = this.Refresher()
                return { this with StudentDetails = studentDetails }
            }
        member this.RefreshAsync (_: Option<CancellationToken>) =
            this.Refresh() |> Task.FromResult

        interface RefreshableAsync<RefreshableStudentDetails> with
            member this.RefreshAsync ct =
                this.RefreshAsync ct
    
    type RefreshableCourseDetails =
        {
            CourseDetails: CourseDetails
            Refresher: unit -> Result<CourseDetails, string>
        }
        member this.Refresh () =
            result {
                let! courseDetails = this.Refresher()
                return { this with CourseDetails = courseDetails }
            }
        member this.RefreshAsync (_: Option<CancellationToken>) =
            this.Refresh() |> Task.FromResult

        interface RefreshableAsync<RefreshableCourseDetails> with
            member this.RefreshAsync ct =
                this.RefreshAsync ct
        