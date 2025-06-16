module Sharpino.Sample._9.CourseManager

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.BalanceCommands
open Sharpino.Sample._9.BalanceEvents
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

type CourseManager
    (eventStore: IEventStore<string>,
     courseViewer: AggregateViewer<Course>,
     studentViewer: AggregateViewer<Student>,
     balanceViewer: AggregateViewer<Balance>,
     initialBalance: Balance
     ) =
   
    do
        let initialized =
            runInit<Balance, BalanceEvents, string> eventStore doNothingBroker initialBalance
        match initialized with
        | Error e -> raise (Exception (sprintf "Could not initialize balance: %s" e))initialBalance
        | Ok _ -> ()
   
        
    member this.Balance =
        result
            {
                let! (_, balance) = balanceViewer initialBalance.Id
                return balance
            }
    
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
                let foundCourseCreation = BalanceCommands.FoundCourseCreation course.Id
                return!
                    runInitAndAggregateCommand<Balance, BalanceEvents, Course, string> initialBalance.Id eventStore doNothingBroker course foundCourseCreation
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
                let foundCourseDeletion = BalanceCommands.FoundCourseCancellation id
                printf "balance id %A\n" initialBalance.Id
                return!
                    runDeleteAndAggregateCommandMd<Course, CourseEvents, Balance, BalanceEvents, string> eventStore doNothingBroker id initialBalance.Id foundCourseDeletion (fun course -> course.Students.Length = 0)
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