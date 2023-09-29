namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open Newtonsoft.Json
open Expecto
open System
open FsToolkit.ErrorHandling

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

    type JsonSerializer(serSettings: JsonSerializerSettings) =
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


module EncriptUtils =

    let keys = 
        [
            ("4b938de9-cb4b-4297-8687-865181836548", 7)
        ]
        |> Map.ofList

    let encriptionKey = 7 |> Some

    let encrypt (text: string) (shift: int) =
        if text = "" then
            ""
        else
            let lText = text |> List.ofSeq
            let rVal = lText |> List.map (fun c -> char (((int c - int 'a' + shift) % 26) + int 'a'))
            let result = rVal |> List.toArray |> System.String
            result

    let decrypt (text: string) (shift: int) = 
        encrypt text (26 - shift) 

    type Secret () =

        // let encriptionKey = 
        //     match keys.TryFind("4b938de9-cb4b-4297-8687-865181836548") with
        //     | Some k -> k |> Some
        //     | None -> None

        member this.encript x = 
            match encriptionKey with
            | Some k ->
                encrypt x k
            | None -> "unavailable"
        member this.decript x = 
            match encriptionKey with
            | Some k ->
                decrypt x k
            | None -> "unavailable"

    let simpleEncriptor: Secret =
        Secret()

    type ForgettableValueConverter() =
        inherit JsonConverter()

        override this.CanConvert(objectType: Type): bool =
            true
        override this.CanRead: bool =
            true
        override this.CanWrite: bool =
            true
        override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: obj, serializer: JsonSerializer): obj =
            let result = reader.Value |> string |> simpleEncriptor.decript 
            result

        override this.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer): unit =
            let result = writer.WriteValue(value.ToString())
            result

    type Forgettable (indexOfKey: string, value: string) =

        member this.IndexOfKey = indexOfKey
        // let encriptionKey = 7 |> Some
        member this.EncriptionKey = 
            printf "indexofkey: %A\n" indexOfKey
            match keys.TryFind(this.IndexOfKey) with
            | Some k -> 
                printf "found key: %A" k
                k |> Some
            | None -> 
                printf "not found key: %A" this.IndexOfKey
                None

        member this.EvalPredicate f fallback =
            if (this.EncriptionKey.IsSome) then
                f
            else fallback

        [<JsonConverter(typeof<ForgettableValueConverter>)>]
        member this.Value = simpleEncriptor.encript value
        override this.GetHashCode() = hash this.Value
        override this.Equals (obj: obj) =
            match obj with
            | :? Forgettable as f -> f.Value = this.Value
            | _ -> false
        override this.ToString() = this.Value 

    // let mkForgettable (v: 'A when 'A: equality and 'A: comparison) =
    let mkForgettable secretKeyIndex v =
        Forgettable(secretKeyIndex, v)