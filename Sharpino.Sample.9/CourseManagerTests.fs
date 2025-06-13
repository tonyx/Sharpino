module Sharpino.Sample._9.CourseManagerTests

open System
open Expecto
open ItemManager.Common
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open Sharpino.CommandHandler
open Sharpino.TestUtils
open FsToolkit.ErrorHandling

open Sharpino.Sample._9.Course
open Sharpino.Sample._9.CourseEvents
open Sharpino.Sample._9.CourseCommands

open Sharpino.Sample._9.Student
open Sharpino.Sample._9.StudentEvents
open Sharpino.Sample._9.StudentCommands
open Sharpino.Sample._9.CourseManager


let pgStorageStudentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore
let pgStorageCourseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore

let memoryStorageStudentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> memEventStore
let memoryStorageCourseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> memEventStore

let instances =
    [
        (fun () -> setUp(pgEventStore)), CourseManager(pgEventStore, pgStorageCourseViewer, pgStorageStudentViewer)
        (fun () -> setUp(memEventStore)),  CourseManager(memEventStore, memoryStorageCourseViewer, memoryStorageStudentViewer)
    ]
[<Tests>]
let tests =
    testList "CourseManagerTests" [
        multipleTestCase "add and retrieve a student - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let student = Student.MkStudent (Guid.NewGuid(), "John")
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Id student.Id "should be equal"
            Expect.equal retrievedStudent.Name student.Name "should be equal"
        
        multipleTestCase "add and retrieve a course - Ok"  instances <| fun (setUp, courseManager) ->
            setUp ()
            let course = Course.MkCourse (Guid.NewGuid(), "Math")
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let result = courseManager.GetCourse course.Id
            Expect.isOk result "should be ok"
            let retrievedCourse = result.OkValue
            Expect.equal retrievedCourse.Id course.Id "should be equal"
            Expect.equal retrievedCourse.Name course.Name "should be equal"
            
        multipleTestCase "add a student add a course and subscribe the student to that course - Ok" instances <| fun (setUp, courseManager) ->
            setUp ()
            let student = Student.MkStudent (Guid.NewGuid(), "John")
            let addStudent = courseManager.AddStudent student
            Expect.isOk addStudent "should be ok"
            let course = Course.MkCourse (Guid.NewGuid(), "Math")
            let addCourse = courseManager.AddCourse course
            Expect.isOk addCourse "should be ok"
            let subscribe = courseManager.SubscribeStudentToCourse student.Id course.Id
            Expect.isOk subscribe "should be ok"
            let result = courseManager.GetStudent student.Id
            Expect.isOk result "should be ok"
            let retrievedStudent = result.OkValue
            Expect.equal retrievedStudent.Courses.Length 1 "should be equal"
            
            let retrievedCourse = courseManager.GetCourse course.Id
            Expect.isOk retrievedCourse "should be ok"
            let retrievedCourse = retrievedCourse.OkValue
            Expect.equal retrievedCourse.Students.Length 1 "should be equal"
            
            let tryDeleteStudent = courseManager.DeleteStudent student.Id
            Expect.isError tryDeleteStudent "should be error"
            
            let tryDeleteCourse = courseManager.DeleteCourse course.Id
            Expect.isError tryDeleteCourse "should be error"
    ]
    