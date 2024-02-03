
namespace Sharpino
open FSharp.Core
open Newtonsoft.Json
open Expecto
// open MBrace.FsPickler
// open MBrace.FsPickler.Json
// open FSharp.Quotations
// open FSharp.Quotations.Evaluator
// open System.IO
// open MBrace.FsPickler
// open MBrace.FsPickler.Json
// open System.IO
// open System.Text

open System

module Definitions =
    type Json = string
    type Name = string
    type Version = string
    type SnapId = int
    type EventId = int
    type SnapshotId = int
    type AggregateId = Guid

module ApplicationInstance =
    type ApplicationInstance() =
        let mutable ApplicationGuid = Guid.NewGuid()
        static let instance = ApplicationInstance()
        static member Instance = instance

        member this.GetGuid() =
            ApplicationGuid
        member this.ResetGuid() =
            ApplicationGuid <- Guid.NewGuid()

module Utils =
    open Definitions
    let serSettings = JsonSerializerSettings()
    serSettings.TypeNameHandling <- TypeNameHandling.Objects
    serSettings.ReferenceLoopHandling <- ReferenceLoopHandling.Ignore

    type ISerializer =
        abstract member Deserialize<'A> : Json -> Result<'A, string>
        abstract member Serialize<'A> : 'A -> Json
        
        
    // // open Utils
    // open MBrace.FsPickler
    // open MBrace.FsPickler.Json
    // open System.IO
    // open System.Text
    // let serializerX = JsonSerializer(indent = true)
    // let utf8 = UTF8Encoding(false)
    //
    // type JsonSerializer(serSettings: JsonSerializerSettings) =
    //         member this.toJson (x: 'a) =
    //             use stream = new MemoryStream()
    //             serializerX.Serialize(stream, x)
    //             stream.ToArray() |> utf8.GetString
    //
    //         member this.parseJson<'a> json =
    //             use reader = new StringReader(json)
    //             serializerX.Deserialize<'a>(reader)
    //         interface ISerializer with
    //             member this.Serialize (x: 'a) =
    //                 this.toJson x
    //             member this.Deserialize<'A> (json: string) =
    //                 try
    //                     this.parseJson<'A> json |> Ok
    //                 with
    //                 | ex  ->
    //                     Error (ex.ToString())
        
    type JsonSerializer(serSettings: JsonSerializerSettings) =
        interface ISerializer with
            member this.Deserialize<'A> (json: string): Result<'A, string> =
                try
                    JsonConvert.DeserializeObject<'A>(json, serSettings) |> Ok
                with
                | ex  ->
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

module Utils' =
    open Utils
    open MBrace.FsPickler
    open MBrace.FsPickler.Json
    open System.IO
    open System.Text
    let serializer = JsonSerializer(indent = true)
    let utf8 = UTF8Encoding(false)
    
    type JsonSerializer(serSettings: JsonSerializerSettings) =
            member this.toJson (x: 'a) =
                use stream = new MemoryStream()
                serializer.Serialize(stream, x)
                stream.ToArray() |> utf8.GetString

            member this.parseJson<'a> json =
                use reader = new StringReader(json)
                serializer.Deserialize<'a>(reader)
            interface ISerializer with
                member this.Serialize (x: 'a) =
                    this.toJson x
                member this.Deserialize<'A> (json: string) =
                    try
                        this.parseJson<'A> json |> Ok
                    with
                    | ex  ->
                        Error (ex.ToString())
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


