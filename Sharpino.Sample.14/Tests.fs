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
          let courseRetrieved = courseManager.GetCourse course.CourseId
          let studentRetrieved = courseManager.GetStudent student.StudentId
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
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.StudentId math.CourseId
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          let enrollStudentToEnglish = courseManager.EnrollStudentToCourse student.StudentId english.CourseId
          Expect.isOk enrollStudentToEnglish "Student non enrolled to english"
          let enrollStudentToPhysics = courseManager.EnrollStudentToCourse student.StudentId physics.CourseId
          Expect.isError enrollStudentToPhysics "Student enrolled to physics"
          
          // then
          let retrieveMath = courseManager.GetCourse math.CourseId |> Result.get
          Expect.equal retrieveMath.Students.Length 1 "should be one"
          
          let retrieveEnglish = courseManager.GetCourse english.CourseId |> Result.get
          Expect.equal retrieveEnglish.Students.Length 1 "should be one"
          let retrievePhysics = courseManager.GetCourse physics.CourseId |> Result.get
          Expect.equal retrievePhysics.Students.Length 0 "should be zero"
          let retrieveStudent = courseManager.GetStudent student.StudentId |> Result.get
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
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.StudentId math.CourseId
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          let enrollStudentToEnglish = courseManager.EnrollStudentToCourse student.StudentId english.CourseId
          Expect.isOk enrollStudentToEnglish "Student non enrolled to english"
          let enrollStudentToPhysics = courseManager.EnrollStudentToCourse student.StudentId physics.CourseId
          Expect.isOk enrollStudentToPhysics "Student not enrolled to physics"
          
          // then
          let studentDetails = courseManager.GetStudentDetails student.StudentId
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
          let studentDetails = courseManager.GetStudentDetails student.StudentId
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 0 "Student courses not retrieved"
          
          // when
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.StudentId math.CourseId
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          
          // then
          
          let studentDetailsRefreshed = courseManager.GetStudentDetails student.StudentId
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
          let studentDetails = courseManager.GetStudentDetails student.StudentId
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 0 "Student courses not retrieved"
          
          let renameStudent = courseManager.RenameStudent (student.StudentId, "John")
          Expect.isOk renameStudent "Student not renamed"
          
          // then
          let retrieveDetailsAgain = courseManager.GetStudentDetails student.StudentId
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
          let enrollStudentToMath = courseManager.EnrollStudentToCourse student.StudentId math.CourseId
          Expect.isOk enrollStudentToMath "Student not enrolled to math"
          
          let studentDetails = courseManager.GetStudentDetails student.StudentId
          Expect.isOk studentDetails "Student details not retrieved"
          let studentDetailsValue: StudentDetails = studentDetails.OkValue
          Expect.equal studentDetailsValue.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValue.Courses.Length 1 "Student courses not retrieved"
          
          let renameCourse = courseManager.RenameCourse (math.CourseId, "mathematics")
          Expect.isOk renameCourse "Course not renamed"
          
          let studentDetailsAgain = courseManager.GetStudentDetails student.StudentId
          let studentDetailsValueAgain: StudentDetails = studentDetailsAgain.OkValue
          Expect.equal studentDetailsValueAgain.Student.Name "Jack" "Student name not retrieved"
          Expect.equal studentDetailsValueAgain.Courses.Length 1 "Student courses not retrieved"
          Expect.equal studentDetailsValueAgain.Courses.Head.Name "mathematics" "Course name not retrieved"
      
       testCase "create a course, get its details, rename the course and the details are refreshed - Ok" <| fun _ ->
          // given
          setUp ()
          let course = Course.MkCourse ("Math", 10)
          let courseAdded = courseManager.AddCourse course
          let (Ok courseDetail) = courseManager.GetCourseDetails course.CourseId

          Expect.equal courseDetail.Course.Name "Math" "should be ok"
          
          // when
          let renameCourse = courseManager.RenameCourse (course.CourseId, "Mathematics")
          Expect.isOk renameCourse "course not renamed"
          
          // then
          let (Ok courseDetailsRetrieved) = courseManager.GetCourseDetails course.CourseId
          Expect.equal courseDetailsRetrieved.Course.Name "Mathematics" "should be equal"
       
       testCase "create two courses,
                  create a student subscribe the student to one course, then retrieve
                  the student details and then subscribe to the second course. The details should
                  be updated"  <| fun _ ->
            
         setUp ()
         let math = Course.MkCourse ("Math", 10)
         let english = Course.MkCourse ("English", 10)
         let john = Student.MkStudent ("John", 3)
         let courseAdded = courseManager.AddMultipleCourses [|math; english|]
         let studentAdded = courseManager.AddStudent john
         let enrollStudentToMath = courseManager.EnrollStudentToCourse john.StudentId math.CourseId
         Expect.isOk enrollStudentToMath "Student not enrolled to math"
         let studentDetails = courseManager.GetStudentDetails john.StudentId
         Expect.isOk studentDetails "Student details not retrieved"
         let studentDetailsValue: StudentDetails = studentDetails.OkValue
         Expect.equal studentDetailsValue.Student.Name "John" "Student name not retrieved"
         Expect.equal studentDetailsValue.Courses.Length 1 "Student courses not retrieved"
         
         let enrollStudentToEnglish = courseManager.EnrollStudentToCourse john.StudentId english.CourseId
         Expect.isOk enrollStudentToEnglish "Student not enrolled to english"
         let studentDetailsAgain = courseManager.GetStudentDetails john.StudentId
         let studentDetailsValueAgain: StudentDetails = studentDetailsAgain.OkValue
         Expect.equal studentDetailsValueAgain.Student.Name "John" "Student name not retrieved"
         Expect.equal studentDetailsValueAgain.Courses.Length 2 "Student courses not retrieved"
         
       testCase "create two courses, create a student, subscribe the student to both the courses, then
                 rename both the courses and verify that the student details update both the course names " <| fun _ ->
         // given
         setUp ()
         let math = Course.MkCourse ("Math", 10)
         let english = Course.MkCourse ("English", 10)
         let john = Student.MkStudent ("John", 3)
         let courseAdded = courseManager.AddMultipleCourses [|math; english|]
         let studentAdded = courseManager.AddStudent john
         let enrollStudentToMath = courseManager.EnrollStudentToCourse john.StudentId math.CourseId
         Expect.isOk enrollStudentToMath "Student not enrolled to math"
         let enrollStudentToEnglish = courseManager.EnrollStudentToCourse john.StudentId english.CourseId
         Expect.isOk enrollStudentToEnglish "Student not enrolled to english"
         let studentDetails = courseManager.GetStudentDetails john.StudentId
         let coursesNames = studentDetails.OkValue.Courses |> List.map (fun c -> c.Name)
         Expect.equal coursesNames ["Math"; "English"] "should be equal"
         
         // when
         let renameMath = courseManager.RenameCourse (math.CourseId, "Mathematics")
         let renameEnglish = courseManager.RenameCourse (english.CourseId, "English reading")
        
         // then 
         let studentDetailsAgain = courseManager.GetStudentDetails john.StudentId
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
         
         let enrollJohnToMath = courseManager.EnrollStudentToCourse john.StudentId math.CourseId
         let enrollJohnToEnglish = courseManager.EnrollStudentToCourse john.StudentId english.CourseId
         let enrollJackToMath = courseManager.EnrollStudentToCourse jack.StudentId math.CourseId
         let enrollJackToEnglish = courseManager.EnrollStudentToCourse jack.StudentId english.CourseId

         let (Ok johnDetails) = courseManager.GetStudentDetails john.StudentId
         let (Ok jackDetails) = courseManager.GetStudentDetails jack.StudentId

         let johnCourses = johnDetails.Courses |> List.map (fun c -> c.Name)
         Expect.equal johnCourses ["Math"; "English"] "should be equal"
         
         let jackCourses = jackDetails.Courses |> List.map (fun c -> c.Name)
         Expect.equal jackCourses ["Math"; "English"] "should be equal"
       
         let renameMath = courseManager.RenameCourse (math.CourseId, "Mathematics")
         let renameEnglish = courseManager.RenameCourse (english.CourseId, "English reading")
         
         let (Ok johnDetails2) = courseManager.GetStudentDetails john.StudentId
         let (Ok jackDetails2) = courseManager.GetStudentDetails jack.StudentId

         let johnCourses2 = johnDetails2.Courses |> List.map (fun c -> c.Name)
         Expect.equal johnCourses2 ["Mathematics"; "English reading"] "should be equal"
         
         let jackCourses2 = jackDetails2.Courses |> List.map (fun c -> c.Name)
         Expect.equal jackCourses2 ["Mathematics"; "English reading"] "should be equal"
         
       testCase "add and remove a student using details, verify that it is not available in the detailcache anymore - Ok" <| fun _ ->
          setUp ()
          // given
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          Expect.isOk addJohn "john not added"
          
          // when
          let removeJohn = courseManager.DeleteStudent john.StudentId

          // then
          let retrieveJohn = courseManager.GetStudent john.StudentId
          Expect.isError retrieveJohn "john should not be retrieved"
          
       testCase "add a student, materialize a details of it, then remove the student, verify the details are not retrieved anymore - Ok"   <| fun _ ->
          setUp ()
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          let (Ok johnDetails) = courseManager.GetStudentDetails john.StudentId
          let removeJohn = courseManager.DeleteStudent john.StudentId
          let retrieveJohnDetails = courseManager.GetStudentDetails john.StudentId
          Expect.isError retrieveJohnDetails "john details should not be retrieved"
       
       testCase "when a student is deleted then they are unsubscribed from any course where they are enrolled - Ok " <| fun _ ->
          setUp ()
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          let math = Course.MkCourse ("Math", 10)
          let addMath = courseManager.AddCourse math
          let enrollJohnToMath = courseManager.EnrollStudentToCourse john.StudentId math.CourseId

          let removeJohn = courseManager.DeleteStudent john.StudentId
          let retrieveJohnDetails = courseManager.GetStudentDetails john.StudentId
          Expect.isError retrieveJohnDetails "john details should not be retrieved"
          
          let retrieveCourse = courseManager.GetCourse math.CourseId
          let courseDetails = retrieveCourse.OkValue
          let students = courseDetails.Students
          Expect.equal students [] "students should be empty"
          
       testCase "when a student is enrolled then the courseDetails should include that student, when the student is removed it will disappear - Ok " <| fun _ ->
          setUp ()
          let john = Student.MkStudent ("John", 3)
          let addJohn = courseManager.AddStudent john
          let math = Course.MkCourse ("Math", 10)
          let addMath = courseManager.AddCourse math
          let enrollJohnToMath = courseManager.EnrollStudentToCourse john.StudentId math.CourseId

          let (Ok courseDetails) = courseManager.GetCourseDetails math.CourseId

          let students = courseDetails.Students
          Expect.equal students.Length 1 "students enrolled should be len 1"
          
          let removeJohn = courseManager.DeleteStudent john.StudentId
          Expect.isOk removeJohn "john should be removed"
          
          let (Ok courseDetails2) = courseManager.GetCourse math.CourseId
          Expect.equal courseDetails2.Students.Length 0 "students enrolled should be len 0"
        
          
    ]
    |> testSequenced
    
    
    
