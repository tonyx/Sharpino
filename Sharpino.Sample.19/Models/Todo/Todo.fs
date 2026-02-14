namespace Sharpino.Template.Models
open Sharpino.Template.Commons
open Sharpino.Core
open System.Text.Json
open FsToolkit.ErrorHandling
open System

    type State =
        | Added of DateTime
        | Started of DateTime
        | Completed of DateTime

    type Todo =
        { TodoId: TodoId
          Text: string
          State: State }

        static member New text =
            { TodoId = TodoId.New
              Text = text
              State = Started DateTime.Now }

        member this.Activate (dateTime: DateTime) =
            result { 
                do!
                    match
                        this.State  with
                        | Added currentDateTime when currentDateTime.CompareTo dateTime < 0  -> Ok ()
                        | _ -> Error "Only added todo at earlier dateTime can be activated"
                return
                    {
                        this with
                            State = Started dateTime
                    }
            }
        member this.Complete dateTime =
            result {
                do!
                    match
                        this.State  with
                        | Started currentDateTime when currentDateTime.CompareTo dateTime < 0  -> Ok ()
                        | _ -> Error "Only active todo at earlier dateTime can be completed"
                return
                    {
                        this with
                            State = Completed dateTime
                    }
            }

        member this.Id = this.TodoId.Value
        static member SnapshotsInterval = 50
        static member StorageName = "_Todo"
        static member Version = "_01"
        member this.Serialize = 
            (this, jsonOptions) |> JsonSerializer.Serialize
        static member Deserialize (data: string) =
            try
                let todo = JsonSerializer.Deserialize<Todo> (data, jsonOptions)
                Ok todo
            with
                | ex -> Error ex.Message
        
        

