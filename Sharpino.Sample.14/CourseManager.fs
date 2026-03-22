namespace Sharpino.Sample._14

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Core

open Sharpino.EventBroker
open Sharpino.Sample._14.Course
open Sharpino.Sample._14.CourseEvents
open Sharpino.Sample._14.CourseCommands
open Sharpino.Sample._14.Definitions
open Sharpino.Sample._14.Student
open Sharpino.Sample._14.StudentEvents
open Sharpino.Sample._14.StudentCommands
open Sharpino.Sample._14.Details.Details
open Sharpino.Storage
open Sharpino
open System
open System.Threading

module CourseManager =
    type CourseManager
        (
            eventStore: IEventStore<string>,
            courseViewer: AggregateViewer<Course>,
            studentViewer: AggregateViewer<Student>,
            courseViewerAsync: AggregateViewerAsync<Course>,
            studentViewerAsync: AggregateViewerAsync<Student>,
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

        member this.AddStudentAsync (student: Student) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    return!
                        runInitAsync<Student, StudentEvents, string>
                        eventStore
                        messageSenders
                        student
                        cancellationToken
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

        member this.AddMultipleStudentsAsync (students: Student[]) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    return!
                        runMultipleInitAsync<Student, StudentEvents, string>
                        eventStore
                        messageSenders
                        students
                        cancellationToken
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

        member this.AddMultipleCoursesAsync (courses: Course[]) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    return!
                        runMultipleInitAsync<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        courses
                        cancellationToken
                }
        
        member this.GetStudent (id: StudentId)  =
            result
                {
                    let! _, student = studentViewer id.Id
                    return student
                }

        member this.GetStudentAsync (id: StudentId) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    let! _, student = studentViewerAsync id.Id cancellationToken
                    return student
                }
        
        member this.GetCourseDetails (id: CourseId) =
            let detailsBuilder =
                fun () ->
                    let refresher =
                        fun () ->
                            result {
                                let! course = this.GetCourse id
                                let! students = this.GetStudents course.Students
                                return course, students
                            }
                    result {
                        let! course, students = refresher ()
                        return
                            (
                                {
                                    Course = course
                                    Students = students
                                    Refresher = refresher
                                } :> Refreshable<_>
                                ,
                                id.Id:: (students |> List.map _.StudentId.Id)
                            )
                    }
            let key = DetailsCacheKey.OfType typeof<CourseDetails> id.Id
            StateView.getRefreshableDetails<CourseDetails> detailsBuilder key
        
        member this.GetStudentDetails (id: StudentId) =
            let detailsBuilder =
                fun () ->
                    let refresher =
                        fun () ->
                            result {
                                let! student = this.GetStudent id
                                let! courses = this.GetCourses (student.Courses |> Array.ofList)
                                return
                                    student, courses
                            }
                    result
                        {
                            let! student, courses = refresher ()
                            return (
                                {
                                    Student = student
                                    Courses = courses
                                    Refresher = refresher
                                } :> Refreshable<_>
                                ,
                                id.Id:: (courses |> List.map _.CourseId.Id)
                            )
                        }
            let key = DetailsCacheKey.OfType typeof<StudentDetails> id.Id
            StateView.getRefreshableDetails<StudentDetails> detailsBuilder key

        member this.GetStudentDetailsAsync (id: StudentId) (cancellationToken: Option<CancellationToken>) =
            let detailsBuilder =
                fun () ->
                    let refresher =
                        fun () ->
                            result {
                                let! student = 
                                    this.GetStudentAsync id cancellationToken
                                    |> Async.AwaitTask
                                    |> Async.RunSynchronously

                                let! courses = 
                                    this.GetCoursesAsync (student.Courses |> Array.ofList) cancellationToken
                                    |> Async.AwaitTask
                                    |> Async.RunSynchronously

                                return
                                    student, courses
                            }
                    result
                        {
                            let! student, courses = refresher ()
                            return (
                                {
                                    Student = student
                                    Courses = courses
                                    Refresher = refresher
                                } :> Refreshable<_>
                                ,
                                id.Id:: (courses |> List.map _.CourseId.Id)
                            )
                        }
            let key = DetailsCacheKey.OfType typeof<StudentDetails> id.Id
            StateView.getRefreshableDetails<StudentDetails> detailsBuilder key

        member this.GetStudents (ids: List<StudentId>) =
            result
                {
                    let!
                        students =
                            ids
                            |> List.traverseResultM (fun id -> studentViewer id.Id |> Result.map snd)
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
        member this.AddCourseAsync (course: Course) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    return!
                        runInitAsync<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        course
                        cancellationToken
                }
        
        member this.GetCourse (id: CourseId): Result<Course, string> =
            result
                {
                    let! _, course = courseViewer id.Id
                    return course
                }

        member this.GetCourseAsync (id: CourseId) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    let! _, course = courseViewerAsync id.Id cancellationToken
                    return course
                }
        
        member this.GetCourses (ids: CourseId[]) =
            result
                {
                    let!
                        courses =
                            ids
                            |> List.ofArray
                            |> List.traverseResultM (fun id -> this.GetCourse id )
                    return courses
                }

        member this.GetCoursesAsync (ids: CourseId[]) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    let!
                        courses =
                            ids
                            |> List.ofArray
                            |> List.traverseTaskResultM (fun id -> this.GetCourseAsync id cancellationToken)
                    return courses
                }
        
        member this.RenameStudent (id: StudentId, newName: string) =
            result
                {
                    let renameCommand = StudentCommands.Rename newName
                    let! result =
                        runAggregateCommand<Student, StudentEvents, string>
                            id.Id
                            eventStore
                            messageSenders
                            renameCommand
                    
                    return result        
                }
                
        member this.DeleteStudent (studentId: StudentId) =
            result
                {
                    let! student = this.GetStudent studentId
                    let courseSubscribed = student.Courses
                    let unsubscriptionCommands: List<AggregateCommand<Course, CourseEvents>> =
                        courseSubscribed
                        |> List.map (fun _ -> CourseCommands.UnenrollStudent studentId)
                   
                    let! result =
                        runDeleteAndNAggregateCommandsMd<Student, StudentEvents, Course, CourseEvents, string>
                            eventStore
                            messageSenders
                            ""
                            studentId.Id
                            (courseSubscribed |>> _.Id)
                            unsubscriptionCommands
                            (fun _ -> true)
                    
                    return result 
                }
        
        member this.RenameCourse (id: CourseId, newName: string) =
            result
                {
                    let renameCommand = CourseCommands.Rename newName
                    let! result =
                        runAggregateCommand<Course, CourseEvents, string>
                            id.Id
                            eventStore
                            messageSenders
                            renameCommand
                    return result       
                }
            
        member this.EnrollStudentToCourse (studentId: StudentId) (courseId: CourseId) =
            result
                {
                    let addCourseToStudentEnrollments = StudentCommands.Enroll courseId
                    let addStudentToCourseEnrollments = CourseCommands.EnrollStudent studentId
                    return!
                        runTwoAggregateCommands
                            studentId.Id
                            courseId.Id
                            eventStore
                            messageSenders
                            addCourseToStudentEnrollments
                            addStudentToCourseEnrollments
                }

        member this.EnrollStudentToCourseAsync (studentId: StudentId) (courseId: CourseId) (cancellationToken: Option<CancellationToken>) =
            taskResult
                {
                    let addCourseToStudentEnrollments = StudentCommands.Enroll courseId
                    let addStudentToCourseEnrollments = CourseCommands.EnrollStudent studentId

                    return! 
                        runTwoNAggregateCommandsMdAsync 
                            [studentId.Id]
                            [courseId.Id]
                            eventStore 
                            messageSenders 
                            ""
                            [addCourseToStudentEnrollments]
                            [addStudentToCourseEnrollments]
                            cancellationToken
                }
        
        
                
