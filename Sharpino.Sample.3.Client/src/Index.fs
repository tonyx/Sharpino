module Index

open Tonyx.SeatsBooking.Shared.Entities

open Elmish
open Fable.Remoting
open Fable.Remoting.Client
open Tonyx.SeatsBooking.Shared
open Tonyx.SeatsBooking.Shared.Services
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

type State =
    {
        Hello: string
    }
and Msg =
    | SetHello of Result<List<Guid>, string>

let routeBuilder typeName methodName =  sprintf "/api/%s/%s" typeName methodName
let seatsBookingApi =
    Remoting.createApi ()
    // |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withBaseUrl "/"
    |> Remoting.buildProxy<IRestStadiumBookingSystem>
    
let init (): State * Cmd<Msg> =
    let state =
        {
            Hello = "hello"
        }
    // let cmd = Cmd.OfAsync.perform seatsBookingApi.GetAllRowReferences () SetHello
    let cmd = Cmd.none
    state, cmd

let update (msg: Msg ) (state: State): State * Cmd<Msg> =
    let cmd = Cmd.none
    state, cmd
    
let containerBox (model: State) (dispatch: Msg -> unit) =
    Bulma.panel [
        
        // Bulma.box [
        //     Bulma.content [
        //         Html.text "hello world!"
        //         str model.Hello
        //     ]
        // ]
    ]
    
let view (model: State) (dispatch: Msg -> unit) =
    Bulma.hero [
        ]
    //     hero.isFullHeight
    //     color.isPrimary
    //     prop.style [
    //         style.backgroundPosition "no-repeat center center fixed"
    //     ]
    //     prop.children [
    //         Bulma.heroBody [
    //             Bulma.container [
    //                 Bulma.column [
    //                     column.is10
    //                     column.isOffset1
    //                     prop.children [
    //                         containerBox model dispatch
    //                     ]
    //                 ]
    //             ]
    //         ]
    //     ]
    // ]
    