open Confluent.Kafka
open System.Net

// For more information see https://aka.ms/fsharp-console-apps
let config = ProducerConfig()
config.BootstrapServers <- "localhost:9092"

let producer = ProducerBuilder<Null, string>(config)
let p = producer.Build()
let message = Message<Null, string>()
message.Key <- null
message.Value <- "asdf"

p.ProduceAsync("quickstart-events", message)
|> Async.AwaitTask 
|> Async.RunSynchronously
|> ignore


// var result = await producer.ProduceAsync("weblog", new Message<Null, string> { Value="a log message" });

printfn "Hello from F#"