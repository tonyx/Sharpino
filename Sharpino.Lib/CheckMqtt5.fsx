#r "nuget: MQTTnet, 5.1.0.1559"
open MQTTnet

let msg = new MqttApplicationMessage()
// check if ConvertPayloadToString exists
let methods = msg.GetType().GetMethods() |> Array.filter (fun m -> m.Name.Contains("Convert"))
for m in methods do
    printfn "Method: %s" m.Name
