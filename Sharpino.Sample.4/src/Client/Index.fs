module Index

open Elmish
open Fable.Remoting.Client
open Shared
open Shared.Entities
open Shared.Services


type Model =
    { Rows: List<SeatsRowTO>
      Input: string
      }

type Msg =
    | GotRows of Result<List<SeatsRowTO>, string>
    | AddRow
    | AddedRow of Result<unit, string>
    | GotRow of SeatsRowTO
    | SetInput of string

let stadiumApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IRestStadiumBookingSystem>

let init () =
    let model = { Rows = []; Input = "" }
    let cmd = Cmd.OfAsync.perform stadiumApi.GetAllRowTOs () GotRows
    model, cmd

let update msg model =
    match msg with
    | GotRows rows ->
            match rows with
            | Error _ -> model, Cmd.none
            | Ok rows ->
                { model with Rows = rows }, Cmd.none
    | SetInput value -> { model with Input = value }, Cmd.none
    | AddRow ->
        let cmd = Cmd.OfAsync.perform stadiumApi.AddRowReference () AddedRow
        { model with Input = "" }, cmd
    | _ ->
        let cmd = Cmd.OfAsync.perform stadiumApi.GetAllRowTOs () GotRows
        model, cmd

open Feliz

let private rowsAction (model: Model) dispatch =
    Html.div [
        prop.className "flex flex-col sm:flex-row mt-4 gap-4"
        prop.children [
            Html.input [
                prop.className
                    "shadow appearance-none border rounded w-full py-2 px-3 outline-none focus:ring-2 ring-teal-300 text-grey-darker"
                prop.value model.Input
                prop.placeholder "What needs to be done?"
                prop.autoFocus true
                prop.onChange (SetInput >> dispatch)
                prop.onKeyPress (fun ev ->
                    if ev.key = "Enter" then
                        dispatch AddRow)
            ]
            Html.button [
                prop.className
                    "flex-no-shrink p-2 px-12 rounded bg-teal-600 outline-none focus:ring-2 ring-teal-300 font-bold text-white hover:bg-teal disabled:opacity-30 disabled:cursor-not-allowed"
                prop.disabled (Todo.isValid model.Input |> not)
                prop.onClick (fun _ -> dispatch AddRow)
                prop.text "Add"
            ]
    ]
]

let private rowsList (model: Model) dispatch =
    Html.div [
        prop.className "bg-white/80 rounded-md shadow-md p-4 w-5/6 lg:w-3/4 lg:max-w-2xl"
        prop.children [
            Html.ol [
                prop.className "list-decimal ml-6"
                prop.children [
                    for row in model.Rows do
                        Html.li [ prop.className "my-1"; prop.text (row.Id.ToString()) ]
                ]
            ]
            rowsAction model dispatch
        ]
    ]

let view (model: Model) dispatch =
    Html.section [
        prop.className "h-screen w-screen"
        prop.style [
            style.backgroundSize "cover"
            style.backgroundImageUrl "https://unsplash.it/1200/900?random"
            style.backgroundPosition "no-repeat center center fixed"
        ]

        prop.children [
            Html.a [
                prop.href "https://safe-stack.github.io/"
                prop.className "absolute block ml-12 h-12 w-12 bg-teal-300 hover:cursor-pointer hover:bg-teal-400"
                prop.children [ Html.img [ prop.src "/favicon.png"; prop.alt "Logo" ] ]
            ]

            Html.div [
                prop.className "flex flex-col items-center justify-center h-full"
                prop.children [
                    Html.h1 [
                        prop.className "text-center text-5xl font-bold text-white mb-3 rounded-md p-4"
                        prop.text "Sharpino.Sample._4"
                    ]
                    rowsList model dispatch
                ]
            ]
        ]
    ]
