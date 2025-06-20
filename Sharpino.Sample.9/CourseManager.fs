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
open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherEvents
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
     historyCourseViewer: AggregateViewer<Course>,
     studentViewer: AggregateViewer<Student>,
     balanceViewer: AggregateViewer<Balance>,
     teacherViewer: AggregateViewer<Teacher>,
     initialBalance: Balance
     ) =
   
    do
        let initialized =
            runInit<Balance, BalanceEvents, string> eventStore doNothingBroker initialBalance
        match initialized with
        | Error e -> raise (Exception $"Could not initialize balance. Error: {e}")
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
    
    member this.AddTeacher (teacher: Teacher) =
        result
            {
                return!
                    runInit<Teacher, TeacherEvents, string> eventStore doNothingBroker teacher
            }
    member this.GetTeacher (id: Guid) =
        result
            {
                let! (_, teacher) = teacherViewer id
                return teacher
            }
    // member this.GetCourse (id: Guid) =
    //     result
    //         {
    //             let! (_, course) = courseViewer id
    //             return course
    //         }
    
    member this.GetHistoryCourse (id: Guid) =
        result
            {
                let! (_, course) = historyCourseViewer id
                return course
            }        
    
    member this.DeleteTeacher (id: Guid) =
        result
            {
                let! teacher = this.GetTeacher id
                return!
                    runDelete<Teacher, TeacherEvents, string> eventStore doNothingBroker id (fun teacher -> teacher.Courses.Length = 0)
            }         
    member this.AssignTeacherToCourse (teacherId: Guid, courseId: Guid) =
        result
            {
                let! _, course = courseViewer courseId
                let! _, teacher = teacherViewer teacherId
                let assignTeacherToCourse = TeacherCommands.AddCourse course.Id
                let assignCourseToTeacher = CourseCommands.SetTeacher teacher.Id
                return!
                    runTwoAggregateCommands<Teacher, TeacherEvents, Course, CourseEvents, string>
                        teacherId courseId eventStore doNothingBroker assignTeacherToCourse assignCourseToTeacher
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
                let foundCourseCreation = BalanceCommands.PayCourseCreationFee course.Id
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
                let foundCourseDeletion = BalanceCommands.PayCourseCancellationFee id
                do!
                    course.Students.Length = 0
                    |> Result.ofBool "can't delete"
                
                match course.Teacher with
                | Some teacherId ->
                    let unsubscribeTeacherFromCourse =
                        TeacherCommands.RemoveCourse id
                    return!    
                        runDeleteAndTwoAggregateCommandsMd<Course, CourseEvents, Balance, BalanceEvents, Teacher, TeacherEvents, string> eventStore doNothingBroker "md" id initialBalance.Id teacherId foundCourseDeletion unsubscribeTeacherFromCourse (fun course -> course.Students.Length = 0)
                | _ ->
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