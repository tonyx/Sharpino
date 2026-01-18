
namespace Tonyx.Sharpino.Pub
open Sharpino.CommandHandler
open Tonyx.Sharpino.Pub.Commons 
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Lib.Core.Commons
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System

module Supplier =

    type Supplier(id: Guid, name: string, email: string, phone: string) =
        member this.Id = id
        member this.Name = name
        member this.Email = email
        member this.Phone = phone
        member this.ChangePhone(newPhone: string) =
               Supplier(this.Id, this.Name, this.Email, newPhone) |> Ok
        member this.ChangeEmail(newEmail: string) =
               Supplier(this.Id, this.Name, newEmail, this.Phone) |> Ok
        static member Deserialize json =
            serializer.Deserialize<Supplier> json
        static member StorageName =
            "_supplier"
        static member Version =
            "_01"
        member this.Serialize = 
            serializer.Serialize this    
        
            
        
            
    
