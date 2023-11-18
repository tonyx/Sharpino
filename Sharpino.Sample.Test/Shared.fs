module Tests.Sharpino.Shared

open Expecto
open System
open FSharp.Core

open Sharpino.Sample.Entities.Tags
open Sharpino.Sample.Entities.Categories
open Sharpino.Sample.Entities.Todos
open Sharpino.Sample.Shared.Entities

let mkTag id name color =
    { Id = id; Name = name; Color = color }
let mkCategory id name =
    { Id = id; Name = name }    

let mkTodo id description categoryIds tagIds =
    { Id = id; Description = description; CategoryIds = categoryIds; TagIds = tagIds }
