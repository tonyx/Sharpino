module Index

open Sharpino.Sample.Shared.Entities
open Sharpino.Sample.Shared.Service
open Sharpino.Sample.Shared

open Elmish
open Fable.Remoting
open Fable.Remoting.Client
open Fable.React
open Fable.Core
open Fable.React.Props
open Fable.Core.JsInterop
open Fable
open System
open System.Reflection.Emit
open System.Reflection
open Feliz
open Feliz.Bulma

type Window =
    // function description
    abstract alert: ?message: string -> unit
let [<Global>] window: Window = jsNative

let mutable myAlert = fun x -> window.alert x    

type State =
    {
        Todos: List<Todo>
        Categories: List<Category> list
        Tags: List<Tag>
        NewTodo: Todo
        NewCategory: Category
        NewTag: Tag
        TodoNameInput: String
    }
and Msg =
    | AddTodo
    | AddCategory
    | AddTag
    | SetTodoInput of string
    | RemoveTodo of Guid
    | RemoveCategory of Guid
    | RemoveTag of Guid
    | UpdateTodo of Todo
    | UpdateCategory of Category
    | UpdateTag of Tag
    | AddedTodo of Result<unit, string>
    | AddedCategory of Result<unit, string>
    | AddedTag of Result<unit, string>
    | RemovedTodo of Result<unit, string>
    | RemovedCategory of Result<unit, string>
    | RemovedTag of Result<unit, string>
    | GetTodos of Result<List<Todo>, string>

let todosApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withBaseUrl "/"
    |> Remoting.buildProxy<ITodosApi>

let init (): State * Cmd<Msg> =
    let state =
        {
            Todos = []
            Categories = []
            Tags = []
            NewTodo = { Id = Guid.Empty; CategoryIds = []; TagIds = []; Description = "" }
            NewCategory = { Id = Guid.Empty; Name = "" }
            NewTag = { Id = Guid.Empty; Name = ""; Color = Color.Red }
            TodoNameInput = ""
        }
    let cmd = Cmd.OfAsync.perform todosApi.GetAllTodos () GetTodos
    state, cmd

let update (msg: Msg) (state: State): State * Cmd<Msg> =
    match msg with
    | GetTodos (Ok todos) ->
        { state with Todos = todos }, Cmd.none
    | SetTodoInput x ->
        { state with TodoNameInput = x }, Cmd.none
    | AddTodo ->
        let todo = Todo.Create state.TodoNameInput
        let cmd = Cmd.OfAsync.perform todosApi.AddTodo todo AddedTodo
        { state with NewTodo = todo; TodoNameInput = "" }, cmd
    | RemoveTodo id ->
        let cmd = Cmd.OfAsync.perform todosApi.RemoveTodo id RemovedTodo
        state, cmd
    | _ -> 
        let cmd = Cmd.OfAsync.perform todosApi.GetAllTodos () GetTodos
        state, cmd


let navBrand =
    Bulma.navbarBrand.div [
        Bulma.navbarItem.a [
            prop.href "https://github.com/tonyx/Sharpino"
            navbarItem.isActive
            prop.children [
                Html.img [
                    prop.src "/favicon.png"
                    prop.alt "Logo"
                ]
                Html.text "Sharpino"
            ]
        ]
    ]

let containerBox (model: State) (dispatch: Msg -> unit) =
    Bulma.panel [
        Bulma.box [
            prop.children [
                Html.div [
                    Html.text "Todos"
                    for todo in model.Todos do
                        Html.div [
                            Html.text todo.Description
                            Html.button [
                                prop.onClick (fun _ -> dispatch (RemoveTodo todo.Id))
                                prop.text "Remove"
                            ]
                        ]
                ]
                Bulma.content [
                    Html.text "Add a new todo" 
                ]
                Bulma.input.text [
                    prop.value model.TodoNameInput
                    prop.placeholder "write a new todo"
                    prop.onChange (fun x -> SetTodoInput x |> dispatch)
                ]
                Bulma.control.p [
                    Html.button [
                        prop.onClick (fun _ -> dispatch AddTodo)
                        prop.text "Add"
                    ]
                ]

                Html.div [
                    Html.text "Categories"
                ]
                Html.div [
                    Html.text "Tags"
                ]
            ]
        ]
    ]

let view (model: State) (dispatch: Msg -> unit) =
    Bulma.hero [
        hero.isFullHeight
        color.isPrimary
        prop.style [
            style.backgroundPosition "no-repeat center center fixed"
        ]
        let todos = model.Todos |> List.map (fun t -> Html.div [ Html.text t.Description ])
        prop.children [
            Bulma.heroBody [
                Bulma.container [
                    Bulma.column [
                        column.is10
                        column.isOffset1
                        prop.children [
                            containerBox model dispatch
                        ]
                    ]
                ]
            ]
        ]
    ]