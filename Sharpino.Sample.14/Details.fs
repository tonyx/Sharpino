namespace Sharpino.Sample._14.Details

open Sharpino.Cache
open Sharpino.Sample._14.Course
open Sharpino.Sample._14.Student
open Sharpino.Sample._14.Definitions

module Details =
    
    type StudentDetails =
        {
            Student: Student
            Courses: List<Course>
            Refresher: unit -> Result<Student * List<Course>, string>
        }
            member this.Refresh () =
                result {
                    let! student, courses = this.Refresher ()
                    return { this with Student = student; Courses = courses }
                }
           
            interface Refreshable<StudentDetails> with
                member this.Refresh () =
                    this.Refresh ()
        
    type CourseDetails =
        {
            Course: Course
            Students: List<Student>
            Refresher: unit -> Result<Course * List<Student>, string>
        }
          
            member this.Refresh () =
                result {
                    let! course, students = this.Refresher ()
                    return { this with Course = course; Students = students }
                }
                
            interface Refreshable<CourseDetails> with
                member this.Refresh () =
                    this.Refresh ()