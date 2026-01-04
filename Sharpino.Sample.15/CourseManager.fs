namespace Sharpino.Sample._15

open System.Threading
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Cache
open FSharpPlus.Operators
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.Storage
open Sharpino.Sample._15.Course
open Sharpino.Sample._15.CourseEvents
open Sharpino.Sample._15.Student
open Sharpino.Sample._15.StudentEvents
open Sharpino.Sample._15.Enrollment
open Sharpino.Sample._15.EnrollmentEvents
open Sharpino.Sample._15.EnrollmentCommands
open Sharpino.Sample._15.Commons.Definitions
open Sharpino.Sample._15.Details
open FsToolkit.ErrorHandling
open System

module CourseManager =
    type CourseManager
        (
            eventStore: IEventStore<string>,
            messageSenders: MessageSenders,
            courseViewer: AggregateViewer<Course>,
            studentViewer: AggregateViewer<Student>,
            enrollmentViewer: AggregateViewer<Enrollments>
        ) =

        member this.AddCourse (course: Course) =
            result
                {
                    return!
                        runInit<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        course
                }
        member this.AddCourseAsync (course: Course, ?ct: CancellationToken) =
            let ct = defaultArg ct CancellationToken.None
            taskResult
                {
                    return!
                        runInitAsync<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        course
                        (Some ct)
                }
        member this.AddCourses (courses: Course[]) =
            result
                {
                    return!
                        runMultipleInit<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        courses
                }
       
        member this.AddCoursesAsync (courses: Course[], ?ct: CancellationToken) =
            let ct = defaultArg ct CancellationToken.None
            taskResult
                {
                    return!
                        runMultipleInitAsync<Course, CourseEvents, string>
                        eventStore
                        messageSenders
                        courses
                        (Some ct)
                }

        member this.AddStudent (student: Student) =
            result
                {
                    return!
                        runInit<Student, StudentEvents, string>
                        eventStore
                        messageSenders
                        student
                }
        member this.AddStudentAsync (student: Student, ?ct: CancellationToken) =
            let ct = defaultArg ct CancellationToken.None
            taskResult
                {
                    return!
                        runInitAsync<Student, StudentEvents, string>
                        eventStore
                        messageSenders
                        student
                        (Some ct)
                }
        
        member this.RenameCourse (id: CourseId) (newName: String) =
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
        
        member private this.GetOrCreateEnrollments () =
            result {
                let existing = enrollmentViewer enrollmentId.Id
                match existing with
                | Ok (_, enrollments) ->
                    return enrollments
                | Error _ ->
                    let newEnrollments = { Id = enrollmentId; Enrollments = [] }
                    do! runInit<Enrollments, EnrollmentEvents, string>
                            eventStore
                            messageSenders
                            newEnrollments
                    return newEnrollments
            }

        member this.GetCoursesForStudent (studentId: StudentId) =
            result {
                let! enrollments = this.GetEnrollmentsForStudent studentId
                let! courses =
                    enrollments
                    |> List.map (fun e -> courseViewer e.CourseId.Id |> Result.map snd)
                    |> Result.sequence
                return courses
            }

        member this.GetStudentsEnrolledInACourse (courseId: CourseId) =
            result {
                let! enrollments = this.GetEnrollmentsForCourse courseId
                let! students =
                    enrollments
                    |> List.map (fun e -> studentViewer e.StudentId.Id |> Result.map snd)
                    |> Result.sequence
                return students
            }

        member this.GetEnrollmentsForCourse (courseId: CourseId) =
            result {
                let! enrollments = this.GetOrCreateEnrollments()
                let courseEnrollments =
                    enrollments.Enrollments
                    |> List.filter (fun e -> e.CourseId = courseId)
                return courseEnrollments
            }

        member this.GetEnrollmentsForStudent (studentId: StudentId) =
            result {
                let! enrollments = this.GetOrCreateEnrollments()
                let studentEnrollments =
                    enrollments.Enrollments
                    |> List.filter (fun e -> e.StudentId = studentId)
                return studentEnrollments
            }

        member this.GetEnrollments () =
            result {
                let! _, enrollments = enrollmentViewer enrollmentId.Id
                return enrollments
            }
        
        member this.GetNonRefreshableStudentDetails (studentId: StudentId) =
            result {
                let! (_, student) = studentViewer studentId.Id
                let! courses = this.GetCoursesForStudent studentId
                return { Student = student; EnrolledInCourses = courses }
            }
        
        member this.GetRefreshableStudentDetails (studentId: StudentId) =
            let detailsBuilder =
                fun () ->
                    let refresher =
                        fun () ->
                            this.GetNonRefreshableStudentDetails studentId
                    result
                        {
                            let! studentDetails = refresher ()
                            return
                                {
                                    StudentDetails = studentDetails
                                    Refresher =  refresher
                                } :> Refreshable<_>
                                ,
                                studentId.Id :: Enrollment.enrollmentId.Id ::  (studentDetails.EnrolledInCourses |> Array.toList |>> _.Id.Id)
                        }
            let key = DetailsCacheKey (typeof<RefreshableStudentDetails>, studentId.Id)
            StateView.getRefreshableDetails<RefreshableStudentDetails> detailsBuilder key

        member this.GetStudentDetails (studentId: StudentId) =
            result {
                let! details = this.GetRefreshableStudentDetails studentId
                return details.StudentDetails
            }
            
        member this.CreateEnrollment (studentId: StudentId) (courseId: CourseId) =
            result {
                let! enrollments = this.GetOrCreateEnrollments()
                do! 
                    enrollments.Enrollments
                    |> List.exists (fun e -> e.StudentId = studentId && e.CourseId = courseId)
                    |> not
                    |> Result.ofBool "Student is already enrolled in this course."

                let enrollmentItem = 
                    { CourseId = courseId
                      StudentId = studentId
                      EnrollmentDate = DateTime.UtcNow }
                    
                let studentDetailsKey =  DetailsCacheKey (typeof<RefreshableStudentDetails>, studentId.Id)
                let _ =
                    DetailsCache.Instance.UpdateMultipleAggregateIdAssociationRef [|courseId.Id|] studentDetailsKey ((TimeSpan.FromMinutes 10.0) |> Some)
                    
                let command = EnrollmentCommands.AddEnrollment enrollmentItem
                let! result = 
                    runAggregateCommand<Enrollments, EnrollmentEvents, string>
                        enrollmentId.Id
                        eventStore
                        messageSenders
                        command
                return result
            }
            
        member this.GetAllEnrollmentEvents () =
            taskResult {
                let! enrollmentEvents = StateView.getFilteredAggregateEventsInATimeIntervalAsync<Enrollments, EnrollmentEvents, string> Enrollment.enrollmentId.Id eventStore DateTime.MinValue DateTime.MaxValue (fun _ -> true) None
                return enrollmentEvents
            }    
        member this.GetAllEnrollmentEvents2 () =
            taskResult {
                let! enrollmentEvents = StateView.getFilteredMultipleAggregateEventsInATimeIntervalAsync<Enrollments, EnrollmentEvents, string> [Enrollment.enrollmentId.Id] eventStore DateTime.MinValue DateTime.MaxValue (fun _ -> true) None
                return enrollmentEvents
            }
        
        member this.GetAllEnrollmentEvents3 () =
            taskResult {
                let! events = StateView.GetAllAggregateEventsInATimeIntervalAsync<Enrollments, EnrollmentEvents, string> eventStore DateTime.MinValue DateTime.MaxValue None
                return events
            }    
