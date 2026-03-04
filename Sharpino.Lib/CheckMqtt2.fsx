#r "nuget: MQTTnet, 5.1.0.1559"
open MQTTnet
try
    let t = typeof<MQTTnet.MqttFactory>
    printfn "MqttFactory found in %s" t.Namespace
with | ex -> printfn "Error: %s" ex.Message
