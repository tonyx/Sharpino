#r "nuget: MQTTnet, 5.1.0.1559"
open MQTTnet

let factory = new MqttClientFactory()
printfn "Factory: %s" (factory.GetType().Name)

// In MQTTnet 5.x, MqttApplicationMessage has a Payload field which is a ReadOnlyMemory<byte> or similar.
let msg = new MqttApplicationMessage()
printfn "MqttApplicationMessage properties:"
for prop in msg.GetType().GetProperties() do
    printfn "%s : %s" prop.Name prop.PropertyType.Name
