namespace Sharpino

open FSharp.Core
open FSharpPlus
open FSharpPlus.Data

open System
open Sharpino
open Sharpino.Utils
open FsToolkit.ErrorHandling

module EventStore =     
    open EventStore.Client
    open Newtonsoft.Json

    type TestEvent =
        {
            EntityId: string
            ImportantData: string
        }

    // going to delete this as I replaced it with the C# version
    type EventStore() =
        member this.SendEvent() =   
            let settings = EventStoreClientSettings.Create("undefined");
            let client = new EventStoreClient(settings);
            let evt =
                {
                    EntityId = Guid.NewGuid().ToString("N")
                    ImportantData = "I wrote my first event!"
                }
            let eventData = EventData(
                Uuid.NewUuid(),
                "TestEvent",
                System.Text.Encoding.UTF8.GetBytes(Utils.serialize<TestEvent> evt)
            )
            async { 
                client.AppendToStreamAsync(
                    "some-stream",
                    StreamState.Any,
                    [ eventData ]) |> ignore
                }
                |> Async.RunSynchronously
                |> ignore
