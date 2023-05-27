namespace Tonyx.EventSourcing
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open Newtonsoft.Json
open Expecto

module Utils =
    let serSettings = JsonSerializerSettings()
    serSettings.TypeNameHandling <- TypeNameHandling.Objects
    serSettings.ReferenceLoopHandling <- ReferenceLoopHandling.Ignore

    let deserialize<'A> (json: string): Result<'A, string> =
        try
            JsonConvert.DeserializeObject<'A>(json, serSettings) |> Ok
        with
        | ex  ->
            printf "error deserialize: %A" ex
            Error (ex.ToString())
    let serialize<'A> (x: 'A): string =
        JsonConvert.SerializeObject(x, serSettings)

    let catchErrors f l =
        let (okList, errors) =
            l
            |> List.map f
            |> Result.partition
        if (errors.Length > 0) then
            Result.Error (errors.Head)
        else
            okList |> Result.Ok

    let optionToResult x =
        match x with
        | Some x -> x |> Ok
        | _ -> Error "is None"

    let boolToResult message x =
        match x with
        | true -> x |> Ok
        | false -> Error message

    let optionToDefault d x =
        match x with
        | Some y -> y
        | None -> d

    let getError x =
        match x with
        | Error e -> e
        | _ -> failwith (sprintf "can't extract error from an Ok: %A" x.OkValue)

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