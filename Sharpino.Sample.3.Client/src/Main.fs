module Main

open Feliz
open Browser.Dom

open Elmish
open Elmish.React
open Elmish.Debug

[<EntryPoint>]
Program.mkProgram Index.init Index.update Index.view
|> Program.withConsoleTrace
|> Program.withReactSynchronous "sharpino-booking-app"
|> Program.run 
