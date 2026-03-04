#r "nuget: Azure.Messaging.ServiceBus, 7.20.1"
open Azure.Messaging.ServiceBus
open System

try
    let clientType = typeof<ServiceBusClient>
    printfn "ServiceBusClient found"
    let processorType = typeof<ServiceBusProcessor>
    printfn "ServiceBusProcessor found"
    let senderType = typeof<ServiceBusSender>
    printfn "ServiceBusSender found"
with ex -> printfn "Error: %s" ex.Message
