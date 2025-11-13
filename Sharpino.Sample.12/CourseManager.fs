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
            eventStore: IEventStore<byte[]>,
            courseViewer: AggregateViewer<Course>,
            studentViewer: AggregateViewer<Student>,
            messageSenders: MessageSenders
        )
        =
        member this.AddStudent (student: Student) =
            result
                {
                    return!
                        runInit<Student, StudentEvents, byte[]>
                        eventStore
                        messageSenders
                        student
                }
        
        member this.AddMultipleStudents (students: Student[]) =
            result
                {
                    return!
                        runMultipleInit<Student, StudentEvents, byte[]>
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
                        runInit<Course, CourseEvents, byte[]>
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
        
        member this.SubscribeStudentToCourse (studentId: Guid) (courseId: Guid) =
            result
                {
                    let addCourseToStudent = StudentCommands.AddCourse courseId
                    let addStudentToCourse = CourseCommands.AddStudent studentId
                    return!
                        runTwoAggregateCommands studentId courseId eventStore messageSenders addCourseToStudent addStudentToCourse
                }
                