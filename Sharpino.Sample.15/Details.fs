namespace Sharpino.Sample._15

open System
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
        interface Refreshable<RefreshableStudentDetails> with
            member this.Refresh () =
                this.Refresh ()
    
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
        interface Refreshable<CourseDetails> with
            member this.Refresh () =
                this.Refresher ()     
        