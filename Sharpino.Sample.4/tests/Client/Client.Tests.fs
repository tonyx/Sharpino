module Client.Tests

open Fable.Mocha

open Index
open Shared

let client =

    // todo adapt to new model
    testList "Client" [
        ptestCase "Added todo"
        <| fun _ ->
            // let newTodo = Todo.create "new todo"
            // let model, _ = init ()
            //
            // let model, _ = update (AddedTodo newTodo) model
            //
            // Expect.equal model.Todos.Length 1 "There should be 1 todo"
            // Expect.equal model.Todos.[0] newTodo "Todo should equal new todo"
            Expect.equal 1 1 "should be equal"
    ]

let all =
    testList "All" [
#if FABLE_COMPILER // This preprocessor directive makes editor happy
        Shared.Tests.shared
#endif
        client
    ]

[<EntryPoint>]
let main _ = Mocha.runTests all