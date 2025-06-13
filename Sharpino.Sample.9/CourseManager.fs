module Sharpino.Sample._9.CourseManager

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents
open Sharpino.Sample._9.CourseCommands
open Sharpino.Sample._9.Student
open Sharpino.Sample._9.StudentEvents
open Sharpino.Sample._9.StudentCommands
open Sharpino.Storage
open Sharpino
open System

let doNothingBroker  =
    {
        notify = None
        notifyAggregate = None
    }

type CourseManager(eventStore: IEventStore<string>, courseViewer: AggregateViewer<Course>, studentViewer: AggregateViewer<Student>) =
    member this.AddStudent (student: Student) =
        result
            {
                return!
                    runInit<Student, StudentEvents, string> eventStore doNothingBroker student
            }
            
    member this.GetStudent (id: Guid) =
        result
            {
                let! (_, student) = studentViewer id
                return student
            }
    
    member this.AddCourse (course: Course) =
        result
            {
                return!
                    runInit<Course, CourseEvents, string> eventStore doNothingBroker course
            }
    
    member this.GetCourse (id: Guid) =
        result
            {
                let! (_, course) = courseViewer id
                return course
            }
            
    member this.DeleteCourse (id: Guid) =
        result
            {
                let! course = this.GetCourse id
                return!
                    runDelete<Course, CourseEvents, string> eventStore doNothingBroker id (fun course -> course.Students.Length = 0)
            }
    member this.DeleteStudent (id: Guid) =
        result
            {
                let! student = this.GetStudent id
                return!
                    runDelete<Student, StudentEvents, string> eventStore doNothingBroker id (fun student -> student.Courses.Length = 0)
            }
             
    member this.SubscribeStudentToCourse (studentId: Guid) (courseId: Guid) =
        result
            {
                let! student = this.GetStudent studentId
                let! course = this.GetCourse courseId
                let addCourseToStudent = StudentCommands.AddCourse courseId
                let addStudentToCourse = CourseCommands.AddStudent studentId
                return!
                    runTwoAggregateCommands studentId courseId eventStore doNothingBroker addCourseToStudent addStudentToCourse
            }