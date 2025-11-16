module BulkLoadTests

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
    
let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName
    pgEventStore.Reset Course.Version Course.StorageName
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName
    AggregateCache3.Instance.Clear()


let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore

let courseManager = CourseManager(pgEventStore, courseViewer, studentViewer, MessageSenders.NoSender)
[<Tests>]
let tests =
    testList "bulk load examples" [
       testCase "add many courses, retrieve them all as a bulk operation" <| fun _ ->
           setUp ()
           let courses =
               [ for i in 1 .. 100 -> Course.MkCourse ("course" + i.ToString(), 10) ]
               |> Array.ofList
           let added = courseManager.AddMultipleCourses courses
           Expect.isOk added "should be ok"
           let coursesIds = courses |> Array.map (fun c -> c.Id)
           let retrieved = courseManager.GetCourses coursesIds
           Expect.isOk retrieved "should be ok"
           let retrievedCourses = retrieved.OkValue |> List.map (fun (x: Course) -> x.Id)
           Expect.equal (coursesIds |> Set.ofArray) (retrievedCourses |> Set.ofList) "should be equal"
           
       testCase "add a course and then get by getCourse" <| fun _ ->
           let course = Course.MkCourse ("course", 10)
           let added = courseManager.AddCourse course
           let removeCache = AggregateCache3.Instance.Clear ()
           let retrieved = courseManager.GetCourse course.Id
           Expect.isOk retrieved "should be ok"
           let retrievedCourse = retrieved.OkValue
           Expect.equal course retrievedCourse "should be equal"
       
       // ftestCase "add a course and then get by getLastAggregateSnapshotOrStateCache - Ok" <| fun _ ->
       //     let course = Course.MkCourse ("course", 10)
       //     let added = courseManager.AddCourse course
       //     let removeCache = AggregateCache3.Instance.Clear ()
       //     let retrieved = Sharpino.StateView.getLastAggregateSnapshotOrStateCache<Course, string> course.Id Course.Version Course.StorageName pgEventStore
       //     Expect.isTrue true "true"
           
    ]
    |> testSequenced
           
