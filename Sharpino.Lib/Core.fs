namespace Sharpino
open FSharp.Core
open FSharpPlus
open FSharpPlus.Data
open log4net
open log4net.Config
open System.Security.Cryptography

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

    // let encriptionKey = "12345678901234567890123456789012" |> Some
    let encriptionKey = 1 |> Some
    let encrypt (text: string) (shift: int) =
        let offset = int 'a'
        let isUpperCase c = c >= 'A' && c <= 'Z'
        let isLowerCase c = c >= 'a' && c <= 'z'

        let shiftChar (c: char) =
            if isUpperCase c || isLowerCase c then
                let baseChar = if isUpperCase c then 'A' else 'a'
                char (baseChar + char((int c - offset + shift) % 26))
            else
                c

        string([for c in text -> shiftChar c])

    let decrypt (text: string) (shift: int) = 
        encrypt text (26 - shift) 

    type Secret<'A, 'B> (encriptor: 'A -> 'B, decriptor: 'B -> 'A)=
        member this.encript x = x
        member this.decript x = x
        // member this.encript x = encriptor x
        // member this.decript x = decriptor x

    let dec x = decrypt x 1
    let enc x = encrypt x 1

    let simpleEncriptor: Secret<string, string> = 
        Secret(dec, enc)

    type Forgettable<'A  when 'A: equality and 'A: comparison> (value: 'A, s: Secret<'A, 'A>) =

        let decript x = s.decript x

        member this.EvalPredicate f fallback = 
            

            // match encriptionKey with
            // | None -> fallback
            // | Some _ -> 

                // f (decript this.Value)
                f (decript this.Value)
                // f (s.decript this.Value)


                // f this.Value

        member this.Value = value
        override this.GetHashCode() = hash this.Value
        override this.Equals (obj: obj) =
            match obj with
            | :? Forgettable<'A> as f -> f.Value = this.Value
            | _ -> false

    // let mkForgettable (v: 'A when 'A: equality and 'A: comparison) =
    let mkForgettable v =
        Forgettable(v, simpleEncriptor)