namespace Sharpino
open Confluent.Kafka
open System
open System.Net

module KafkaStorageSpike =
    let produceMessage (topic: string) (message: string) =
        let config = ProducerConfig()
        config.BootstrapServers <- "localhost:9092"
        use producer = (ProducerBuilder<string, string>(config)).Build()
        let msg = new Message<string, string>()
        msg.Key <- topic
        msg.Value <- message
        let deliveryReport = producer.ProduceAsync(topic, msg)
        let msg = deliveryReport.Result
        printfn "Message delivered to %A" msg.TopicPartitionOffset

    let consumeMessages (topic: string) =
        let config = ConsumerConfig()
        config.BootstrapServers <- "localhost:9092"
        config.GroupId <- "your-group-id"
        config.AutoOffsetReset <- AutoOffsetReset.Earliest
        let consumer = new ConsumerBuilder<string, string>(config)
        let cns = consumer.Build()
        cns.Subscribe(topic)
        while true do
            let consumeResult = cns.Consume()
            printfn "Received message: %s at offset %A" consumeResult.Message.Value consumeResult.Offset
    let topic = "your-topic"

// produceMessage topic "Hello, Kafka!"
// consumeMessages topic

module KafkaStorage =
    type KafkaStorage(bootstrapServer) =
        interface IStorage with
            member this.Reset version name =
                failwith "not implemented"
            member this.TryGetLastSnapshot version name =
                failwith "not implemented"
            member this.TryGetLastEventId version name =
                failwith "not implemented"
            member this.TryGetLastSnapshotEventId version name =
                failwith "not implemented"
            member this.TryGetLastSnapshotId version name =
                failwith "not implemented"
            member this.TryGetEvent version id name =
                failwith "not implemented"
            member this.SetSnapshot version (id, json) name =
                let topic = "snapshots" + version + name
                let config = ProducerConfig()
                config.BootstrapServers <- bootstrapServer
                use producer = (ProducerBuilder<string, string>(config)).Build()
                let msg = new Message<string, string>()
                msg.Key <- topic
                msg.Value <- json
                let deliveryReport = producer.ProduceAsync(topic, msg)
                let ret = deliveryReport.Result
                () |> Result.Ok
                
            member this.AddEvents version events name =
                let topic = "events" + version + name
                let config = ProducerConfig()
                config.BootstrapServers <- bootstrapServer
                use producer = (ProducerBuilder<string, string>(config)).Build()
                let msg = new Message<string, string>()
                msg.Key <- topic
                let sending =
                    [
                        for event in events do
                            let msg = new Message<string, string>()
                            msg.Key <- topic
                            msg.Value <- event
                            let delivered = producer.ProduceAsync(topic, msg)
                            delivered.Result 
                    ]
                () |> Result.Ok
            member this.MultiAddEvents events =
                failwith "not implemented"
            member this.GetEventsAfterId version id name =
                failwith "not implemented"
