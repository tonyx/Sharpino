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
let connection =
    Environment.GetEnvironmentVariable("CONNECTION_STRING")
    
let pgEventStore:IEventStore<byte[]> = PgBinaryStore.PgBinaryStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName
    pgEventStore.Reset Course.Version Course.StorageName
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName
    AggregateCache3.Instance.Clear()

let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, byte[]> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, byte[]> pgEventStore

let courseViewerAsync = 
    fun (id: Guid) -> getAggregateStorageFreshStateViewerAsync<Course, CourseEvents, byte[]> pgEventStore None id |> Async.AwaitTask |> Async.RunSynchronously
let studentViewerAsync = 
    fun (id: Guid) -> getAggregateStorageFreshStateViewerAsync<Student, StudentEvents, byte[]> pgEventStore None id |> Async.AwaitTask |> Async.RunSynchronously

let courseManager = CourseManager(pgEventStore, courseViewer, studentViewer, MessageSenders.NoSender)
let courseManagerAsync = CourseManager(pgEventStore, courseViewerAsync, studentViewerAsync, MessageSenders.NoSender)


[<Tests>]
let tests =
    testList "samples" 
        [
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

            testCase "add a student only async" <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)
                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"
                let studentRetrieved = courseManagerAsync.GetStudent student.Id
                Expect.isOk studentRetrieved "Student not retrieved"

            testCase "add a student async, retrieve snapshot " <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)
                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"
                let snapshot = pgEventStore.TryGetLastAggregateSnapshot Student.Version Student.StorageName student.Id
                Expect.isTrue snapshot.IsOk "snapshot not found"
                let (_, snap) = snapshot.OkValue
                let student = Student.Deserialize snap
                Expect.isOk student "student not deserialized"

            testCase "add a studen, retrieve snapshot " <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)
                let studentCreated = courseManager.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"
                let snapshot = pgEventStore.TryGetLastAggregateSnapshot Student.Version Student.StorageName student.Id
                Expect.isTrue snapshot.IsOk "snapshot not found"
                let (_, snap) = snapshot.OkValue
                let student = Student.Deserialize snap
                Expect.isOk student "student not deserialized"

            testCase "serialize and deserialize a student" <| fun _ ->
                let student = Student.MkStudent ("Jack", 3)
                let serialized = student.Serialize
                let deserialized = Student.Deserialize serialized
                Expect.isOk deserialized "student not deserialized"
                let student' =  deserialized.OkValue
                Expect.equal student student' "must be equal"

            testCase "create a snapshot of student, retrieve it and deserialize it " <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)
                let serialized = student.Serialize
                let stored = pgEventStore.SetInitialAggregateState student.Id Student.Version Student.StorageName serialized
                Expect.isOk stored "should store the initial state"
                let snapshot = pgEventStore.TryGetLastAggregateSnapshot Student.Version Student.StorageName student.Id
                Expect.isOk snapshot "snapshot not found"
                let (_, snap) = snapshot.OkValue
                Expect.equal snap serialized "snapshot not equal to serialized"

            testCase "add a course and a student async" <| fun _ ->
                setUp ()
                let course = Course.MkCourse ("math", 10)
                let student = Student.MkStudent ("Jack", 3)
                let courseCreated = courseManagerAsync.AddCourse course
                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue courseCreated.IsOk "Course not created"
                Expect.isTrue studentCreated.IsOk "student not created"
                let courseRetrieved = courseManagerAsync.GetCourse course.Id
                let studentRetrieved = courseManagerAsync.GetStudent student.Id
                Expect.isOk courseRetrieved "Course not retrieved"
                Expect.isOk studentRetrieved "Student not retrieved"
                
            testCase "insert 1000 students async" <| fun _ ->
                setUp ()
                let students = Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 1000 students took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 5000 students async" <| fun _ ->
                setUp ()
                let students = Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 5000 students took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 10000 students async" <| fun _ ->
                setUp ()
                let students = Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 10000 students took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 1000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 1000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
            
            testCase "insert 5000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 5000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
            
            testCase "insert 10000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 10000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 100000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 10000 students in batch took %d ms" stopwatch.ElapsedMilliseconds   

            testCase "filtered students retrieval" <| fun _ ->
                setUp ()
                let students = [|
                    Student.MkStudent ("Jack", 3)
                    Student.MkStudent ("John", 5)
                    Student.MkStudent ("James", 3)
                |]
                courseManagerAsync.AddMultipleStudents students |> ignore
                let filtered = courseManagerAsync.GetStudentsFilteredAsync (fun s -> s.MaxNumberOfCourses = 5) |> Async.AwaitTask |> Async.RunSynchronously
                Expect.isOk filtered "Filtered results should be ok"
                let result = filtered.OkValue
                Expect.equal result.Length 1 "Should find only one student"
                Expect.equal (result.[0] |> snd).Name "John" "Should be John"
        ]
        |> testSequenced
    
    
    
