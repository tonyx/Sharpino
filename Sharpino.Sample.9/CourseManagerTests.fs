module Sharpino.Sample._9.CourseManagerTests

open System
open Expecto
open ItemManager.Common
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open Sharpino.CommandHandler
open Sharpino.Sample._9.BalanceEvents
open Sharpino.Sample._9.Teacher
open Sharpino.Sample._9.TeacherEvents
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


let pgStorageStudentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore
let pgStorageCourseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let pgStorageBalanceViewer = getAggregateStorageFreshStateViewer<Balance, BalanceEvents, string> pgEventStore
let pgTeacherViewer = getAggregateStorageFreshStateViewer<Teacher, TeacherEvents, string> pgEventStore

let memoryStorageStudentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> memEventStore
let memoryStorageCourseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> memEventStore
let memoryStorageBalanceViewer = getAggregateStorageFreshStateViewer<Balance, BalanceEvents, string> memEventStore
let memoryStorageTeacherViewer = getAggregateStorageFreshStateViewer<Teacher, TeacherEvents, string> memEventStore

let instances =
    [
        (fun () -> setUp pgEventStore), fun () -> CourseManager (pgEventStore, pgStorageCourseViewer, pgStorageStudentViewer, pgStorageBalanceViewer, pgTeacherViewer, Balance.MkBalance 1000.0M)
        (fun () -> setUp memEventStore),  fun () ->CourseManager (memEventStore, memoryStorageCourseViewer, memoryStorageStudentViewer, memoryStorageBalanceViewer, memoryStorageTeacherViewer, Balance.MkBalance 1000.0M)
    ]
[<Tests>]
let tests =
    testList "CourseManagerTests" [
        multipleTestCase "check initial balance - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let courseManager = courseManager ()
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 1000.0M "should be equal"
            
        multipleTestCase "add and retrieve a student - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let student = Student.MkStudent ("John", 50)
            let courseManager = courseManager ()
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Id student.Id "should be equal"
            Expect.equal retrievedStudent.Name student.Name "should be equal"
      
        multipleTestCase "add a course will costs 100, verify it - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 900.0M "should be equal"
        
        multipleTestCase "add a course, which costs 100, then delete the course, witch costs 50 more. Verify the balance is decreased by 150 - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 900.0M "should be equal"
            
            let teacher = Teacher.MkTeacher ("John")
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            
            let assignTeacher = courseManager.AssignTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            let balance = courseManager.Balance 
            Expect.isOk balance "should be ok"
            let balance = balance.OkValue
            Expect.equal balance.Amount 850.0M "should be equal"
            
            let tryGetCourse = courseManager.GetCourse course.Id
            Expect.isError tryGetCourse "should be error"
            
        multipleTestCase "add and retrieve a course - Ok"  instances <| fun (setUp, courseManager) ->
            setUp ()
            let course = Course.MkCourse ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let result = courseManager.GetCourse course.Id
            Expect.isOk result "should be ok"
            let retrievedCourse = result.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
        multipleTestCase "add a student add a course and subscribe the student to that course;
            verify the subscriptions;
            verify that both course and student cannot be deleted - Ok" instances <| fun (setUp, courseManager) ->
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
            let subscribe = courseManager.SubscribeStudentToCourse student.Id course.Id
            Expect.isOk subscribe "should be ok"
            
            // then
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Courses.Length 1 "should be equal"
            
            let retrievedCourse = courseManager.GetCourse course.Id
            Expect.isOk retrievedCourse "should be ok"
            let retrievedCourse = retrievedCourse.OkValue
            Expect.equal retrievedCourse.Students.Length 1 "should be equal"
           
            // and also  
            let tryDeleteStudent = courseManager.DeleteStudent student.Id
            Expect.isError tryDeleteStudent "should be error"
            
            let tryDeleteCourse = courseManager.DeleteCourse course.Id
            Expect.isError tryDeleteCourse "should be error"
       
        multipleTestCase "if a students exceeds the max number of courses, the subscription fails - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            // given
            let student = Student.MkStudent ("John", 1)
            let courseManager = courseManager ()
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let course1 = Course.MkCourse  ("Math", 10)
            let addCourse1 = courseManager.AddCourse course1
            Expect.isOk addCourse1 "should be ok"
            let course2 = Course.MkCourse  ("Physics", 10)
            let addCourse2 = courseManager.AddCourse course2
            Expect.isOk addCourse2 "should be ok"
            
            // when
            let firstSubscription = courseManager.SubscribeStudentToCourse student.Id course1.Id
            Expect.isOk firstSubscription "should be ok"
            
            // then
            let secondSubscription = courseManager.SubscribeStudentToCourse student.Id course2.Id
            Expect.isError secondSubscription "should be error"
            
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
         
        multipleTestCase "add and delete a student - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let student = Student.MkStudent ("John", 5)
            let courseManager = courseManager ()
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Id student.Id "should be equal"
            Expect.equal retrievedStudent.Name student.Name "should be equal"
            
            let deleteStudent = courseManager.DeleteStudent student.Id
            Expect.isOk deleteStudent "should be ok"
            
            let tryGetStudent = courseManager.GetStudent student.Id
            Expect.isError tryGetStudent "should be error"
            
        multipleTestCase "add a course and assign a teacher to that course - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let result = courseManager.GetCourse course.Id
            Expect.isOk result "should be ok"
            let retrievedCourse = result.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
            let teacher = Teacher.MkTeacher ("John")
            let addTeacher = courseManager.AddTeacher teacher
            let assignTeacher = courseManager.AssignTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            let retrievedCourse = courseManager.GetCourse course.Id
            Expect.isOk retrievedCourse "should be ok"
            let retrievedCourse = retrievedCourse.OkValue
            Expect.equal retrievedCourse.Teacher.Value teacher.Id "should be equal"
            
            let retrievedTeacher = courseManager.GetTeacher teacher.Id
            Expect.isOk retrievedTeacher "should be ok"
            let retrievedTeacher = retrievedTeacher.OkValue
            Expect.equal retrievedTeacher.Courses.Length 1 "should be equal"
        
        multipleTestCase "can't delete a teacher when there are courses assigned to them - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let teacher = Teacher.MkTeacher ("John")
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let course = Course.MkCourse  ("Math", 10)
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let assignTeacher = courseManager.AssignTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            let deleteTeacher = courseManager.DeleteTeacher teacher.Id
            Expect.isError deleteTeacher "should be error"
        
        multipleTestCase "add and delete a techer - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()            
            let teacher = Teacher.MkTeacher ("John")
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let result = courseManager.GetTeacher teacher.Id
            Expect.isOk result "should be ok"
            let retrievedTeacher = result.OkValue
            Expect.equal retrievedTeacher.Id teacher.Id "should be equal"
            Expect.equal retrievedTeacher.Name teacher.Name "should be equal"
            
            let deleteTeacher = courseManager.DeleteTeacher teacher.Id
            Expect.isOk deleteTeacher "should be ok"
            
            let tryGetTeacher = courseManager.GetTeacher teacher.Id
            Expect.isError tryGetTeacher "should be error"
        
        multipleTestCase "add a teacher to a course, then delete that course, and the teacher courses list will be decreased by 1 - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()            
            let teacher = Teacher.MkTeacher ("John")
            let courseManager = courseManager ()
            let addTeacher = courseManager.AddTeacher teacher
            Expect.isOk addTeacher "should be ok"
            let course = Course.MkCourse  ("Math", 10)
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let assignTeacher = courseManager.AssignTeacherToCourse (teacher.Id, course.Id)
            Expect.isOk assignTeacher "should be ok"
            
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            
            let retrievedTeacher = courseManager.GetTeacher teacher.Id
            Expect.isOk retrievedTeacher "should be ok"
            let retrievedTeacher = retrievedTeacher.OkValue
            Expect.equal retrievedTeacher.Courses.Length 0 "should be equal"
            
        multipleTestCase "should be able to delete a course when there is no teacher assigned to it - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()            
            let course = Course.MkCourse  ("Math", 10)
            let courseManager = courseManager ()
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            
            let deleteCourse = courseManager.DeleteCourse course.Id
            Expect.isOk deleteCourse "should be ok"
            
            let tryGetCourse = courseManager.GetCourse course.Id
            Expect.isError tryGetCourse "should be error"
            
    ]
    |> testSequenced
    