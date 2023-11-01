namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open Newtonsoft.Json
open Expecto
open System
open FsToolkit.ErrorHandling

module Definitions =
    type Json = string
    type Name = string
    type version = string

module ApplicationInstance =
    type ApplicationInstance() =
        let ApplicationGuid = Guid.NewGuid()
        static let instance = ApplicationInstance()
        static member Instance = instance

        member this.GetGuid() =
            ApplicationGuid

module Utils =
    open Definitions
    let serSettings = JsonSerializerSettings()
    serSettings.TypeNameHandling <- TypeNameHandling.Objects
    serSettings.ReferenceLoopHandling <- ReferenceLoopHandling.Ignore

    type ISerializer =
        abstract member Deserialize<'A> : Json -> Result<'A, string>
        abstract member Serialize<'A> : 'A -> Json

    type JsonSerializer(serSettings: JsonSerializerSettings) =
        interface ISerializer with
            member this.Deserialize<'A> (json: string): Result<'A, string> =
                try
                    JsonConvert.DeserializeObject<'A>(json, serSettings) |> Ok
                with
                | ex  ->
                    printf "error deserialize: %A" ex
                    Error (ex.ToString())
        
            member this.Serialize<'A> (x: 'A): string =
                JsonConvert.SerializeObject(x, serSettings)

    let catchErrors f l =
        l
        |> List.fold (fun acc x ->
            match acc with
            | Error e -> Error e
            | Ok acc ->
                match f x with
                | Ok y -> Ok (acc @ [y])
                | Error e -> Error e
        ) (Ok [])

    let boolToResult message x =
        match x with
        | true -> x |> Ok
        | false -> Error message

    let getError x =
        match x with
        | Error e -> e
        | _ -> failwith (sprintf "can't extract error from an Ok: %A" x.OkValue)

    [<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
    type CurrentVersion() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
    type UpgradedVersion() =
        inherit Attribute()

module TestUtils =
    let multipleTestCase name par myTest =
        testList name (
            par
            |> List.map 
                (fun (app,  upgd, shdTstUpgrd) ->
                testParam (app,  upgd, shdTstUpgrd) [
                        (app.ToString()) + (upgd.ToString()), 
                            fun (app, upgd, shdTstUpgrd) () ->
                                myTest(app, upgd, shdTstUpgrd)
                ]
                |> List.ofSeq
            )
            |> List.concat
        ) 

    let fmultipleTestCase name cnf myTest =
        ftestList name (
            cnf
            |> List.map 
                (fun (ap,  upgd, upgrader) ->
                testParam (ap,  upgd, upgrader ) [
                        (ap.ToString()) + (upgd.ToString()), 
                            fun (ap,  upgd, upgrader) () ->
                                myTest(ap, upgd, upgrader)
                ]
                |> List.ofSeq
            )
            |> List.concat
        )
    let pmultipleTestCase name cnf test =
        ptestList name (
            cnf
            |> List.map 
                (fun (ap, upgd, upgrader) ->
                testParam (ap, upgd, upgrader) [
                        (ap.ToString()) + (upgd.ToString()),
                            fun (ap, upgd, upgrader) () ->
                                test(ap, upgd, upgrader)
                ]
                |> List.ofSeq
            )
            |> List.concat
        )