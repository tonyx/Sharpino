namespace Sharpino.Sample._11

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging
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
            messageSenders: MessageSenders,
            allStudentsAggregateStatesViewer: unit -> Result<(Definitions.EventId * Student) list, string>
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
        
        member this.AddMultipleCourses (courses: Course[]) =
            result
                {
                    return!
                        runMultipleInit<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        courses
                }        
        
        member this.GetStudent (id: Guid)  =
            result
                {
                    let! _, student = studentViewer id
                    return student
                }
        
        member this.GetStudents (ids: List<Guid>) =
            result
                {
                    let!
                        students =
                            ids
                            |> List.traverseResultM (fun id -> studentViewer id |> Result.map snd)
                    return students
                }
        
        member this.GetAllStudents () =
            result
                {
                    let! students = allStudentsAggregateStatesViewer()
                    return (students |>> snd)
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
        
        member this.GetCourses (ids: Guid[]) =
            result
                {
                    let!
                        courses =
                            ids
                            |> List.ofArray
                            |> List.traverseResultM (fun id -> this.GetCourse id )
                    return courses
                }        
        
        member this.EnrollStudentToCourse (studentId: Guid) (courseId: Guid) =
            result
                {
                    let addCourseToStudentEnrollments = StudentCommands.Enroll courseId
                    let addStudentToCourseEnrollments = CourseCommands.EnrollStudent studentId
                    return!
                        runTwoAggregateCommands
                            studentId
                            courseId
                            eventStore
                            messageSenders
                            addCourseToStudentEnrollments
                            addStudentToCourseEnrollments
                }
        
        
                
        // member this.EnrollStudentToCourseCompensationVersion (studentId: Guid) (courseId: Guid) =
        //     result
        //         {
        //             let addCourseToStudent: AggregateCommand<Student, StudentEvents> = StudentCommands.Enroll courseId
        //             let addStudentToCourse: AggregateCommand<Course, CourseEvents> = CourseCommands.EnrollStudent studentId
        //             let rollBackAddCourseToStudent =
        //                 addCourseToStudent.Undoer
        //                 |> Option.get
        //                 
        //             let! tryAddCourseToStudent =
        //                 runAggregateCommand
        //                     studentId
        //                     eventStore
        //                     messageSenders
        //                     addCourseToStudent
        //            
        //             let! tryAddStudentToCourse =
        //                 runAggregateCommand
        //                     courseId
        //                     eventStore
        //                     messageSenders
        //                     addStudentToCourse
        //            
        //             let result = 
        //                 match tryAddStudentToCourse with
        //                 | Ok _ -> Ok ()
        //                 | Error e ->
        //                     
        //         }
