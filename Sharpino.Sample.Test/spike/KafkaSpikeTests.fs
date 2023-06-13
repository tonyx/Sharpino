module kafkaSpike 

open Expecto
open Confluent.Kafka
open Sharpino.KafkaStorage
open Sharpino

[<Tests>]
let utilTest =
    testList "kafka spike"
        [
            ftestCase "kafka storage single event producer test" <| fun _ ->
                let bootStrapServer = "localhost:9092"
                let kafkaStorage = KafkaStorage(bootStrapServer) //:>IStorage
                let event = "event"
                let version = "_v1"
                let name = "_name"
                let result = kafkaStorage.AddEvents version [event] name
                Expect.isTrue true "true"

            ptestCase "kafka storage multiple events producer test" <| fun _ ->
                let bootStrapServer = "localhost:9092"
                let kafkaStorage = KafkaStorage(bootStrapServer) //:>IStorage
                let events = ["event1"; "event2"; "event3"]
                let version = "_v1"
                let name = "_name"
                let result = kafkaStorage.AddEvents version events name
                Expect.isTrue true "true"

            ptestCase "reset kafka storage" <| fun _ ->
                let bootStrapServer = "localhost:9092"
                let kafkaStorage = KafkaStorage(bootStrapServer) //:>IStorage
                let version = "_v1"
                let name = "_name"
                let result = kafkaStorage.Reset version name
                Expect.isTrue true "true"
        ]