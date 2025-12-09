module Test2

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

open DotNetEnv
open Sharpino.Sample._11.Student2
open Sharpino.Sample._11.StudentEvents2
open Sharpino.Sample._11.StudentManager2
open Sharpino.Storage

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;"+
    "Database=sharpino_coursemanager;" +
    "User Id=safe;"+
    $"Password={password}"
    
let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Student2.Version Student2.StorageName
    pgEventStore.ResetAggregateStream Student2.Version Student2.StorageName
    AggregateCache3.Instance.Clear()
    
let studentViewer = getAggregateStorageFreshStateViewer<Student2, StudentEvents2, string> pgEventStore
let allStudentsViewer = fun () ->StateView.getFilteredAggregateStatesInATimeInterval2<Student2, StudentEvents2, string> pgEventStore DateTime.MinValue DateTime.MaxValue (fun _ -> true)

let studentManager2: StudentManager2 =
    StudentManager2
        (
            pgEventStore,
            studentViewer,
            MessageSenders.NoSender,
            allStudentsViewer
        )

let random = new Random(System.DateTime.Now.Millisecond)
    
let randomText (size: int) =
    let chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    let text =
        [ for _ in 1 .. size -> chars.[random.Next(chars.Length)] ]
        |> List.toArray
        |> System.String
    text


[<Tests>]
let test =
    ptestList "student examples" [
       testCase "insert 10000 students in batch and retrieve them - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 10000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 10000 students no cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 10000 students cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them add five random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 5 ]
              |> List.map (fun _ -> randomText 10)
          
          AggregateCache3.Instance.Clear()
          printf "going to add events\n"
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n"
          printf "adding 5 events on no cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with few events no cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with few events cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them add forty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          
          let fourtyRandomAnnotations =
              [ 1 .. 40 ]
              |> List.map (fun _ -> randomText 10)
          
          AggregateCache3.Instance.Clear()
          printf "going to add events\n"
          do
              for student in students do
                  for annotation in fourtyRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n"
          printf "adding 40 events on no cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with forty events no cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with forty events cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them add fifty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 50 ]
              |> List.map (fun _ -> randomText 10)
          
          AggregateCache3.Instance.Clear()
          printf "going to add events\n"
          let stopwatch = Stopwatch()
          stopwatch.Start()
          do
              for student in students do
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n" 
          printf "adding 50 events on no cached elements %d ms\n" stopwatch.ElapsedMilliseconds 
          
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with fifty events with no cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with fifty events cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them add sixty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 60 ]
              |> List.map (fun _ -> randomText 10)
          
          AggregateCache3.Instance.Clear()
          let stopwatch = Stopwatch()
          stopwatch.Start()
          printf "going to add events\n"
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n"
          printf "adding 60 events on no cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with sixty events and no cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with sixty events and cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them add seventy random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 70 ]
              |> List.map (fun _ -> randomText 10)
          
          AggregateCache3.Instance.Clear()
          printf "going to add events\n"
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          stopwatch.Stop()
          printf "events added\n"
          printf "adding 70 events on no cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with seventy events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with seventy events cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them add eighty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let eightyRandomAnnotations =
              [ 1 .. 80 ]
              |> List.map (fun _ -> randomText 10)
          
          AggregateCache3.Instance.Clear()
          let stopwatch = Stopwatch()
          stopwatch.Start()
          printf "going to add events\n"
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in eightyRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n" 
          
          stopwatch.Stop()
          printf "time to add 80 events on non cached items %A\n " stopwatch.ElapsedMilliseconds
          AggregateCache3.Instance.Clear()
          
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with 80 events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with 80 events cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
    
    /// repeating the same pattern but with cached elements 
    
       ftestCase "insert 1000 students in batch, for each of them, which are cached, add five random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 5 ]
              |> List.map (fun _ -> randomText 10)
          
          printf "going to add events\n"
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n"
          printf "adding 5 events on cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with few events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with few events cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them, which are cached, add forty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          
          let fourtyRandomAnnotations =
              [ 1 .. 40 ]
              |> List.map (fun _ -> randomText 10)
          
          printf "Going to add events\n"
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in fourtyRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "Events added\n"
          printf "Adding 40 events on cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with forty events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with forty events cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them, which are cached, add fifty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 50 ]
              |> List.map (fun _ -> randomText 10)
          
          printf "going to add events\n"
          let stopwatch = Stopwatch()
          stopwatch.Start()
          do
              for student in students do
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n" 
          printf "adding 50 events on cached elements %d ms\n" stopwatch.ElapsedMilliseconds 
          
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with fifty events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with fifty events cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them, which are cached, add sixty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 60 ]
              |> List.map (fun _ -> randomText 10)
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          printf "going to add events\n"
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n"
          printf "adding 60 events on cached elements %d ms\n" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with sixty events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with sixty events cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them, which are cached, add seventy random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let fiveRandomAnnotations =
              [ 1 .. 70 ]
              |> List.map (fun _ -> randomText 10)
          
          printf "going to add events\n"
          let stopwatch = Stopwatch()
          stopwatch.Start()
          do
              for student in students do
                  for annotation in fiveRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          stopwatch.Stop()
          printf "events added\n"
          printf "adding 70 events on cached elements %d ms" stopwatch.ElapsedMilliseconds
          
          AggregateCache3.Instance.Clear()
          
          let stopwatch = Stopwatch()
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with seventy events no cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with seventy events cache enabled took %d ms\n" stopwatch.ElapsedMilliseconds
          
       ftestCase "insert 1000 students in batch, for each of them, which are cached, add eighty random events and then retrieve all - Ok" <| fun _ ->
          setUp ()
          
          let students =
             Array.init 1000 (fun _ -> Student2.MkStudent2 (Guid.NewGuid().ToString(), 3))
          let added = studentManager2.AddMultipleStudents students
          
          let eightyRandomAnnotations =
              [ 1 .. 80 ]
              |> List.map (fun _ -> randomText 10)
          
          let stopwatch = Stopwatch()
          stopwatch.Start()
          printf "going to add events\n"
          let mutable count = 0
          do
              for student in students do
                  count <- count + 1
                  for annotation in eightyRandomAnnotations do
                      let added = studentManager2.AddAnnotationToStudent (student.Id, annotation)
                      ()
          printf "events added\n" 
          
          stopwatch.Stop()
          printf "time to add 80 events on cached items %A\n " stopwatch.ElapsedMilliseconds
          AggregateCache3.Instance.Clear()
          
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with 80 events no cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = studentManager2.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students with 80 events cache enabled took %d ms" stopwatch.ElapsedMilliseconds
    ]
    |> testSequenced

