
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

open System.Threading.Tasks
open FsToolkit.ErrorHandling

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

    type Command =
        | AsyncTaskCommand of (unit -> Task<Result<unit, string>>)
        | AsyncComputationCommand of (unit -> Async<Result<unit, string>>)
        | SyncCommand of (unit -> Result<unit, string>)

    type UnitResult = Command * AsyncReplyChannel<Result<unit, string>>
    
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
                        let! (cmd, reply) = inbox.Receive()
                        try
                            match cmd with
                            | AsyncTaskCommand f ->
                                let! result = f() |> Async.AwaitTask
                                reply.Reply result
                            | AsyncComputationCommand f ->
                                let! result = f()
                                reply.Reply result
                            | SyncCommand f ->
                                let result = f()
                                reply.Reply result
                        with ex ->
                            logger.LogError(sprintf "Exception executing command in MailboxProcessor: %A" ex)
                            reply.Reply (Error ex.Message)
                        do! loop()
                    }
                loop()
            )
   
    let postToTheProcessorAsync (processor: MailboxProcessor<UnitResult>) (f: unit -> Task<Result<unit, string>>) : Task<Result<unit, string>> =
        // timeout is hardcoded here. next release will be a conf
        Async.StartAsTask (processor.PostAndAsyncReply ((fun reply -> (AsyncTaskCommand f, reply)), Commons.generalAsyncTimeOut))

    let postToTheProcessorComputationAsync (processor: MailboxProcessor<UnitResult>) (f: unit -> Async<Result<unit, string>>) : Async<Result<unit, string>> =
        processor.PostAndAsyncReply ((fun reply -> (AsyncComputationCommand f, reply)), Commons.generalAsyncTimeOut)

    let postToTheProcessorSync (processor: MailboxProcessor<UnitResult>) (f: unit -> Result<unit, string>) : Result<unit, string> =
        Async.RunSynchronously (processor.PostAndAsyncReply ((fun reply -> (SyncCommand f, reply)), Commons.generalAsyncTimeOut))

    type PostInvoker =
        static member inline Invoke (_: PostInvoker, processor: MailboxProcessor<UnitResult>, f: unit -> Task<Result<unit, string>>) : Task<Result<unit, string>> =
            postToTheProcessorAsync processor f
        static member inline Invoke (_: PostInvoker, processor: MailboxProcessor<UnitResult>, f: unit -> Async<Result<unit, string>>) : Async<Result<unit, string>> =
            postToTheProcessorComputationAsync processor f
        static member inline Invoke (_: PostInvoker, processor: MailboxProcessor<UnitResult>, f: unit -> Result<unit, string>) : Result<unit, string> =
            postToTheProcessorSync processor f

    let inline postToTheProcessor (processor: MailboxProcessor<UnitResult>) (f: 'F) =
        ((^Invoker or ^F) : (static member Invoke: ^Invoker * MailboxProcessor<UnitResult> * 'F -> 'Res) (Unchecked.defaultof<PostInvoker>, processor, f))
        
