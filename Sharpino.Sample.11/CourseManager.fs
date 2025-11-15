namespace Sharpino.Sample._11

open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino.Core

open Sharpino.EventBroker
open Sharpino.Sample._11.Course
open Sharpino.Sample._11.CourseEvents
open Sharpino.Sample._11.CourseCommands
open Sharpino.Sample._11.Student
open Sharpino.Sample._11.StudentEvents
open Sharpino.Sample._11.StudentCommands

open Sharpino.Storage
open Sharpino
open System

module CourseManager =
    type CourseManager
        (
            eventStore: IEventStore<string>,
            courseViewer: AggregateViewer<Course>,
            studentViewer: AggregateViewer<Student>,
            messageSenders: MessageSenders
        )
        =
        member this.AddStudent (student: Student) =
            result
                {
                    return!
                        runInit<Student, StudentEvents, string>
                        eventStore
                        messageSenders
                        student
                }
        
        member this.AddMultipleStudents (students: Student[]) =
            result
                {
                    return!
                        runMultipleInit<Student, StudentEvents, string>
                        eventStore
                        messageSenders
                        students
                }
        
        member this.GetStudent (id: Guid)  =
            result
                {
                    let! _, student = studentViewer id
                    return student
                }
        
        member this.AddCourse (course: Course) =
            result
                {
                    return!
                        runInit<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        course
                }
        
        member this.GetCourse (id: Guid) =
            result
                {
                    let! _, course = courseViewer id
                    return course
                }
        
        member this.EnrolleStudentToCourse (studentId: Guid) (courseId: Guid) =
            result
                {
                    let addCourseToStudent = StudentCommands.Enroll courseId
                    let addStudentToCourse = CourseCommands.EnrollStudent studentId
                    return!
                        runTwoAggregateCommands studentId courseId eventStore messageSenders addCourseToStudent addStudentToCourse
                }
                