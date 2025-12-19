# CACHING\_DETAILS\_VIEW

## Background
the "aggregates" (event sourced objects) are in general cached.
Any application mainly needs composed views of multiple objects. Usually those composed objects are called
_details_. 
Even though the details benefits from the fact that the single objects are cached, they can be benefits from 
being cached as well.
This is a reason for it:

Let's say that the StudentDetails expand the view of the Student object (which is event sourced):

```fsharp
    type Student =
        {
            Id: StudentId
            Name: string
            Courses: List<CourseId>
            MaxNumberOfCourses: int
        }

```

To be able to expand the dependencies so that the Course is fully expanded the StudentDetails will need to include the list of Courses

```fsharp
    type StudentDetails =
        {
            Student: Student
            Courses: List<Course>
        }

```

The code to generate the detailsView can be like

```fsharp
    let getStudentDetails =
        result {
            let! student = this.GetStudent id
            let! courses = this.GetCourses student.Courses
            return { Student = student; Courses = courses }
        }

```
The computation is repeated each time the detailsView is retrieved whereas it is logical to assume 
that as long as the course itself and any related course are not changed the detailsView should not be changed.

Because of this principle the detailsView can be cached implementing the Refreshable interface to take care of
updating the detailsView when the related objects are changed.



## Purpose
Caching of detailsView and make it possible that the related objects can trigger ther refresh of any details which contains that object.

Describe how detail views participate in the details cache and how they declare which single-object ids should trigger an automatic refresh when those objects emit commands or change.
The current version will force the application to take care of some extra code needed to populate dependencies between
objects id and any cached detailsView so that generating and storing new events will be able to trigger a refresh of the relevant detailsView.

## Example:
Consider the structure of a course which is
```fsharp
    type Course =
        {
            Name: string
            Id: CourseId
            Students: List<StudentId>
            MaxNumberOfStudents: int
        }

```

The related details allows to expand to the value of dependent objects like the students, so that
the information for each student (like the Name) will be available

```fsharp
    type StudentDetails =
        {
            Student: Student
            Courses: List<Course>
        }

```

The previous definition misses an extra field that encapsulates a function able 
to refresh the content of the other fields.

```fsharp
    type StudentDetails =
        {
            Student: Student
            Courses: List<Course>
            Refresher: unit -> Result<Student * List<Course>, string>
        }

```

By implementing the Refreshable interface it can invoke the Refresher to return an up to date version of the detail object:
```fsharp
    type StudentDetails =
        {
            Student: Student
            Courses: List<Course>
            Refresher: unit -> Result<Student * List<Course>, string>
        }
            member this.Refresh () =
                result {
                    let! student, courses = this.Refresher ()
                    return { this with Student = student; Courses = courses }
                }
           
            interface Refreshable<StudentDetails> with
                member this.Refresh () =
                    this.Refresh ()

```

The application code will need some extra code to populate the dependencies between objects id and any cached detailsView so that generating and storing new events will be able to trigger a refresh of the relevant detailsView.
The type of code needed is at the moment verbose, and a simplified version may be available in the future

```fsharp

        member this.GetCourseDetails (id: CourseId) =
            let detailsBuilder =
                fun () ->
                    let refresher =
                        fun () ->
                            result {
                                let! course = this.GetCourse id
                                let! students = this.GetStudents course.Students
                                return course, students
                            }
                    result {
                        let! course, students = refresher ()
                        return
                            (
                                {
                                    Course = course
                                    Students = students
                                    Refresher = refresher
                                } :> Refreshable<_>
                                ,
                                id.Id:: (students |> List.map _.Id.Id)
                            )
                    }
            let key = DetailsCacheKey (typeof<CourseDetails>, id.Id)
            StateView.getRefreshableDetails<CourseDetails> detailsBuilder key
```
the detailsBuilder contains a pair of the actual details and the list of ids that should trigger a refresh.
The stateView will be able to use memoization to store the details and to populate the list of
dependencies between the object that will build the detail state (ids of the couse itself and the students).

Thi implementations of the getRefreshableDetails:
```fsharp
    let inline getRefreshableDetails<'A>
        (refreshableDetails: unit -> Result<Refreshable<'A> * List<Guid>, string>)
        (key: DetailsCacheKey) =
        
        let result = DetailsCache.Instance.Memoize refreshableDetails key
        match result with
        | Error e -> Error e
        | Ok res -> Ok (res :?> 'A)
```

## Requirements
- Every detail view that can be cached must implement the `Refreshable` interface.
- By sending the refreshable details to the cache the application code will be able to populate the dependencies between objects id and any cached detailsView so that generating and storing new events will be able to trigger a refresh of the relevant detailsView.
- Declarations must include all the ids relevant to keep the refresh in sync

## How to not use the 'Refreshable interface
- Avoding using the refreshable version of details means thet those details are computed each time they are retrieved. They will benefit from the fact that the objects that it uses are cached anyway (but the computation needed to arrange those object as in List.TraverseResultM... is repeated each time the details is retrieved if it skip the Refeshable<_> mechanism.

## Issues
There is no a strict policy related to keeping the details cache element in
sync with the objectId->List<DetailsKey> map
However they have both a built in expiration time.
