#r "nuget: MQTTnet, 5.1.0.1559"
open MQTTnet
try
    let t = typeof<MQTTnet.MqttFactory>
    printfn "MqttFactory found in %s" t.Namespace
with _ -> printfn "MqttFactory not found in MQTTnet"

try
    let t = typeof<MQTTnet.MqttClientFactory>
    printfn "MqttClientFactory found in %s" t.Namespace
with _ -> printfn "MqttClientFactory not found in MQTTnet"
