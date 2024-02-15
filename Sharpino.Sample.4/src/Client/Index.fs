module Index

open Elmish
open System
open Fable.Remoting.Client
open Shared
open Shared.Entities
open Shared.Services
open Feliz.Bulma

type Model =
    { Rows: List<SeatsRowTO>
      Input: string
      SelectedRow: Option<Guid>
      }

type Msg =
    | GotRows of Result<List<SeatsRowTO>, string>
    | AddRow
    | AddSeatToRow of SeatsRowTO
    | RemoveSeatFromRow of SeatsRowTO
    | RemovedSeat of Result<unit, string>
    | AddedSeat of Result<unit, string>
    | AddedRow of Result<unit, string>
    | GotRow of SeatsRowTO
    | SetInput of string
    | SelectRow of Guid

let stadiumApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IRestStadiumBookingSystem>

let init () =
    let model = { Rows = []; Input = ""; SelectedRow = None}
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
    | SelectRow id ->
        let cmd = Cmd.none
        { model with SelectedRow = Some id}, cmd
    | AddSeatToRow row ->
        let seatNProgressive = row.Seats.Length + 1
        let seat = { Id = seatNProgressive ; State = SeatState.Free; RowId = None }
        let cmd = Cmd.OfAsync.perform stadiumApi.AddSeat (row.Id, seat) AddedSeat
        model,cmd
    | RemoveSeatFromRow row ->
        let seatsLength = row.Seats.Length
        if seatsLength > 0 then
            let seat = row.Seats |> List.find (fun x -> x.Id = seatsLength)
            let cmd = Cmd.OfAsync.perform stadiumApi.RemoveSeat seat RemovedSeat
            model, cmd
        else
            model, Cmd.none
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
    let seatRender (row: SeatsRowTO) =
        row.Seats
        |> List.sortBy (fun x -> x.Id)
        |> List.map (fun (x: Seat) -> (if x.State = SeatState.Free then "O" else "X") + " ")
        |> String.concat ""

    let seatsAsCheckboxes (row: SeatsRowTO) =
        row.Seats
        |> List.sortBy (fun x -> x.Id)
        |> List.map (fun (x: Seat) ->
            Html.div [
                prop.className "flex"
                prop.children [
                    Bulma.input.checkbox [
                        prop.name "options"
                        prop.value (x.Id.ToString())
                        // prop.onClick (fun _ -> dispatch (SelectRow row.Id))
                    ]
                ]
            ]
        )
        |> List.toArray
        // |> Html.div [ prop.children ]

    Html.div [
        prop.className "bg-white/80 rounded-md shadow-md p-4 w-5/6 lg:w-3/4 lg:max-w-2xl"
        prop.children [
            Html.ol [
                prop.className "list-decimal ml-6"
                prop.children [
                    for row in model.Rows do
                        // row |> seatsAsCheckboxes
                        Html.li
                            [
                                prop.className "my-1"
                                prop.text
                                    ((row |> seatRender) + " _ " + (row.Seats.Length.ToString()))
                                prop.children (row |> seatsAsCheckboxes)
                            ]

                        // Html.div [
                        //     seatsAsCheckboxes row
                        // ]

                        // Bulma.input.checkbox [
                        //     prop.name "options"
                        //     prop.value (row.Id.ToString()+"0")
                        //     // prop.onClick (fun _ -> dispatch (SelectRow row.Id))
                        // ]

                        // Bulma.input.checkbox [
                        //     prop.name "options"
                        //     prop.value (row.Id.ToString()+"1")
                        //     // prop.onClick (fun _ -> dispatch (SelectRow row.Id))
                        // ]
                        // Html.li [ prop.className "my-1"; prop.text  ((row |> seatRender) + " _ " + (row.Seats.Length.ToString())) ]
                        Bulma.input.radio [
                           prop.name "options"
                           prop.value (row.Id.ToString())
                           prop.onClick (fun _ -> dispatch (SelectRow row.Id))
                        ]
                        Html.button [
                            prop.className "bg-teal-600 text-white p-2 rounded-md ml-2"
                            prop.onClick (fun _ -> dispatch (AddSeatToRow row))
                            prop.text "AddSeat"
                        ]
                        Html.button [
                            prop.className "bg-red-600 text-white p-2 rounded-md ml-2"
                            prop.onClick (fun _ -> dispatch (RemoveSeatFromRow row))
                            prop.text "RemoveSeat"
                        ]
                ]
            ]
            Html.ol [
                prop.text "olaXXX"
                match model.SelectedRow with
                | Some x ->
                    prop.text ("selected" + (x.ToString()))
                | None ->
                    prop.text ""
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
