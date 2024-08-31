
namespace Sharpino

open Sharpino
open Sharpino.Core
open Sharpino.Definitions
open System.Runtime.CompilerServices
open System.Collections
open FSharp.Core
open log4net
open System.Runtime.CompilerServices
open System

module MailBoxProcessors =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    let config = 
        try
            Conf.config ()
        with
        | :? _ as ex -> 
            // if appSettings.json is missing
            log.Error (sprintf "appSettings.json file not found using default!!! %A\n" ex)
            Conf.defaultConf

    type UnitResult = ((unit -> Result<unit, string>) * AsyncReplyChannel<Result<unit, string>>)
    
    type Processors private() =
        let processors = Generic.Dictionary<string, MailboxProcessor<UnitResult>>()
        static let instance = Processors()
        let queue = Generic.Queue<string>()
        static member Instance = instance

        member this.GetProcessor (name: string) =
            let (b, processor) = processors.TryGetValue name
            if b then
                processor
            else
                this.addAndGetNewProcessor name
  
        [<MethodImpl(MethodImplOptions.Synchronized)>]
        member this.addAndGetNewProcessor name =
            if (queue.Count > config.MailBoxCommandProcessorsSize) then
                try
                    let removed = queue.Dequeue()
                    let processor = processors.[removed]
                    processor.Dispose()
                    processors.Remove removed |> ignore
                with :? _ as e -> 
                    log.Error(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)    
                
            let processor = this.createProcessor ()
            processors.Add(name, processor)
            queue.Enqueue name
            processor
        
        member this.createProcessor () =
            MailboxProcessor<UnitResult>.Start (fun inbox ->
                let rec loop () =
                    async {
                        let! (f, reply) = inbox.Receive()
                        let result = f()
                        reply.Reply result
                        do! loop()
                    }
                loop()
            )
   
    let postToTheProcessor (processor: MailboxProcessor<UnitResult>) f =
        // timeout is harcode here. next release will be a conf
        Async.RunSynchronously (processor.PostAndAsyncReply (fun reply -> (f, reply)), Commons.generalAsyncTimeOut)
        
