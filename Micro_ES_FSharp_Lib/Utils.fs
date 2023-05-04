namespace Tonyx.EventSourcing
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open Newtonsoft.Json

module Utils =
    let serSettings = JsonSerializerSettings()
    serSettings.TypeNameHandling <- TypeNameHandling.Objects

    let deserialize<'A> (json: string): Result<'A, string> =
        try
            JsonConvert.DeserializeObject<'A>(json, serSettings) |> Ok
        with
        | ex  ->
            printf "error deserialize: %A" ex
            Error (ex.ToString())
    let serialize<'A> (x: 'A): string =
        JsonConvert.SerializeObject(x, serSettings)

    type CeResultBuilder()  =
        member this.Bind(x, f) =
            match x with
            | Error x1 -> Error x1
            | Ok x1 -> f x1
        member this.MergeSources (x, y) =
            match x, y with
                | Error x1, _ -> Error x1
                | _, Error y1 -> Error y1
                | Ok x1, Ok y2 -> (x1, y2) |> Ok
        member this.Return(x) =
            x |> Ok
        member this.ReturnFrom(x) =
            x
        member this.Zero() = () |> Error

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
