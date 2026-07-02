
module Sharpino.Sample._9.CourseManagerAsyncTests

open System
open System.Threading.Tasks
open Expecto
open ItemManager.Common
open Microsoft.Extensions.Logging
open Sharpino.Cache
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.RabbitMq
open Sharpino.Sample._9.BalanceConsumer
open Sharpino.Sample._9.BalanceEvents
open Sharpino.Sample._9.CourseConsumer
open Sharpino.Sample._9.StudentConsumer
open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherConsumer
open Sharpino.Sample._9.TeacherEvents
open Sharpino.StateView
open Sharpino.TestUtils
open FsToolkit.ErrorHandling

open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents
open Sharpino.Sample._9.CourseCommands

open Sharpino.Sample._9.Student
open Sharpino.Sample._9.StudentEvents
open Sharpino.Sample._9.StudentCommands
open Sharpino.Sample._9.CourseManager
open Sharpino.Sample._9.Balance

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Sharpino.Sample._9
open CourseManagerAsync
open System.Threading


let courseViewerAsync = fun ct -> getAggregateStorageFreshStateViewerAsync<Course, CourseEvents, string> pgEventStore ct 
let studentViewerAsync = fun ct -> getAggregateStorageFreshStateViewerAsync<Student, StudentEvents, string> pgEventStore ct 
let teacherViewerAsync = fun ct -> getAggregateStorageFreshStateViewerAsync<Teacher, TeacherEvents, string> pgEventStore ct 
let balanceViewerAsync = fun ct -> getAggregateStorageFreshStateViewerAsync<Balance, BalanceEvents, string> pgEventStore ct 

let mkCourseManagerAsync =
    fun () ->
        CourseManagerAsync(
            pgEventStore,
            MessageSenders.NoSender,
            courseViewerAsync,
            studentViewerAsync,
            teacherViewerAsync,
            balanceViewerAsync,
            Balance.MkBalance 1000.0M
        )

[<Tests>]
let tests =
    testList "CourseManagerAsyncTests"
        [
            testCaseTask "Teacher John will be able to teach Math if student Jack is enrolled in that course and not in literature - Ok" <| fun _ ->
                task {
                    setUp pgEventStore 
                    let courseManager = mkCourseManagerAsync  ()
                    let teacher = Teacher.MkTeacher "John Doe" 
                    let! addTeacher = courseManager.AddTeacherAsync teacher 
                    Expect.isOk addTeacher "addTeacher"
                    let math = Course.MkCourse ("Math" , 10)
                    let! addCourse = courseManager.AddCourseAsync math 
                    Expect.isOk addCourse "should be ok"

                    let literature = Course.MkCourse ("Literature" , 10)
                    let! addLiterature = courseManager.AddCourseAsync literature 
                    Expect.isOk addLiterature "should be ok"

                    let jack = Student.MkStudent ("Jack" , 10)
                    let! addStudent = courseManager.AddStudentAsync jack 
                    Expect.isOk addStudent "should be ok"
                    let! subscribeJackToMath = courseManager.SubscribeStudentAsync(jack.Id, math.Id)
                    Expect.isOk subscribeJackToMath "should be ok"

                    let jackShouldNotBeEnrolledInLitAndMath =
                        fun (ct: CancellationToken) ->
                            taskResult
                                {
                                    let! (jackEvId, jack) = studentViewerAsync (ct |> Some) jack.Id 
                                    let extraStreamsLocks =
                                        [((jack.Id, Student.Version + Student.StorageName), jackEvId)] |> Map.ofList

                                    let! constraintToBeMet =
                                        (not 
                                            (jack.Courses |> List.exists (fun c -> c = literature.Id)
                                            && (jack.Courses |> List.exists (fun c -> c = math.Id)))
                                        )
                                        |> Result.ofBool "constraint not met"
                                    return extraStreamsLocks
                                }
                    let! addTeacher = courseManager.ConditionallyAddTeacher (teacher.Id, math.Id, jackShouldNotBeEnrolledInLitAndMath)
                    Expect.isOk addTeacher "should be ok"
                }

            testCaseTask "Teacher John will be able to teach Math if student Jack is enrolled in that course and not in literature - Error" <| fun _ ->
                task {
                    setUp pgEventStore 
                    let courseManager = mkCourseManagerAsync  ()
                    let teacher = Teacher.MkTeacher "John Doe" 
                    let! addTeacher = courseManager.AddTeacherAsync teacher 
                    Expect.isOk addTeacher "addTeacher"
                    let math = Course.MkCourse ("Math" , 10)
                    let! addCourse = courseManager.AddCourseAsync math 
                    Expect.isOk addCourse "should be ok"

                    let literature = Course.MkCourse ("Literature" , 10)
                    let! addLiterature = courseManager.AddCourseAsync literature 
                    Expect.isOk addLiterature "should be ok"

                    let jack = Student.MkStudent ("Jack" , 10)
                    let! addStudent = courseManager.AddStudentAsync jack 
                    Expect.isOk addStudent "should be ok"
                    let! subscribeJackToMath = courseManager.SubscribeStudentAsync(jack.Id, math.Id)
                    Expect.isOk subscribeJackToMath "should be ok"
                    let! subscribeJackToLit = courseManager.SubscribeStudentAsync(jack.Id, literature.Id)
                    Expect.isOk subscribeJackToLit "should be fail"

                    let jackShouldNotBeEnrolledInLitAndMath =
                        fun (ct: CancellationToken) ->
                            taskResult
                                {
                                    let! (jackEvId, jack) = studentViewerAsync (ct |> Some) jack.Id 
                                    let extraStreamsLocks =
                                        [((jack.Id, Student.Version + Student.StorageName), jackEvId)] |> Map.ofList

                                    let! constraintToBeMet =
                                        (not 
                                            (jack.Courses |> List.exists (fun c -> c = literature.Id)
                                            && (jack.Courses |> List.exists (fun c -> c = math.Id)))
                                        )
                                        |> Result.ofBool "constraint not met"
                                    return extraStreamsLocks
                                }
                    let! addTeacher = courseManager.ConditionallyAddTeacher (teacher.Id, math.Id, jackShouldNotBeEnrolledInLitAndMath)
                    Expect.isError addTeacher "should fail"
                }
        ]