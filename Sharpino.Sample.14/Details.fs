namespace Sharpino.Sample._14.Details

open Sharpino.Cache
open Sharpino.Sample._14.Course
open Sharpino.Sample._14.Student
open Sharpino.Sample._14.Definitions
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling

module Details =
    
    type StudentDetails =
        {
            Student: Student
            Courses: List<Course>
            Refresher: Option<CancellationToken> -> TaskResult<Student * List<Course>, string>
        }
            member this.RefreshAsync ct =
                taskResult {
                    let! student, courses = this.Refresher ct
                    return { this with Student = student; Courses = courses }
                }

            interface RefreshableAsync<StudentDetails> with
                member this.RefreshAsync ct =
                    this.RefreshAsync ct
        
    type CourseDetails =
        {
            Course: Course
            Students: List<Student>
            Refresher: Option<CancellationToken> -> TaskResult<Course * List<Student>, string>
        }
          
            member this.RefreshAsync ct =
                taskResult {
                    let! course, students = this.Refresher ct 
                    return { this with Course = course; Students = students }
                }

            interface RefreshableAsync<CourseDetails> with
                member this.RefreshAsync ct =
                    this.RefreshAsync ct