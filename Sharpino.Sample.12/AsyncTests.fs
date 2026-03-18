module AsyncTests

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
    "Database=sharpino_coursemanager_bin;" +
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

let courseViewerAsync = 
    fun (id: Guid) -> getAggregateStorageFreshStateViewerAsync<Course, CourseEvents, byte[]> pgEventStore None id |> Async.AwaitTask |> Async.RunSynchronously
let studentViewerAsync = 
    fun (id: Guid) -> getAggregateStorageFreshStateViewerAsync<Student, StudentEvents, byte[]> pgEventStore None id |> Async.AwaitTask |> Async.RunSynchronously

let courseManager = CourseManager(pgEventStore, courseViewer, studentViewer, MessageSenders.NoSender)
let courseManagerAsync = CourseManager(pgEventStore, courseViewerAsync, studentViewerAsync, MessageSenders.NoSender)


[<Tests>]
let tests =
    testList "async tests" 
        [
            testCase "add a course and a student async 1" <| fun _ ->
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

            testCase "add only a student" <| fun _ ->
                setUp ()
                let course = Course.MkCourse ("math", 10)
                let student = Student.MkStudent ("Jack", 3)
                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"
                let studentRetrieved = courseManagerAsync.GetStudent student.Id
                Expect.isOk studentRetrieved "Student not retrieved"
                Expect.equal student.Name studentRetrieved.OkValue.Name "must be equal"

            testCase "add only a student get fresh state on state View" <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)
                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"

                let studentRetrieved = StateView.getAggregateFreshState<Student, StudentEvents, byte[]> student.Id pgEventStore 
                Expect.isOk studentRetrieved "State not retrieved"

            testCase "add only a student get fresh state on by the eventstore " <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)

                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"

                let stateRetrieved = pgEventStore.TryGetLastAggregateSnapshot Student.Version Student.StorageName student.Id

                Expect.isOk stateRetrieved "State not retrieved"
                let value = stateRetrieved.OkValue |> snd |> Student.Deserialize
                Expect.isOk value "State not deserialized"

            testCase "binary write and read test" <| fun _ ->
                setUp ()
                let id = Guid.NewGuid()

                let anyRandomBin: byte[] = [|1uy; 2uy; 3uy; 4uy; 5uy; 6uy; 7uy; 8uy; 9uy; 10uy|]

                let snapshotInitialized =
                    pgEventStore.SetInitialAggregateState id Student.Version Student.StorageName anyRandomBin

                Expect.isOk snapshotInitialized "Snapshot not initialized"

                let stateRetrieved = pgEventStore.TryGetLastAggregateSnapshot Student.Version Student.StorageName id

                Expect.equal anyRandomBin (stateRetrieved.OkValue |> snd) "State not equal"
                
            testCase "insert 1000 students" <| fun _ ->
                setUp ()
                let students = Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 1000 students took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 5000 students" <| fun _ ->
                setUp ()
                let students = Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 5000 students took %d ms" stopwatch.ElapsedMilliseconds
                
            ftestCase "insert 10000 students and retrieve them" <| fun _ ->
                setUp ()
                let students = Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 10000 students took %d ms" stopwatch.ElapsedMilliseconds

                let stopwatch2 = Stopwatch()
                stopwatch2.Start()
                let students = courseManagerAsync.GetAllStudents()
                Expect.isOk students "Courses not retrieved"
                Expect.equal students.OkValue.Length 10000 "Courses not equal"
                stopwatch2.Stop()
                printfn "Retrieving 10000 students non async took %d ms" stopwatch2.ElapsedMilliseconds

                let stopwatch3 = Stopwatch()
                stopwatch3.Start()
                let studentsAsync = courseManagerAsync.GetAllStudentsAsync() |> Async.AwaitTask |> Async.RunSynchronously 
                Expect.isOk studentsAsync "Courses not retrieved"
                Expect.equal studentsAsync.OkValue.Length 10000 "Courses not equal"
                stopwatch3.Stop()
                printfn "Retrieving 10000 courses async took %d ms" stopwatch3.ElapsedMilliseconds
                
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
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 5000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
            
            testCase "insert 10000 students in batch" <| fun _ ->
                setUp ()
                let students =
                    Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 10000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 100000 students in batch" <| fun _ ->
                setUp ()
                let students =
                    Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 10000 students in batch took %d ms" stopwatch.ElapsedMilliseconds   

            testCase "add a student only async" <| fun _ ->
                setUp ()
                let student = Student.MkStudent ("Jack", 3)
                let studentCreated = courseManagerAsync.AddStudent student
                Expect.isTrue studentCreated.IsOk "student not created"
                let studentRetrieved = courseManagerAsync.GetStudent student.Id
                Expect.isOk studentRetrieved "Student not retrieved"


            testCase "serialize and deserialize a student" <| fun _ ->
                let student = Student.MkStudent ("Jack", 3)
                let serialized = student.Serialize
                let deserialized = Student.Deserialize serialized
                Expect.isOk deserialized "student not deserialized"
                let student' =  deserialized.OkValue
                Expect.equal student student' "must be equal"


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
                printfn "Inserting 1000 students async took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 5000 students async" <| fun _ ->
                setUp ()
                let students = Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 5000 students async took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 10000 students async" <| fun _ ->
                setUp ()
                let students = Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                Array.iter (fun student -> courseManagerAsync.AddStudent student |> ignore) students
                stopwatch.Stop()
                printfn "Inserting 10000 students async took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 1000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 1000 students in batch async took %d ms" stopwatch.ElapsedMilliseconds
            
            testCase "insert 5000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 5000 students in batch async took %d ms" stopwatch.ElapsedMilliseconds
            
            testCase "insert 10000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 10000 students in batch async took %d ms" stopwatch.ElapsedMilliseconds
                
            testCase "insert 100000 students in batch async" <| fun _ ->
                setUp ()
                let students =
                    Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
                let stopwatch = Stopwatch()
                stopwatch.Start()
                courseManagerAsync.AddMultipleStudents students |> ignore
                stopwatch.Stop()
                printfn "Inserting 10000 students in batch async took %d ms" stopwatch.ElapsedMilliseconds   
        ]
        |> testSequenced
    
    