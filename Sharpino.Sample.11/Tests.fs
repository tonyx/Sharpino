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
    "Database=sharpino_item;" +
    "User Id=safe;"+
    $"Password={password}"

#if RABBITMQ
let hostBuilder =
   Host.CreateDefaultBuilder()
      .ConfigureServices (fun (services: IServiceCollection) ->
         services.AddSingleton<RabbitMqReceiver2>() |> ignore
         services.AddHostedService<CourseConsumer>() |> ignore
         services.AddHostedService<StudentConsumer>() |> ignore
         ()
      )

let host = hostBuilder.Build()
let hostTask = host.StartAsync()
let services = host.Services

let courseConsumer =
   host.Services.GetServices<IHostedService>()
   |> Seq.find (fun x -> x.GetType() = typeof<CourseConsumer>)
   :?> CourseConsumer

let studentConsumer =
   host.Services.GetServices<IHostedService>()
   |> Seq.find (fun x -> x.GetType() = typeof<StudentConsumer>)
   :?> StudentConsumer   

let courseMessageSender =
   mkMessageSender "127.0.0.1" $"{Course.Version}{Course.StorageName}"
   |> Result.get

let studentMessageSender =
   mkMessageSender "127.0.0.1" $"{Student.Version}{Student.StorageName}"
   |> Result.get
   
let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()

aggregateMessageSenders.Add($"{Course.Version}{Course.StorageName}", courseMessageSender)
aggregateMessageSenders.Add($"{Student.Version}{Student.StorageName}", studentMessageSender)

let rabbitMqMessageSender =
   MessageSenders.MessageSender
      (fun queueName ->
         let sender = aggregateMessageSenders.TryGetValue(queueName)
         match sender with
         | true, sender -> sender |> Ok
         | false, _ -> sprintf "sender not found %s" queueName |> Error
      )

let purgeRabbitMqQueue (queueName: string) =
   let factory = ConnectionFactory (HostName = "localhost")
   let connection =
      factory.CreateConnectionAsync()
      |> Async.AwaitTask
      |> Async.RunSynchronously
   let channel =
      connection.CreateChannelAsync ()
      |> Async.AwaitTask
      |> Async.RunSynchronously
   let queueDeclare =
      channel.QueueDeclareAsync (queueName, false, false, false, null)
      |> Async.AwaitTask
      |> Async.RunSynchronously
   channel.QueuePurgeAsync (queueName)
      |> Async.AwaitTask
      |> Async.RunSynchronously
      |> ignore
   connection.Dispose()
#endif

let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName
    pgEventStore.Reset Course.Version Course.StorageName
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName
    AggregateCache3.Instance.Clear()
    
let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore

#if RABBITMQ
let rabbitMqCourseStateViewer = courseConsumer.GetAggregateState
let rabbitMqStudentStateViewer = studentConsumer.GetAggregateState

let _ =
   studentConsumer.SetFallbackAggregateStateRetriever studentViewer
   courseConsumer.SetFallbackAggregateStateRetriever courseViewer

let distributedStudentsViewer =
   fun () -> studentConsumer.GetAllAggregateStates () 

let distributedCourseManager = CourseManager(pgEventStore, rabbitMqCourseStateViewer, rabbitMqStudentStateViewer, rabbitMqMessageSender, distributedStudentsViewer)

#endif
   

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
          let courseRetrieved = courseManager.GetCourse course.CourseId
          let studentRetrieved = courseManager.GetStudent student.StudentId
          Expect.isOk courseRetrieved "Course not retrieved"
          Expect.isOk studentRetrieved "Student not retrieved"
          
       testCase "insert 1000 students" <| fun _ ->
          setUp ()
          let students = Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          Array.iter (fun student -> courseManager.AddStudent student |> ignore) students
          stopwatch.Stop()
          printfn "Inserting 1000 students one by one took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 5000 students" <| fun _ ->
          setUp ()
          let students = Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          Array.iter (fun student -> courseManager.AddStudent student |> ignore) students
          stopwatch.Stop()
          printfn "Inserting 5000 students one by one took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 10000 students" <| fun _ ->
          setUp ()
          let students = Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let stopwatch = Stopwatch()
          stopwatch.Start()
          Array.iter (fun student -> courseManager.AddStudent student |> ignore) students
          stopwatch.Stop()
          printfn "Inserting 10000 students one by one took %d ms" stopwatch.ElapsedMilliseconds
          
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
          printfn "Inserting 100000 students in batch took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 1000 students in batch and retrieve them - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 1000 students passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students without passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
       
       testCase "insert 5000 students in batch and retrieve them - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 5000 students passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 5000 students without passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
       
       testCase "insert 10000 students in batch and retrieve them - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 10000 students passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 10000 students without passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
      
       testCase "insert 100000 student in batch and retrieve them - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 100000 students passing ids with cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 100000 students without passing ids cache enabled took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 1000 students in batch and retrieve them without cache - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let _ =
             AggregateCache3.Instance.Clear ()
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 1000 students passing ids cache disabled took %d ms" stopwatch.ElapsedMilliseconds
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 1000 students without passing ids cache disabled took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 5000 students in batch and retrieve them without cache - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 5000 students passing ids cache purged took %d ms" stopwatch.ElapsedMilliseconds
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 5000 students passing ids without cache took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 10000 students in batch and retrieve them without cache - Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 10000 students passing ids cache purged took %d ms" stopwatch.ElapsedMilliseconds
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 10000 students no ids without cache took %d ms" stopwatch.ElapsedMilliseconds
          
       testCase "insert 100000 student in batch and retrieve them without cache- Ok" <| fun _ ->
          setUp ()
          let students =
             Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = courseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.StudentId) |> List.ofArray
          let stopwatch = Stopwatch()
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Start()
          let retrieved = courseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 100000 students cache purged took %d ms" stopwatch.ElapsedMilliseconds
          
          let _ =
             AggregateCache3.Instance.Clear ()
          stopwatch.Restart()
          let retrieved = courseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 100000 students without passing their ids took %d ms" stopwatch.ElapsedMilliseconds  
        
    #if RABBITMQ  
       ftestCase "insert 1000 students in batch and retrieve them using message receiver and passing their ids - Ok" <| fun _ ->
          setUp ()
          purgeRabbitMqQueue (Student.Version + Student.StorageName)
          
          let students =
             Array.init 1000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = distributedCourseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.Id) |> List.ofArray
          
          Thread.Sleep 1000
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = distributedCourseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 1000 students using message bus passing their ids took %d ms" stopwatch.ElapsedMilliseconds
       
       ftestCase "insert 5000 students in batch and retrieve them using message receiver  - Ok" <| fun _ ->
          setUp ()
          purgeRabbitMqQueue (Student.Version + Student.StorageName)
          
          let students =
             Array.init 5000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = distributedCourseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.Id) |> List.ofArray
          
          Thread.Sleep 1000
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = distributedCourseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 5000 students using message bus passing their ids took %d ms" stopwatch.ElapsedMilliseconds
          
          stopwatch.Restart()
          let retrieved = distributedCourseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 5000 students without passing their ids took %d ms" stopwatch.ElapsedMilliseconds
       
       ftestCase "insert 10000 students in batch and retrieve them using message receiver and passing their ids - Ok" <| fun _ ->
          setUp ()
          purgeRabbitMqQueue (Student.Version + Student.StorageName)
          
          let students =
             Array.init 10000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = distributedCourseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.Id) |> List.ofArray
          
          Thread.Sleep 1000
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = distributedCourseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 10000 students using message bus passing their ids took %d ms" stopwatch.ElapsedMilliseconds
          
          stopwatch.Restart()
          let retrieved = distributedCourseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 10000 students without passing their ids took %d ms" stopwatch.ElapsedMilliseconds
       
       ftestCase "insert 100000 students in batch and retrieve them using message receiver and passing their ids - Ok" <| fun _ ->
          setUp ()
          purgeRabbitMqQueue (Student.Version + Student.StorageName)
          
          let students =
             Array.init 100000 (fun _ -> Student.MkStudent (Guid.NewGuid().ToString(), 3))
          let added = distributedCourseManager.AddMultipleStudents students
          let ids = students |> Array.map (fun (x: Student) -> x.Id) |> List.ofArray
          
          Thread.Sleep 1000
          let stopwatch = Stopwatch()
          stopwatch.Start()
          let retrieved = distributedCourseManager.GetStudents ids
          stopwatch.Stop()
          printfn "Retrieving 100000 students using message bus passing their ids took %d ms" stopwatch.ElapsedMilliseconds
          
          stopwatch.Restart()
          let retrieved = distributedCourseManager.GetAllStudents ()
          stopwatch.Stop()
          printfn "Retrieving 100000 students without passing their ids took %d ms" stopwatch.ElapsedMilliseconds
          
      #endif 
    ]
    |> testSequenced
    
    
    
