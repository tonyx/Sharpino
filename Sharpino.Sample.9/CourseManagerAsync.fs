
module Sharpino.Sample._9.CourseManagerAsync

open System.Threading.Tasks
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Sample._9.Balance
open Sharpino.Sample._9.BalanceCommands
open Sharpino.Sample._9.BalanceEvents
open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents
open Sharpino.Sample._9.CourseCommands
open Sharpino.Sample._9.Student
open Sharpino.Sample._9.StudentEvents
open Sharpino.Sample._9.StudentCommands
open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherEvents
open Sharpino.Storage
open Sharpino
open System
open System.Threading

type AggregateViewerAsync2<'A> = Option<CancellationToken> -> Guid -> Task<Result<int * 'A,string>>

type CourseManagerAsync    
    (
        eventStore: IEventStore<string>,
        messageSenders: MessageSenders,
        courseViewerAsync: AggregateViewerAsync2<Course>,
        studentViewerAsync: AggregateViewerAsync2<Student>,
        teacherViewerAsync: AggregateViewerAsync2<Teacher>,
        balanceViewerAsync: AggregateViewerAsync2<Balance>,
        initialBalance: Balance
    ) =
    do
        let initialized = 
            runInit<Balance, BalanceEvents, string> eventStore messageSenders initialBalance
        match initialized with
        | Error e -> raise (Exception $"Could not initialize balance. Error: {e}")
        | Ok _ -> ()
        
    member this.Balance = 
        taskResult
            {
                let! (_, balance) = balanceViewerAsync None initialBalance.Id
                return balance
            }

    member this.AddStudentAsync (student: Student, ?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                return!    
                    runInitAsync<Student, StudentEvents, string> eventStore messageSenders student (Some ct)
            }

    member this.AddTeacherAsync (teacher: Teacher, ?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                return!    
                    runInitAsync<Teacher, TeacherEvents, string> eventStore messageSenders teacher (Some ct)
            }

    member this.GetTeacherAsync (id: Guid, ?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                return!    
                    teacherViewerAsync (Some ct) id
            }
    
    member this.GetStudentAsync (id: Guid, ?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                return!    
                    studentViewerAsync (Some ct) id
            }

    member this.GetCourseAsync (id: Guid, ?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                return!    
                    courseViewerAsync (Some ct) id
            }

    member this.AddCourseAsync (course: Course, ?ct: CancellationToken) =
        taskResult
            {
                let ct = defaultArg ct CancellationToken.None
                return!    
                    runInitAsync<Course, CourseEvents, string> eventStore messageSenders course (Some ct)
            }
    
    member this.SubscribeStudentAsync (studentId: Guid, courseId: Guid, ?ct: CancellationToken) =
        let ct = defaultArg ct CancellationToken.None
        taskResult
            {
                let! student = this.GetStudentAsync (studentId,  ct)
                let! course = this.GetCourseAsync (courseId,  ct)
                let addCourseToStudent = StudentCommands.AddCourse courseId
                let addStudentToCourse = CourseCommands.AddStudent studentId
                let! result =
                    runTwoNAggregateCommandsMdAsync<Student, StudentEvents, Course, CourseEvents, string> 
                        [studentId] 
                        [courseId] 
                        eventStore 
                        messageSenders
                        "md" 
                        [addCourseToStudent] 
                        [addStudentToCourse] 
                        (ct |> Some)
                return result
            }
            
    member this.ConditionallyAddTeacher (teacherId: Guid, courseId: Guid, crossAggregatesConstraint, ?ct: CancellationToken) =
        taskResult
            {
                let! _, course = courseViewerAsync ct  courseId
                let! _, teacher = teacherViewerAsync ct teacherId
                let assignTeacherToCourse = TeacherCommands.AddCourse course.Id
                let assignCourseToTeacher = CourseCommands.AddTeacher teacher.Id
                let! result =
                    forceRunTwoNAggregateCommandsMdAsync3<Teacher, TeacherEvents, Course, CourseEvents, string>
                        [teacherId] [courseId] eventStore messageSenders "md" [assignTeacherToCourse] [assignCourseToTeacher] crossAggregatesConstraint None
                return result 
            }
    