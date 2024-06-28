
namespace Tonyx.Sharpino.Pub
open Sharpino.CommandHandler
open Tonyx.Sharpino.Pub.Commons 
open Sharpino.Definitions
open System
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Sharpino
open Sharpino.Lib.Core.Commons
open System.Text.RegularExpressions
open Sharpino.Storage
open Sharpino.Core
open Sharpino.Utils
open System

module Supplier =
    let emailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b"
    let phonePattern = @"\(?\d{3}\)?-? *\d{3}-? *-?\d{4}"

    type Supplier(id: Guid, name: string, email: string, phone: string) =
        member this.Id = id
        member this.Name = name
        member this.Email = email
        member this.Phone = phone
        member this.ChangePhone(newPhone: string) =
            let matches = Regex.Matches (newPhone, phonePattern)
            if matches.Count > 0 then
               Supplier(this.Id, this.Name, this.Email, newPhone) |> Ok
            else "Phone number must have 10 digits" |> Error
        member this.ChangeEmail(newEmail: string) =
           let matches = Regex.Matches (newEmail, emailPattern)
           if matches.Count > 0 then
               Supplier(this.Id, this.Name, newEmail, this.Phone) |> Ok
           else "Invalid email" |> Error
        static member Deserialize json =
            serializer.Deserialize<Supplier> json
        static member StorageName =
            "_supplier"
        static member Version =
            "_01"
        member this.Serialize (serializer: ISerializer): Json =
            serializer.Serialize this    
        
        interface Aggregate<string> with
            member this.Id = this.Id
            member this.Serialize =
                serializer.Serialize this
        
        interface Entity with
            member this.Id = this.Id
            
        
            
    
