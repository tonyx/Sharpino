namespace Sharpino.Sample._15

open System
open Sharpino.Commons
open Sharpino.Core
open Sharpino
open System.Text.Json
open System.Text.Json.Serialization

open Sharpino.Sample._15
open Sharpino.Sample._15.Student
open Sharpino.Sample._15.StudentEvents

module StudentCommands =
    type StudentCommands =
        | Rename of string
        interface AggregateCommand<Student,StudentEvents> with
            member this.Execute (student: Student) =
                match this with
                | Rename newName ->
                    student.Rename newName
                    |> Result.map (fun s -> (s, [Renamed newName]))
           
            member this.Undoer = None
            
