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
            // interface Observer<Student, StudentDetails> with
            //     member this.Update student =
            //         { this with Student = student }
            //
            // interface Observer<Course, StudentDetails> with
            //     member this.Update course =
            //         if this.Courses |> List.exists (fun x -> x.Id = course.Id) then
            //             {
            //                 this with
            //                     Courses =
            //                         this.Courses
            //                         |> List.filter (fun x -> x.Id <> course.Id)
            //                         |> List.append [course]
            //             }
            //         else
            //             this
                        
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
            interface Observer<Course, CourseDetails> with
                member this.Update course =
                    { this with Course = course }
   
            interface Observer<Student, CourseDetails> with
                member this.Update student =
                    if this.Students |> List.exists (fun x -> x.Id = student.Id) then
                        {
                            this with
                                Students =
                                    this.Students
                                    |> List.filter (fun x -> x.Id <> student.Id)
                                    |> List.append [student]
                        }
                    else
                        this
          
            member this.Refresh () =
                result {
                    let! course, students = this.Refresher ()
                    return { this with Course = course; Students = students }
                }
                
            interface Refreshable<CourseDetails> with
                member this.Refresh () =
                    this.Refresh ()