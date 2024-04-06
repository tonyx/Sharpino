
namespace Sharpino
open FSharp.Core
open Newtonsoft.Json
open Expecto
open System

module Definitions =
    type Json = string
    type Name = string
    type Version = string
    type SnapId = int
    type EventId = int
    type SnapshotId = int
    type AggregateId = Guid
    type AggregateStateId = Guid
    type ContextStateId = Guid

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
                
    // obsolete: use List.traverseResultM instead            
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
                (fun x  ->
                testParam x [
                        (x.ToString().Substring(0, 5)), 
                            fun x () ->
                                myTest x
                ]
                |> List.ofSeq
            )
            |> List.concat
        ) 

    let fmultipleTestCase name cnf myTest =
        ftestList name (
            cnf
            |> List.map 
                (fun x ->
                testParam x [
                        (x.ToString().Substring(0, 5)),
                            fun x () ->
                                myTest x
                ]
                |> List.ofSeq
            )
            |> List.concat
        )
    let pmultipleTestCase name cnf test =
        ptestList name (
            cnf
            |> List.map 
                (fun x ->
                testParam x [
                        (x.ToString()),
                            fun x () ->
                                test x
                ]
                |> List.ofSeq
            )
            |> List.concat
        )
        
module Result =
    let ofBool message x =
        match x with
        | true ->  Ok ()
        | false -> Error message
