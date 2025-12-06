module Tests

open System
open System.Diagnostics
open System.Threading
open Expecto
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open Shaprino.Sample._11.StudentConsumer
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.RabbitMq
open Sharpino.Sample._11.Course
open Sharpino.Sample._11.CourseConsumer
open Sharpino.Sample._11.CourseEvents
open Sharpino.Sample._11.CourseManager

open DotNetEnv
open Sharpino.Sample._11.Student
open Sharpino.Sample._11.StudentEvents
open Sharpino.Storage

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;"+
    "Database=sharpino_sample14;" +
    "User Id=safe;"+
    $"Password={password}"

let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName
    pgEventStore.Reset Course.Version Course.StorageName
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName
    AggregateCache3.Instance.Clear()
    
let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore

let courseManager = CourseManager(pgEventStore, courseViewer, studentViewer, MessageSenders.NoSender,
                                     fun () ->
                                        StateView.getFilteredAggregateStatesInATimeInterval2<Student, StudentEvents, string>
                                           pgEventStore
                                           DateTime.MinValue
                                           DateTime.MaxValue
                                           (fun _ -> true)
                                     )

[<Tests>]
let tests =
    testList "samples" [
       testCase "add a course and a student" <| fun _ ->
          setUp ()
          let course = Course.MkCourse ("math", 10)
          let student = Student.MkStudent ("Jack", 3)
          let courseCreated = courseManager.AddCourse course
          let studentCreated = courseManager.AddStudent student
          Expect.isTrue courseCreated.IsOk "Course not created"
          Expect.isTrue studentCreated.IsOk "student not created"
          let courseRetrieved = courseManager.GetCourse course.Id
          let studentRetrieved = courseManager.GetStudent student.Id
          Expect.isOk courseRetrieved "Course not retrieved"
          Expect.isOk studentRetrieved "Student not retrieved"
          
       ftestCase "a student can enroll to only two courses, and they tries to enroll to the third one and it will be rejected - Ok" <| fun _ ->
          setUp ()
          let math = Course.MkCourse ("math", 10)
          let english = Course.MkCourse ("english", 10)
          let physics = Course.MkCourse ("physics", 10)
          let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
          Expect.isOk courseAdded "Courses not created"
          let student = Student.MkStudent ("Jack", 2)
          let addStudent = courseManager.AddStudent student
          Expect.isOk addStudent "Student not created"
          
          // when
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.Id math.Id
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          let enrollStudentToEnglish = courseManager.EnrollStudentToCourse student.Id english.Id
          Expect.isOk enrollStudentToEnglish "Student non enrolled to english"
          let enrollStudentToPhysics = courseManager.EnrollStudentToCourse student.Id physics.Id
          Expect.isError enrollStudentToPhysics "Student enrolled to physics"
          
          // then
          let retrieveMath = courseManager.GetCourse math.Id |> Result.get
          Expect.equal retrieveMath.Students.Length 1 "should be one"
          
          let retrieveEnglish = courseManager.GetCourse english.Id |> Result.get
          Expect.equal retrieveEnglish.Students.Length 1 "should be one"
          let retrievePhysics = courseManager.GetCourse physics.Id |> Result.get
          Expect.equal retrievePhysics.Students.Length 0 "should be zero"
          let retrieveStudent = courseManager.GetStudent student.Id |> Result.get
          Expect.equal retrieveStudent.Courses.Length 2 "should be two"
          
       // ftestCase "a student can enroll to only two courses, and they tries to enroll to the third one and it will be rejected. Version using compensation events - Ok" <| fun _ ->
       //    setUp ()
       //    let math = Course.MkCourse ("math", 10)
       //    let english = Course.MkCourse ("english", 10)
       //    let physics = Course.MkCourse ("physics", 10)
       //    let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
       //    Expect.isOk courseAdded "Courses not created"
       //    let student = Student.MkStudent ("Jack", 2)
       //    let addStudent = courseManager.AddStudent student
       //    Expect.isOk addStudent "Student not created"
       //    
       //    // when
       //    let enrollStudentToMath = courseManager.EnrollStudentToCourseCompensationVersion student.Id math.Id
       //    Expect.isOk enrollStudentToMath "Student not enrolled to math"
       //    let enrollStudentToEnglish = courseManager.EnrollStudentToCourseCompensationVersion student.Id english.Id
       //    Expect.isOk enrollStudentToEnglish "Student non enrolled to english"
       //    let enrollStudentToPhysics = courseManager.EnrollStudentToCourseCompensationVersion student.Id physics.Id
       //    Expect.isError enrollStudentToPhysics "Student enrolled to physics"
       //    
       //    // then
       //    let retrieveMath = courseManager.GetCourse math.Id |> Result.get
       //    Expect.equal retrieveMath.Students.Length 1 "should be one"
       //    
       //    let retrieveEnglish = courseManager.GetCourse english.Id |> Result.get
       //    Expect.equal retrieveEnglish.Students.Length 1 "should be one"
       //    let retrievePhysics = courseManager.GetCourse physics.Id |> Result.get
       //    Expect.equal retrievePhysics.Students.Length 0 "should be zero"
       //    let retrieveStudent = courseManager.GetStudent student.Id |> Result.get
       //    Expect.equal retrieveStudent.Courses.Length 2 "should be two"
          
    ]
    |> testSequenced
    
    
    
