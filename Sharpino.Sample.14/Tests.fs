module Tests

open System
open System.Diagnostics
open System.Threading
open Expecto
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open Shaprino.Sample._14.StudentConsumer
open Sharpino
open Sharpino.Cache
open Sharpino.CommandHandler
open Sharpino.EventBroker
open Sharpino.RabbitMq
open Sharpino.Sample._14.Course
open Sharpino.Sample._14.CourseConsumer
open Sharpino.Sample._14.CourseEvents
open Sharpino.Sample._14.CourseManager

open DotNetEnv
open Sharpino.Sample._14.Details.Details
open Sharpino.Sample._14.Student
open Sharpino.Sample._14.StudentEvents
open Sharpino.Storage

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

Env.Load() |> ignore
let password = Environment.GetEnvironmentVariable("password")
let connection =
    "Server=127.0.0.1;"+
    "Database=sharpino_sample14;" +
    "User Id=safe;"+
    $"Password={password}"

let pgEventStore:IEventStore<string> = PgStorage.PgEventStore connection

let setUp () =
    pgEventStore.Reset Student.Version Student.StorageName
    pgEventStore.ResetAggregateStream Student.Version Student.StorageName
    pgEventStore.Reset Course.Version Course.StorageName
    pgEventStore.ResetAggregateStream Course.Version Course.StorageName
    AggregateCache3.Instance.Clear()
    DetailsCache.Instance.Clear()
    
let courseViewer = getAggregateStorageFreshStateViewer<Course, CourseEvents, string> pgEventStore
let studentViewer = getAggregateStorageFreshStateViewer<Student, StudentEvents, string> pgEventStore

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
          let courseRetrieved = courseManager.GetCourse course.Id
          let studentRetrieved = courseManager.GetStudent student.Id
          Expect.isOk courseRetrieved "Course not retrieved"
          Expect.isOk studentRetrieved "Student not retrieved"
          
       testCase "a student can enroll to only two courses, and they tries to enroll to the third one and it will be rejected - Ok" <| fun _ ->
          setUp ()
          let math = Course.MkCourse ("math", 10)
          let english = Course.MkCourse ("english", 10)
          let physics = Course.MkCourse ("physics", 10)
          let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
          Expect.isOk courseAdded "Courses not created"
          let student = Student.MkStudent ("Jack", 2)
          let addStudent = courseManager.AddStudent student
          Expect.isOk addStudent "Student not created"
          
          // when
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.Id math.Id
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          let enrollStudentToEnglish = courseManager.EnrollStudentToCourse student.Id english.Id
          Expect.isOk enrollStudentToEnglish "Student non enrolled to english"
          let enrollStudentToPhysics = courseManager.EnrollStudentToCourse student.Id physics.Id
          Expect.isError enrollStudentToPhysics "Student enrolled to physics"
          
          // then
          let retrieveMath = courseManager.GetCourse math.Id |> Result.get
          Expect.equal retrieveMath.Students.Length 1 "should be one"
          
          let retrieveEnglish = courseManager.GetCourse english.Id |> Result.get
          Expect.equal retrieveEnglish.Students.Length 1 "should be one"
          let retrievePhysics = courseManager.GetCourse physics.Id |> Result.get
          Expect.equal retrievePhysics.Students.Length 0 "should be zero"
          let retrieveStudent = courseManager.GetStudent student.Id |> Result.get
          Expect.equal retrieveStudent.Courses.Length 2 "should be two"
    
       testCase "enroll a student in some courses and retrieve the details of the student - Ok" <| fun _ ->
          // given
          setUp ()
          let math = Course.MkCourse ("math", 10)
          let english = Course.MkCourse ("english", 10)
          let physics = Course.MkCourse ("physics", 10)
          let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
          Expect.isOk courseAdded "Courses not created"
          let student = Student.MkStudent ("Jack", 3)
          let addStudent = courseManager.AddStudent student
          Expect.isOk addStudent "Student not created"
          
          // when
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.Id math.Id
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          let enrollStudentToEnglish = courseManager.EnrollStudentToCourse student.Id english.Id
          Expect.isOk enrollStudentToEnglish "Student non enrolled to english"
          let enrollStudentToPhysics = courseManager.EnrollStudentToCourse student.Id physics.Id
          Expect.isOk enrollStudentToPhysics "Student not enrolled to physics"
          
          // then
          let studentDetails = courseManager.GetStudentDetails student.Id
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 3 "Student courses not retrieved"
      
       testCase "enroll a student in a course, then retrieve the details, then enroll it in another course and finally refresh the details using warmup ()" <| fun _ ->
          setUp ()
          let math = Course.MkCourse ("math", 10)
          let english = Course.MkCourse ("english", 10)
          let physics = Course.MkCourse ("physics", 10)
          let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
          Expect.isOk courseAdded "Courses not created"
          let student = Student.MkStudent ("Jack", 3)
          let addStudent = courseManager.AddStudent student
          Expect.isOk addStudent "Student not created"
          
          // when
          let studentDetails = courseManager.GetStudentDetails student.Id
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 0 "Student courses not retrieved"
          
          // when
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.Id math.Id
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          
          // then
          
          let studentDetailsRefreshed = courseManager.GetStudentDetails student.Id
          Expect.isOk studentDetailsRefreshed "Student details not refreshed"
          Expect.equal studentDetailsRefreshed.OkValue.Student.Name "Jack" "Student name not refreshed"
          Expect.equal studentDetailsRefreshed.OkValue.Courses.Length 1 "Student courses not refreshed"
      
       testCase "enroll a student to a course,
                 then retrieve the details,
                 then change the student name and the existing instance retrieved from the cache is updated - Ok" <| fun _ ->
          setUp ()
          let math = Course.MkCourse ("math", 10)
          let english = Course.MkCourse ("english", 10)
          let physics = Course.MkCourse ("physics", 10)
          let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
          Expect.isOk courseAdded "Courses not created"
          let student = Student.MkStudent ("Jack", 3)
          let addStudent = courseManager.AddStudent student
          Expect.isOk addStudent "Student not created"
          
          // when
          let studentDetails = courseManager.GetStudentDetails student.Id
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 0 "Student courses not retrieved"
          
          let renameStudent = courseManager.RenameStudent (student.Id, "John")
          Expect.isOk renameStudent "Student not renamed"
          
          // then
          let retrieveDetailsAgain = courseManager.GetStudentDetails student.Id
          let studentDetailsValueAgain: StudentDetails = retrieveDetailsAgain.OkValue
          Expect.equal studentDetailsValueAgain.Student.Name "John" "Student name not retrieved"
       
       testCase "enroll a student to a course, then retrieve the details, then change the course name etc... - Ok" <| fun _ ->
          setUp ()
          let math = Course.MkCourse ("math", 10)
          let english = Course.MkCourse ("english", 10)
          let physics = Course.MkCourse ("physics", 10)
          let courseAdded = courseManager.AddMultipleCourses [|math; english; physics|]
          Expect.isOk courseAdded "Courses not created"
          let student = Student.MkStudent ("Jack", 3)
          let addStudent = courseManager.AddStudent student
          Expect.isOk addStudent "Student not created"
          
          // when
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.Id math.Id
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          
          let studentDetails = courseManager.GetStudentDetails student.Id
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 1 "Student courses not retrieved"
          
          let renameCourse = courseManager.RenameCourse (math.Id, "mathematics")
          Expect.isOk renameCourse "Course not renamed"
          
          let studentDetailsAgain = courseManager.GetStudentDetails student.Id
          let studentDetailsValueAgain: StudentDetails = studentDetailsAgain.OkValue
          Expect.equal studentDetailsValueAgain.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValueAgain.Courses.Length 1 "Student courses not retrieved"
          Expect.equal studentDetailsValueAgain.Courses.Head.Name "mathematics" "Course name not retrieved"
      
       testCase "create a course, get its details, rename the course and the details are refreshed - Ok" <| fun _ ->
          // given
          setUp ()
          let course = Course.MkCourse ("Math", 10)
          let courseAdded = courseManager.AddCourse course
          let (Ok courseDetail) = courseManager.GetCourseDetails course.Id
          
          Expect.equal courseDetail.Course.Name "Math" "should be ok"
          
          // when
          let renameCourse = courseManager.RenameCourse (course.Id, "Mathematics")
          Expect.isOk renameCourse "course not renamed"
          
          // then
          let (Ok courseDetailsRetrieved) = courseManager.GetCourseDetails course.Id
          Expect.equal courseDetailsRetrieved.Course.Name "Mathematics" "should be equal"
          
       testCase "create two courses, create a student, subscribe the student to both the courses, then
                 rename both the courses and verify that the student details update both the course names " <| fun _ ->
         // given
         setUp ()
         let math = Course.MkCourse ("Math", 10)
         let english = Course.MkCourse ("English", 10)
         let john = Student.MkStudent ("John", 3)
         let courseAdded = courseManager.AddMultipleCourses [|math; english|]
         let studentAdded = courseManager.AddStudent john
         let enrollStudentToMath = courseManager.EnrollStudentToCourse john.Id math.Id
         Expect.isOk enrollStudentToMath "Student not enrolled to math"
         let enrollStudentToEnglish = courseManager.EnrollStudentToCourse john.Id english.Id
         Expect.isOk enrollStudentToEnglish "Student not enrolled to english"
         let studentDetails = courseManager.GetStudentDetails john.Id
         let coursesNames = studentDetails.OkValue.Courses |> List.map (fun c -> c.Name)
         Expect.equal coursesNames ["Math"; "English"] "should be equal"
         
         // when
         let renameMath = courseManager.RenameCourse (math.Id, "Mathematics")
         let renameEnglish = courseManager.RenameCourse (english.Id, "English reading")
        
         // then 
         let studentDetailsAgain = courseManager.GetStudentDetails john.Id
         let coursesNamesAgain = studentDetailsAgain.OkValue.Courses |> List.map (fun c -> c.Name)
         Expect.equal coursesNamesAgain ["Mathematics"; "English reading"] "should be equal"
     
       testCase "create two courses, two students, subscribe both the students to both the courses, then
                 retrieve both the students details, and at the end rename both the courses and
                 finally retrieve again the student details varifying that for both the renaming of
                 the courses actuall happen" <| fun _ ->
         setUp ()
         let math = Course.MkCourse ("Math", 10)
         let english = Course.MkCourse ("English", 10)
         let john = Student.MkStudent ("John", 3)
         let jack = Student.MkStudent ("Jack", 3)
         
         let addJohn = courseManager.AddStudent john
         let addJack = courseManager.AddStudent jack
         let addMath = courseManager.AddCourse math
         let addEnglish = courseManager.AddCourse english
         
         let enrollJohnToMath = courseManager.EnrollStudentToCourse john.Id math.Id
         let enrollJohnToEnglish = courseManager.EnrollStudentToCourse john.Id english.Id
         let enrollJackToMath = courseManager.EnrollStudentToCourse jack.Id math.Id
         let enrollJackToEnglish = courseManager.EnrollStudentToCourse jack.Id english.Id
         
         let (Ok johnDetails) = courseManager.GetStudentDetails john.Id
         let (Ok jackDetails) = courseManager.GetStudentDetails jack.Id
         
         let johnCourses = johnDetails.Courses |> List.map (fun c -> c.Name)
         Expect.equal johnCourses ["Math"; "English"] "should be equal"
         
         let jackCourses = jackDetails.Courses |> List.map (fun c -> c.Name)
         Expect.equal jackCourses ["Math"; "English"] "should be equal"
       
         let renameMath = courseManager.RenameCourse (math.Id, "Mathematics")
         let renameEnglish = courseManager.RenameCourse (english.Id, "English reading")
         
         let (Ok johnDetails2) = courseManager.GetStudentDetails john.Id
         let (Ok jackDetails2) = courseManager.GetStudentDetails jack.Id
         
         let johnCourses2 = johnDetails2.Courses |> List.map (fun c -> c.Name)
         Expect.equal johnCourses2 ["Mathematics"; "English reading"] "should be equal"
         
         let jackCourses2 = jackDetails2.Courses |> List.map (fun c -> c.Name)
         Expect.equal jackCourses2 ["Mathematics"; "English reading"] "should be equal"
         
       testCase "add and remove a student, verify it cannot be retrieved anymore - Ok" <| fun _ ->
          setUp ()
          // given
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          Expect.isOk addJohn "john not added"
          
          // when
          let removeJohn = courseManager.DeleteStudent john.Id
          
          // then
          let retrieveJohn = courseManager.GetStudent john.Id
          Expect.isError retrieveJohn "john should not be retrieved"
          
       testCase "add a student, materialize a details of it, then remove the student, verify the details are not retrieved anymore - Ok"   <| fun _ ->
          setUp ()
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          let (Ok johnDetails) = courseManager.GetStudentDetails john.Id
          let removeJohn = courseManager.DeleteStudent john.Id
          let retrieveJohnDetails = courseManager.GetStudentDetails john.Id
          Expect.isError retrieveJohnDetails "john details should not be retrieved"
       
       testCase "when a student is deleted then they are unsubscribed from any course where they are enrolled - Ok " <| fun _ ->
          setUp ()
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          let math = Course.MkCourse ("Math", 10)
          let addMath = courseManager.AddCourse math
          let enrollJohnToMath = courseManager.EnrollStudentToCourse john.Id math.Id
          
          let removeJohn = courseManager.DeleteStudent john.Id
          let retrieveJohnDetails = courseManager.GetStudentDetails john.Id
          Expect.isError retrieveJohnDetails "john details should not be retrieved"
          
          let retrieveCourse = courseManager.GetCourse math.Id
          let courseDetails = retrieveCourse.OkValue
          let students = courseDetails.Students
          Expect.equal students [] "students should be empty"
          
       testCase "when a student is enrolled then the courseDetails should include that student, when the student is removed it will disappear - Ok " <| fun _ ->
          setUp ()
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          let math = Course.MkCourse ("Math", 10)
          let addMath = courseManager.AddCourse math
          let enrollJohnToMath = courseManager.EnrollStudentToCourse john.Id math.Id
          
          let (Ok courseDetails) = courseManager.GetCourseDetails math.Id
          
          let students = courseDetails.Students
          Expect.equal students.Length 1 "students enrolled should be len 1"
          
          let removeJohn = courseManager.DeleteStudent john.Id
          Expect.isOk removeJohn "john should be removed"
          
          let (Ok courseDetails2) = courseManager.GetCourse math.Id
          Expect.equal courseDetails2.Students.Length 0 "students enrolled should be len 0"
        
          
    ]
    |> testSequenced
    
    
    
