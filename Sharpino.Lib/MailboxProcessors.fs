
namespace Sharpino

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Sharpino
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open System.Runtime.CompilerServices
open System.Collections
open FSharp.Core

module MailBoxProcessors =
    let builder = Host.CreateApplicationBuilder()
    let config = builder.Configuration
    let loggerFactory = LoggerFactory.Create(fun b ->
        if config.GetValue<bool>("Logging:Console", true) then
            b.AddConsole() |> ignore
        )
    
    let logger = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("Sharpino.MailBoxProcessors")
    
    [<Obsolete("This method is deprecated and will be removed in a future version. Please config log on appsettings.json")>]
    let setLogger (newLogger: ILogger) =
        ()

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
            if (queue.Count > config.GetValue<int>("MailBoxCommandProcessorsSize", 100)) then
                try
                    let removed = queue.Dequeue()
                    let processor = processors.[removed]
                    processor.Dispose()
                    processors.Remove removed |> ignore
                with :? _ as e ->
                    logger.LogError(sprintf "error: cache is doing something wrong. Resetting. %A\n" e)
                
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
        
