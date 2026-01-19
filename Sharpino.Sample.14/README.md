## A copy of Sharpino Example 11 using "undoers"/copensator feature

In this example I will address Primitive obsession

It is worth targeting the Id disentangling from the actual Guid represenation using a wrapper for the id itself like follows:


```fsharp
    type StudentId =
        StudentId of Guid
        with
            static member New = StudentId (Guid.NewGuid())
            member this.Id =
                this |> fun (StudentId id) -> id
```

In this way any exposed service layer function that needs more than one parameter can enforce compile time parameters checking
In this particular example the "undoers" features will be explored

Now the model and the events and commands are able to use the wrapper type:

```fsharp
    type Student = {
        Id: StudentId
        Name: string
        Courses: List<CourseId>
        MaxNumberOfCourses: int
    }
    with
        static member MkStudent (name: string, maxNumberOfCourses: int) =
            { Id = StudentId.New; Name = name; Courses = List.empty; MaxNumberOfCourses = maxNumberOfCourses }
```

Note: the undoers are defined so that for the future an example of classic undoers to emit compensation events can be explored
