module Sharpino.Sample._9.CourseManagerTests

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

let balanceConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<BalanceConsumer>)
    :?> BalanceConsumer

let courseConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<CourseConsumer>)
    :?> CourseConsumer

let studentConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<StudentConsumer>)
    :?> StudentConsumer

let teacherConsumer =
    host.Services.GetServices<IHostedService>()
    |> Seq.find (fun s -> s.GetType() = typeof<TeacherConsumer>)
    :?> TeacherConsumer

let logger = host.Services.GetService<ILogger<CourseManager>>()

let rabbitMqBalanceStateViewer = balanceConsumer.GetAggregateState
let rabbitMqCourseStateViewer = courseConsumer.GetAggregateState
let rabbitMqStudentStateViewer = studentConsumer.GetAggregateState
let rabbitMqTeacherStateViewer = teacherConsumer.GetAggregateState
    
let pgStorageBalanceViewer = getAggregateStorageFreshStateViewer<Balance, BalanceEvents, string> pgEventStore
let pgStorageCourseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let pgStorageStudentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore
let pgStorageHistoryCourseViewer = getHistoryAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore 
let pgTeacherViewer = getAggregateStorageFreshStateViewer<Teacher, TeacherEvents, string> pgEventStore

let memoryStorageStudentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> memEventStore
let memoryStorageCourseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> memEventStore
let memoryStorageHistoryCourseViewer = getHistoryAggregateStorageFreshStateViewer<Course, CourseEvents, string> memEventStore 
let memoryStorageBalanceViewer = getAggregateStorageFreshStateViewer<Balance, BalanceEvents, string> memEventStore
let memoryStorageTeacherViewer = getAggregateStorageFreshStateViewer<Teacher, TeacherEvents, string> memEventStore

let aggregateMessageSenders = System.Collections.Generic.Dictionary<string, MessageSender>()
let balanceMessageSender =
    let streamName = Balance.Version + Balance.StorageName
    mkMessageSender "127.0.0.1" streamName
    |> Result.get

balanceConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Balance, BalanceEvents, string> pgEventStore)
courseConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore)
studentConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore)
teacherConsumer.SetFallbackAggregateStateRetriever (getAggregateStorageFreshStateViewer<Teacher, TeacherEvents, string> pgEventStore)

let courseMessageSender =
    let streamName = Course.Version + Course.StorageName
    mkMessageSender "127.0.0.1" streamName
    |> Result.get
    
let studentMessageSender =
    let streamName = Student.Version + Student.StorageName
    mkMessageSender "127.0.0.1" streamName
    |> Result.get

let teacherMessageSender =
    let streamName = Teacher.Version + Teacher.StorageName
    mkMessageSender "127.0.0.1" streamName
    |> Result.get    

aggregateMessageSenders.Add(Balance.Version+Balance.StorageName, balanceMessageSender)
aggregateMessageSenders.Add(Course.Version+Course.StorageName, courseMessageSender)
aggregateMessageSenders.Add(Student.Version+Student.StorageName, studentMessageSender)
aggregateMessageSenders.Add(Teacher.Version+Teacher.StorageName, teacherMessageSender)


let messageSenders =
    fun queueName ->
        let sender = aggregateMessageSenders.TryGetValue(queueName)
        match sender with
        | true, sender -> sender
        | _ -> failwith (sprintf "not found %s" queueName)

let emptyMessageSenders =
    fun queueName ->
        fun message ->
            ValueTask.CompletedTask
let resetConsumers () =
    balanceConsumer.ResetAllStates()
    courseConsumer.ResetAllStates()
    studentConsumer.ResetAllStates()
    teacherConsumer.ResetAllStates()

let instances =
    [
        // (fun () -> setUp pgEventStore),
        // (fun () -> CourseManager
        //               (pgEventStore,
        //                pgStorageCourseViewer,
        //                pgStorageHistoryCourseViewer,
        //                pgStorageStudentViewer,
        //                pgStorageBalanceViewer,
        //                pgTeacherViewer,
        //                Balance.MkBalance 1000.0M,
        //                emptyMessageSenders)),
        // pgStorageCourseViewer,
        // pgStorageStudentViewer, 0;
        
        (fun () ->
            setUp memEventStore
            resetConsumers ()
            ),
        (fun () -> CourseManager
                      (memEventStore,
                       rabbitMqCourseStateViewer,
                       memoryStorageHistoryCourseViewer,
                       rabbitMqStudentStateViewer,
                       rabbitMqBalanceStateViewer,
                       rabbitMqTeacherStateViewer,
                       Balance.MkBalance 1000.0M,
                       messageSenders
         )),
        pgStorageCourseViewer,
        pgStorageStudentViewer, 200;
    ]
    
[<Tests>]
let tests =
    testList "CourseManagerTests" [
        multipleTestCase "check initial balance - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let courseManager = courseManager ()
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 1000.0M "should be equal"
            
        multipleTestCase "add and retrieve a student - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let student = Student.MkStudent ("John", 50)
            let courseManager = courseManager ()
            Async.Sleep delay |> Async.RunSynchronously
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Id student.Id "should be equal"
            Expect.equal retrievedStudent.Name student.Name "should be equal"
      
        multipleTestCase "add a course will costs 100, verify it - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            Async.Sleep delay |> Async.RunSynchronously
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 900.0M "should be equal"
        
        multipleTestCase "add a course, which costs 100, then delete the course, witch costs 50 more. Verify the balance is decreased by 150 - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()

            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Async.Sleep delay |> Async.RunSynchronously
            Expect.equal balance.Amount 900.0M "should be equal"
            
            let teacher = Teacher.MkTeacher ("John")
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher = courseManager.AddTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 850.0M "should be equal"
            
            let tryGetCourse = courseManager.GetCourse course.Id
            Expect.isError tryGetCourse "should be error"
        
        multipleTestCase "add many courses and verify balance - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse (Course.MkCourse  ("Math", 10))
            Expect.isOk addCourse "should be ok"
            let addCourse = courseManager.AddCourse (Course.MkCourse  ("English", 10))
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 800.0M "should be equal"
        
        multipleTestCase "add two courses, delete one of them and verify balance - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse (Course.MkCourse  ("Math", 10))
            Expect.isOk addCourse "should be ok"
            let englishCourse = Course.MkCourse  ("English", 10)
            let addCourse = courseManager.AddCourse (englishCourse)
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 800.0M "should be equal"
            let deleteCourse = courseManager.DeleteCourse (englishCourse.Id)
            Expect.isOk deleteCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 750.0M "should be equal"
            
        multipleTestCase "add and retrieve a course - Ok"  instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let course = Course.MkCourse ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let result = courseManager.GetCourse course.Id
            Expect.isOk result "should be ok"
            let retrievedCourse = result.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
        multipleTestCase "add a student add a course and subscribe the student to that course;
            verify the subscriptions;
            verify that both course and student cannot be deleted - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            // given
            setUp ()
            let student = Student.MkStudent ("John", 5)
            let courseManager = courseManager ()
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let course = Course.MkCourse  ("Math", 10)
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            
            // when
            Async.Sleep delay |> Async.RunSynchronously
            let subscribe = courseManager.SubscribeStudentToCourse student.Id course.Id
            Expect.isOk subscribe "should be ok"
            
            // then
            Async.Sleep delay |> Async.RunSynchronously
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Courses.Length 1 "should be equal"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedCourse = courseManager.GetCourse course.Id
            Expect.isOk retrievedCourse "should be ok"
            let retrievedCourse = retrievedCourse.OkValue
            Expect.equal retrievedCourse.Students.Length 1 "should be equal"
           
            // and also  
            let tryDeleteStudent = courseManager.DeleteStudent student.Id
            Expect.isError tryDeleteStudent "should be error"
            
            let tryDeleteCourse = courseManager.DeleteCourse course.Id
            Expect.isError tryDeleteCourse "should be error"
       
        multipleTestCase "if a students exceeds the max number of courses, the subscription fails - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            // given
            let student = Student.MkStudent ("John", 1)
            let courseManager = courseManager ()
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let course1 = Course.MkCourse  ("Math", 10)
            Async.Sleep delay |> Async.RunSynchronously
            let addCourse1 = courseManager.AddCourse course1
            Expect.isOk addCourse1 "should be ok"
            let course2 = Course.MkCourse  ("Physics", 10)
            Async.Sleep delay |> Async.RunSynchronously
            let addCourse2 = courseManager.AddCourse course2
            Expect.isOk addCourse2 "should be ok"
            
            // when
            Async.Sleep delay |> Async.RunSynchronously
            let firstSubscription = courseManager.SubscribeStudentToCourse student.Id course1.Id
            Expect.isOk firstSubscription "should be ok"
            
            // then
            let secondSubscription = courseManager.SubscribeStudentToCourse student.Id course2.Id
            Expect.isError secondSubscription "should be error"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedStudent = courseManager.GetStudent student.Id
            Expect.isOk retrievedStudent "should be ok"
            let retrievedStudent = retrievedStudent.OkValue
            Expect.equal retrievedStudent.Courses.Length 1 "should be equal"
            
            let retrievedCourse1 = courseManager.GetCourse course1.Id
            Expect.isOk retrievedCourse1 "should be ok"
            let retrievedCourse1 = retrievedCourse1.OkValue
            Expect.equal retrievedCourse1.Students.Length 1 "should be equal"
            
            let retrievedCourse2 = courseManager.GetCourse course2.Id
            Expect.isOk retrievedCourse2 "should be ok"
            let retrievedCourse2 = retrievedCourse2.OkValue
            Expect.equal retrievedCourse2.Students.Length 0 "should be equal"
         
        multipleTestCase "add and delete a student - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let student = Student.MkStudent ("John", 5)
            let courseManager = courseManager ()
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Id student.Id "should be equal"
            Expect.equal retrievedStudent.Name student.Name "should be equal"
            
            let deleteStudent = courseManager.DeleteStudent student.Id
            Expect.isOk deleteStudent "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let tryGetStudent = courseManager.GetStudent student.Id
            Expect.isError tryGetStudent "should be error"
            
        multipleTestCase "add a course and assign a teacher to that course - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let result = courseManager.GetCourse course.Id
            Expect.isOk result "should be ok"
            let retrievedCourse = result.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
            let teacher = Teacher.MkTeacher ("John")
            let addTeacher = courseManager.AddTeacher teacher
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher = courseManager.AddTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedCourse = courseManager.GetCourse course.Id
            Expect.isOk retrievedCourse "should be ok"
            let retrievedCourse = retrievedCourse.OkValue
            Expect.equal retrievedCourse.Teachers.Head teacher.Id "should be equal"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedTeacher = courseManager.GetTeacher teacher.Id
            Expect.isOk retrievedTeacher "should be ok"
            let retrievedTeacher = retrievedTeacher.OkValue
            Expect.equal retrievedTeacher.Courses.Length 1 "should be equal"
        
        multipleTestCase "can't delete a teacher when there are courses assigned to them - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let teacher = Teacher.MkTeacher ("John")
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let course = Course.MkCourse  ("Math", 10)
            Async.Sleep delay |> Async.RunSynchronously
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher = courseManager.AddTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            let deleteTeacher = courseManager.DeleteTeacher teacher.Id
            Expect.isError deleteTeacher "should be error"
        
        multipleTestCase "add and delete a techer - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()            
            let teacher = Teacher.MkTeacher ("John")
            let courseManager = courseManager ()
            Async.Sleep delay |> Async.RunSynchronously
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let result = courseManager.GetTeacher teacher.Id
            Expect.isOk result "should be ok"
            let retrievedTeacher = result.OkValue
            Expect.equal retrievedTeacher.Id teacher.Id "should be equal"
            Expect.equal retrievedTeacher.Name teacher.Name "should be equal"
            
            let deleteTeacher = courseManager.DeleteTeacher teacher.Id
            Expect.isOk deleteTeacher "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let tryGetTeacher = courseManager.GetTeacher teacher.Id
            Expect.isError tryGetTeacher "should be error"
        
        multipleTestCase "add a teacher to a course, then delete that course, and the teacher courses list will be decreased by 1 - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            
            Async.Sleep delay |> Async.RunSynchronously
            let teacher = Teacher.MkTeacher "John"
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let course = Course.MkCourse  ("Math", 10)
            Async.Sleep delay |> Async.RunSynchronously
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher = courseManager.AddTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedTeacher = courseManager.GetTeacher teacher.Id
            Expect.isOk retrievedTeacher "should be ok"
            let retrievedTeacher = retrievedTeacher.OkValue
            Expect.equal retrievedTeacher.Courses.Length 0 "should be equal"
            
        multipleTestCase "should be able to delete a course when there is no teacher assigned to it - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()            
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let tryGetCourse = courseManager.GetCourse course.Id
            Expect.isError tryGetCourse "should be error"
        
        multipleTestCase "create and retrieve a course - Ok"  instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let retrieve = courseManager.GetCourse course.Id
            Expect.isOk retrieve "should be Ok"
            let retrievedCourse = retrieve.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
        multipleTestCase "create and retrieve a course skipping cache - Ok"  instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let voidCache = AggregateCache2.Instance.Clear()
            Async.Sleep delay |> Async.RunSynchronously
            let retrieve = courseManager.GetCourse course.Id
            Expect.isOk retrieve "should be Ok"
            let retrievedCourse = retrieve.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
        multipleTestCase "even though a curse has been deleted, it can still be retrieved by the history state viewer its id - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            
            let voidCache = AggregateCache2.Instance.Clear()
            Async.Sleep delay |> Async.RunSynchronously
            let retrieve1 = courseManager.GetCourse course.Id
            Expect.isOk retrieve1 "should be Ok"
            
            let voidCache = AggregateCache2.Instance.Clear()
            let retrieveH = courseManager.GetHistoryCourse course.Id
            Expect.isOk retrieveH "should be Ok"
        
        multipleTestCase "add and retrieve a course without the cache - Ok"   instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            Async.Sleep delay |> Async.RunSynchronously
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let retrieve = courseManager.GetCourse course.Id
            Expect.isOk retrieve "should be Ok"
            
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
        
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedCourse = courseManager.GetCourse course.Id
            Expect.isError retrievedCourse "should be error"
            
            Async.Sleep delay |> Async.RunSynchronously
            
            let retrieveByHistory = courseManager.GetHistoryCourse course.Id
            Expect.isOk retrieveByHistory "should be ok"
         
        multipleTestCase "add more teacher to a course and then, after deleting the course, verify that the teachers will not be part of the course anymore - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()            
            Async.Sleep delay |> Async.RunSynchronously
            let teacher1 = Teacher.MkTeacher ("John")
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher1
            let teacher2 = Teacher.MkTeacher ("Jane")
            let addTeacher = courseManager.AddTeacher teacher2
            
            Expect.isOk addTeacher "should be ok"
            let course = Course.MkCourse  ("Math", 10)
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher1 = courseManager.AddTeacherToCourse (teacher1.Id, course.Id)
            Expect.isOk assignTeacher1 "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher2 = courseManager.AddTeacherToCourse (teacher2.Id, course.Id)
            Expect.isOk assignTeacher2 "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let tryGetCourse = courseManager.GetCourse course.Id
            Expect.isError tryGetCourse "should be error"
            
            let retrievedTeacher1 = courseManager.GetTeacher teacher1.Id
            Expect.isOk retrievedTeacher1 "should be ok"
            let retrievedTeacher1 = retrievedTeacher1.OkValue
            Expect.equal retrievedTeacher1.Courses.Length 0 "should be equal"
            
            let retrievedTeacher2 = courseManager.GetTeacher teacher2.Id
            Expect.isOk retrievedTeacher2 "should be ok"
            let retrievedTeacher2 = retrievedTeacher2.OkValue
            Expect.equal retrievedTeacher2.Courses.Length 0 "should be equal"
            
        multipleTestCase "add more teacher to a course and also some students. Try to delete the course and will be unable at it - Error " instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            // given
            setUp ()            
            let teacher1 = Teacher.MkTeacher "John"
            let courseManager = courseManager ()
            let addTeacher1 = courseManager.AddTeacher teacher1
            Expect.isOk addTeacher1 "should be ok"
            let teacher2 = Teacher.MkTeacher "Jane"
            let addTeacher2 = courseManager.AddTeacher teacher2
            Expect.isOk addTeacher2 "should be ok"
            
            let student = Student.MkStudent ("Jack", 5)
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            
            let course = Course.MkCourse  ("Math", 10)
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let subscrbribeToCourse = courseManager.SubscribeStudentToCourse student.Id course.Id
            Expect.isOk subscrbribeToCourse "should be ok"
            
            let assignTeacher1 = courseManager.AddTeacherToCourse (teacher1.Id, course.Id)
            Expect.isOk assignTeacher1 "should be ok"
            
            let assignTeacher2 = courseManager.AddTeacherToCourse (teacher2.Id, course.Id)
            Expect.isOk assignTeacher2 "should be ok"
            
            
            Async.Sleep delay |> Async.RunSynchronously
            // when
            let deleteCourse = courseManager.DeleteCourse course.Id
            
            // then
            Expect.isError deleteCourse "should be error"
           
            Async.Sleep delay |> Async.RunSynchronously
            // and also 
            let tryGetCourse = courseManager.GetCourse course.Id
            Expect.isOk tryGetCourse "should be Ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedTeacher1 = courseManager.GetTeacher teacher1.Id
            Expect.isOk retrievedTeacher1 "should be ok"
            let retrievedTeacher1 = retrievedTeacher1.OkValue
            Expect.equal retrievedTeacher1.Courses.Length 1 "should be equal"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrievedTeacher2 = courseManager.GetTeacher teacher2.Id
            Expect.isOk retrievedTeacher2 "should be ok"
            let retrievedTeacher2 = retrievedTeacher2.OkValue
            Expect.equal retrievedTeacher2.Courses.Length 1 "should be equal"
            
            Async.Sleep delay |> Async.RunSynchronously
            let retrieveStudent = courseManager.GetStudent student.Id
            Expect.isOk retrieveStudent "should be ok"
            let retrieveStudent = retrieveStudent.OkValue
            Expect.equal retrieveStudent.Courses.Length 1 "should be equal"
       
        // starting deailing with cross aggregates invariants explicitly passed to command handler
        multipleTestCase "Teacher John don't want to teach Math if student Jack is enrolled in that course and in literature as well - Error" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            // given
            setUp ()
            let teacher = Teacher.MkTeacher "John"
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let math = Course.MkCourse  ("Math", 10)
            let addCourse = courseManager.AddCourse math
            Expect.isOk addCourse "should be ok"
            
            let literature = Course.MkCourse  ("Literature", 10)
            let addLiterature = courseManager.AddCourse literature
            Expect.isOk addLiterature "should be ok"
            
            let jack = Student.MkStudent ("Jack", 5)
            let addStudent = courseManager.AddStudent jack
            
            Async.Sleep delay |> Async.RunSynchronously
            let subscribeJackToMath = courseManager.SubscribeStudentToCourse jack.Id math.Id
            Expect.isOk subscribeJackToMath "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let subscribeJackToLiterature = courseManager.SubscribeStudentToCourse jack.Id literature.Id
            Expect.isOk subscribeJackToLiterature "should be ok"
            
            let crossAggregatesConstraint =
                fun (_: Teacher, _: Course) ->
                    result
                        {
                            let! (_, jack) = studentViewer jack.Id
                            return!
                                (not 
                                     (jack.Courses |> List.exists (fun c -> c = literature.Id)
                                      && (jack.Courses |> List.exists (fun c -> c = math.Id)))
                                )
                                |> Result.ofBool "constraint not met"
                            // return ()    
                        }
             
            // when 
            let assignTeacher = courseManager.AddTeacherToCourseConsideringIncompatibilities (teacher.Id, math.Id, crossAggregatesConstraint)
           
            // then 
            Expect.isError assignTeacher "should be error"
            
        fmultipleTestCase "Teacher John will be able to teach Math if student Jack is enrolled in that course and not in literature - Ok" instances <| fun (setUp, courseManager, courseViewer, studentViewer, delay) ->
            setUp ()
            Async.Sleep delay |> Async.RunSynchronously
            let teacher = Teacher.MkTeacher "John"
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let math = Course.MkCourse  ("Math", 10)
            Async.Sleep delay |> Async.RunSynchronously
            let addCourse = courseManager.AddCourse math
            Expect.isOk addCourse "should be ok"
            
            let literature = Course.MkCourse  ("Literature", 10)
            let addLiterature = courseManager.AddCourse literature
            Expect.isOk addLiterature "should be ok"
            
            Async.Sleep delay |> Async.RunSynchronously
            let jack = Student.MkStudent ("Jack", 5)
            let addStudent = courseManager.AddStudent jack
            
            Async.Sleep delay |> Async.RunSynchronously
            let subscribeJackToMath = courseManager.SubscribeStudentToCourse jack.Id math.Id
            Expect.isOk subscribeJackToMath "should be ok"
            
            //beware there is not literature subscribed
            
            let crossAggregatesConstraint =
                fun (_: Teacher, _: Course) ->
                    result
                        {
                            let! (_, jack) = studentViewer jack.Id
                            do!
                                (not 
                                     (jack.Courses |> List.exists (fun c -> c = literature.Id)
                                      && (jack.Courses |> List.exists (fun c -> c = math.Id)))
                                )
                                |> Result.ofBool "constraint not met"
                            return ()    
                        }
             
            Async.Sleep delay |> Async.RunSynchronously
            let assignTeacher = courseManager.AddTeacherToCourseConsideringIncompatibilities (teacher.Id, math.Id, crossAggregatesConstraint)
            
            Expect.isOk assignTeacher "should be Ok"
            
    ]
    |> testSequenced
    