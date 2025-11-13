module Tests

open System
open System.Diagnostics
open Expecto
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.Sample._11.Course
open Sharpino.Sample._11.CourseEvents
open Sharpino.Sample._11.CourseManager

open DotNetEnv
open Sharpino.Sample._11.Student
open Sharpino.Sample._11.StudentEvents
open Sharpino.Storage

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;"+
    "Database=sharpino_item;" +
    "User Id=safe;"+
    $"Password={password}"
    
let pgEventStore:IEventStore<byte[]> = PgBinaryStore.PgBinaryStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName
    pgEventStore.Reset Course.Version Course.StorageName
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName
    AggregateCache3.Instance.Clear()

let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, byte[]> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, byte[]> pgEventStore

let courseManager = CourseManager(pgEventStore, courseViewer, studentViewer, MessageSenders.NoSender)

[<Tests>]
let tests =
    testList "samples" [
       ptestCase "add a course and a student" <| fun _ ->
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
          
       testCase "insert 1000 students" <| fun _ ->
          setUp ()
          let students = Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          Array.iter (fun student -> courseManager.AddStudent student |> ignore) students
          stopwatch.Stop()
          printfn "Inserting 1000 students took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 5000 students" <| fun _ ->
          setUp ()
          let students = Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          Array.iter (fun student -> courseManager.AddStudent student |> ignore) students
          stopwatch.Stop()
          printfn "Inserting 5000 students took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 10000 students" <| fun _ ->
          setUp ()
          let students = Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          Array.iter (fun student -> courseManager.AddStudent student |> ignore) students
          stopwatch.Stop()
          printfn "Inserting 10000 students took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 1000 students in batch" <| fun _ ->
          setUp ()
          let students =
             Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          courseManager.AddMultipleStudents students |> ignore
          stopwatch.Stop()
          printfn "Inserting 1000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
       
       testCase "insert 5000 students in batch" <| fun _ ->
          setUp ()
          let students =
             Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          courseManager.AddMultipleStudents students |> ignore
          stopwatch.Stop()
          printfn "Inserting 5000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
       
       testCase "insert 10000 students in batch" <| fun _ ->
          setUp ()
          let students =
             Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          courseManager.AddMultipleStudents students |> ignore
          stopwatch.Stop()
          printfn "Inserting 10000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 100000 students in batch" <| fun _ ->
          setUp ()
          let students =
             Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          courseManager.AddMultipleStudents students |> ignore
          stopwatch.Stop()
          printfn "Inserting 10000 students in batch took %d ms" stopwatch.ElapsedMilliseconds   
    ]
    |> testSequenced
    
    
    
