namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open log4net
open log4net.Config
open System.Security.Cryptography

open Newtonsoft.Json
open Newtonsoft.Json.Converters
open System

module Core =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type Event<'A> =
        abstract member Process: 'A -> Result<'A, string>

    type Undoer<'A, 'E when 'E :> Event<'A>> = 'A -> Result<List<'E>, string>

    type Command<'A, 'E when 'E :> Event<'A>> =
        abstract member Execute: 'A -> Result<List<'E>, string>
        abstract member Undoer: Option<'A -> Result<Undoer<'A, 'E>, string>>

    let inline evolveUNforgivingErrors<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>) =
        events
        |> List.fold
            (fun (acc: Result<'A, string>) (e: 'E) ->
                match acc with
                    | Error err -> Error err 
                    | Ok h -> h |> e.Process
            ) (h |> Ok)

    // warning: I cannot write this function less convoluted than this, at the moment
    let inline evolve<'A, 'E when 'E :> Event<'A>> (h: 'A) (events: List<'E>): Result<'A, string> =
        let rec evolveSkippingErrors (acc: Result<'A, string>) (events: List<'E>) (guard: 'A) =
            match acc, events with
            // if the accumulator is an error then skip it, and use the guard instead which was the 
            // latest valid value of the accumulator
            | Error err, _::es -> 
                log.Info (sprintf "warning 1: %A" err)
                evolveSkippingErrors (guard |> Ok) es guard
            // if the accumulator is error and the list is empty then we are at the end, and so we just
            // get the guard as the latest valid value of the accumulator
            | Error err, [] -> 
                log.Info (sprintf "warning 2: %A" err)
                guard |> Ok
            // if the accumulator is Ok and the list is not empty then we use a new guard as the value of the 
            // accumulator processed if is not error itself, otherwise we keep using the old guard
            | Ok state, e::es ->
                let newGuard = state |> e.Process
                match newGuard with
                | Error err -> 
                    log.Info (sprintf "warning 3: %A" err)
                    evolveSkippingErrors (guard |> Ok) es guard
                | Ok h' ->
                    evolveSkippingErrors (h' |> Ok) es h'
            | Ok h, [] -> h |> Ok

        evolveSkippingErrors (h |> Ok) events h


    let encriptionKey = 1 |> Some

    let encrypt (text: string) (shift: int) =
        if text = "" then
            ""
        else
            let lText = text |> List.ofSeq
            // let rVal = lText |> List.map (fun c -> char ((int c + shift)))
            let rVal = lText |> List.map (fun c -> char (((int c - int 'a' + shift) % 26) + int 'a'))
            let result = rVal |> List.toArray |> System.String
            result

    let decrypt (text: string) (shift: int) = 
        encrypt text (26 - shift) 

    type Secret () =
        member this.encript x = encrypt x 1
        member this.decript x = decrypt x 1
        // member this.encript x = x
        // member this.decript x = x

    let simpleEncriptor: Secret =
        Secret()

    type ForgettableValueConverter() =
        inherit JsonConverter()

        override this.CanConvert(objectType: Type): bool =
            failwith "Not Implemented"
        override this.CanRead: bool =
            true
        override this.CanWrite: bool =
            true
        override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: obj, serializer: JsonSerializer): obj =
            let result = reader.Value |> string |> simpleEncriptor.decript 
            result

        override this.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer): unit =
            let result = writer.WriteValue(value.ToString())
            result
    type Forgettable (value: string) =

        member this.EvalPredicate f fallback  = 
            f (this.Value)

        member this.EvalPredicate' f =
            f

        [<JsonConverter(typeof<ForgettableValueConverter>)>]
        member this.Value = simpleEncriptor.encript value
        override this.GetHashCode() = hash this.Value
        // override this.GetHashCode() = hash (simpleEncriptor.decript this.Value)
        override this.Equals (obj: obj) =
            match obj with
            | :? Forgettable as f -> f.Value = this.Value
            | _ -> false
        override this.ToString() = this.Value 

    // let mkForgettable (v: 'A when 'A: equality and 'A: comparison) =
    let mkForgettable v =
        Forgettable(v)