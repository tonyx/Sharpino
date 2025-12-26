module Tests

open System
open Expecto
open DotNetEnv
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.Storage
open Sharpino.Sample._15.Course
open Sharpino.Sample._15.CourseEvents
open Sharpino.Sample._15.Student
open Sharpino.Sample._15.StudentEvents
open Sharpino.Sample._15.Enrollment
open Sharpino.Sample._15.EnrollmentEvents
open Sharpino.Sample._15.CourseManager
open Sharpino.EventBroker
open Sharpino.Core
open Sharpino.Sample._15.Commons.Definitions
open FsToolkit.ErrorHandling

// Load environment variables from .env file
Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;" +
    "Database=sharpino_sample15;" +
    "User Id=safe;" +
    $"Password={password}"

let pgEventStore: IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName |> ignore
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName |> ignore
    pgEventStore.Reset Course.Version Course.StorageName |> ignore
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName |> ignore
    pgEventStore.Reset Enrollments.Version Enrollments.StorageName |> ignore
    pgEventStore.ResetAggregateStream Enrollments.Version Enrollments.StorageName |> ignore
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()

let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore
let enrollmentViewer = getAggregateStorageFreshStateViewer<Enrollments, EnrollmentEvents, string> pgEventStore

let courseManager = 
    CourseManager(
        pgEventStore, 
        MessageSenders.NoSender,
        courseViewer,
        studentViewer,
        enrollmentViewer
    )

[<Tests>]
let tests =
    testList "samples" [
        testCase "add a course and a student" <| fun _ ->
            setUp ()
            let course = Course.MkCourse "math" 10
            let student = Student.MkStudent "Jack" 3
            let courseCreated = courseManager.AddCourse course
            let studentCreated = courseManager.AddStudent student
            Expect.isTrue courseCreated.IsOk "Course not created"
            Expect.isTrue studentCreated.IsOk "student not created"

        testCaseAsync "enroll a student to a course twice should fail" <| async {
            setUp()
            let course = Course.MkCourse "Math" 10
            let student = Student.MkStudent "John" 3
            let courseCreated = courseManager.AddCourse course
            let studentCreated = courseManager.AddStudent student

            Expect.isOk courseCreated "Course creation failed"
            Expect.isOk studentCreated "Student creation failed"

            let firstEnrollment = courseManager.CreateEnrollment student.Id course.Id
            Expect.isOk firstEnrollment "First enrollment failed"

            let secondEnrollment = courseManager.CreateEnrollment student.Id course.Id
            Expect.isError secondEnrollment "Second enrollment should have failed"
        }

        testCaseAsync "get non-refreshable student details" <| async {
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddCourse course2
            let _ = courseManager.AddStudent student

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let details = courseManager.GetNonRefreshableStudentDetails student.Id
            Expect.isOk details "Could not get student details"

            let studentDetails = details.OkValue
            Expect.equal studentDetails.Student student "Student should be the same"
            Expect.hasLength studentDetails.EnrolledInCourses 2 "Expected two courses"
            Expect.contains studentDetails.EnrolledInCourses course1 "Student should be enrolled in Math"
            Expect.contains studentDetails.EnrolledInCourses course2 "Student should be enrolled in Science"
        }
        
        testCase "get student details" <| fun _ -> 
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddCourse course2
            let _ = courseManager.AddStudent student

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let details = courseManager.GetStudentDetails student.Id
            Expect.isOk details "Could not get student details"

            let studentDetails = details.OkValue
            Expect.equal studentDetails.Student student "Student should be the same"
            Expect.hasLength studentDetails.EnrolledInCourses 2 "Expected two courses"
            Expect.contains studentDetails.EnrolledInCourses course1 "Student should be enrolled in Math"
            Expect.contains studentDetails.EnrolledInCourses course2 "Student should be enrolled in Science"
        
        testCase "enroll a student in a course then get the studentDetails, then rename the course
                then get the student detail again and the course results renamed" <| fun _ ->
            setUp ()
            let course1 = Course.MkCourse "Math" 10
            let student = Student.MkStudent "Science" 10
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddStudent student
            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let details = courseManager.GetStudentDetails student.Id
            let _ = courseManager.RenameCourse course1.Id "Mathematics"
            let details2 = courseManager.GetStudentDetails student.Id
            Expect.isOk details "Could not get student details"
            Expect.isOk details2 "Could not get student details"
            
        testCase "enroll a student in a course and then get the studentDetails,
            then enroll the student in another course and get the studentDetails again" <| fun _ ->
            setUp ()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "English" 10
            let student  = Student.MkStudent "John" 10
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddCourse course2
            let _ = courseManager.AddStudent student
            
            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let details = courseManager.GetStudentDetails student.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id
            let details2 = courseManager.GetStudentDetails student.Id
            Expect.equal details.OkValue.EnrolledInCourses.Length 1 "Expected one course"
            Expect.equal details2.OkValue.EnrolledInCourses.Length 2 "Expected two courses"
        
        testCase "enroll a student in a course and then get the studentDetails
                then enroll the student in another course and rename that course and get the student
                details again. Verify that the course name is updated" <| fun _ ->
            setUp ()
            let course1 = Course.MkCourse "English" 10
            let course2 = Course.MkCourse "Math" 10
            let student = Student.MkStudent "John" 10
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddCourse course2
            let _ = courseManager.AddStudent student
            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let details = courseManager.GetStudentDetails student.Id
            Expect.isOk details "Could not get student details"
            let _ = courseManager.CreateEnrollment student.Id course2.Id
            let _ = courseManager.RenameCourse course2.Id "Mathematics"
            let details2 = courseManager.GetStudentDetails student.Id
            
            Expect.equal details.OkValue.EnrolledInCourses.Length 1 "Expected one course"
            Expect.equal details2.OkValue.EnrolledInCourses.Length 2 "Expected two courses"
            Expect.equal (details2.OkValue.EnrolledInCourses |> Array.tryFind (fun c -> c.Id = course2.Id)).Value.Name "Mathematics" "Expected course name to be updated"

        testCaseAsync "get courses for a student" <| async {
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddCourse course2
            let _ = courseManager.AddStudent student

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let courses = courseManager.GetCoursesForStudent student.Id
            Expect.isOk courses "Could not get courses for student"

            let coursesList = courses.OkValue
            Expect.hasLength coursesList 2 "Expected two courses"
            Expect.contains coursesList course1 "Student should be enrolled in Math"
            Expect.contains coursesList course2 "Student should be enrolled in Science"
        }
        
        testCase "get courses for a student, async version" <| fun _ ->
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let _ =
                courseManager.AddCourseAsync course1
                |> Async.AwaitTask
                |> Async.RunSynchronously
            let _ =
                courseManager.AddCourseAsync course2
                |> Async.AwaitTask
                |> Async.RunSynchronously
            let _ =
                courseManager.AddStudentAsync student
                |> Async.AwaitTask
                |> Async.RunSynchronously

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let courses = courseManager.GetCoursesForStudent student.Id
            Expect.isOk courses "Could not get courses for student"

            let coursesList = courses.OkValue
            Expect.hasLength coursesList 2 "Expected two courses"
            Expect.contains coursesList course1 "Student should be enrolled in Math"
            Expect.contains coursesList course2 "Student should be enrolled in Science"
        
        testCaseAsync "get courses for a student, async version 2" <| async {
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let! _ =
                courseManager.AddCourseAsync course1
                |> Async.AwaitTask
            let! _ =
                courseManager.AddCourseAsync course2
                |> Async.AwaitTask
            let! _ =
                courseManager.AddStudentAsync student
                |> Async.AwaitTask

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let courses = courseManager.GetCoursesForStudent student.Id
            Expect.isOk courses "Could not get courses for student"

            let coursesList = courses.OkValue
            Expect.hasLength coursesList 2 "Expected two courses"
            Expect.contains coursesList course1 "Student should be enrolled in Math"
            Expect.contains coursesList course2 "Student should be enrolled in Science"
        }
        
        testCaseAsync "get courses for a student, async version 3" <| async {
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let _ =
                courseManager.AddCoursesAsync [|course1; course2|]
                |> Async.AwaitTask
                
            let! _ =
                courseManager.AddStudentAsync student
                |> Async.AwaitTask

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let courses = courseManager.GetCoursesForStudent student.Id
            Expect.isOk courses "Could not get courses for student"

            let coursesList = courses.OkValue
            Expect.hasLength coursesList 2 "Expected two courses"
            Expect.contains coursesList course1 "Student should be enrolled in Math"
            Expect.contains coursesList course2 "Student should be enrolled in Science"
        }

        testCaseAsync "get students enrolled in a course" <| async {
            setUp()
            let course = Course.MkCourse "Math" 10
            let student1 = Student.MkStudent "John" 3
            let student2 = Student.MkStudent "Jane" 3
            let _ = courseManager.AddCourse course
            let _ = courseManager.AddStudent student1
            let _ = courseManager.AddStudent student2

            let _ = courseManager.CreateEnrollment student1.Id course.Id
            let _ = courseManager.CreateEnrollment student2.Id course.Id

            let students = courseManager.GetStudentsEnrolledInACourse course.Id
            Expect.isOk students "Could not get students for course"

            let studentsList = students.OkValue
            Expect.hasLength studentsList 2 "Expected two students"
            Expect.contains studentsList student1 "Student John should be enrolled"
            Expect.contains studentsList student2 "Student Jane should be enrolled"
        }

        testCaseAsync "get enrollments for a course" <| async {
            setUp()
            let course = Course.MkCourse "Math" 10
            let student1 = Student.MkStudent "John" 3
            let student2 = Student.MkStudent "Jane" 3
            let _ = courseManager.AddCourse course
            let _ = courseManager.AddStudent student1
            let _ = courseManager.AddStudent student2

            let _ = courseManager.CreateEnrollment student1.Id course.Id
            let _ = courseManager.CreateEnrollment student2.Id course.Id

            let enrollments = courseManager.GetEnrollmentsForCourse course.Id
            Expect.isOk enrollments "Could not get enrollments for course"

            let enrollmentsList = enrollments.OkValue
            Expect.hasLength enrollmentsList 2 "Expected two enrollments"
        }

        testCaseAsync "get enrollments for a student" <| async {
            setUp()
            let course1 = Course.MkCourse "Math" 10
            let course2 = Course.MkCourse "Science" 10
            let student = Student.MkStudent "John" 3
            let _ = courseManager.AddCourse course1
            let _ = courseManager.AddCourse course2
            let _ = courseManager.AddStudent student

            let _ = courseManager.CreateEnrollment student.Id course1.Id
            let _ = courseManager.CreateEnrollment student.Id course2.Id

            let enrollments = courseManager.GetEnrollmentsForStudent student.Id
            Expect.isOk enrollments "Could not get enrollments for student"

            let enrollmentsList = enrollments.OkValue
            Expect.hasLength enrollmentsList 2 "Expected two enrollments"
        }

        testCase "enroll a student to a course" <| fun _ ->
            setUp()
            let course = Course.MkCourse "Math" 10
            let student = Student.MkStudent"John" 3
            let courseCreated = courseManager.AddCourse course
            let studentCreated = courseManager.AddStudent student
            
            Expect.isOk courseCreated "Course creation failed"
            Expect.isOk studentCreated "Student creation failed"

            let enrollmentResult = courseManager.CreateEnrollment student.Id course.Id
            Expect.isOk enrollmentResult "Enrollment failed"

            let enrollments = courseManager.GetEnrollments()
            Expect.isOk enrollments "Could not get enrollments"
            
            let enrollmentsList = enrollments.OkValue.Enrollments
            Expect.hasLength enrollmentsList 1 "Enrollment was not recorded"
            let recordedEnrollment = List.head enrollmentsList
            Expect.equal recordedEnrollment.StudentId student.Id "Student ID does not match"
            Expect.equal recordedEnrollment.CourseId course.Id "Course ID does not match"
    ]
    |> testSequenced
